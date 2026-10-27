using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Gifs;
using Aetherphone.Core.Net;

namespace Aetherphone.Apps.Chirper;

internal readonly record struct ThreadOpening(string Text, string? QuotedPostId, GifItem? Gif, PostPollInput? Poll,
    bool Sensitive);

internal sealed partial class ChirperStore
{
    private volatile PostDto[] threadChain = Array.Empty<PostDto>();
    private volatile string? threadChainRootId;
    private volatile string? threadChainRequestedId;

    public PostDto[] ThreadChainFor(string rootId) =>
        string.Equals(threadChainRootId, rootId, StringComparison.Ordinal) ? threadChain : Array.Empty<PostDto>();

    // Every opening of a thread reloads the detail; the chain is fetched again once that settles,
    // so a chirp deleted elsewhere drops out the next time the thread is opened.
    public void EnsureThreadChain(PostDto root)
    {
        if (root.ThreadCount <= 1)
        {
            return;
        }

        if (DetailLoading)
        {
            threadChainRequestedId = null;
            return;
        }

        var rootId = root.Id;
        if (string.Equals(threadChainRequestedId, rootId, StringComparison.Ordinal))
        {
            return;
        }

        threadChainRequestedId = rootId;
        work.Run("thread chain", async token =>
        {
            var chain = await client.ThreadAsync(rootId, token).ConfigureAwait(false);
            if (chain is null || !string.Equals(threadChainRequestedId, rootId, StringComparison.Ordinal))
            {
                return;
            }

            threadChain = chain;
            threadChainRootId = rootId;
        });
    }

    public void ComposeThread(ThreadOpening opening, IReadOnlyList<string> imagePaths, string[] followUps,
        Action<bool> onComplete, Action<AepFailure>? onFailure = null)
    {
        if (posting)
        {
            return;
        }

        posting = true;
        work.Run("compose thread", async token =>
        {
            var uploaded = await UploadImagesAsync(imagePaths, MaxImages, MaxImageDimension, UploadScope, token,
                onFailure).ConfigureAwait(false);
            if (uploaded is null)
            {
                return false;
            }

            var (keys, width, height) = uploaded.Value;
            var items = new CreatePostRequest[followUps.Length + 1];
            var text = opening.Text.Trim();
            items[0] = opening.Gif is { } gif
                ? new CreatePostRequest(text, opening.QuotedPostId, MediaWidth: gif.Width, MediaHeight: gif.Height,
                    Sensitive: opening.Sensitive, GifUrl: gif.Url, GifSlug: gif.Slug)
                : new CreatePostRequest(text, opening.QuotedPostId, keys.Length > 0 ? keys : null, width, height,
                    opening.Sensitive && keys.Length > 0, opening.Poll);
            for (var index = 0; index < followUps.Length; index++)
            {
                items[index + 1] = new CreatePostRequest(followUps[index].Trim());
            }

            var created = await client.CreateThreadAsync(items, token, onFailure).ConfigureAwait(false);
            if (created is null)
            {
                return false;
            }

            AcceptCreatedPost(created);
            return true;
        }, onComplete, () => posting = false);
    }
}
