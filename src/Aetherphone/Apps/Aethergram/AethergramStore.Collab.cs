using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramStore
{
    private volatile AepFailureBox? createFailure;

    public AepFailure CreateFailure => createFailure?.Failure ?? AepFailure.None;

    private void RecordCreateFailure(AepFailure failure)
    {
        createFailure = new AepFailureBox(failure);
    }

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

            MapPostEverywhere(postId, WithoutCollaborator);
            return true;
        }, onComplete);
    }

    public void RemoveCollab(string postId, string collaboratorId, Action<bool> onComplete)
    {
        work.Run("collab remove", async token =>
        {
            if (!await grams.RemoveCollabAsync(postId, token).ConfigureAwait(false))
            {
                return false;
            }

            MapPostEverywhere(postId, WithoutCollaborator);
            archivedLane.Items = CopyOnWrite.MapById(archivedLane.Items, postId, WithoutCollaborator);
            DropFromCollaboratorGrid(postId, collaboratorId);
            return true;
        }, onComplete);
    }

    private static PostDto WithoutCollaborator(PostDto post) => post with { Collaborator = null };

    // The post only sat on the collaborator's grid because of the collab, so a grid of theirs that is
    // on screen loses it now instead of on the next refresh.
    private void DropFromCollaboratorGrid(string postId, string collaboratorId)
    {
        if (!string.Equals(ProfileUserId, collaboratorId, StringComparison.Ordinal))
        {
            return;
        }

        profileLane.Items = CopyOnWrite.RemoveById(profileLane.Items, postId);
    }
}
