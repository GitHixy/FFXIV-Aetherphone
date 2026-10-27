using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReelLayoutTests
{
    [Theory]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(2560, 1440, 1280, 720)]
    [InlineData(1080, 1920, 720, 1280)]
    [InlineData(640, 360, 640, 360)]
    public void KeepsAnInRangeAspectAndCapsTheLongSide(int sourceWidth, int sourceHeight, int width, int height)
    {
        var layout = ReelLayout.Plan(sourceWidth, sourceHeight, null);

        Assert.Equal(width, layout.Width);
        Assert.Equal(height, layout.Height);
        Assert.Equal(new PixelRect(0, 0, width, height), layout.Content);
    }

    [Fact]
    public void LetterboxesUltrawideIntoTheWidestAllowedFrame()
    {
        var layout = ReelLayout.Plan(2560, 1080, null);

        Assert.Equal(1280, layout.Width);
        Assert.Equal(670, layout.Height);
        Assert.Equal(new PixelRect(0, 64, 1280, 540), layout.Content);
    }

    [Fact]
    public void PillarboxesATallScreenRecordingIntoNineBySixteen()
    {
        var layout = ReelLayout.Plan(1080, 2340, null);

        Assert.Equal(720, layout.Width);
        Assert.Equal(1280, layout.Height);
        Assert.Equal(new PixelRect(64, 0, 590, 1280), layout.Content);
    }

    [Theory]
    [InlineData(1920, 1080, 608, 1080)]
    [InlineData(2560, 1440, 720, 1280)]
    public void FillsNineBySixteenFromALandscapeClip(int sourceWidth, int sourceHeight, int width, int height)
    {
        var crop = ReelCrop.FillPortrait(sourceWidth, sourceHeight, 0.5f);

        var layout = ReelLayout.Plan(sourceWidth, sourceHeight, crop);

        Assert.Equal(width, layout.Width);
        Assert.Equal(height, layout.Height);
        Assert.Equal(sourceHeight, layout.SourceHeight, 2);
        Assert.Equal((sourceWidth - layout.SourceWidth) / 2f, layout.SourceX, 2);
    }

    [Fact]
    public void FillWindowSlidesAcrossTheWholeClip()
    {
        var left = ReelCrop.FillPortrait(1920, 1080, 0f);
        var right = ReelCrop.FillPortrait(1920, 1080, 1f);

        Assert.Equal(0f, left.X);
        Assert.Equal(1f, right.X + right.Width, 5);
        Assert.Equal(1f, left.Height);
    }

    [Fact]
    public void KeepsTheEvenFramesOfASixtyFrameSource()
    {
        var cadence = ReelCadence.For(60, 1);
        var kept = new List<int>();
        var lastSlot = -1L;
        for (var frame = 0; frame < 12; frame++)
        {
            var slot = cadence.SlotOf((long)Math.Round(frame * 10_000_000d / 60d));
            if (slot > lastSlot)
            {
                kept.Add(frame);
                lastSlot = slot;
            }
        }

        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10 }, kept);
        Assert.Equal(30, cadence.Numerator);
        Assert.Equal(1, cadence.Denominator);
    }

    [Theory]
    [InlineData(24, 1)]
    [InlineData(30000, 1001)]
    [InlineData(30, 1)]
    public void KeepsSourcesAtOrBelowThirtyAtTheirOwnRate(int numerator, int denominator)
    {
        var cadence = ReelCadence.For(numerator, denominator);

        Assert.Equal(numerator, cadence.Numerator);
        Assert.Equal(denominator, cadence.Denominator);
    }

    [Fact]
    public void StampsSlotsWithoutDrift()
    {
        var cadence = ReelCadence.For(60000, 1001);

        Assert.Equal(900_000_000L, cadence.TimeOf(2700));
    }
}
