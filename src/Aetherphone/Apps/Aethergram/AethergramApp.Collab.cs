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
    private const float CollabAvatarCutout = 1.5f;
    private const int CollabAvatarSegments = 28;
    private const float CollabAuthorShare = 0.5f;
    private const float CollabNameGap = 4f;
    private const float CollabPillGap = 8f;
    private const float CollabClearSize = 16f;
    private const float CollabClearHit = 14f;
    private const float CollabTrailingShare = 0.45f;

    private MentionSuggestDto? composeCollaborator;
    private PersonPicker? collabPicker;
    private volatile string? collabBusyPostId;
    private volatile HashSet<string> settledCollabInvites = new(StringComparer.Ordinal);
    private volatile CollabNotice pendingCollabNotice;

    private PersonPicker CollabPicker =>
        collabPicker ??= new PersonPicker(store.NewMentionSuggestions(), L.Aethergram.CollabInvite);

    private string? ComposeCollaboratorId => composeCollaborator?.UserId;

    private void ResetComposeCollab()
    {
        composeCollaborator = null;
        collabPicker?.Close();
    }

    private void GateComposeCollabPicker()
    {
        collabPicker?.Gate();
    }

    private void DrawComposeCollabPicker(Rect area)
    {
        if (collabPicker is null)
        {
            return;
        }

        var picked = collabPicker.Draw(area, theme, images, lodestone);
        if (picked is null || string.Equals(picked.UserId, store.Me?.Id, StringComparison.Ordinal))
        {
            return;
        }

        composeCollaborator = picked;
    }

    private float DrawComposeCollabRow(ImDrawListPtr drawList, Rect card, float top, float scale)
    {
        var row = new Rect(new Vector2(card.Min.X, top), new Vector2(card.Max.X, top + ComposeRowHeight * scale));
        var hovered = UiInteract.Hover(row.Min, row.Max);
        if (hovered)
        {
            drawList.AddRectFilled(row.Min, row.Max, ImGui.GetColorU32(Ink.HoverTint));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var rowTapped = UiInteract.Click(row.Min, row.Max, hovered);
        var trailingRight = row.Max.X - CellPadX * scale;
        var picked = composeCollaborator;
        var clearTapped = false;
        if (picked is null)
        {
            var chevronCenter = new Vector2(trailingRight - 8f * scale, row.Center.Y);
            PhoneIcon.Draw(drawList, chevronCenter, PhoneIcons.ChevronRight, Ink.MutedInk, 18f * scale);
            trailingRight = chevronCenter.X - 14f * scale;
        }
        else
        {
            var hit = CollabClearHit * scale;
            var clearCenter = new Vector2(trailingRight - hit * 0.5f, row.Center.Y);
            var clearRect = new Rect(clearCenter - new Vector2(hit, hit), clearCenter + new Vector2(hit, hit));
            PhoneIcon.Draw(drawList, clearCenter, PhoneIcons.X, Ink.MutedInk, CollabClearSize * scale);
            HoverTooltip.Show(clearRect, Loc.T(L.Aethergram.CollabClear), HoverLabelSide.Above);
            clearTapped = UiInteract.HoverClick(clearRect.Min, clearRect.Max);
            var nameRight = clearRect.Min.X - 4f * scale;
            var name = Typography.FitText(SocialIdentity.Name(picked.DisplayName, picked.Handle),
                MathF.Max(1f, row.Width * CollabTrailingShare), TextStyles.Subheadline);
            var nameSize = Typography.Measure(name, TextStyles.Subheadline);
            Typography.Draw(drawList, new Vector2(nameRight - nameSize.X, row.Center.Y - nameSize.Y * 0.5f), name,
                Ink.MutedInk, TextStyles.Subheadline);
            trailingRight = nameRight - nameSize.X - 10f * scale;
        }

        DrawComposeRowLabel(drawList, row, PhoneIcons.Users, Loc.T(L.Aethergram.CollabInvite), trailingRight);
        DrawHairline(drawList, ComposeRowLabelLeft(row, scale), card.Max.X - ComposeCardPad * scale, row.Max.Y);
        if (clearTapped)
        {
            composeCollaborator = null;
        }
        else if (rowTapped)
        {
            CollabPicker.Open();
        }

        return row.Max.Y;
    }

    private void DrawCardAvatar(PostDto post, Vector2 center, float radius, string displayName)
    {
        if (CollabStates.AcceptedFor(post) is not { } collaborator)
        {
            DrawAvatar(center, radius, displayName, string.Empty, post.AuthorAvatarUrl, 0.85f, 32,
                Frames.Of(post.AuthorFrameId));
            return;
        }

        var small = radius * CollabAvatarFraction;
        var offset = new Vector2(radius - small, radius - small);
        var collaboratorCenter = center + offset;
        DrawAvatar(center - offset, small, displayName, string.Empty, post.AuthorAvatarUrl, 0.85f,
            CollabAvatarSegments);
        ImGui.GetWindowDrawList().AddCircleFilled(collaboratorCenter, small + CollabAvatarCutout * UiScale.Current,
            ImGui.GetColorU32(ActivityBadgeRing), CollabAvatarSegments);
        DrawAvatar(collaboratorCenter, small, SocialIdentity.Name(collaborator.DisplayName, collaborator.Handle),
            string.Empty, collaborator.AvatarUrl, 0.85f, CollabAvatarSegments);
    }

    private static float CardAuthorNameWidth(PostDto post, float headerTextMaxWidth) =>
        CollabStates.AcceptedFor(post) is null ? headerTextMaxWidth : headerTextMaxWidth * CollabAuthorShare;

    // Drawn after the header's own tap target so a tap on the collaborator's name or face claims the
    // press, and the rest of the header still opens the author.
    private void DrawCollabByline(ImDrawListPtr drawList, PostDto post, float left, float top, float right,
        Vector2 avatarCenter, float avatarRadius)
    {
        if (CollabStates.AcceptedFor(post) is not { } collaborator)
        {
            return;
        }

        var scale = UiScale.Current;
        var gap = CollabNameGap * scale;
        var joiner = Loc.T(L.Aethergram.CollabAnd);
        var joinerLeft = left + gap;
        Typography.Draw(drawList, new Vector2(joinerLeft, top), joiner, Ink.BodyInk, CardNameStyle);
        var nameLeft = joinerLeft + Typography.Measure(joiner, CardNameStyle).X + gap;
        var name = Typography.FitText(SocialIdentity.Name(collaborator.DisplayName, collaborator.Handle),
            MathF.Max(1f, right - nameLeft), CardNameStyle);
        var nameSize = Typography.Measure(name, CardNameStyle);
        Typography.Draw(drawList, new Vector2(nameLeft, top), name, Ink.TitleInk, CardNameStyle);
        var nameMin = new Vector2(nameLeft, top);
        var nameMax = new Vector2(nameLeft + nameSize.X, top + nameSize.Y);
        if (UiInteract.Hover(nameMin, nameMax))
        {
            drawList.AddLine(new Vector2(nameMin.X, nameMax.Y - 1f * scale),
                new Vector2(nameMax.X, nameMax.Y - 1f * scale), ImGui.GetColorU32(Ink.TitleInk), 1f);
        }

        var small = avatarRadius * CollabAvatarFraction;
        var collaboratorCenter = avatarCenter + new Vector2(avatarRadius - small, avatarRadius - small);
        var nameTapped = UiInteract.HoverClick(nameMin, nameMax);
        var avatarTapped = UiInteract.HoverClickCircle(collaboratorCenter, small);
        if (nameTapped || avatarTapped)
        {
            OpenProfileFromPost(collaborator.UserId, post.Id);
        }
    }

    private void AddCollabSheetItem(PostDto post)
    {
        if (post.Collaborator is null)
        {
            return;
        }

        if (IsOwnPost(post))
        {
            AddPostSheetItem(PostSheetAction.RemoveCollab, Loc.T(L.Aethergram.CollabRemove), true);
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
        confirm.Ask(new ConfirmRequest
        {
            Title = Loc.T(own ? L.Aethergram.CollabRemove : L.Aethergram.CollabLeave),
            Message = Loc.T(own ? L.Aethergram.CollabRemoveMessage : L.Aethergram.CollabLeaveMessage),
            ConfirmLabel = Loc.T(L.Aethergram.CollabRemoveConfirm),
            CancelLabel = Loc.T(L.Common.Cancel),
            Sheet = true,
            BusyLabel = Loc.T(L.Aethergram.Saving),
            FailedMessage = Loc.T(L.Aethergram.CollabFailed),
            ConfirmAsync = done => store.RemoveCollab(post.Id, post.Collaborator!.UserId, removed =>
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
        if (composeStoryMode || failure.Code != FailureCodes.GramInvalidCollaborator)
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
