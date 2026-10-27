using System.Text;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class GramClient
{
    public Task<ReelFeedPage?> ReelFeedAsync(string? session, string? cursor, string? regions, bool includeSensitive,
        CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        var path = new StringBuilder("/reels/feed");
        var separator = '?';
        AppendQuery(path, ref separator, "session", session);
        AppendQuery(path, ref separator, "cursor", cursor);
        AppendQuery(path, ref separator, "regions", regions);
        if (!includeSensitive)
        {
            AppendQuery(path, ref separator, "sensitive", "false");
        }

        return net.GetAsync(path.ToString(), AethernetJsonContext.Default.ReelFeedPage, token, null, onFailure);
    }

    public Task<bool> ReportReelWatchesAsync(ReelWatchItem[] items, string? session, CancellationToken token)
    {
        return net.SendJsonForStatusAsync(HttpMethod.Post, "/reels/watch", new ReelWatchRequest(items, session),
            AethernetJsonContext.Default.ReelWatchRequest, token);
    }

    public Task<FeedPage?> UserReelsAsync(string userId, string? cursor, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        var path = $"/users/{Uri.EscapeDataString(userId)}/posts?kind=1&media=video";
        if (cursor is not null)
        {
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return net.GetAsync(path, AethernetJsonContext.Default.FeedPage, token, null, onFailure);
    }

    private static void AppendQuery(StringBuilder path, ref char separator, string name, string? value)
    {
        if (value is null)
        {
            return;
        }

        path.Append(separator).Append(name).Append('=').Append(Uri.EscapeDataString(value));
        separator = '&';
    }
}
