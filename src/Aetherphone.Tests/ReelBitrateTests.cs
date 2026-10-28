using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReelBitrateTests
{
    private static ReelMediaInfo Info(long fileBytes, double seconds, int videoBits = 0, string? audioCodec = "AAC",
        int audioBits = 0) =>
        new("video/mp4", fileBytes, seconds, "H264", 1920, 1080, 1920, 1080, 0, 30, 1, audioCodec, 48000, 2,
            videoBits, audioBits);

    [Theory]
    [InlineData(370_000L, ReelBitrate.FloorBitsPerSecond)]
    [InlineData(1_200_000L, 1_200_000)]
    [InlineData(2_500_000L, ReelFormat.VideoBitsPerSecond)]
    [InlineData(18_000_000L, ReelFormat.VideoBitsPerSecond)]
    [InlineData(0L, ReelFormat.VideoBitsPerSecond)]
    public void TheTargetFollowsTheSourceBetweenTheFloorAndTheCeiling(long source, int expected)
    {
        Assert.Equal(expected, ReelBitrate.Cap(source));
    }

    [Fact]
    public void ASmallSourceNeverComesOutAboveTheFloor()
    {
        var target = ReelBitrate.Cap(ReelBitrate.SourceVideoBitsPerSecond(Info(0, 60d, videoBits: 370_000)));

        Assert.Equal(ReelBitrate.FloorBitsPerSecond, target);
        Assert.True(target < 1_730_000);
    }

    [Fact]
    public void TheStreamsOwnBitrateWinsOverTheFileEstimate()
    {
        Assert.Equal(900_000, ReelBitrate.SourceVideoBitsPerSecond(Info(100_000_000, 10d, videoBits: 900_000)));
    }

    [Fact]
    public void TheEstimateTakesTheAudioOutOfTheWholeFile()
    {
        var info = Info(15_000_000, 60d, audioBits: 192_000);

        Assert.Equal(2_000_000L - 192_000L, ReelBitrate.SourceVideoBitsPerSecond(info));
    }

    [Fact]
    public void AnUnknownAudioRateAssumesTheUsualOne()
    {
        var info = Info(15_000_000, 60d);

        Assert.Equal(2_000_000L - ReelBitrate.AssumedAudioBitsPerSecond, ReelBitrate.SourceVideoBitsPerSecond(info));
    }

    [Fact]
    public void ASilentFileCountsEveryBitAsVideo()
    {
        Assert.Equal(2_000_000L, ReelBitrate.SourceVideoBitsPerSecond(Info(15_000_000, 60d, audioCodec: null)));
    }

    [Fact]
    public void NoSizeOrNoLengthMeansUnknown()
    {
        Assert.Equal(0L, ReelBitrate.SourceVideoBitsPerSecond(Info(0, 60d)));
        Assert.Equal(0L, ReelBitrate.SourceVideoBitsPerSecond(Info(15_000_000, 0d)));
    }
}
