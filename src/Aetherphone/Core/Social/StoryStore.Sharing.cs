using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Social;

internal sealed partial class StoryStore
{
    public void ShareGram(string postId, string caption, Action<bool, AepFailure> onComplete)
    {
        if (posting)
        {
            return;
        }

        posting = true;
        var failure = AepFailure.None;
        work.Run("share gram to story", async token =>
        {
            var created = await client.SharePostToStoryAsync(postId, caption.Trim(), token,
                reported => failure = reported).ConfigureAwait(false);
            if (created is null)
            {
                return false;
            }

            RefreshTray();
            return true;
        }, shared => onComplete(shared, failure), () => posting = false);
    }

    public void ToggleLike(StoryDto story)
    {
        var liked = !story.Liked;
        SetOpenStoryLiked(story.Id, liked);
        work.Run("story like", async token =>
        {
            if (!await client.SetStoryLikeAsync(story.Id, liked, token).ConfigureAwait(false))
            {
                SetOpenStoryLiked(story.Id, !liked);
            }
        });
    }

    private void SetOpenStoryLiked(string storyId, bool liked)
    {
        var snapshot = openStories;
        for (var index = 0; index < snapshot.Length; index++)
        {
            if (snapshot[index].Id != storyId)
            {
                continue;
            }

            var updated = (StoryDto[])snapshot.Clone();
            updated[index] = snapshot[index] with { Liked = liked };
            openStories = updated;
            return;
        }
    }
}
