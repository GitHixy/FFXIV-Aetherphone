namespace Aetherphone.Core.Aethernet.Contracts;

internal sealed record StorySharedPostDto(
    string PostId,
    bool Available,
    string AuthorId = "",
    string AuthorDisplayName = "",
    string AuthorHandle = "",
    string? AuthorAvatarUrl = null,
    string? MediaUrl = null,
    int MediaWidth = 0,
    int MediaHeight = 0);
