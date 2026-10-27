using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class SocialClient
{
    public Task<ScheduledPostDto?> SchedulePostAsync(CreatePostRequest post, long publishAtUnix,
        CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.PostAsync("/posts/scheduled", new CreateScheduledPostRequest(post, publishAtUnix),
            AethernetJsonContext.Default.CreateScheduledPostRequest, AethernetJsonContext.Default.ScheduledPostDto,
            token, null, onFailure);
    }

    public Task<ScheduledPostPage?> ScheduledPostsAsync(CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync("/posts/scheduled", AethernetJsonContext.Default.ScheduledPostPage, token, null, onFailure);
    }

    public Task<bool> DeleteScheduledPostAsync(string scheduledId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Delete, $"/posts/scheduled/{Uri.EscapeDataString(scheduledId)}", token, null,
            onFailure);
    }
}
