using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const float ReelCommentsShare = 0.68f;
    private const float ReelCommentsTitleHeight = 34f;
    private const float ReelCommentsVeil = 0.12f;
    private const ImGuiWindowFlags ReelCommentsFlags = ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground;

    private readonly Sheet reelCommentsSheet = new();

    private bool ReelCommentsCaptures => reelCommentsSheet.CapturesPointer;

    private bool ReelSurfaceShown =>
        router.Current.Screen == AethergramScreen.Reels
        || (router.Depth == 1 && activeTab == AethergramTab.Reels);

    private void OpenReelComments(PostDto post)
    {
        store.ReportFeedSignal(post.Id, FeedSignalKinds.DetailOpen);
        store.OpenDetail(post);
        commentDraft = string.Empty;
        commentFocusPending = false;
        reelCommentsSheet.Open();
    }

    private void DrawReelComments(Rect screen)
    {
        if (!reelCommentsSheet.CapturesPointer)
        {
            return;
        }

        if (reelCommentsSheet.IsOpen && !ReelSurfaceShown)
        {
            reelCommentsSheet.Close();
        }

        if (reelCommentsSheet.IsOpen)
        {
            if (UiInteract.WindowFocused)
            {
                ImGui.SetNextFrameWantCaptureKeyboard(true);
            }

            // The first Escape leaves a focused composer, as ImGui does; the next one closes the sheet.
            if (ImGui.IsKeyPressed(ImGuiKey.Escape) && !ImGui.IsAnyItemActive())
            {
                reelCommentsSheet.Close();
            }
        }

        ImGui.SetCursorScreenPos(screen.Min);
        using (ImRaii.Child("##aethergramReelComments", screen.Size, false, ReelCommentsFlags))
        {
            var frame = reelCommentsSheet.Begin(ImGui.GetWindowDrawList(), screen, theme,
                SheetDetents.Fitted(screen.Height * ReelCommentsShare), ReelCommentsVeil);
            if (!frame.Visible)
            {
                return;
            }

            DrawReelCommentsContent(in frame);
            reelCommentsSheet.End(in frame);
        }
    }

    private void DrawReelCommentsContent(in SheetFrame frame)
    {
        var scale = UiScale.Current;
        var content = frame.Content;
        var titleHeight = ReelCommentsTitleHeight * scale;
        Typography.DrawCentered(frame.DrawList, new Vector2(content.Center.X, content.Min.Y + titleHeight * 0.4f),
            Loc.T(L.Aethergram.ReelComments), frame.Ink with { W = frame.Ink.W * frame.Opacity },
            TextStyles.Headline);
        if (store.DetailPost is not { } post)
        {
            Typography.DrawCentered(frame.DrawList, content.Center, Loc.T(L.Common.Loading), Ink.MutedInk,
                TextStyles.Subheadline);
            return;
        }

        var composerHeight = DetailComposerHeight * scale;
        var composerBottom = frame.Panel.Max.Y - Metrics.Size.HomeIndicatorInset * scale;
        var listRect = new Rect(new Vector2(content.Min.X, content.Min.Y + titleHeight),
            new Vector2(content.Max.X, composerBottom - composerHeight));
        var composerRect = new Rect(new Vector2(content.Min.X, composerBottom - composerHeight),
            new Vector2(content.Max.X, composerBottom));
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && (UiInteract.HoverWindowOnly(listRect.Min, listRect.Max)
                || UiInteract.HoverWindowOnly(composerRect.Min, composerRect.Max)))
        {
            reelCommentsSheet.YieldPointer();
        }

        using (AppSurface.BeginEdgeToEdge(listRect))
        {
            DrawComments();
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Lg * scale));
        }

        DrawCommentComposer(composerRect, screenRect, post);
    }
}
