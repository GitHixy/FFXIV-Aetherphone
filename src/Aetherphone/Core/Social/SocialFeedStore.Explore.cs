using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    private const long TrendingRefreshMilliseconds = 600_000;

    private static readonly TimeSpan ExploreMoreRetry = TimeSpan.FromSeconds(10);

    private readonly FeedLane<PostDto> exploreLane = FeedLane<PostDto>.ServerOrdered();
    private readonly RetryGate exploreMoreGate = new(ExploreMoreRetry);
    private volatile bool exploreRequested;
    private volatile string? exploreRegions;
    private volatile bool exploreIncludesSensitive;
    private volatile TagSummaryDto[] trendingTags = Array.Empty<TagSummaryDto>();
    private volatile bool trendingLoading;
    private long trendingAttemptTick;

    public PostDto[] ExplorePosts => exploreLane.Items;

    public bool ExploreLoading => exploreLane.Loading;

    public bool ExploreLoadingMore => exploreLane.LoadingMore;

    public bool HasMoreExplore => exploreLane.HasMore;

    public bool ExploreFailed => exploreLane.Failed;

    public AepFailure ExploreFailure => exploreLane.Failure;

    public TagSummaryDto[] TrendingTags => trendingTags;

    public bool TrendingLoading => trendingLoading;

    protected virtual Task<FeedPage?> FetchExploreAsync(string? cursor, string? regions, bool includeSensitive,
        CancellationToken token, Action<AepFailure>? onFailure) =>
        Task.FromResult<FeedPage?>(null);

    // One request per filter set: a grid that came back empty or failed waits for a pull to refresh
    // instead of asking again on the next frame.
    public void EnsureExplore()
    {
        if (exploreRequested && exploreIncludesSensitive == feedIncludesSensitive
            && string.Equals(exploreRegions, feedRegions, StringComparison.Ordinal))
        {
            return;
        }

        RefreshExplore();
    }

    // A refresh ranks again from the server's seen marks, so the impressions still waiting for the next
    // timed flush are sent first; otherwise the tiles just scrolled past come straight back.
    public void RefreshExplore()
    {
        if (!session.IsSignedIn || exploreLane.Loading)
        {
            return;
        }

        var regions = feedRegions;
        var includeSensitive = feedIncludesSensitive;
        if (exploreIncludesSensitive != includeSensitive || !string.Equals(exploreRegions, regions, StringComparison.Ordinal))
        {
            exploreLane.Clear();
        }

        exploreRequested = true;
        exploreRegions = regions;
        exploreIncludesSensitive = includeSensitive;
        exploreMoreGate.Reset();
        exploreLane.Loading = true;
        work.Run("explore refresh", async token =>
        {
            await feedSignals.FlushAsync(token).ConfigureAwait(false);
            var reported = AepFailure.None;
            var page = await FetchExploreAsync(null, regions, includeSensitive, token, failure => reported = failure)
                .ConfigureAwait(false);
            if (page is not null)
            {
                exploreLane.ApplyRefresh(page.Items, page.NextCursor);
                return;
            }

            exploreLane.RecordFailure(reported.Failed ? reported : AepFailure.Transport(AepFailureKind.Offline));
            AepLog.Warning($"Explore failed to refresh: {exploreLane.Failure.Describe()}");
        }, () => exploreLane.Loading = false);
    }

    // The grid asks for more on every frame it sits at the bottom, so after a failed page the next
    // attempt waits for the retry gate instead of firing a request per frame.
    public void LoadMoreExplore()
    {
        var cursor = exploreLane.Cursor;
        if (!session.IsSignedIn || cursor is null || exploreLane.LoadingMore || exploreLane.Loading)
        {
            return;
        }

        if (!exploreMoreGate.TryPass() && exploreLane.Failed)
        {
            return;
        }

        exploreLane.LoadingMore = true;
        var regions = exploreRegions;
        var includeSensitive = exploreIncludesSensitive;
        work.Run("explore more", async token =>
        {
            var reported = AepFailure.None;
            var page = await FetchExploreAsync(cursor, regions, includeSensitive, token, failure => reported = failure)
                .ConfigureAwait(false);
            if (page is not null)
            {
                exploreLane.ApplyMore(page.Items, page.NextCursor);
                return;
            }

            exploreLane.RecordFailure(reported.Failed ? reported : AepFailure.Transport(AepFailureKind.Offline));
            AepLog.Warning($"Explore failed to load more: {exploreLane.Failure.Describe()}");
        }, () => exploreLane.LoadingMore = false);
    }

    public void EnsureTrendingTags()
    {
        if (!session.IsSignedIn || trendingLoading)
        {
            return;
        }

        var now = Environment.TickCount64;
        var lastAttempt = Interlocked.Read(ref trendingAttemptTick);
        if (lastAttempt != 0 && now - lastAttempt < TrendingRefreshMilliseconds)
        {
            return;
        }

        Interlocked.Exchange(ref trendingAttemptTick, now);
        trendingLoading = true;
        work.Run("trending tags", async token =>
        {
            var trending = await client.TrendingTagsAsync(token).ConfigureAwait(false);
            if (trending is not null && trending.Tags.Length >= TrendingTagList.Minimum)
            {
                trendingTags = trending.Tags;
                return;
            }

            var recent = await client.TagSearchAsync(string.Empty, token).ConfigureAwait(false);
            if (recent is not null)
            {
                trendingTags = TrendingTagList.Merge(trending?.Tags ?? Array.Empty<TagSummaryDto>(), recent.Tags);
            }
            else if (trending is not null)
            {
                trendingTags = trending.Tags;
            }
        }, () => trendingLoading = false);
    }

    public void RefreshTrendingTags()
    {
        Interlocked.Exchange(ref trendingAttemptTick, 0);
        EnsureTrendingTags();
    }

    private void ResetExploreState()
    {
        exploreLane.Clear();
        exploreRequested = false;
        exploreRegions = null;
        exploreMoreGate.Reset();
        trendingTags = Array.Empty<TagSummaryDto>();
        Interlocked.Exchange(ref trendingAttemptTick, 0);
    }
}
