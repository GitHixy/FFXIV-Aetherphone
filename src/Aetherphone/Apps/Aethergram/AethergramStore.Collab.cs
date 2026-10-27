namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramStore
{
    public void AcceptCollab(string postId, Action<bool> onComplete)
    {
        work.Run("collab accept", async token =>
        {
            var accepted = await grams.AcceptCollabAsync(postId, token).ConfigureAwait(false);
            if (accepted is null)
            {
                return false;
            }

            ReplacePost(accepted);
            return true;
        }, onComplete);
    }

    public void DeclineCollab(string postId, Action<bool> onComplete)
    {
        work.Run("collab decline", async token =>
        {
            if (!await grams.DeclineCollabAsync(postId, token).ConfigureAwait(false))
            {
                return false;
            }

            MapPostEverywhere(postId, post => post with { Collaborator = null });
            return true;
        }, onComplete);
    }

    public void RemoveCollab(string postId, Action<bool> onComplete)
    {
        work.Run("collab remove", async token =>
        {
            if (!await grams.RemoveCollabAsync(postId, token).ConfigureAwait(false))
            {
                return false;
            }

            MapPostEverywhere(postId, post => post with { Collaborator = null });
            return true;
        }, onComplete);
    }
}
