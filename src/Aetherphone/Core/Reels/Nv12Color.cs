namespace Aetherphone.Core.Reels;

// Limited range YCbCr to RGBA in 8.8 fixed point. HD sources are BT.709 and SD sources BT.601 in practice, so the
// matrix follows the picture size when the stream does not say.
internal static class Nv12Color
{
    private const int HighDefinitionHeight = 720;
    private const int Shift = 8;
    private const int Rounding = 1 << (Shift - 1);
    private const int LumaScale = 298;

    private static readonly Coefficients Rec709 = new(459, 55, 136, 541);
    private static readonly Coefficients Rec601 = new(409, 100, 208, 516);

    internal static bool UsesRec709(int displayWidth, int displayHeight) =>
        Math.Min(displayWidth, displayHeight) >= HighDefinitionHeight;

    internal static void ToRgba(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> chroma, int width, int height,
        bool rec709, Span<byte> rgba)
    {
        var matrix = rec709 ? Rec709 : Rec601;
        for (var row = 0; row < height; row++)
        {
            var lumaRow = luma.Slice(row * width, width);
            var chromaRow = chroma.Slice(row / 2 * width, width);
            var target = rgba.Slice(row * width * 4, width * 4);
            for (var column = 0; column < width; column++)
            {
                var chromaColumn = column & ~1;
                var y = (lumaRow[column] - 16) * LumaScale;
                var u = chromaRow[chromaColumn] - 128;
                var v = chromaRow[chromaColumn + 1] - 128;
                var offset = column * 4;
                target[offset] = Clamp((y + matrix.RedFromV * v + Rounding) >> Shift);
                target[offset + 1] = Clamp((y - matrix.GreenFromU * u - matrix.GreenFromV * v + Rounding) >> Shift);
                target[offset + 2] = Clamp((y + matrix.BlueFromU * u + Rounding) >> Shift);
                target[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, byte.MaxValue);

    private readonly record struct Coefficients(int RedFromV, int GreenFromU, int GreenFromV, int BlueFromU);
}
