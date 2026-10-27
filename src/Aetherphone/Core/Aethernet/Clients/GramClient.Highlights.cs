using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Aethernet.Clients;

internal sealed partial class GramClient
{
    public Task<UserListPage?> CloseFriendsAsync(string query, string? cursor, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync(ListPath("/close-friends", query, cursor), AethernetJsonContext.Default.UserListPage, token,
            null, onFailure);
    }

    public Task<UserListPage?> CloseFriendCandidatesAsync(string query, string? cursor, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync(ListPath("/close-friends/candidates", query, cursor),
            AethernetJsonContext.Default.UserListPage, token, null, onFailure);
    }

    public Task<bool> AddCloseFriendAsync(string userId, CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Put, $"/close-friends/{Uri.EscapeDataString(userId)}", token, null, onFailure);
    }

    public Task<bool> RemoveCloseFriendAsync(string userId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Delete, $"/close-friends/{Uri.EscapeDataString(userId)}", token, null,
            onFailure);
    }

    public Task<StoryHighlightList?> HighlightsAsync(string userId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync($"/users/{Uri.EscapeDataString(userId)}/highlights",
            AethernetJsonContext.Default.StoryHighlightList, token, null, onFailure);
    }

    public Task<StoryGroup?> HighlightStoriesAsync(string highlightId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync($"/highlights/{Uri.EscapeDataString(highlightId)}/stories",
            AethernetJsonContext.Default.StoryGroup, token, null, onFailure);
    }

    public Task<StoryHighlightMembership?> HighlightsHoldingAsync(string storyId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.GetAsync($"/stories/{Uri.EscapeDataString(storyId)}/highlights",
            AethernetJsonContext.Default.StoryHighlightMembership, token, null, onFailure);
    }

    public Task<StoryHighlightDto?> CreateHighlightAsync(string title, string[] storyIds, string? coverStoryId,
        CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.PostAsync("/highlights", new CreateHighlightRequest(title, storyIds, coverStoryId),
            AethernetJsonContext.Default.CreateHighlightRequest, AethernetJsonContext.Default.StoryHighlightDto, token,
            null, onFailure);
    }

    public Task<StoryHighlightDto?> UpdateHighlightAsync(string highlightId, string? title, string? coverStoryId,
        CancellationToken token, Action<AepFailure>? onFailure = null)
    {
        return net.SendJsonAsync(HttpMethod.Put, $"/highlights/{Uri.EscapeDataString(highlightId)}",
            new UpdateHighlightRequest(title, coverStoryId), AethernetJsonContext.Default.UpdateHighlightRequest,
            AethernetJsonContext.Default.StoryHighlightDto, token, null, onFailure);
    }

    public Task<StoryHighlightDto?> AddToHighlightAsync(string highlightId, string storyId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.RequestAsync(HttpMethod.Post,
            $"/highlights/{Uri.EscapeDataString(highlightId)}/stories/{Uri.EscapeDataString(storyId)}",
            AethernetJsonContext.Default.StoryHighlightDto, token, null, onFailure);
    }

    public Task<bool> RemoveFromHighlightAsync(string highlightId, string storyId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Delete,
            $"/highlights/{Uri.EscapeDataString(highlightId)}/stories/{Uri.EscapeDataString(storyId)}", token, null,
            onFailure);
    }

    public Task<bool> DeleteHighlightAsync(string highlightId, CancellationToken token,
        Action<AepFailure>? onFailure = null)
    {
        return net.SendAsync(HttpMethod.Delete, $"/highlights/{Uri.EscapeDataString(highlightId)}", token, null,
            onFailure);
    }

    private static string ListPath(string path, string query, string? cursor)
    {
        if (query.Length > 0)
        {
            return $"{path}?q={Uri.EscapeDataString(query)}";
        }

        return cursor is null ? path : $"{path}?cursor={Uri.EscapeDataString(cursor)}";
    }
}
