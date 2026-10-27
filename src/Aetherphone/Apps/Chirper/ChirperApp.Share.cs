using System.Collections.Concurrent;
using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Confirm;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Message;
using Aetherphone.Core.Net;
using Aetherphone.Core.Social;
using Aetherphone.Core.Telephony;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private enum ShareSendState : byte
    {
        Sending,
        Sent,
    }

    private enum StoryShareOutcome
    {
        None,
        Shared,
        Failed,
    }

    private readonly record struct ShareTarget(
        string Id,
        string PeerId,
        string Title,
        string Subtitle,
        string? AvatarUrl,
        string FrameId,
        ConversationDto? Conversation);

    private readonly record struct ShareResult(string TargetId, string PostId, bool Sent);

    private const string MessageAppId = "message";
    private const string AethergramAppId = "aethergram";
    private const float ShareRowHeight = 62f;
    private const float ShareAvatarRadius = 21f;
    private const float ShareSectionPadY = 8f;
    private const float ShareEmptyTop = 60f;
    private const int ShareFilterMaxLength = 64;

    private static readonly TextStyle ShareSectionStyle = TextStyles.FootnoteEmphasized;

    private readonly IChatShareTarget chats;
    private readonly Dictionary<string, ShareSendState> shareStates = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ShareResult> shareResults = new();
    private readonly List<ShareTarget> shareChats = new();
    private readonly List<ShareTarget> shareFriends = new();
    private ConversationDto[] shareConversationSource = Array.Empty<ConversationDto>();
    private ContactDto[] shareFriendSource = Array.Empty<ContactDto>();
    private LanguageInfo? shareLanguage;
    private string shareFilter = string.Empty;
    private string shareFilterApplied = string.Empty;
    private string? storySharePostId;
    private volatile StoryShareOutcome storyShareOutcome;
    private volatile AepFailureBox? storyShareFailure;

    private void OpenShareSheet(PostDto post)
    {
        actions.Reset();
        sheetPost = post;
        sheetKind = SheetKind.Share;
        sheetCount = 0;
        if (navigation.IsAvailable(MessageAppId))
        {
            AddSheetItem(PostSheetAction.SendInMessage, Loc.T(L.Chirper.SendInMessage), false,
                IconGlyph.Of(FontAwesomeIcon.PaperPlane));
        }

        if (CanShareToStory(post) && navigation.IsAvailable(AethergramAppId))
        {
            AddSheetItem(PostSheetAction.AddToStory, Loc.T(L.Chirper.AddToAethergramStory), false,
                IconGlyph.Of(FontAwesomeIcon.PlusCircle));
        }

        AddSheetItem(PostSheetAction.CopyLink, Loc.T(L.Chirper.CopyLink), false, IconGlyph.Of(FontAwesomeIcon.Link));
        AddSheetItem(PostSheetAction.CopyText, Loc.T(L.Chirper.CopyText), false, IconGlyph.Of(FontAwesomeIcon.Copy));
        sheet.Open();
    }

    // The server refuses a veiled, archived or in-review chirp, so the sheet never offers what cannot succeed.
    private static bool CanShareToStory(PostDto post) =>
        !post.Sensitive && post.ArchivedAtUnix is null && !ContentModeration.IsInReview(post.ScanStatus);

    private void DrawShareSheet(Rect screen)
    {
        var picked = sheet.Draw(screen, SheetStyle, sheetItems.AsSpan(0, sheetCount), Loc.T(L.Common.Cancel),
            false, Loc.T(L.Chirper.ShareChirp));
        if (picked < 0 || sheetPost is not { } post)
        {
            return;
        }

        switch (sheetActions[picked])
        {
            case PostSheetAction.SendInMessage:
                OpenShareToChat(post.Id);
                break;
            case PostSheetAction.AddToStory:
                AskShareToStory(post.Id);
                break;
            case PostSheetAction.CopyLink:
                ImGui.SetClipboardText(ChirpShare.Compose(post.Id));
                toast.Show(Loc.T(L.Chirper.LinkCopied));
                break;
            case PostSheetAction.CopyText:
                CopyChirp(post);
                break;
        }
    }

    private void OpenShareToChat(string postId)
    {
        shareStates.Clear();
        shareFilter = string.Empty;
        chats.RefreshShareTargets();
        router.Push(ChirperRoute.ShareToChat(postId));
    }

    private void AskShareToStory(string postId)
    {
        confirm.Ask(new ConfirmRequest
        {
            Title = Loc.T(L.Chirper.StoryShareTitle),
            Message = Loc.T(L.Chirper.StoryShareMessage),
            ConfirmLabel = Loc.T(L.Chirper.StoryShareConfirm),
            CancelLabel = Loc.T(L.Common.Cancel),
            Danger = false,
            Sheet = true,
            Confirm = () => ShareToStory(postId),
        });
    }

    private void ShareToStory(string postId)
    {
        if (store.SharingToStory)
        {
            return;
        }

        storySharePostId = postId;
        store.ShareToStory(postId, (shared, failure) =>
        {
            storyShareFailure = shared ? null : new AepFailureBox(failure);
            storyShareOutcome = shared ? StoryShareOutcome.Shared : StoryShareOutcome.Failed;
        });
    }

    private void ConsumeShareOutcomes()
    {
        while (shareResults.TryDequeue(out var result))
        {
            if (result.Sent)
            {
                shareStates[result.TargetId] = ShareSendState.Sent;
                store.ReportFeedSignal(result.PostId, FeedSignalKinds.Send);
                continue;
            }

            shareStates.Remove(result.TargetId);
            toast.Show(Loc.T(L.Chirper.ShareSendFailed));
        }

        var outcome = storyShareOutcome;
        if (outcome == StoryShareOutcome.None)
        {
            return;
        }

        storyShareOutcome = StoryShareOutcome.None;
        if (outcome == StoryShareOutcome.Shared)
        {
            if (storySharePostId is { } postId)
            {
                store.ReportFeedSignal(postId, FeedSignalKinds.Send);
            }

            toast.Show(Loc.T(L.Story.AddedToStory));
            return;
        }

        toast.Show(StoryShareFailureText(storyShareFailure?.Failure ?? AepFailure.None));
    }

    private static string StoryShareFailureText(AepFailure failure)
    {
        return failure.Code is FailureCodes.StoryPostUnavailable or FailureCodes.StoryLimitReached
            ? FailureText.Resolve(failure)
            : Loc.T(L.Chirper.AddToStoryFailed);
    }

    private void DrawShareToChat(Rect area, string postId)
    {
        DrawScreenHeader(area, Loc.T(L.Chirper.SendTo));
        if (!chats.ConversationsLoaded && !chats.LoadingConversations)
        {
            chats.RefreshShareTargets();
        }

        var scale = UiScale.Current;
        var fieldHeight = SearchBar.HeightUnits * scale;
        var fieldTop = area.Min.Y + AppHeader.Height * scale + UserListSearchPadY * scale;
        var field = new Rect(new Vector2(area.Min.X + 14f * scale, fieldTop),
            new Vector2(area.Max.X - 14f * scale, fieldTop + fieldHeight));
        SearchBar.Draw(field, "##chirperShareSearch", Loc.T(L.Common.Search), ref shareFilter, ui.Ink,
            ShareFilterMaxLength);
        RefreshShareTargets();
        var listRect = new Rect(new Vector2(area.Min.X, field.Max.Y + UserListSearchPadY * scale), area.Max);
        using (AppSurface.BeginEdgeToEdge(listRect))
        {
            if (shareChats.Count == 0 && shareFriends.Count == 0)
            {
                var message = chats.LoadingConversations ? Loc.T(L.Common.Loading) : Loc.T(L.Chirper.ShareNoChats);
                Typography.DrawCentered(new Vector2(listRect.Center.X, listRect.Min.Y + ShareEmptyTop * scale), message,
                    ChirperInk.MutedInk, MetaStyle);
                return;
            }

            DrawShareSection(Loc.T(L.Chirper.ShareRecentChats), shareChats, postId);
            DrawShareSection(Loc.T(L.Chirper.ShareFriends), shareFriends, postId);
            ImGui.Dummy(new Vector2(0f, 24f * scale));
        }
    }

    private void DrawShareSection(string title, List<ShareTarget> targets, string postId)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var scale = UiScale.Current;
        var origin = ImGui.GetCursorScreenPos();
        var labelHeight = Typography.LineHeight(ShareSectionStyle);
        Typography.Draw(ImGui.GetWindowDrawList(), new Vector2(origin.X + CellPadX * scale,
            origin.Y + ShareSectionPadY * scale), title, ChirperInk.MutedInk, ShareSectionStyle);
        ImGui.Dummy(new Vector2(ScrollLayout.StableContentWidth(), labelHeight + ShareSectionPadY * 2f * scale));
        for (var index = 0; index < targets.Count; index++)
        {
            if (SocialUserRow.SkipHidden(UserRowStyle))
            {
                continue;
            }

            DrawShareRow(targets[index], postId);
        }
    }

    private void DrawShareRow(in ShareTarget target, string postId)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var height = ShareRowHeight * scale;
        var padX = CellPadX * scale;
        var radius = ShareAvatarRadius * scale;
        var avatarCenter = new Vector2(origin.X + padX + radius, origin.Y + height * 0.5f);
        if (target.Conversation is { } conversation)
        {
            ConversationAvatar.Draw(drawList, conversation, target.Title, avatarCenter, radius, theme, ui, images,
                lodestone);
        }
        else
        {
            AvatarView.DrawRemote(drawList, avatarCenter, radius, theme, target.Title, string.Empty,
                target.AvatarUrl, images, lodestone, 0.95f, 32, 1f, Frames.Of(target.FrameId));
        }

        var state = ShareStateOf(target);
        var label = Loc.T(state is null ? L.Chirper.ShareSend : L.Chirper.ShareSent);
        var pillWidth = Button.WidthFor(label, ButtonSize.Small);
        var pillHeight = Button.Height(ButtonSize.Small) * scale;
        var pill = new Rect(new Vector2(origin.X + width - padX - pillWidth, avatarCenter.Y - pillHeight * 0.5f),
            new Vector2(origin.X + width - padX, avatarCenter.Y + pillHeight * 0.5f));
        var textLeft = avatarCenter.X + radius + AvatarGap * scale;
        var textWidth = MathF.Max(1f, pill.Min.X - AvatarGap * scale - textLeft);
        var nameHeight = Typography.LineHeight(UserNameStyle);
        var subHeight = target.Subtitle.Length > 0 ? Typography.LineHeight(UserSubStyle) : 0f;
        var textTop = avatarCenter.Y - (nameHeight + subHeight) * 0.5f;
        Typography.Draw(drawList, new Vector2(textLeft, textTop),
            Typography.FitText(target.Title, textWidth, UserNameStyle), ChirperInk.TitleInk, UserNameStyle);
        if (subHeight > 0f)
        {
            Typography.Draw(drawList, new Vector2(textLeft, textTop + nameHeight),
                Typography.FitText(target.Subtitle, textWidth, UserSubStyle), ChirperInk.MutedInk, UserSubStyle);
        }

        bool clicked;
        using (UiScope.PushId(target.Id))
        {
            clicked = Button.Draw(drawList, pill, label, ui.Ink,
                state is null ? ButtonStyle.Prominent : ButtonStyle.Gray, enabled: state is null);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        if (clicked)
        {
            SendShare(target, postId);
        }
    }

    private ShareSendState? ShareStateOf(in ShareTarget target)
    {
        if (shareStates.TryGetValue(target.Id, out var state))
        {
            return state;
        }

        return target.PeerId.Length > 0 && shareStates.TryGetValue(target.PeerId, out var peerState)
            ? peerState
            : null;
    }

    private void SendShare(in ShareTarget target, string postId)
    {
        var targetId = target.Id;
        var token = ChirpShare.Compose(postId);
        shareStates[targetId] = ShareSendState.Sending;
        Action<bool> done = sent => shareResults.Enqueue(new ShareResult(targetId, postId, sent));
        if (target.Conversation is not null)
        {
            chats.SendShare(targetId, token, done);
            return;
        }

        chats.SendShareToUser(targetId, token, done);
    }

    private void RefreshShareTargets()
    {
        var conversations = chats.Conversations;
        var friends = chats.Friends;
        var filter = shareFilter.Trim();
        if (ReferenceEquals(conversations, shareConversationSource) && ReferenceEquals(friends, shareFriendSource)
            && ReferenceEquals(Loc.Current, shareLanguage)
            && string.Equals(filter, shareFilterApplied, StringComparison.Ordinal))
        {
            return;
        }

        shareConversationSource = conversations;
        shareFriendSource = friends;
        shareLanguage = Loc.Current;
        shareFilterApplied = filter;
        shareChats.Clear();
        shareFriends.Clear();
        var directPeers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < conversations.Length; index++)
        {
            var conversation = conversations[index];
            if (!conversation.IsGroup)
            {
                directPeers.Add(conversation.OtherUserId);
            }

            var title = chats.DisplayTitle(conversation);
            if (!ShareMatches(filter, title, conversation.OtherHandle))
            {
                continue;
            }

            var subtitle = conversation.IsGroup
                ? Loc.T(L.DirectMessages.MembersCount, conversation.MemberCount)
                : HandleSubtitle(conversation.OtherHandle);
            shareChats.Add(new ShareTarget(conversation.Id, conversation.IsGroup ? string.Empty : conversation.OtherUserId,
                title, subtitle, conversation.IsGroup ? conversation.AvatarUrl : conversation.OtherAvatarUrl,
                conversation.FrameId, conversation));
        }

        for (var index = 0; index < friends.Length; index++)
        {
            var friend = friends[index];
            var title = ContactBook.DisplayLabel(friend);
            if (directPeers.Contains(friend.UserId) || !ShareMatches(filter, title, friend.Handle))
            {
                continue;
            }

            shareFriends.Add(new ShareTarget(friend.UserId, friend.UserId, title, HandleSubtitle(friend.Handle),
                friend.AvatarUrl, friend.FrameId, null));
        }
    }

    private static bool ShareMatches(string filter, string title, string handle) =>
        filter.Length == 0 || title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                           || handle.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string HandleSubtitle(string handle) => handle.Length > 0 ? "@" + handle : string.Empty;
}
