using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    public string? AccountId => me?.Id ?? session.CurrentUser?.Id;

    public void LoadPost(string postId, Action<PostDto> onLoaded)
    {
        work.Run("draft post", async token =>
        {
            var post = await client.PostAsync(postId, token).ConfigureAwait(false);
            if (post is not null)
            {
                onLoaded(post);
            }
        });
    }
}
