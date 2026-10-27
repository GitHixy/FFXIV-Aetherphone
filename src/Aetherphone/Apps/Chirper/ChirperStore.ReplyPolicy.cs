namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperStore
{
    public void SetReplyPolicy(string postId, int replyPolicy, Action<bool> onComplete)
    {
        work.Run("reply policy", async token =>
        {
            var updated = await client.SetReplyPolicyAsync(postId, replyPolicy, token).ConfigureAwait(false);
            if (updated is null)
            {
                return false;
            }

            ReplacePost(updated);
            return true;
        }, onComplete);
    }
}
