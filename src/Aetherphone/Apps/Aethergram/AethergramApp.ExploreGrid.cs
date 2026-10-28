using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private readonly PullToRefresh explorePull = new();
    private readonly ChipRail trendingRail = new();
    private Action? refreshExplore;
    private TagSummaryDto[]? trendingLabelsSource;
    private string[] trendingLabels = Array.Empty<string>();
    private bool[] trendingActive = Array.Empty<bool>();

    private void DrawExploreGrid(Rect listRect)
    {
        var scale = UiScale.Current;
        store.EnsureExplore();
        store.EnsureTrendingTags();
        refreshExplore ??= RefreshExploreGrid;
        var posts = store.ExplorePosts;
        using (var surface = AppSurface.BeginEdgeToEdge(listRect))
        {
            explorePull.Draw(listRect, surface.Pull, surface.Dragging, store.ExploreLoading, Ink.MutedInk,
                refreshExplore);
            DrawTrendingTagRail();
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Xxs * scale));
            if (posts.Length > 0)
            {
                DrawExploreMosaic(posts);
                return;
            }

            if (store.ExploreLoading)
            {
                DrawExploreSkeleton();
                return;
            }

            var body = new Rect(ImGui.GetCursorScreenPos(), listRect.Max);
            if (store.ExploreFailed)
            {
                feedFailure.Set(store.ExploreFailure);
                DrawEmptyState(body, feedFailure.Text(), Loc.T(L.Failure.PullToRetry));
                return;
            }

            DrawEmptyState(body, Loc.T(L.Aethergram.ExploreNothingNew), Loc.T(L.Aethergram.ExploreNothingNewHint));
        }
    }

    private void RefreshExploreGrid()
    {
        store.RefreshExplore();
        store.RefreshTrendingTags();
    }

    private void DrawTrendingTagRail()
    {
        var tags = store.TrendingTags;
        if (tags.Length == 0)
        {
            return;
        }

        var scale = UiScale.Current;
        SyncTrendingLabels(tags);
        ImGui.Dummy(new Vector2(0f, Metrics.Space.Xxs * scale));
        var tapped = trendingRail.Draw(ui, trendingLabels, trendingActive);
        if (tapped >= 0)
        {
            OpenHashtag(tags[tapped].Tag);
        }
    }

    private void SyncTrendingLabels(TagSummaryDto[] tags)
    {
        if (ReferenceEquals(trendingLabelsSource, tags))
        {
            return;
        }

        var labels = new string[tags.Length];
        for (var index = 0; index < tags.Length; index++)
        {
            labels[index] = "#" + tags[index].Tag;
        }

        trendingLabels = labels;
        trendingActive = new bool[tags.Length];
        trendingLabelsSource = tags;
    }

    private void DrawExploreMosaic(PostDto[] posts)
    {
        var scale = UiScale.Current;
        var width = ScrollLayout.StableContentWidth();
        var origin = ImGui.GetCursorScreenPos();
        var gap = GridGap * scale;
        var cellWidth = ExploreCellWidth(width, gap);
        var cellHeight = cellWidth * ExploreGrid.Aspect;
        BeginImpressionPass(PostViewSource.Explore);
        for (var index = 0; index < posts.Length; index++)
        {
            var tile = ExploreTileRect(origin, ExploreMosaic.Cell(index), cellWidth, cellHeight, gap);
            if (!ImGui.IsRectVisible(tile.Min, tile.Max))
            {
                continue;
            }

            DrawGridTile(posts[index], tile.Min, tile.Max, ExploreGrid, false);
            store.ObserveImpression(posts[index].Id, tile.Min.Y, tile.Max.Y);
            if (UiInteract.Click(tile.Min, tile.Max, UiInteract.Hover(tile.Min, tile.Max)))
            {
                OpenGridPost(posts, index, PostSource.Explore);
            }
        }

        ImGui.Dummy(new Vector2(width, ExploreMosaicHeight(ExploreMosaic.Rows(posts.Length), cellHeight, gap)));
        if (store.ExploreLoadingMore)
        {
            InfiniteScroll.DrawLoadingRow(origin.X + width * 0.5f, Ink.MutedInk);
        }
        else if (store.HasMoreExplore && InfiniteScroll.ReachedBottom())
        {
            store.LoadMoreExplore();
        }

        ImGui.Dummy(new Vector2(0f, Metrics.Space.Xl * scale));
    }

    private static void DrawExploreSkeleton()
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var width = ScrollLayout.StableContentWidth();
        var origin = ImGui.GetCursorScreenPos();
        var gap = GridGap * scale;
        var cellWidth = ExploreCellWidth(width, gap);
        var cellHeight = cellWidth * ExploreGrid.Aspect;
        var fill = ImGui.GetColorU32(Ink.ThumbFill);
        for (var index = 0; index < ExploreMosaic.TilesPerBlock; index++)
        {
            var tile = ExploreTileRect(origin, ExploreMosaic.Cell(index), cellWidth, cellHeight, gap);
            drawList.AddRectFilled(tile.Min, tile.Max, fill);
        }

        ImGui.Dummy(new Vector2(width,
            ExploreMosaicHeight(ExploreMosaic.Rows(ExploreMosaic.TilesPerBlock), cellHeight, gap)));
    }

    private static float ExploreCellWidth(float width, float gap) =>
        (width - gap * (ExploreMosaic.Columns - 1)) / ExploreMosaic.Columns;

    private static Rect ExploreTileRect(Vector2 origin, MosaicCell cell, float cellWidth, float cellHeight, float gap)
    {
        var min = new Vector2(origin.X + cell.Column * (cellWidth + gap), origin.Y + cell.Row * (cellHeight + gap));
        var size = new Vector2(cell.Span * cellWidth + (cell.Span - 1) * gap, cell.Span * cellHeight + (cell.Span - 1) * gap);
        return new Rect(min, min + size);
    }

    private static float ExploreMosaicHeight(int rows, float cellHeight, float gap) =>
        rows == 0 ? 0f : rows * cellHeight + (rows - 1) * gap;
}
