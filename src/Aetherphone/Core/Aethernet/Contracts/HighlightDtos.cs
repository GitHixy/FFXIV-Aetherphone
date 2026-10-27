namespace Aetherphone.Core.Aethernet.Contracts;

internal static class StoryAudiences
{
    public const int Everyone = 0;
    public const int CloseFriends = 1;
}

internal sealed record StoryHighlightDto(
    string Id,
    string OwnerId,
    string Title,
    string CoverStoryId,
    string? CoverUrl,
    int ItemCount,
    int Position,
    long CreatedAtUnix);

internal sealed record StoryHighlightList(StoryHighlightDto[] Items);

internal sealed record CreateHighlightRequest(string Title, string[] StoryIds, string? CoverStoryId = null);

internal sealed record UpdateHighlightRequest(string? Title = null, string? CoverStoryId = null);

internal sealed record StoryHighlightMembership(string[] HighlightIds);
