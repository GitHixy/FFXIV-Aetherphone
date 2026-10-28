namespace Aetherphone.Core.Reels;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

internal readonly record struct ReelCrop(float X, float Y, float Width, float Height)
{
    internal static readonly ReelCrop Whole = new(0f, 0f, 1f, 1f);

    internal static ReelCrop FillPortrait(int displayWidth, int displayHeight, float position)
    {
        var placement = Math.Clamp(position, 0f, 1f);
        var sourceAspect = (float)displayWidth / displayHeight;
        if (sourceAspect > ReelFormat.TallestAspect)
        {
            var width = ReelFormat.TallestAspect / sourceAspect;
            return new ReelCrop((1f - width) * placement, 0f, width, 1f);
        }

        var height = sourceAspect / ReelFormat.TallestAspect;
        return new ReelCrop(0f, (1f - height) * placement, 1f, height);
    }
}

internal readonly record struct ReelLayout(
    int Width,
    int Height,
    PixelRect Content,
    float SourceX,
    float SourceY,
    float SourceWidth,
    float SourceHeight)
{
    internal static ReelLayout Plan(int displayWidth, int displayHeight, ReelCrop? crop)
    {
        var window = crop ?? ReelCrop.Whole;
        var left = Math.Clamp(window.X, 0f, 1f);
        var top = Math.Clamp(window.Y, 0f, 1f);
        var sourceX = left * displayWidth;
        var sourceY = top * displayHeight;
        var sourceWidth = Math.Clamp(window.Width, 0f, 1f - left) * displayWidth;
        var sourceHeight = Math.Clamp(window.Height, 0f, 1f - top) * displayHeight;
        if (sourceWidth < 2f || sourceHeight < 2f)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), "The crop window is smaller than two pixels.");
        }

        var aspect = sourceWidth / sourceHeight;
        if (aspect > ReelFormat.WidestAspect)
        {
            var width = Even(MathF.Min(ReelFormat.MaxLongSide, sourceWidth));
            var height = Even(width / ReelFormat.WidestAspect);
            var contentHeight = Math.Min(height, Even(width / aspect));
            var offset = (height - contentHeight) / 4 * 2;
            return new ReelLayout(width, height, new PixelRect(0, offset, width, contentHeight), sourceX, sourceY,
                sourceWidth, sourceHeight);
        }

        if (aspect < ReelFormat.TallestAspect)
        {
            var height = Even(MathF.Min(ReelFormat.MaxLongSide, sourceHeight));
            var width = Even(height * ReelFormat.TallestAspect);
            var contentWidth = Math.Min(width, Even(height * aspect));
            var offset = (width - contentWidth) / 4 * 2;
            return new ReelLayout(width, height, new PixelRect(offset, 0, contentWidth, height), sourceX, sourceY,
                sourceWidth, sourceHeight);
        }

        var shrink = MathF.Min(1f, ReelFormat.MaxLongSide / MathF.Max(sourceWidth, sourceHeight));
        var fittedWidth = Even(sourceWidth * shrink);
        var fittedHeight = Even(sourceHeight * shrink);
        return new ReelLayout(fittedWidth, fittedHeight, new PixelRect(0, 0, fittedWidth, fittedHeight), sourceX,
            sourceY, sourceWidth, sourceHeight);
    }

    internal static int Even(float value) => Math.Max(2, (int)MathF.Round(value / 2f) * 2);
}

internal readonly record struct ReelCadence(int Numerator, int Denominator)
{
    internal static ReelCadence For(int sourceNumerator, int sourceDenominator)
    {
        if (sourceNumerator <= 0 || sourceDenominator <= 0)
        {
            return new ReelCadence(ReelFormat.FrameRate, 1);
        }

        var rate = (double)sourceNumerator / sourceDenominator;
        return rate > ReelFormat.FrameRate + ReelFormat.FrameRateTolerance
            ? new ReelCadence(ReelFormat.FrameRate, 1)
            : new ReelCadence(sourceNumerator, sourceDenominator);
    }

    internal double FramesPerSecond => (double)Numerator / Denominator;

    // A frame a tenth of a slot early still lands in that slot, so a 60 fps source keeps its even frames
    // instead of flickering between neighbours on timestamp rounding.
    internal long SlotOf(long ticks) =>
        (ticks * Numerator + ReelFormat.TicksPerSecond * Denominator / 10) / (ReelFormat.TicksPerSecond * Denominator);

    internal long TimeOf(long slot) => slot * ReelFormat.TicksPerSecond * Denominator / Numerator;
}
