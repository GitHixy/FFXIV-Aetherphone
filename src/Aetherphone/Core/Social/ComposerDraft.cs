namespace Aetherphone.Core.Social;

internal sealed class ComposerDraft
{
    public string Id { get; set; } = string.Empty;

    public long SavedAtUnix { get; set; }

    public string Text { get; set; } = string.Empty;

    public List<string> PhotoPaths { get; set; } = new();

    public string? QuotedPostId { get; set; }

    public ComposerDraftGif? Gif { get; set; }

    public List<string> PollOptions { get; set; } = new();

    public int PollDurationIndex { get; set; }

    public List<ComposerDraftTag> PhotoTags { get; set; } = new();

    public bool Sensitive { get; set; }

    public int ReplyPolicy { get; set; }

    public List<string> AltTexts { get; set; } = new();

    public List<ComposerDraftPerson> Collaborators { get; set; } = new();

    public List<ComposerDraftFrame> Frames { get; set; } = new();

    public int Aspect { get; set; }
}

internal sealed class ComposerDraftPerson
{
    public string UserId { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;
}

internal sealed class ComposerDraftFrame
{
    public float Zoom { get; set; } = 1f;

    public float CenterX { get; set; } = 0.5f;

    public float CenterY { get; set; } = 0.5f;

    public int QuarterTurns { get; set; }

    public bool Mirrored { get; set; }

    public float Straighten { get; set; }

    public float Brightness { get; set; }

    public float Contrast { get; set; }

    public float Saturation { get; set; }

    public float Warmth { get; set; }

    public float Vignette { get; set; }

    public int Look { get; set; }

    public float LookStrength { get; set; } = 1f;
}

internal sealed class ComposerDraftGif
{
    public string Slug { get; set; } = string.Empty;

    public string PreviewUrl { get; set; } = string.Empty;

    public int PreviewWidth { get; set; }

    public int PreviewHeight { get; set; }

    public string Url { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public string Query { get; set; } = string.Empty;
}

internal sealed class ComposerDraftTag
{
    public string UserId { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int PhotoIndex { get; set; }

    public float X { get; set; }

    public float Y { get; set; }
}
