using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class GramClient
{
    public Task<PostDto?> CreateReelAsync(CreateReelRequest request, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.PostAsync("/reels", request, AethernetJsonContext.Default.CreateReelRequest,
            AethernetJsonContext.Default.PostDto, token, null, onFailure);
    }

    // The first page of the account's own reels; any item at all means a reel is up.
    public Task<FeedPage?> OwnReelsAsync(string userId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync($"/users/{Uri.EscapeDataString(userId)}/posts?kind=1&media=video",
            AethernetJsonContext.Default.FeedPage, token, null, onFailure);
    }
}
