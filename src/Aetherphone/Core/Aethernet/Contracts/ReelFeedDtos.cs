namespace Aetherphone.Core.Aethernet.Contracts;

internal sealed record VideoDto(string Url, string PosterUrl, int DurationMs, int Width, int Height);

internal sealed record ReelFeedPage(PostDto[] Items, string Session, string? NextCursor, FeedItemNote[]? Notes = null);

internal sealed record ReelWatchRequest(ReelWatchItem[] Items, string? Session = null);

internal readonly record struct ReelWatchItem(
    string PostId,
    int WatchedMs,
    int DurationMs,
    int Loops,
    bool Completed,
    bool QuickSkip,
    int? Source = null);
