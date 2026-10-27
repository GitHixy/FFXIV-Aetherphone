using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class SocialClient
{
    public Task<PostDto?> EditChirpTextAsync(string postId, string text, string[]? altTexts, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.SendJsonAsync(HttpMethod.Put, $"/posts/{Uri.EscapeDataString(postId)}/text",
            new EditChirpRequest(text, altTexts), AethernetJsonContext.Default.EditChirpRequest,
            AethernetJsonContext.Default.PostDto, token, null, onFailure);
    }

    public Task<PostEditDto[]?> ChirpEditsAsync(string postId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync($"/posts/{Uri.EscapeDataString(postId)}/edits",
            AethernetJsonContext.Default.PostEditDtoArray, token, null, onFailure);
    }
}
