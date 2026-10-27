using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class Nv12ScalerTests
{
    [Fact]
    public void CopiesAnUnscaledFrameExactlyThroughAPaddedPitch()
    {
        const int width = 8;
        const int height = 4;
        const int pitch = 16;
        var luma = new byte[pitch * height];
        var chroma = new byte[pitch * height / 2];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                luma[y * pitch + x] = (byte)(y * 30 + x * 3);
            }
        }

        for (var index = 0; index < chroma.Length; index++)
        {
            chroma[index] = (byte)(index * 7);
        }

        var (outputLuma, outputChroma) = Render(luma, chroma, pitch, new PixelRect(0, 0, width, height),
            new ReelLayout(width, height, new PixelRect(0, 0, width, height), 0f, 0f, width, height), 0);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal(luma[y * pitch + x], outputLuma[y * width + x]);
            }
        }

        for (var y = 0; y < height / 2; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal(chroma[y * pitch + x], outputChroma[y * width + x]);
            }
        }
    }

    [Fact]
    public void AveragesNeighboursWhenHalving()
    {
        var luma = new byte[16];
        for (var index = 0; index < luma.Length; index++)
        {
            luma[index] = (byte)(index % 2 == 0 ? 0 : 100);
        }

        var chroma = Enumerable.Repeat((byte)128, 8).ToArray();
        var layout = new ReelLayout(2, 2, new PixelRect(0, 0, 2, 2), 0f, 0f, 4f, 4f);

        var (outputLuma, _) = Render(luma, chroma, 4, new PixelRect(0, 0, 4, 4), layout, 0);

        Assert.All(outputLuma, value => Assert.Equal(50, value));
    }

    [Fact]
    public void LetterboxesAnUltrawideClipWithBlackBars()
    {
        const int width = 32;
        const int height = 8;
        var luma = Enumerable.Repeat((byte)200, width * height).ToArray();
        var chroma = Enumerable.Repeat((byte)90, width * height / 2).ToArray();
        var layout = ReelLayout.Plan(width, height, null);

        var (outputLuma, outputChroma) = Render(luma, chroma, width, new PixelRect(0, 0, width, height), layout, 0);

        var content = layout.Content;
        for (var y = 0; y < layout.Height; y++)
        {
            var inside = y >= content.Y && y < content.Y + content.Height;
            Assert.Equal(inside ? 200 : Nv12Scaler.BlackLuma, outputLuma[y * layout.Width]);
        }

        for (var y = 0; y < layout.Height / 2; y++)
        {
            var inside = y >= content.Y / 2 && y < (content.Y + content.Height) / 2;
            Assert.Equal(inside ? 90 : Nv12Scaler.NeutralChroma, outputChroma[y * layout.Width]);
        }
    }

    [Fact]
    public void RotatesAQuarterTurnClockwise()
    {
        const int storedWidth = 4;
        const int storedHeight = 2;
        var luma = new byte[storedWidth * storedHeight];
        for (var y = 0; y < storedHeight; y++)
        {
            for (var x = 0; x < storedWidth; x++)
            {
                luma[y * storedWidth + x] = (byte)(10 * y + x);
            }
        }

        var chroma = Enumerable.Repeat((byte)128, storedWidth * storedHeight / 2).ToArray();
        var layout = new ReelLayout(2, 4, new PixelRect(0, 0, 2, 4), 0f, 0f, 2f, 4f);

        var (outputLuma, _) = Render(luma, chroma, storedWidth, new PixelRect(0, 0, storedWidth, storedHeight),
            layout, 90);

        for (var displayY = 0; displayY < 4; displayY++)
        {
            for (var displayX = 0; displayX < 2; displayX++)
            {
                var storedX = displayY;
                var storedY = storedHeight - 1 - displayX;
                Assert.Equal(luma[storedY * storedWidth + storedX], outputLuma[displayY * 2 + displayX]);
            }
        }
    }

    [Fact]
    public void SamplesOnlyInsideTheVisibleAperture()
    {
        const int pitch = 8;
        const int codedHeight = 6;
        var luma = Enumerable.Repeat((byte)255, pitch * codedHeight).ToArray();
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                luma[y * pitch + x] = 60;
            }
        }

        var chroma = Enumerable.Repeat((byte)128, pitch * codedHeight / 2).ToArray();
        var layout = new ReelLayout(8, 8, new PixelRect(0, 0, 8, 8), 0f, 0f, 8f, 4f);

        var (outputLuma, _) = Render(luma, chroma, pitch, new PixelRect(0, 0, 8, 4), layout, 0);

        Assert.All(outputLuma, value => Assert.Equal(60, value));
    }

    private static (byte[] Luma, byte[] Chroma) Render(byte[] luma, byte[] chroma, int pitch, PixelRect visible,
        ReelLayout layout, int rotation)
    {
        var outputLuma = new byte[layout.Width * layout.Height];
        var outputChroma = new byte[layout.Width * layout.Height / 2];
        Nv12Scaler.Render(new Nv12Source(luma, chroma, pitch, visible),
            new Nv12Target(outputLuma, outputChroma, layout.Width, layout.Height), layout, rotation);
        return (outputLuma, outputChroma);
    }
}
