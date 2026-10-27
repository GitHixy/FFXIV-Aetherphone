using System.Text.Json.Serialization;

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
    int MediaHeight = 0,
    int? Kind = null,
    string? Text = null)
{
    public const int ChirpKind = 0;

    [JsonIgnore] public bool IsChirp => Available && Kind == ChirpKind;
}
