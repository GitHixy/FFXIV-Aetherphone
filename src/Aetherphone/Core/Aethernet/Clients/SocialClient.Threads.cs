using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class SocialClient
{
    public Task<PostDto?> CreateThreadAsync(CreatePostRequest[] items, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.PostAsync("/posts/thread", new CreateThreadRequest(items),
            AethernetJsonContext.Default.CreateThreadRequest, AethernetJsonContext.Default.PostDto, token, null,
            onFailure);
    }

    public Task<PostDto[]?> ThreadAsync(string rootId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync($"/posts/{Uri.EscapeDataString(rootId)}/thread", AethernetJsonContext.Default.PostDtoArray,
            token, null, onFailure);
    }
}
