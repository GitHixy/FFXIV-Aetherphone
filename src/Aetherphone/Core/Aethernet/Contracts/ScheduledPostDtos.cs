namespace Aetherphone.Core.Aethernet.Contracts;

internal sealed record CreateScheduledPostRequest(CreatePostRequest Post, long PublishAtUnix);

internal sealed record ScheduledPostDto(
    string Id,
    CreatePostRequest Post,
    long PublishAtUnix,
    long CreatedAtUnix = 0,
    int Attempts = 0,
    string? LastError = null,
    bool Failed = false);

internal sealed record ScheduledPostPage(ScheduledPostDto[] Items);
