using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;
using Aetherphone.Core.Social;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramStore
{
    private volatile AepFailureBox? createFailure;
    private volatile AepFailureBox? collabFailure;

    public AepFailure CreateFailure => createFailure?.Failure ?? AepFailure.None;

    public AepFailure CollabFailure => collabFailure?.Failure ?? AepFailure.None;

    private void RecordCreateFailure(AepFailure failure)
    {
        createFailure = new AepFailureBox(failure);
    }

    private void RecordCollabFailure(AepFailure failure)
    {
        collabFailure = new AepFailureBox(failure);
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
        var meId = Me?.Id;
        work.Run("collab decline", async token =>
        {
            if (!await grams.DeclineCollabAsync(postId, token).ConfigureAwait(false))
            {
                return false;
            }

            if (meId is not null)
            {
                MapPostEverywhere(postId, post => CollabStates.Without(post, meId));
            }

            return true;
        }, onComplete);
    }

    public void InviteCollab(string postId, string userId, Action<PostDto?> onComplete)
    {
        collabFailure = null;
        PostDto? invited = null;
        work.Run("collab invite", async token =>
        {
            invited = await grams.InviteCollabAsync(postId, userId, token, RecordCollabFailure).ConfigureAwait(false);
            if (invited is null)
            {
                return false;
            }

            ReplacePost(invited);
            archivedLane.Items = CopyOnWrite.Replace(archivedLane.Items, invited);
            return true;
        }, succeeded => onComplete(succeeded ? invited : null));
    }

    public void RemoveCollab(string postId, string collaboratorId, Action<bool> onComplete)
    {
        work.Run("collab remove", async token =>
        {
            if (!await grams.RemoveCollabAsync(postId, collaboratorId, token).ConfigureAwait(false))
            {
                return false;
            }

            MapPostEverywhere(postId, post => CollabStates.Without(post, collaboratorId));
            archivedLane.Items = CopyOnWrite.MapById(archivedLane.Items, postId,
                post => CollabStates.Without(post, collaboratorId));
            DropFromCollaboratorGrid(postId, collaboratorId);
            return true;
        }, onComplete);
    }

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
