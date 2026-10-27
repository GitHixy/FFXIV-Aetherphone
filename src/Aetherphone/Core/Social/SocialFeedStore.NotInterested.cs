namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    private volatile HashSet<string> notInterested = new(StringComparer.Ordinal);

    public bool IsNotInterested(string postId) => notInterested.Contains(postId);

    public void MarkNotInterested(string postId)
    {
        var current = notInterested;
        if (current.Contains(postId))
        {
            return;
        }

        notInterested = new HashSet<string>(current, StringComparer.Ordinal) { postId };
        work.Run("not interested", async token => await client.NotInterestedAsync(postId, token).ConfigureAwait(false));
    }

    public void UndoNotInterested(string postId)
    {
        var current = notInterested;
        if (!current.Contains(postId))
        {
            return;
        }

        var next = new HashSet<string>(current, StringComparer.Ordinal);
        next.Remove(postId);
        notInterested = next;
        work.Run("not interested undo",
            async token => await client.UndoNotInterestedAsync(postId, token).ConfigureAwait(false));
    }

    private void ResetNotInterested()
    {
        notInterested = new HashSet<string>(StringComparer.Ordinal);
    }
}
