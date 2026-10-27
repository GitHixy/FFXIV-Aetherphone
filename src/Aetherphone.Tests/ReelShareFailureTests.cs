using Aetherphone.Core.Media;
using Aetherphone.Core.Net;
using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReelShareFailureTests
{
    private static AepFailure Server(int status, string? code = null) =>
        new(AepFailureKind.Server, status, code, null, null, null);

    [Theory]
    [InlineData((int)AepFailureKind.Offline, true)]
    [InlineData((int)AepFailureKind.Timeout, true)]
    [InlineData((int)AepFailureKind.RateLimitPaused, true)]
    [InlineData((int)AepFailureKind.BadResponse, true)]
    [InlineData((int)AepFailureKind.SignedOut, false)]
    public void TransportFailuresDuringUploadRetry(int kindValue, bool retryable)
    {
        var kind = (AepFailureKind)kindValue;
        Assert.Equal(retryable, ReelShareFailure.Uploading(AepFailure.Transport(kind)).Retryable);
    }

    [Theory]
    [InlineData(502, null, true)]
    [InlineData(429, "rate_limited", true)]
    [InlineData(429, "post_cooldown", true)]
    [InlineData(400, "media_unsupported_type", false)]
    [InlineData(403, "forbidden", false)]
    public void ServerFailuresRetryOnlyWhenTheyPass(int status, string? code, bool retryable)
    {
        Assert.Equal(retryable, ReelShareFailure.Creating(Server(status, code)).Retryable);
    }

    [Fact]
    public void OnlyACreateThatFailedInTransitKeepsItsUploads()
    {
        Assert.True(ReelShareFailure.Creating(AepFailure.Transport(AepFailureKind.Timeout)).KeepsUpload);
        Assert.True(ReelShareFailure.Creating(Server(429, "post_cooldown")).KeepsUpload);
        Assert.False(ReelShareFailure.Creating(Server(400, ReelErrorCodes.MissingVideo)).KeepsUpload);
        Assert.False(ReelShareFailure.Uploading(AepFailure.Transport(AepFailureKind.Offline)).KeepsUpload);
    }

    [Fact]
    public void EveryTranscodeFailureHasItsOwnMessage()
    {
        var failures = Enum.GetValues<ReelTranscodeFailure>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < failures.Length; index++)
        {
            if (failures[index] is ReelTranscodeFailure.None or ReelTranscodeFailure.Cancelled)
            {
                continue;
            }

            Assert.True(seen.Add(ReelShareText.ForTranscode(failures[index]).Key), failures[index].ToString());
        }
    }

    [Fact]
    public void EveryReelErrorCodeHasItsOwnMessage()
    {
        var codes = new[]
        {
            ReelErrorCodes.MissingVideo, ReelErrorCodes.TooBig, ReelErrorCodes.NotMp4, ReelErrorCodes.NotFaststart,
            ReelErrorCodes.HeaderTooLarge, ReelErrorCodes.Fragmented, ReelErrorCodes.NoVideoTrack,
            ReelErrorCodes.VideoCodec, ReelErrorCodes.AudioCodec, ReelErrorCodes.TooLong, ReelErrorCodes.NoDuration,
            ReelErrorCodes.Resolution, ReelErrorCodes.Aspect, ReelErrorCodes.MediaInUse, ReelErrorCodes.PosterInvalid,
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < codes.Length; index++)
        {
            var text = ReelShareText.Server(Server(400, codes[index]));
            Assert.False(string.IsNullOrWhiteSpace(text), codes[index]);
            Assert.True(seen.Add(text), codes[index]);
        }
    }

    [Fact]
    public void ProbeRefusesAFileWithoutALengthOrAPicture()
    {
        var info = new ReelMediaInfo("video/mp4", 1000, 12d, "H264", 1920, 1080, 1920, 1080, 0, 30, 1, null, 0, 0, 0,
            0);

        Assert.Equal(ReelTranscodeFailure.None, new ReelProbeResult(ReelTranscodeFailure.None, info).Verdict);
        Assert.Equal(ReelTranscodeFailure.UnknownDuration,
            new ReelProbeResult(ReelTranscodeFailure.None, info with { DurationSeconds = 0d }).Verdict);
        Assert.Equal(ReelTranscodeFailure.NoVideo,
            new ReelProbeResult(ReelTranscodeFailure.None, info with { DisplayWidth = 0 }).Verdict);
        Assert.Equal(ReelTranscodeFailure.UnreadableInput,
            new ReelProbeResult(ReelTranscodeFailure.UnreadableInput, default).Verdict);
    }

    [Fact]
    public void LimitedRangeBlackAndWhiteMapToTheEnds()
    {
        var luma = new byte[] { 16, 16, 235, 235 };
        var chroma = new byte[] { 128, 128, 128, 128 };
        var rgba = new byte[16];

        Nv12Color.ToRgba(luma, chroma, 2, 2, true, rgba);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, rgba[..4]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, rgba[8..12]);
        Assert.Equal(PixelImage.BytesPerPixel * 4, rgba.Length);
    }
}
