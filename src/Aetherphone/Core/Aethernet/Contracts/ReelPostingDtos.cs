namespace Aetherphone.Core.Aethernet.Contracts;

internal sealed record CreateReelRequest(
    string Caption,
    string VideoKey,
    string PosterKey,
    PhotoTagInput[]? PhotoTags = null,
    string[]? CollaboratorIds = null,
    string? AltText = null,
    bool Sensitive = false);
