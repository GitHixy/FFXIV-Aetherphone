using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Media;
using Aetherphone.Core.Net;
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
    private const float StoryShareSheetHeight = 512f;
    private const float StoryShareTitleTop = 16f;
    private const float StorySharePreviewTop = 40f;
    private const float StorySharePreviewHeight = 240f;
    private const float StoryShareButtonGap = 16f;
    private const float StoryShareButtonHeight = 40f;
    private const float StoryShareButtonSpacing = 8f;
    private const float StoryShareCaptionInset = 16f;
    private const int StoryShareCaptionMaxLength = 200;
    private const int OutcomeNone = 0;
    private const int OutcomeShared = 1;
    private const int OutcomeFailed = 2;

    private const ImGuiWindowFlags StoryShareSheetFlags = ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground;

    private readonly Sheet storyShareSheet = new();
    private PostDto? storyShareCandidate;
    private string? storySharePhotoUrl;
    private string storyShareCaption = string.Empty;
    private AethergramRoute storyShareRoute;
    private volatile int storyShareOutcome;
    private volatile AepFailureBox? storyShareFailure;

    private bool StoryShareCaptures => storyShareSheet.CapturesPointer;

    private void OpenShare(PostDto post)
    {
        storySharePhotoUrl = StorySharePhoto(post);
        storyShareCandidate = storySharePhotoUrl is null ? null : post;
        OpenShare(post.Id);
    }

    private static string? StorySharePhoto(PostDto post)
    {
        if (post.Sensitive || post.ArchivedAtUnix is not null || ContentModeration.IsInReview(post.ScanStatus))
        {
            return null;
        }

        var photos = PostMedia.Photos(post.MediaUrls, post.MediaUrl);
        return photos.Length > 0 ? photos[0] : null;
    }

    private void OpenStoryShareSheet(PostDto post, string photoUrl)
    {
        storyShareCandidate = post;
        storySharePhotoUrl = photoUrl;
        storyShareCaption = string.Empty;
        storyShareOutcome = OutcomeNone;
        storyShareRoute = router.Current;
        storyShareSheet.Open();
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
        if (cell.Tapped && storySharePhotoUrl is { } photoUrl)
        {
            OpenStoryShareSheet(post, photoUrl);
        }
    }

    private void DrawStoryShareSheet(Rect screen)
    {
        ConsumeStoryShareOutcome();
        if (!storyShareSheet.CapturesPointer)
        {
            return;
        }

        if (storyShareSheet.IsOpen && router.Current != storyShareRoute)
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
        var captionTop = preview.Max.Y + StoryShareButtonGap * scale;
        var captionRect = new Rect(new Vector2(content.Min.X + inset, captionTop),
            new Vector2(content.Max.X - inset, captionTop + GlassField.HeightUnits * scale));
        Squircle.Fill(frame.DrawList, captionRect.Min, captionRect.Max, GlassField.Radius(captionRect),
            ImGui.GetColorU32(Ink.FieldFill));
        GlassField.Text(captionRect, "##aethergramStoryShareCaption", Loc.T(L.Aethergram.CaptionHint),
            ref storyShareCaption, Ink.TitleInk, Ink.MutedInk, StoryShareCaptionInset * scale,
            StoryShareCaptionMaxLength);
        var buttonHeight = StoryShareButtonHeight * scale;
        var shareTop = captionRect.Max.Y + StoryShareButtonGap * scale;
        var shareRect = new Rect(new Vector2(content.Min.X + inset, shareTop),
            new Vector2(content.Max.X - inset, shareTop + buttonHeight));
        var cancelTop = shareRect.Max.Y + StoryShareButtonSpacing * scale;
        var cancelRect = new Rect(new Vector2(content.Min.X + inset, cancelTop),
            new Vector2(content.Max.X - inset, cancelTop + buttonHeight));
        var posting = stories.Posting;
        if (DrawAccentPill(shareRect, Loc.T(posting ? L.Aethergram.Saving : L.Share.Action),
                frame.Interactive && !posting))
        {
            stories.ShareGram(post.Id, storyShareCaption, (shared, failure) =>
            {
                storyShareFailure = shared ? null : new AepFailureBox(failure);
                storyShareOutcome = shared ? OutcomeShared : OutcomeFailed;
            });
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

        toast.Show(StoryFailureText(storyShareFailure?.Failure ?? AepFailure.None, L.Story.AddToStoryFailed));
    }
}
