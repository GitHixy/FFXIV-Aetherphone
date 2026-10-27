using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class SocialClient
{
    public Task<PostViewsDto?> PostViewsAsync(string postId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync($"/posts/{Uri.EscapeDataString(postId)}/views", AethernetJsonContext.Default.PostViewsDto,
            token, null, onFailure);
    }
}
