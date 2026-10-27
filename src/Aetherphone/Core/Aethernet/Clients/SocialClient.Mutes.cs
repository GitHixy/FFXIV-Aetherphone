using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class SocialClient
{
    public Task<bool> MuteAsync(string userId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Post, $"/mutes/{Uri.EscapeDataString(userId)}", token, null, onFailure);
    }

    public Task<bool> UnmuteAsync(string userId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Delete, $"/mutes/{Uri.EscapeDataString(userId)}", token, null, onFailure);
    }

    public Task<UserListPage?> MutedUsersAsync(string? cursor, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return UserListAsync("/mutes", cursor, token, onFailure);
    }
}
