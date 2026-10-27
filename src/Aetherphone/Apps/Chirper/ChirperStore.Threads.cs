using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Gifs;
using Aetherphone.Core.Net;

namespace Aetherphone.Apps.Chirper;

internal readonly record struct ThreadOpening(string Text, string? QuotedPostId, GifItem? Gif, PostPollInput? Poll,
    bool Sensitive, int ReplyPolicy, string[]? AltTexts);

internal readonly record struct ThreadFollowUp(string Text, string[] ImagePaths, GifItem? Gif, string[]? AltTexts);

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

    public void ComposeThread(ThreadOpening opening, IReadOnlyList<string> imagePaths, ThreadFollowUp[] followUps,
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
                    Sensitive: opening.Sensitive, GifUrl: gif.Url, GifSlug: gif.Slug, ReplyPolicy: opening.ReplyPolicy)
                : new CreatePostRequest(text, opening.QuotedPostId, keys.Length > 0 ? keys : null, width, height,
                    opening.Sensitive && keys.Length > 0, opening.Poll, ReplyPolicy: opening.ReplyPolicy,
                    AltTexts: keys.Length > 0 ? opening.AltTexts : null);
            for (var index = 0; index < followUps.Length; index++)
            {
                var followUp = await FollowUpRequestAsync(followUps[index], opening, token, onFailure)
                    .ConfigureAwait(false);
                if (followUp is null)
                {
                    return false;
                }

                items[index + 1] = followUp;
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

    // Each chirp of the thread carries its own photos or GIF, uploaded right before the thread is posted
    // so no upload waits unattached longer than the request takes.
    private async Task<CreatePostRequest?> FollowUpRequestAsync(ThreadFollowUp followUp, ThreadOpening opening,
        CancellationToken token, Action<AepFailure>? onFailure)
    {
        var text = followUp.Text.Trim();
        if (followUp.Gif is { } gif)
        {
            return new CreatePostRequest(text, MediaWidth: gif.Width, MediaHeight: gif.Height,
                Sensitive: opening.Sensitive, GifUrl: gif.Url, GifSlug: gif.Slug, ReplyPolicy: opening.ReplyPolicy);
        }

        if (followUp.ImagePaths.Length == 0)
        {
            return new CreatePostRequest(text, ReplyPolicy: opening.ReplyPolicy);
        }

        var uploaded = await UploadImagesAsync(followUp.ImagePaths, MaxImages, MaxImageDimension, UploadScope, token,
            onFailure).ConfigureAwait(false);
        if (uploaded is null)
        {
            return null;
        }

        var (keys, width, height) = uploaded.Value;
        return new CreatePostRequest(text, null, keys.Length > 0 ? keys : null, width, height,
            opening.Sensitive && keys.Length > 0, ReplyPolicy: opening.ReplyPolicy,
            AltTexts: keys.Length > 0 ? followUp.AltTexts : null);
    }
}
