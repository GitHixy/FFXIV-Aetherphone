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
    private enum CollabNotice : byte
    {
        None,
        Accepted,
        Declined,
        Removed,
        Left,
        Failed,
    }

    private const float CollabAvatarFraction = 0.72f;
    private const float CollabTrioFraction = 0.6f;
    private const float CollabTrioRise = 0.6f;
    private const float CollabTrioDrop = 0.9f;
    private const int CollabAvatarsShown = 3;
    private const float CollabAvatarCutout = 1.5f;
    private const int CollabAvatarSegments = 28;
    private const float CollabAuthorShare = 0.5f;
    private const float CollabNameGap = 4f;
    private const float CollabPillGap = 8f;

    private readonly ActionSheet collabAuthorsSheet = new();
    private readonly string[] collabOthersText = new string[CollabStates.MaxCollaborators + 1];
    private LanguageInfo? collabOthersLanguage;
    private ActionSheet.Item[] collabAuthorItems = Array.Empty<ActionSheet.Item>();
    private string[] collabAuthorIds = Array.Empty<string>();
    private string collabAuthorsPostId = string.Empty;
    private volatile string? collabBusyPostId;
    private volatile HashSet<string> settledCollabInvites = new(StringComparer.Ordinal);
    private volatile CollabNotice pendingCollabNotice;

    private static float CollabAvatarRadius(float radius, int accepted) =>
        radius * (accepted == 1 ? CollabAvatarFraction : CollabTrioFraction);

    // Slot 0 is the author, then the first collaborators in invite order. One collaborator sits on a
    // diagonal; more make a triangle inside the same circle, so the header keeps its width.
    private static Vector2 CollabAvatarCenter(Vector2 center, float radius, int accepted, int slot)
    {
        var reach = radius - CollabAvatarRadius(radius, accepted);
        if (accepted == 1)
        {
            return slot == 0 ? center - new Vector2(reach, reach) : center + new Vector2(reach, reach);
        }

        return slot switch
        {
            0 => center + new Vector2(-reach, -reach * CollabTrioRise),
            1 => center + new Vector2(reach, -reach * CollabTrioRise),
            _ => center + new Vector2(0f, reach * CollabTrioDrop),
        };
    }

    private static int CollabAvatarSlots(int accepted) => Math.Min(accepted + 1, CollabAvatarsShown);

    private void DrawCardAvatar(PostDto post, Vector2 center, float radius, string displayName)
    {
        var accepted = CollabStates.AcceptedCount(post);
        if (accepted == 0)
        {
            DrawAvatar(center, radius, displayName, string.Empty, post.AuthorAvatarUrl, 0.85f, 32,
                Frames.Of(post.AuthorFrameId));
            return;
        }

        var small = CollabAvatarRadius(radius, accepted);
        DrawAvatar(CollabAvatarCenter(center, radius, accepted, 0), small, displayName, string.Empty,
            post.AuthorAvatarUrl, 0.85f, CollabAvatarSegments);
        var slots = CollabAvatarSlots(accepted);
        for (var slot = 1; slot < slots; slot++)
        {
            var collaborator = CollabStates.AcceptedAt(post, slot - 1)!;
            var collaboratorCenter = CollabAvatarCenter(center, radius, accepted, slot);
            ImGui.GetWindowDrawList().AddCircleFilled(collaboratorCenter,
                small + CollabAvatarCutout * UiScale.Current, ImGui.GetColorU32(ActivityBadgeRing),
                CollabAvatarSegments);
            DrawAvatar(collaboratorCenter, small, SocialIdentity.Name(collaborator.DisplayName, collaborator.Handle),
                string.Empty, collaborator.AvatarUrl, 0.85f, CollabAvatarSegments);
        }
    }

    private static float CardAuthorNameWidth(PostDto post, float headerTextMaxWidth) =>
        CollabStates.AcceptedCount(post) == 0 ? headerTextMaxWidth : headerTextMaxWidth * CollabAuthorShare;

    // Drawn after the header's own tap target so a tap on a collaborator's name or face claims the
    // press, and the rest of the header still opens the author. One collaborator reads "A and B" and
    // opens B; more read "A and N others" and open the list of everyone on the post.
    private void DrawCollabByline(ImDrawListPtr drawList, PostDto post, float left, float top, float right,
        Vector2 avatarCenter, float avatarRadius)
    {
        var accepted = CollabStates.AcceptedCount(post);
        if (accepted == 0)
        {
            return;
        }

        var scale = UiScale.Current;
        var gap = CollabNameGap * scale;
        Vector2 nameMin;
        Vector2 nameMax;
        if (accepted == 1)
        {
            var joiner = Loc.T(L.Aethergram.CollabAnd);
            var joinerLeft = left + gap;
            Typography.Draw(drawList, new Vector2(joinerLeft, top), joiner, Ink.BodyInk, CardNameStyle);
            var collaborator = CollabStates.AcceptedAt(post, 0)!;
            var nameLeft = joinerLeft + Typography.Measure(joiner, CardNameStyle).X + gap;
            var name = Typography.FitText(SocialIdentity.Name(collaborator.DisplayName, collaborator.Handle),
                MathF.Max(1f, right - nameLeft), CardNameStyle);
            nameMin = new Vector2(nameLeft, top);
            nameMax = nameMin + Typography.Measure(name, CardNameStyle);
            Typography.Draw(drawList, nameMin, name, Ink.TitleInk, CardNameStyle);
        }
        else
        {
            var others = Typography.FitText(CollabOthersText(accepted), MathF.Max(1f, right - left - gap),
                CardNameStyle);
            nameMin = new Vector2(left + gap, top);
            nameMax = nameMin + Typography.Measure(others, CardNameStyle);
            Typography.Draw(drawList, nameMin, others, Ink.TitleInk, CardNameStyle);
        }

        if (UiInteract.Hover(nameMin, nameMax))
        {
            drawList.AddLine(new Vector2(nameMin.X, nameMax.Y - 1f * scale),
                new Vector2(nameMax.X, nameMax.Y - 1f * scale), ImGui.GetColorU32(Ink.TitleInk), 1f);
        }

        var tapped = UiInteract.HoverClick(nameMin, nameMax);
        var small = CollabAvatarRadius(avatarRadius, accepted);
        var slots = CollabAvatarSlots(accepted);
        for (var slot = 1; slot < slots && !tapped; slot++)
        {
            tapped = UiInteract.HoverClickCircle(CollabAvatarCenter(avatarCenter, avatarRadius, accepted, slot), small);
        }

        if (!tapped)
        {
            return;
        }

        if (accepted == 1)
        {
            OpenProfileFromPost(CollabStates.AcceptedAt(post, 0)!.UserId, post.Id);
            return;
        }

        OpenCollabAuthors(post);
    }

    private string CollabOthersText(int count)
    {
        if (!ReferenceEquals(collabOthersLanguage, Loc.Current))
        {
            Array.Clear(collabOthersText);
            collabOthersLanguage = Loc.Current;
        }

        if (count >= collabOthersText.Length)
        {
            return Loc.Plural(L.Aethergram.CollabOthers, count);
        }

        return collabOthersText[count] ??= Loc.Plural(L.Aethergram.CollabOthers, count);
    }

    private void OpenCollabAuthors(PostDto post)
    {
        var accepted = CollabStates.AcceptedCount(post);
        collabAuthorItems = new ActionSheet.Item[accepted + 1];
        collabAuthorIds = new string[accepted + 1];
        collabAuthorsPostId = post.Id;
        collabAuthorIds[0] = post.AuthorId;
        collabAuthorItems[0] = new ActionSheet.Item(SocialIdentity.Name(post.AuthorDisplayName, post.AuthorHandle));
        for (var index = 0; index < accepted; index++)
        {
            var collaborator = CollabStates.AcceptedAt(post, index)!;
            collabAuthorIds[index + 1] = collaborator.UserId;
            collabAuthorItems[index + 1] =
                new ActionSheet.Item(SocialIdentity.Name(collaborator.DisplayName, collaborator.Handle));
        }

        collabAuthorsSheet.Open();
    }

    private void DrawCollabAuthorsSheet(Rect screen)
    {
        if (!collabAuthorsSheet.CapturesPointer)
        {
            return;
        }

        var picked = collabAuthorsSheet.Draw(screen, ActionSheetStyle.From(ui), collabAuthorItems,
            Loc.T(L.Common.Cancel), false, Loc.T(L.Aethergram.CollabAuthors));
        if (picked < 0)
        {
            return;
        }

        OpenProfileFromPost(collabAuthorIds[picked], collabAuthorsPostId);
    }

    // With several collaborators the author manages them from Edit post; the menu keeps the one-tap
    // removal only for the single-collaborator case it always had.
    private void AddCollabSheetItem(PostDto post)
    {
        if (IsOwnPost(post))
        {
            if (CollabStates.Count(post) == 1)
            {
                AddPostSheetItem(PostSheetAction.RemoveCollab, Loc.T(L.Aethergram.CollabRemove), true);
            }

            return;
        }

        if (CollabStates.IsCollaborator(post, store.Me?.Id))
        {
            AddPostSheetItem(PostSheetAction.RemoveCollab, Loc.T(L.Aethergram.CollabLeave), true);
        }
    }

    private void AskRemoveCollab(PostDto post)
    {
        var own = IsOwnPost(post);
        var targetId = own ? post.Collaborators![0].UserId : store.Me!.Id;
        confirm.Ask(new ConfirmRequest
        {
            Title = Loc.T(own ? L.Aethergram.CollabRemove : L.Aethergram.CollabLeave),
            Message = Loc.T(own ? L.Aethergram.CollabRemoveOneMessage : L.Aethergram.CollabLeaveMessage),
            ConfirmLabel = Loc.T(L.Aethergram.CollabRemoveConfirm),
            CancelLabel = Loc.T(L.Common.Cancel),
            Sheet = true,
            BusyLabel = Loc.T(L.Aethergram.Saving),
            FailedMessage = Loc.T(L.Aethergram.CollabFailed),
            ConfirmAsync = done => store.RemoveCollab(post.Id, targetId, removed =>
            {
                if (removed)
                {
                    SettleCollabInvite(post.Id);
                    pendingCollabNotice = own ? CollabNotice.Removed : CollabNotice.Left;
                }

                done(removed);
            }),
        });
    }

    private string? CollabRefusalText()
    {
        var failure = store.CreateFailure;
        if (composeStoryMode || failure.Code is not (FailureCodes.GramInvalidCollaborator
                or FailureCodes.GramTooManyCollaborators))
        {
            return null;
        }

        return FailureText.Resolve(failure);
    }

    private bool ShowsCollabInvitePills(NotificationDto item) =>
        item.Type == SocialActivity.TypeCollabInvite
        && item.PostId is { } postId
        && !settledCollabInvites.Contains(postId);

    private float CollabInvitePillsHeight(NotificationDto item) =>
        ShowsCollabInvitePills(item) ? (CollabPillGap + TagPromptPillHeight) * UiScale.Current : 0f;

    private void DrawCollabInvitePills(NotificationDto item, float left, float bottom)
    {
        if (!ShowsCollabInvitePills(item))
        {
            return;
        }

        var scale = UiScale.Current;
        var pillWidth = RequestPillWidth * scale;
        var top = bottom - TagPromptPillHeight * scale;
        var acceptRect = new Rect(new Vector2(left, top), new Vector2(left + pillWidth, bottom));
        var declineLeft = acceptRect.Max.X + RequestPillGap * scale;
        var declineRect = new Rect(new Vector2(declineLeft, top), new Vector2(declineLeft + pillWidth, bottom));
        DrawCollabDecision(item.PostId!, item.Id, acceptRect, declineRect);
    }

    private void DrawCollabInvitePrompt(PostDto post)
    {
        if (!CollabStates.InvitePendingFor(post, store.Me?.Id) || settledCollabInvites.Contains(post.Id))
        {
            return;
        }

        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var inset = CellPadX * scale;
        var innerWidth = width - inset * 2f;
        var pad = TagPromptPad * scale;
        var iconSize = TagPromptIconSize * scale;
        var textWidth = TagPromptTextWidth(innerWidth, scale);
        var hint = Loc.T(L.Aethergram.CollabPromptHint);
        var hintHeight = Typography.MeasureWrapped(hint, textWidth, CardMetaStyle.Scale, CardMetaStyle.Weight);
        var titleHeight = Typography.LineHeight(CardNameStyle);
        var pillHeight = TagPromptPillHeight * scale;
        var panelHeight = pad * 2f + titleHeight + TagPromptTitleGap * scale + hintHeight + TagPromptRowGap * scale
            + pillHeight;
        var panelTop = origin.Y + CardPadTop * scale;
        var panel = new Rect(new Vector2(origin.X + inset, panelTop),
            new Vector2(origin.X + inset + innerWidth, panelTop + panelHeight));
        ui.Card(drawList, panel.Min, panel.Max, Metrics.Radius.Grouped * scale);
        var titleTop = panel.Min.Y + pad;
        PhoneIcon.Draw(drawList, new Vector2(panel.Min.X + pad + iconSize * 0.5f, titleTop + titleHeight * 0.5f),
            PhoneIcons.Users, Ink.AccentLink, iconSize);
        var textLeft = panel.Min.X + pad + iconSize + TagPromptIconGap * scale;
        Typography.Draw(drawList, new Vector2(textLeft, titleTop),
            Typography.FitText(Loc.T(L.Aethergram.CollabPromptTitle), textWidth, CardNameStyle), Ink.TitleInk,
            CardNameStyle);
        ImGui.SetCursorScreenPos(new Vector2(textLeft, titleTop + titleHeight + TagPromptTitleGap * scale));
        using (Typography.WrapAt(textLeft + textWidth))
        using (UiScope.PushColor(ImGuiCol.Text, Ink.MutedInk))
        using (Plugin.Fonts.Push(CardMetaStyle.Scale, CardMetaStyle.Weight))
        {
            Typography.Wrapped(hint);
        }

        var pillWidth = TagPromptPillWidth * scale;
        var pillTop = panel.Max.Y - pad - pillHeight;
        var acceptRect = new Rect(new Vector2(textLeft, pillTop), new Vector2(textLeft + pillWidth, pillTop + pillHeight));
        var declineLeft = acceptRect.Max.X + TagPromptPillGap * scale;
        var declineRect = new Rect(new Vector2(declineLeft, pillTop),
            new Vector2(declineLeft + pillWidth, pillTop + pillHeight));
        DrawCollabDecision(post.Id, post.Id, acceptRect, declineRect);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, panel.Max.Y - origin.Y));
    }

    private void DrawCollabDecision(string postId, string scopeId, Rect acceptRect, Rect declineRect)
    {
        var busy = string.Equals(collabBusyPostId, postId, StringComparison.Ordinal);
        using (UiScope.PushId(scopeId))
        {
            if (DrawAccentPill(acceptRect, Loc.T(L.Aethergram.CollabAccept), !busy))
            {
                DecideCollab(postId, true);
            }

            if (DrawGrayPill(declineRect, Loc.T(L.Aethergram.CollabDecline)) && !busy)
            {
                DecideCollab(postId, false);
            }
        }
    }

    private void DecideCollab(string postId, bool accept)
    {
        collabBusyPostId = postId;
        void Done(bool succeeded)
        {
            collabBusyPostId = null;
            if (succeeded)
            {
                SettleCollabInvite(postId);
            }

            pendingCollabNotice = !succeeded ? CollabNotice.Failed
                : accept ? CollabNotice.Accepted
                : CollabNotice.Declined;
        }

        if (accept)
        {
            store.AcceptCollab(postId, Done);
            return;
        }

        store.DeclineCollab(postId, Done);
    }

    private void SettleCollabInvite(string postId)
    {
        settledCollabInvites = new HashSet<string>(settledCollabInvites, StringComparer.Ordinal) { postId };
    }

    private void DrainCollabNotice()
    {
        var notice = pendingCollabNotice;
        if (notice == CollabNotice.None)
        {
            return;
        }

        pendingCollabNotice = CollabNotice.None;
        switch (notice)
        {
            case CollabNotice.Accepted:
                toast.Show(Loc.T(L.Aethergram.CollabAcceptedToast));
                activityFeed.Invalidate();
                break;
            case CollabNotice.Declined:
                toast.Show(Loc.T(L.Aethergram.CollabDeclinedToast));
                activityFeed.Invalidate();
                break;
            case CollabNotice.Removed:
                toast.Show(Loc.T(L.Aethergram.CollabRemovedToast));
                break;
            case CollabNotice.Left:
                toast.Show(Loc.T(L.Aethergram.CollabLeftToast));
                break;
            case CollabNotice.Failed:
                confirm.Alert(null, Loc.T(L.Aethergram.CollabFailed), Loc.T(L.Common.Close));
                break;
        }
    }
}
