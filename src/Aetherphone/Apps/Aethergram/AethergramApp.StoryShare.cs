using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Media;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const float AddToStoryRowHeight = 60f;
    private const float AddToStoryIconRadius = 21f;
    private const float AddToStoryIconSize = 18f;
    private const float AddToStoryLabelGap = 12f;
    private const float StoryShareSheetHeight = 456f;
    private const float StoryShareTitleTop = 16f;
    private const float StorySharePreviewTop = 40f;
    private const float StorySharePreviewHeight = 240f;
    private const float StoryShareButtonGap = 16f;
    private const float StoryShareButtonHeight = 40f;
    private const float StoryShareButtonSpacing = 8f;
    private const int OutcomeNone = 0;
    private const int OutcomeShared = 1;
    private const int OutcomeFailed = 2;

    private const ImGuiWindowFlags StoryShareSheetFlags = ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground;

    private readonly Sheet storyShareSheet = new();
    private PostDto? storyShareCandidate;
    private string? storySharePhotoUrl;
    private volatile int storyShareOutcome;

    private bool StoryShareCaptures => storyShareSheet.CapturesPointer;

    private void OpenShare(PostDto post)
    {
        var photos = PostMedia.Photos(post.MediaUrls, post.MediaUrl);
        var shareable = !post.Sensitive && post.ArchivedAtUnix is null && !ContentModeration.IsInReview(post.ScanStatus)
            && photos.Length > 0;
        storyShareCandidate = shareable ? post : null;
        storySharePhotoUrl = shareable ? photos[0] : null;
        OpenShare(post.Id);
    }

    private void DrawAddToStoryRow(string postId)
    {
        if (storyShareCandidate is not { } post || post.Id != postId)
        {
            return;
        }

        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var cell = FeedCell.Begin(drawList, AddToStoryRowHeight * scale, Ink.HoverTint);
        var radius = AddToStoryIconRadius * scale;
        var iconCenter = new Vector2(cell.Bounds.Min.X + CellPadX * scale + radius, cell.Bounds.Center.Y);
        AethergramArt.StoryRing(drawList, iconCenter, radius + 2f * scale, scale, true);
        drawList.AddCircleFilled(iconCenter, radius - 1f * scale, ImGui.GetColorU32(Ink.FieldFill), 32);
        PhoneIcon.Draw(drawList, iconCenter, PhoneIcons.Plus, Ink.TitleInk, AddToStoryIconSize * scale);
        var labelLeft = iconCenter.X + radius + AddToStoryLabelGap * scale;
        var label = Typography.FitText(Loc.T(L.Story.AddPostToStory),
            MathF.Max(1f, cell.Bounds.Max.X - CellPadX * scale - labelLeft), TextStyles.SubheadlineEmphasized);
        var labelSize = Typography.Measure(label, TextStyles.SubheadlineEmphasized);
        Typography.Draw(drawList, new Vector2(labelLeft, cell.Bounds.Center.Y - labelSize.Y * 0.5f), label,
            Ink.TitleInk, TextStyles.SubheadlineEmphasized);
        FeedCell.End(drawList, cell, Ink.Hairline);
        if (cell.Tapped)
        {
            storyShareOutcome = OutcomeNone;
            storyShareSheet.Open();
        }
    }

    private void DrawStoryShareSheet(Rect screen)
    {
        ConsumeStoryShareOutcome();
        if (!storyShareSheet.CapturesPointer)
        {
            return;
        }

        if (storyShareSheet.IsOpen && router.Current.Screen != AethergramScreen.Share)
        {
            storyShareSheet.Close();
        }

        ImGui.SetCursorScreenPos(screen.Min);
        using (ImRaii.Child("##aethergramStoryShare", screen.Size, false, StoryShareSheetFlags))
        {
            var frame = storyShareSheet.Begin(ImGui.GetWindowDrawList(), screen, theme,
                SheetDetents.Fitted(StoryShareSheetHeight * UiScale.Current), SheetMetrics.VeilFor(true));
            if (!frame.Visible)
            {
                return;
            }

            DrawStoryShareContent(in frame);
            storyShareSheet.End(in frame);
        }
    }

    private void DrawStoryShareContent(in SheetFrame frame)
    {
        if (storyShareCandidate is not { } post)
        {
            storyShareSheet.Close();
            return;
        }

        var scale = UiScale.Current;
        var content = frame.Content;
        var ink = frame.Ink with { W = frame.Ink.W * frame.Opacity };
        Typography.DrawCentered(frame.DrawList, new Vector2(content.Center.X, content.Min.Y + StoryShareTitleTop * scale),
            Loc.T(L.Story.AddPostToStory), ink, TextStyles.Headline);
        var previewHeight = StorySharePreviewHeight * scale;
        var previewWidth = previewHeight * StoryStore.StoryWidth / StoryStore.StoryHeight;
        var previewTop = content.Min.Y + StorySharePreviewTop * scale;
        var preview = new Rect(new Vector2(content.Center.X - previewWidth * 0.5f, previewTop),
            new Vector2(content.Center.X + previewWidth * 0.5f, previewTop + previewHeight));
        var card = new SharedPostCardContent(SocialIdentity.Name(post.AuthorDisplayName, post.AuthorHandle),
            post.AuthorAvatarUrl, storySharePhotoUrl, post.MediaWidth, post.MediaHeight, true);
        SharedPostCard.Draw(frame.DrawList, preview, theme, card, images, lodestone, scale, false);

        var inset = CellPadX * scale;
        var buttonHeight = StoryShareButtonHeight * scale;
        var shareTop = preview.Max.Y + StoryShareButtonGap * scale;
        var shareRect = new Rect(new Vector2(content.Min.X + inset, shareTop),
            new Vector2(content.Max.X - inset, shareTop + buttonHeight));
        var cancelTop = shareRect.Max.Y + StoryShareButtonSpacing * scale;
        var cancelRect = new Rect(new Vector2(content.Min.X + inset, cancelTop),
            new Vector2(content.Max.X - inset, cancelTop + buttonHeight));
        var posting = stories.Posting;
        if (DrawAccentPill(shareRect, Loc.T(posting ? L.Aethergram.Saving : L.Share.Action),
                frame.Interactive && !posting))
        {
            stories.ShareGram(post.Id, shared => storyShareOutcome = shared ? OutcomeShared : OutcomeFailed);
        }

        if (DrawGrayPill(cancelRect, Loc.T(L.Common.Cancel)) && frame.Interactive)
        {
            storyShareSheet.Close();
        }
    }

    private void ConsumeStoryShareOutcome()
    {
        var outcome = storyShareOutcome;
        if (outcome == OutcomeNone)
        {
            return;
        }

        storyShareOutcome = OutcomeNone;
        if (outcome == OutcomeShared)
        {
            storyShareSheet.Close();
            toast.Show(Loc.T(L.Story.AddedToStory));
            return;
        }

        toast.Show(Loc.T(L.Story.AddToStoryFailed));
    }
}
