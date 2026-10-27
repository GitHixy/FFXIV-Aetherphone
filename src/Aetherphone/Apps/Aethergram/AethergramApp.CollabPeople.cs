using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Confirm;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private enum CollabRows : byte
    {
        None,
        Compose,
        Edit,
    }

    private const float CollabChevronInset = 8f;
    private const float CollabChevronSize = 18f;
    private const float CollabChevronGap = 14f;
    private const float CollabTrailingGap = 10f;

    private readonly List<MentionSuggestDto> composeCollaborators = new(CollabStates.MaxCollaborators);
    private readonly string[] composeCollabLabels = new string[CollabStates.MaxCollaborators];
    private readonly bool[] composeCollabActive = new bool[CollabStates.MaxCollaborators];
    private readonly ChipRail composeCollabRail = new();
    private readonly ChipRail editCollabRail = new();
    private PersonPicker? collabPicker;
    private string[] editCollabLabels = Array.Empty<string>();
    private bool[] editCollabActive = Array.Empty<bool>();
    private CollaboratorDto[]? editCollabSource;
    private LanguageInfo? editCollabLanguage;
    private string collabRemainingText = string.Empty;
    private int collabRemainingCount = -1;
    private LanguageInfo? collabRemainingLanguage;
    private volatile PostDto? editCollabUpdate;
    private volatile bool editCollabInvited;
    private volatile bool editCollabFailed;
    private volatile bool editCollabBusy;

    private PersonPicker CollabPicker =>
        collabPicker ??= new PersonPicker(store.NewMentionSuggestions(), L.Aethergram.CollabInvite);

    private static float CollabOptionRows(int collaborators) => collaborators > 0 ? 2f : 1f;

    private string[]? ComposeCollaboratorIds()
    {
        if (composeCollaborators.Count == 0)
        {
            return null;
        }

        var ids = new string[composeCollaborators.Count];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = composeCollaborators[index].UserId;
        }

        return ids;
    }

    private void ResetComposeCollab()
    {
        composeCollaborators.Clear();
        composeCollabRail.Reset();
        collabPicker?.Close();
    }

    private void GateComposeCollabPicker()
    {
        collabPicker?.Gate();
    }

    private void AddComposeCollaborator(MentionSuggestDto person)
    {
        if (composeCollaborators.Count >= CollabStates.MaxCollaborators
            || string.Equals(person.UserId, store.Me?.Id, StringComparison.Ordinal))
        {
            return;
        }

        for (var index = 0; index < composeCollaborators.Count; index++)
        {
            if (string.Equals(composeCollaborators[index].UserId, person.UserId, StringComparison.Ordinal))
            {
                return;
            }
        }

        composeCollabLabels[composeCollaborators.Count] = SocialIdentity.Name(person.DisplayName, person.Handle);
        composeCollaborators.Add(person);
    }

    private void RemoveComposeCollaborator(int removeIndex)
    {
        composeCollaborators.RemoveAt(removeIndex);
        for (var index = removeIndex; index < composeCollaborators.Count; index++)
        {
            composeCollabLabels[index] = composeCollabLabels[index + 1];
        }
    }

    private void DrawComposeCollabPicker(Rect area)
    {
        if (collabPicker?.Draw(area, theme, images, lodestone) is not { } picked)
        {
            return;
        }

        AddComposeCollaborator(picked);
    }

    private float DrawComposeCollabRows(ImDrawListPtr drawList, Rect card, float top, float scale)
    {
        var count = composeCollaborators.Count;
        var bottom = DrawCollabRows(drawList, card, top, scale, Loc.T(L.Aethergram.CollabInviteMany), count,
            composeCollabRail, composeCollabLabels.AsSpan(0, count), composeCollabActive.AsSpan(0, count),
            !store.Posting, out var removeIndex, out var inviteTapped);
        if (removeIndex >= 0)
        {
            RemoveComposeCollaborator(removeIndex);
        }
        else if (inviteTapped)
        {
            CollabPicker.Open();
        }

        return bottom;
    }

    private void BeginEditPostCollab()
    {
        editCollabSource = null;
        editCollabUpdate = null;
        editCollabInvited = false;
        editCollabFailed = false;
        editCollabRail.Reset();
        collabPicker?.Close();
    }

    // Invites and removals from Edit post go to the server at once, like the post menu's removal, so
    // the caption Save only ever carries the caption, tags, alt text and the sensitive flag.
    private void ApplyEditPostCollab()
    {
        if (editCollabUpdate is { } updated)
        {
            editCollabUpdate = null;
            if (editPost is { } current && current.Id == updated.Id)
            {
                editPost = updated;
            }
        }

        if (editCollabInvited)
        {
            editCollabInvited = false;
            toast.Show(Loc.T(L.Aethergram.CollabInviteSent));
        }

        if (editCollabFailed)
        {
            editCollabFailed = false;
            var failure = store.CollabFailure;
            editPostStatus = failure.Failed ? FailureText.Resolve(failure) : Loc.T(L.Aethergram.CollabFailed);
        }
    }

    private float DrawEditCollabRows(ImDrawListPtr drawList, Rect card, float top, float scale, PostDto post)
    {
        SyncEditCollabLabels(post);
        var bottom = DrawCollabRows(drawList, card, top, scale, Loc.T(L.Aethergram.CollabSection),
            editCollabLabels.Length, editCollabRail, editCollabLabels, editCollabActive,
            !editCollabBusy && !editPostBusy, out var removeIndex, out var inviteTapped);
        if (removeIndex >= 0)
        {
            AskRemoveEditCollaborator(post, post.Collaborators![removeIndex]);
        }
        else if (inviteTapped)
        {
            CollabPicker.Open();
        }

        return bottom;
    }

    private void SyncEditCollabLabels(PostDto post)
    {
        if (ReferenceEquals(post.Collaborators, editCollabSource) && ReferenceEquals(editCollabLanguage, Loc.Current))
        {
            return;
        }

        editCollabSource = post.Collaborators;
        editCollabLanguage = Loc.Current;
        var collaborators = post.Collaborators ?? Array.Empty<CollaboratorDto>();
        editCollabLabels = new string[collaborators.Length];
        editCollabActive = new bool[collaborators.Length];
        for (var index = 0; index < collaborators.Length; index++)
        {
            var collaborator = collaborators[index];
            var name = SocialIdentity.Name(collaborator.DisplayName, collaborator.Handle);
            var accepted = collaborator.State == CollabStates.Accepted;
            editCollabLabels[index] = accepted ? name : Loc.T(L.Aethergram.CollabPendingChip, name);
            editCollabActive[index] = accepted;
        }
    }

    private void DrawEditCollabPicker(Rect area, PostDto post)
    {
        if (collabPicker?.Draw(area, theme, images, lodestone) is not { } picked
            || string.Equals(picked.UserId, store.Me?.Id, StringComparison.Ordinal)
            || CollabStates.For(post, picked.UserId) is not null)
        {
            return;
        }

        editCollabBusy = true;
        editPostStatus = string.Empty;
        store.InviteCollab(post.Id, picked.UserId, invited =>
        {
            editCollabBusy = false;
            if (invited is null)
            {
                editCollabFailed = true;
                return;
            }

            editCollabUpdate = invited;
            editCollabInvited = true;
        });
    }

    private void AskRemoveEditCollaborator(PostDto post, CollaboratorDto collaborator)
    {
        var collaboratorId = collaborator.UserId;
        confirm.Ask(new ConfirmRequest
        {
            Title = Loc.T(L.Aethergram.CollabRemove),
            Message = Loc.T(L.Aethergram.CollabRemoveOneMessage),
            ConfirmLabel = Loc.T(L.Aethergram.CollabRemoveConfirm),
            CancelLabel = Loc.T(L.Common.Cancel),
            Sheet = true,
            BusyLabel = Loc.T(L.Aethergram.Saving),
            FailedMessage = Loc.T(L.Aethergram.CollabFailed),
            ConfirmAsync = done => store.RemoveCollab(post.Id, collaboratorId, removed =>
            {
                if (removed)
                {
                    editCollabUpdate = CollabStates.Without(post, collaboratorId);
                    pendingCollabNotice = CollabNotice.Removed;
                }

                done(removed);
            }),
        });
    }

    private float DrawCollabRows(ImDrawListPtr drawList, Rect card, float top, float scale, string label, int count,
        ChipRail rail, ReadOnlySpan<string> labels, ReadOnlySpan<bool> active, bool enabled, out int removeIndex,
        out bool inviteTapped)
    {
        removeIndex = -1;
        var row = new Rect(new Vector2(card.Min.X, top), new Vector2(card.Max.X, top + ComposeRowHeight * scale));
        var remaining = Math.Max(0, CollabStates.MaxCollaborators - count);
        var canInvite = enabled && remaining > 0;
        var hovered = canInvite && UiInteract.Hover(row.Min, row.Max);
        if (hovered)
        {
            drawList.AddRectFilled(row.Min, row.Max, ImGui.GetColorU32(Ink.HoverTint));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        inviteTapped = canInvite && UiInteract.Click(row.Min, row.Max, hovered);
        var trailingRight = row.Max.X - CellPadX * scale;
        if (canInvite)
        {
            var chevronCenter = new Vector2(trailingRight - CollabChevronInset * scale, row.Center.Y);
            PhoneIcon.Draw(drawList, chevronCenter, PhoneIcons.ChevronRight, Ink.MutedInk, CollabChevronSize * scale);
            trailingRight = chevronCenter.X - CollabChevronGap * scale;
        }

        if (count > 0)
        {
            var remainingText = CollabRemainingText(remaining);
            var remainingSize = Typography.Measure(remainingText, TextStyles.Subheadline);
            Typography.Draw(drawList, new Vector2(trailingRight - remainingSize.X, row.Center.Y - remainingSize.Y * 0.5f),
                remainingText, Ink.MutedInk, TextStyles.Subheadline);
            trailingRight -= remainingSize.X + CollabTrailingGap * scale;
        }

        DrawComposeRowLabel(drawList, row, PhoneIcons.Users, label, trailingRight);
        DrawHairline(drawList, ComposeRowLabelLeft(row, scale), card.Max.X - ComposeCardPad * scale, row.Max.Y);
        if (count == 0)
        {
            return row.Max.Y;
        }

        var chipRow = new Rect(new Vector2(card.Min.X + CellPadX * scale, row.Max.Y),
            new Vector2(card.Max.X - CellPadX * scale, row.Max.Y + ComposeRowHeight * scale));
        removeIndex = rail.Draw(chipRow, ui, labels, active, interactive: enabled, trailingGlyph: PhoneIcons.X);
        DrawHairline(drawList, ComposeRowLabelLeft(row, scale), card.Max.X - ComposeCardPad * scale, chipRow.Max.Y);
        return chipRow.Max.Y;
    }

    private string CollabRemainingText(int remaining)
    {
        if (remaining == collabRemainingCount && ReferenceEquals(collabRemainingLanguage, Loc.Current))
        {
            return collabRemainingText;
        }

        collabRemainingCount = remaining;
        collabRemainingLanguage = Loc.Current;
        collabRemainingText = Loc.Plural(L.Aethergram.CollabRemaining, remaining);
        return collabRemainingText;
    }
}
