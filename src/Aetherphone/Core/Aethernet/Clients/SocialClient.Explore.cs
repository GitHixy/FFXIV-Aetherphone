using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class SocialClient
{
    public Task<FeedPage?> ExploreGramsAsync(string? cursor, string? regions, bool includeSensitive, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        var path = includeSensitive ? "/explore/grams?sensitive=true" : "/explore/grams?sensitive=false";
        if (regions is not null)
        {
            path += $"&regions={Uri.EscapeDataString(regions)}";
        }

        if (cursor is not null)
        {
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return net.GetAsync(path, AethernetJsonContext.Default.FeedPage, token, null, onFailure);
    }

    public Task<TagSearchResult?> TrendingTagsAsync(CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync("/tags/trending", AethernetJsonContext.Default.TagSearchResult, token, null, onFailure);
    }
}
