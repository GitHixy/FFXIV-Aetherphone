using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal sealed partial class StoryStore
{
    public void ShareGram(string postId, Action<bool> onComplete)
    {
        if (posting)
        {
            return;
        }

        posting = true;
        work.Run("share gram to story", async token =>
        {
            var created = await client.ShareGramToStoryAsync(postId, token).ConfigureAwait(false);
            if (created is null)
            {
                return false;
            }

            RefreshTray();
            return true;
        }, onComplete, () => posting = false);
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
