namespace Aetherphone.Core.Reels;

internal enum ReelFraming : byte
{
    Original,
    Fill,
}

// What the composer edits: the kept range of the clip, the framing, and the cover frame. Every setter clamps, so
// the request built from it is always one the transcoder accepts.
internal struct ReelEdit
{
    internal const double MinimumLengthSeconds = 1d;
    private const float CenteredFill = 0.5f;
    private const float AspectTolerance = 0.01f;

    internal double DurationSeconds { get; private set; }
    internal int DisplayWidth { get; private set; }
    internal int DisplayHeight { get; private set; }
    internal double StartSeconds { get; private set; }
    internal double EndSeconds { get; private set; }
    internal double CoverSeconds { get; private set; }
    internal ReelFraming Framing { get; private set; }
    internal float FillPosition { get; private set; }

    internal readonly double LengthSeconds => EndSeconds - StartSeconds;

    internal readonly double LongestSeconds => Math.Min(DurationSeconds, ReelFormat.MaxDurationSeconds);

    internal readonly bool CanFill =>
        DisplayHeight > 0 && MathF.Abs((float)DisplayWidth / DisplayHeight - ReelFormat.TallestAspect) > AspectTolerance;

    internal readonly bool FillsHorizontally => (float)DisplayWidth / Math.Max(1, DisplayHeight) > ReelFormat.TallestAspect;

    internal readonly ReelCrop? Crop => Framing == ReelFraming.Fill && CanFill
        ? ReelCrop.FillPortrait(DisplayWidth, DisplayHeight, FillPosition)
        : null;

    internal static ReelEdit Begin(double durationSeconds, int displayWidth, int displayHeight)
    {
        var edit = new ReelEdit
        {
            DurationSeconds = Math.Max(0d, durationSeconds),
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight,
            Framing = ReelFraming.Original,
            FillPosition = CenteredFill,
        };
        edit.EndSeconds = edit.LongestSeconds;
        edit.CoverSeconds = 0d;
        return edit;
    }

    // A handle dragged past the longest allowed reel pulls the other edge along instead of stopping.
    internal void MoveStart(double seconds)
    {
        StartSeconds = Math.Clamp(seconds, 0d, Math.Max(0d, EndSeconds - ShortestSeconds));
        EndSeconds = Math.Min(EndSeconds, StartSeconds + ReelFormat.MaxDurationSeconds);
        KeepCoverInside();
    }

    internal void MoveEnd(double seconds)
    {
        EndSeconds = Math.Clamp(seconds, Math.Min(DurationSeconds, StartSeconds + ShortestSeconds), DurationSeconds);
        StartSeconds = Math.Max(StartSeconds, EndSeconds - ReelFormat.MaxDurationSeconds);
        KeepCoverInside();
    }

    internal void Slide(double startSeconds)
    {
        var length = LengthSeconds;
        var start = Math.Clamp(startSeconds, 0d, Math.Max(0d, DurationSeconds - length));
        var shift = start - StartSeconds;
        StartSeconds = start;
        EndSeconds = start + length;
        CoverSeconds += shift;
        KeepCoverInside();
    }

    internal void SetCover(double seconds)
    {
        CoverSeconds = seconds;
        KeepCoverInside();
    }

    internal void SetFraming(ReelFraming framing)
    {
        Framing = framing == ReelFraming.Fill && !CanFill ? ReelFraming.Original : framing;
    }

    internal void SetFillPosition(float position)
    {
        FillPosition = Math.Clamp(position, 0f, 1f);
    }

    // The crop window travels the part of the frame it does not cover, so a drag of the whole travel moves the
    // position from one edge to the other.
    internal void DragFill(float pixels, float frameExtent)
    {
        if (Crop is not { } crop || frameExtent <= 0f)
        {
            return;
        }

        var window = FillsHorizontally ? crop.Width : crop.Height;
        var travel = (1f - window) * frameExtent;
        if (travel <= 0f)
        {
            return;
        }

        SetFillPosition(FillPosition + pixels / travel);
    }

    private readonly double ShortestSeconds => Math.Min(MinimumLengthSeconds, DurationSeconds);

    private void KeepCoverInside()
    {
        CoverSeconds = Math.Clamp(CoverSeconds, StartSeconds, Math.Max(StartSeconds, EndSeconds));
    }
}
