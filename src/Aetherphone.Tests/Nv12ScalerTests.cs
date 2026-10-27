using System.Runtime.InteropServices;
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

    [Theory]
    [InlineData("letterbox", 2)]
    [InlineData("letterbox", 16)]
    [InlineData("fill", 3)]
    [InlineData("downscale", 6)]
    [InlineData("rotate90", 4)]
    [InlineData("rotate180", 5)]
    [InlineData("rotate270", 6)]
    [InlineData("aperture", 3)]
    public void BandsMatchTheSingleThreadedPathByteForByte(string scenario, int workers)
    {
        var (frame, layout, rotation) = Scenario(scenario);
        var expectedLuma = new byte[layout.Width * layout.Height];
        var expectedChroma = new byte[layout.Width * layout.Height / 2];
        var expectedPath = Nv12Scaler.Render(frame.Source(), new Nv12Target(expectedLuma, expectedChroma,
            layout.Width, layout.Height), layout, rotation);

        var actualLuma = new byte[expectedLuma.Length];
        var actualChroma = new byte[expectedChroma.Length];
        Nv12RenderPath actualPath;
        using (var bands = new Nv12BandWorkers(workers))
        using (var source = new PinnedPlanes(frame.Luma, frame.Chroma, frame.Pitch))
        using (var target = new PinnedPlanes(actualLuma, actualChroma, layout.Width))
        {
            actualPath = bands.Render(source.Planes, frame.Visible, target.Planes, layout.Width, layout.Height, layout,
                rotation);
        }

        Assert.Equal(Nv12RenderPath.Sampled, expectedPath);
        Assert.Equal(Nv12RenderPath.Sampled, actualPath);
        Assert.Equal(expectedLuma, actualLuma);
        Assert.Equal(expectedChroma, actualChroma);
    }

    [Fact]
    public void CopyPathMatchesTheSampledPath()
    {
        var frame = Frame(80, 36, 80, new PixelRect(2, 2, 60, 32), seed: 7);
        var layout = ReelLayout.Plan(60, 32, null);
        var copiedLuma = new byte[layout.Width * layout.Height];
        var copiedChroma = new byte[copiedLuma.Length / 2];
        var sampledLuma = new byte[copiedLuma.Length];
        var sampledChroma = new byte[copiedChroma.Length];

        var path = Nv12Scaler.Render(frame.Source(), new Nv12Target(copiedLuma, copiedChroma, layout.Width,
            layout.Height), layout, 0);
        Nv12Scaler.RenderBand(frame.Source(), new Nv12Target(sampledLuma, sampledChroma, layout.Width, layout.Height),
            layout, 0, 0, layout.Height);

        Assert.Equal(Nv12RenderPath.Copy, path);
        Assert.Equal(sampledLuma, copiedLuma);
        Assert.Equal(sampledChroma, copiedChroma);
    }

    [Fact]
    public void KeepsTheSampledPathForAnOddAperture()
    {
        var layout = ReelLayout.Plan(60, 32, null);

        Assert.False(Nv12Scaler.IsPassThrough(layout, new PixelRect(1, 0, 60, 32), 0));
        Assert.False(Nv12Scaler.IsPassThrough(layout, new PixelRect(0, 0, 60, 32), 90));
        Assert.True(Nv12Scaler.IsPassThrough(layout, new PixelRect(0, 0, 60, 32), 0));
    }

    private static (TestFrame Frame, ReelLayout Layout, int Rotation) Scenario(string name) => name switch
    {
        "letterbox" => (Frame(96, 24, 96, new PixelRect(0, 0, 96, 24), 1), ReelLayout.Plan(96, 24, null), 0),
        "fill" => (Frame(192, 108, 192, new PixelRect(0, 0, 192, 108), 2),
            ReelLayout.Plan(192, 108, ReelCrop.FillPortrait(192, 108, 0.3f)), 0),
        "downscale" => (Frame(200, 120, 208, new PixelRect(0, 0, 200, 120), 3),
            new ReelLayout(130, 78, new PixelRect(0, 0, 130, 78), 0f, 0f, 200f, 120f), 0),
        "rotate90" => (Frame(120, 64, 128, new PixelRect(0, 0, 120, 64), 4), ReelLayout.Plan(64, 120, null), 90),
        "rotate180" => (Frame(120, 64, 120, new PixelRect(0, 0, 120, 64), 5), ReelLayout.Plan(120, 64, null), 180),
        "rotate270" => (Frame(120, 64, 128, new PixelRect(0, 0, 120, 64), 6), ReelLayout.Plan(64, 120, null), 270),
        "aperture" => (Frame(144, 72, 144, new PixelRect(0, 0, 128, 68), 8),
            new ReelLayout(96, 52, new PixelRect(0, 0, 96, 52), 0f, 0f, 128f, 68f), 0),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static TestFrame Frame(int codedWidth, int codedHeight, int pitch, PixelRect visible, int seed)
    {
        var random = new Random(seed);
        var luma = new byte[pitch * codedHeight];
        var chroma = new byte[pitch * codedHeight / 2];
        random.NextBytes(luma);
        random.NextBytes(chroma);
        return new TestFrame(luma, chroma, pitch, visible);
    }

    private sealed record TestFrame(byte[] Luma, byte[] Chroma, int Pitch, PixelRect Visible)
    {
        internal Nv12Source Source() => new(Luma, Chroma, Pitch, Visible);
    }

    private sealed class PinnedPlanes : IDisposable
    {
        private GCHandle luma;
        private GCHandle chroma;

        internal PinnedPlanes(byte[] lumaBytes, byte[] chromaBytes, int pitch)
        {
            luma = GCHandle.Alloc(lumaBytes, GCHandleType.Pinned);
            chroma = GCHandle.Alloc(chromaBytes, GCHandleType.Pinned);
            Planes = new Nv12Planes(luma.AddrOfPinnedObject(), lumaBytes.Length, chroma.AddrOfPinnedObject(),
                chromaBytes.Length, pitch);
        }

        internal Nv12Planes Planes { get; }

        public void Dispose()
        {
            luma.Free();
            chroma.Free();
        }
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
