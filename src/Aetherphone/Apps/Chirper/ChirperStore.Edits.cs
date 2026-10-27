using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperStore
{
    private volatile bool editingChirp;

    public bool EditingChirp => editingChirp;

    public void EditChirp(string postId, string text, Action<bool> onComplete, Action<AepFailure>? onFailure = null)
    {
        if (editingChirp)
        {
            return;
        }

        editingChirp = true;
        var trimmed = text.Trim();
        work.Run("edit chirp", async token =>
        {
            var edited = await client.EditChirpTextAsync(postId, trimmed, token, onFailure).ConfigureAwait(false);
            if (edited is null)
            {
                return false;
            }

            PostDto Apply(PostDto host) => ChirperEdits.WithEdit(host, edited);
            MapPostEverywhere(postId, Apply);
            MapReferencedEverywhere(postId, Apply);
            return true;
        }, onComplete, () => editingChirp = false);
    }

    public void LoadChirpEdits(string postId, Action<PostEditDto[]?> onLoaded)
    {
        PostEditDto[]? versions = null;
        work.Run("chirp edits", async token =>
        {
            versions = await client.ChirpEditsAsync(postId, token).ConfigureAwait(false);
        }, () => onLoaded(versions));
    }
}
