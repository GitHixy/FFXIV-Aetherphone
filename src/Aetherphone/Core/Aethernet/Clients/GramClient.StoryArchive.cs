using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class GramClient
{
    public Task<StoryArchivePage?> StoryArchiveAsync(string? cursor, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        var path = cursor is null ? "/stories/archive" : $"/stories/archive?cursor={Uri.EscapeDataString(cursor)}";
        return net.GetAsync(path, AethernetJsonContext.Default.StoryArchivePage, token, null, onFailure);
    }
}
