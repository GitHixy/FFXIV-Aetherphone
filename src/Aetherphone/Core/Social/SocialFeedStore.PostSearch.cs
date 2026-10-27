using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    public const int PostSearchMinLength = 2;

    private readonly FeedLane<PostDto> postSearchLane = new(PostOrder.NewestFirst);
    private volatile string? postSearchQuery;
    private int postSearchGeneration;

    public PostDto[] PostSearchResults => postSearchLane.Items;
    public string? PostSearchQuery => postSearchQuery;
    public bool PostSearchLoading => postSearchLane.Loading;
    public bool PostSearchLoadingMore => postSearchLane.LoadingMore;
    public bool HasMorePostSearchResults => postSearchLane.HasMore;
    public bool PostSearchFailed => postSearchLane.Failed;

    public void SearchPosts(string query)
    {
        var trimmed = query.Trim();
        if (!session.IsSignedIn || trimmed.Length < PostSearchMinLength)
        {
            ClearPostSearch();
            return;
        }

        var generation = Interlocked.Increment(ref postSearchGeneration);
        postSearchQuery = trimmed;
        postSearchLane.Clear();
        postSearchLane.ClearFailure();
        postSearchLane.Loading = true;
        work.Run("post search", async token =>
        {
            var page = await client.SearchPostsAsync(trimmed, null, token, RecordPostSearchFailure(generation))
                .ConfigureAwait(false);
            if (page is not null && IsCurrentPostSearch(generation))
            {
                postSearchLane.ApplyRefresh(page.Items, page.NextCursor);
            }
        }, () =>
        {
            if (IsCurrentPostSearch(generation))
            {
                postSearchLane.Loading = false;
            }
        });
    }

    public void LoadMorePostSearch()
    {
        var query = postSearchQuery;
        var cursor = postSearchLane.Cursor;
        if (!session.IsSignedIn || query is null || cursor is null || postSearchLane.Loading
            || postSearchLane.LoadingMore)
        {
            return;
        }

        var generation = Volatile.Read(ref postSearchGeneration);
        postSearchLane.LoadingMore = true;
        work.Run("post search more", async token =>
        {
            var page = await client.SearchPostsAsync(query, cursor, token).ConfigureAwait(false);
            if (page is not null && IsCurrentPostSearch(generation))
            {
                postSearchLane.ApplyMore(page.Items, page.NextCursor);
            }
        }, () =>
        {
            if (IsCurrentPostSearch(generation))
            {
                postSearchLane.LoadingMore = false;
            }
        });
    }

    public void ClearPostSearch()
    {
        Interlocked.Increment(ref postSearchGeneration);
        postSearchQuery = null;
        postSearchLane.Clear();
        postSearchLane.ClearFailure();
    }

    private bool IsCurrentPostSearch(int generation) => Volatile.Read(ref postSearchGeneration) == generation;

    private Action<AepFailure> RecordPostSearchFailure(int generation) => failure =>
    {
        if (IsCurrentPostSearch(generation))
        {
            postSearchLane.RecordFailure(failure);
        }
    };
}
