using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;
using Aetherphone.Core.Social;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramStore
{
    private readonly FeedLane<PostDto> userReelsLane = new(PostOrder.PinnedThenNewest);
    private volatile string? userReelsUserId;

    public ReelDeck ReelTab { get; } = new();

    public ReelDeck ReelOpened { get; } = new();

    public PostDto[] UserReels => userReelsLane.Items;

    public bool UserReelsLoading => userReelsLane.Loading;

    public bool UserReelsLoadingMore => userReelsLane.LoadingMore;

    public bool HasMoreUserReels => userReelsLane.HasMore;

    private void AttachReelLanes()
    {
        AttachLanes(ReelTab.Lane, ReelOpened.Lane, userReelsLane);
    }

    private void ResetReels()
    {
        ReelTab.Reset();
        ReelOpened.Reset();
        userReelsLane.Clear();
        userReelsUserId = null;
    }

    public void LoadReels(ReelDeck deck)
    {
        if (!IsSignedIn
            || !deck.TryBeginFetch(Environment.TickCount64, out var fetchGeneration, out var session, out var cursor))
        {
            return;
        }

        var regions = FeedRegions;
        var includeSensitive = FeedIncludesSensitive;
        var settled = false;
        var reported = AepFailure.None;
        work.Run("reels feed", async token =>
        {
            var page = await grams.ReelFeedAsync(session, cursor, regions, includeSensitive, token,
                failure => reported = failure).ConfigureAwait(false);
            if (page is null)
            {
                return;
            }

            deck.AcceptPage(fetchGeneration, page);
            settled = true;
        }, () =>
        {
            if (!settled)
            {
                deck.FailPage(fetchGeneration, reported, Environment.TickCount64);
            }
        });
    }

    public void ReportReelWatches(ReelWatchItem[] items, string? session)
    {
        if (items.Length == 0 || !IsSignedIn)
        {
            return;
        }

        work.Run("reel watches", async token =>
            await grams.ReportReelWatchesAsync(items, session, token).ConfigureAwait(false));
    }

    public void EnsureUserReels(string userId)
    {
        if (string.Equals(userReelsUserId, userId, StringComparison.Ordinal) || !IsSignedIn)
        {
            return;
        }

        userReelsUserId = userId;
        userReelsLane.Clear();
        userReelsLane.Loading = true;
        work.Run("user reels", async token =>
        {
            var page = await grams.UserReelsAsync(userId, null, token).ConfigureAwait(false);
            if (page is not null && string.Equals(userReelsUserId, userId, StringComparison.Ordinal))
            {
                userReelsLane.ApplyRefresh(page.Items, page.NextCursor);
            }
        }, () => userReelsLane.Loading = false);
    }

    public void ForgetUserReels()
    {
        userReelsUserId = null;
    }

    public void LoadMoreUserReels()
    {
        var userId = userReelsUserId;
        var cursor = userReelsLane.Cursor;
        if (userId is null || cursor is null || userReelsLane.LoadingMore)
        {
            return;
        }

        userReelsLane.LoadingMore = true;
        work.Run("user reels more", async token =>
        {
            var page = await grams.UserReelsAsync(userId, cursor, token).ConfigureAwait(false);
            if (page is not null && string.Equals(userReelsUserId, userId, StringComparison.Ordinal))
            {
                userReelsLane.ApplyMore(page.Items, page.NextCursor);
            }
        }, () => userReelsLane.LoadingMore = false);
    }
}
