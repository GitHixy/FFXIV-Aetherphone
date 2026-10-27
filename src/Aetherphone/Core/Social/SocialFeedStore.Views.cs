using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    private volatile string? postViewsId;
    private volatile PostViewsDto? postViews;
    private volatile bool postViewsLoading;

    public PostViewsDto? PostViews => postViews;

    public bool PostViewsLoading => postViewsLoading;

    public void LoadPostViews(string postId)
    {
        postViewsId = postId;
        postViews = null;
        postViewsLoading = true;
        work.Run("post views", async token =>
        {
            var views = await client.PostViewsAsync(postId, token).ConfigureAwait(false);
            if (string.Equals(postViewsId, postId, StringComparison.Ordinal))
            {
                postViews = views;
            }
        }, () =>
        {
            if (string.Equals(postViewsId, postId, StringComparison.Ordinal))
            {
                postViewsLoading = false;
            }
        });
    }

    private void ResetPostViews()
    {
        postViewsId = null;
        postViews = null;
        postViewsLoading = false;
    }
}
