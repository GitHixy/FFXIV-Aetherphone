using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class GramClient
{
    public Task<PostDto?> AcceptCollabAsync(string postId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.RequestAsync(HttpMethod.Post, $"/posts/{Uri.EscapeDataString(postId)}/collab/accept",
            AethernetJsonContext.Default.PostDto, token, null, onFailure);
    }

    public Task<bool> DeclineCollabAsync(string postId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Post, $"/posts/{Uri.EscapeDataString(postId)}/collab/decline", token, null,
            onFailure);
    }

    public Task<bool> RemoveCollabAsync(string postId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Delete, $"/posts/{Uri.EscapeDataString(postId)}/collab", token, null,
            onFailure);
    }
}
