using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class SocialClient
{
    public Task<PostDto?> SetReplyPolicyAsync(string postId, int replyPolicy, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.SendJsonAsync(HttpMethod.Put, $"/posts/{Uri.EscapeDataString(postId)}/reply-policy",
            new SetReplyPolicyRequest(replyPolicy), AethernetJsonContext.Default.SetReplyPolicyRequest,
            AethernetJsonContext.Default.PostDto, token, null, onFailure);
    }
}
