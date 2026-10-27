namespace Aetherphone.Core.Reels;

internal readonly ref struct Nv12Source
{
    internal readonly ReadOnlySpan<byte> Luma;
    internal readonly ReadOnlySpan<byte> Chroma;
    internal readonly int Pitch;
    internal readonly PixelRect Visible;

    internal Nv12Source(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> chroma, int pitch, PixelRect visible)
    {
        Luma = luma;
        Chroma = chroma;
        Pitch = pitch;
        Visible = visible;
    }
}

internal readonly ref struct Nv12Target
{
    internal readonly Span<byte> Luma;
    internal readonly Span<byte> Chroma;
    internal readonly int Width;
    internal readonly int Height;

    internal Nv12Target(Span<byte> luma, Span<byte> chroma, int width, int height)
    {
        Luma = luma;
        Chroma = chroma;
        Width = width;
        Height = height;
    }
}

internal static class Nv12Scaler
{
    internal const byte BlackLuma = 16;
    internal const byte NeutralChroma = 128;

    private const int WeightOne = 256;
    private const int WeightShift = 16;
    private const int WeightRounding = 1 << (WeightShift - 1);

    internal static void Render(in Nv12Source source, in Nv12Target target, in ReelLayout layout,
        int clockwiseRotation)
    {
        var content = layout.Content;
        if (content.Width != target.Width || content.Height != target.Height)
        {
            target.Luma[..(target.Width * target.Height)].Fill(BlackLuma);
            target.Chroma[..(target.Width * target.Height / 2)].Fill(NeutralChroma);
        }

        var displayToStored = DisplayToStored(clockwiseRotation, source.Visible.Width, source.Visible.Height);
        var contentToDisplay = Matrix3x2.CreateScale(layout.SourceWidth / content.Width,
                layout.SourceHeight / content.Height)
            * Matrix3x2.CreateTranslation(layout.SourceX, layout.SourceY);

        var lumaMap = Matrix3x2.CreateTranslation(0.5f - content.X, 0.5f - content.Y)
            * contentToDisplay
            * displayToStored
            * Matrix3x2.CreateTranslation(source.Visible.X - 0.5f, source.Visible.Y - 0.5f);
        SampleLuma(source, target, content, lumaMap);

        var chromaMap = Matrix3x2.CreateScale(2f)
            * Matrix3x2.CreateTranslation(1f - content.X, 1f - content.Y)
            * contentToDisplay
            * displayToStored
            * Matrix3x2.CreateScale(0.5f)
            * Matrix3x2.CreateTranslation(source.Visible.X * 0.5f - 0.5f, source.Visible.Y * 0.5f - 0.5f);
        SampleChroma(source, target, content, chromaMap);
    }

    internal static Matrix3x2 DisplayToStored(int clockwiseRotation, int storedWidth, int storedHeight) =>
        clockwiseRotation switch
        {
            90 => new Matrix3x2(0f, -1f, 1f, 0f, 0f, storedHeight),
            180 => new Matrix3x2(-1f, 0f, 0f, -1f, storedWidth, storedHeight),
            270 => new Matrix3x2(0f, 1f, -1f, 0f, storedWidth, 0f),
            _ => Matrix3x2.Identity,
        };

    private static void SampleLuma(in Nv12Source source, in Nv12Target target, PixelRect content, in Matrix3x2 map)
    {
        var minimumX = source.Visible.X;
        var maximumX = source.Visible.X + source.Visible.Width - 1;
        var minimumY = source.Visible.Y;
        var maximumY = source.Visible.Y + source.Visible.Height - 1;
        for (var row = 0; row < content.Height; row++)
        {
            var targetY = content.Y + row;
            var start = Vector2.Transform(new Vector2(content.X, targetY), map);
            var targetRow = target.Luma.Slice(targetY * target.Width + content.X, content.Width);
            for (var column = 0; column < targetRow.Length; column++)
            {
                var sampleX = Math.Clamp(start.X + column * map.M11, minimumX, maximumX);
                var sampleY = Math.Clamp(start.Y + column * map.M12, minimumY, maximumY);
                targetRow[column] = Bilinear(source.Luma, source.Pitch, 1, 0, sampleX, sampleY, maximumX, maximumY);
            }
        }
    }

    private static void SampleChroma(in Nv12Source source, in Nv12Target target, PixelRect content,
        in Matrix3x2 map)
    {
        var minimumX = source.Visible.X / 2;
        var maximumX = (source.Visible.X + source.Visible.Width) / 2 - 1;
        var minimumY = source.Visible.Y / 2;
        var maximumY = (source.Visible.Y + source.Visible.Height) / 2 - 1;
        var firstColumn = content.X / 2;
        var columns = content.Width / 2;
        for (var row = 0; row < content.Height / 2; row++)
        {
            var targetY = content.Y / 2 + row;
            var start = Vector2.Transform(new Vector2(firstColumn, targetY), map);
            var targetRow = target.Chroma.Slice(targetY * target.Width + firstColumn * 2, columns * 2);
            for (var column = 0; column < columns; column++)
            {
                var sampleX = Math.Clamp(start.X + column * map.M11, minimumX, maximumX);
                var sampleY = Math.Clamp(start.Y + column * map.M12, minimumY, maximumY);
                targetRow[column * 2] = Bilinear(source.Chroma, source.Pitch, 2, 0, sampleX, sampleY, maximumX,
                    maximumY);
                targetRow[column * 2 + 1] = Bilinear(source.Chroma, source.Pitch, 2, 1, sampleX, sampleY, maximumX,
                    maximumY);
            }
        }
    }

    private static byte Bilinear(ReadOnlySpan<byte> plane, int pitch, int elementStride, int channel, float x,
        float y, int maximumX, int maximumY)
    {
        var left = (int)x;
        var top = (int)y;
        var right = Math.Min(left + 1, maximumX);
        var bottom = Math.Min(top + 1, maximumY);
        var weightX = (int)((x - left) * WeightOne);
        var weightY = (int)((y - top) * WeightOne);
        var upperRow = top * pitch + channel;
        var lowerRow = bottom * pitch + channel;
        var upper = plane[upperRow + left * elementStride] * (WeightOne - weightX)
            + plane[upperRow + right * elementStride] * weightX;
        var lower = plane[lowerRow + left * elementStride] * (WeightOne - weightX)
            + plane[lowerRow + right * elementStride] * weightX;
        return (byte)((upper * (WeightOne - weightY) + lower * weightY + WeightRounding) >> WeightShift);
    }
}
