using System.Runtime.InteropServices;
using NAudio.MediaFoundation;

namespace Aetherphone.Core.Reels;

internal readonly record struct ReelMediaInfo(
    string Container,
    long FileBytes,
    double DurationSeconds,
    string VideoCodec,
    int CodedWidth,
    int CodedHeight,
    int DisplayWidth,
    int DisplayHeight,
    int RotationAttribute,
    int FrameRateNumerator,
    int FrameRateDenominator,
    string? AudioCodec,
    int AudioSampleRate,
    int AudioChannels,
    int VideoBitsPerSecond,
    int AudioBitsPerSecond)
{
    internal double FrameRate => FrameRateDenominator > 0 ? (double)FrameRateNumerator / FrameRateDenominator : 0d;
}

internal readonly record struct ReelStreams(int Video, int Audio);

internal static class ReelMediaProbe
{
    internal static ReelMediaInfo Read(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.MediaFoundationMissing,
                "Media Foundation only exists on Windows.");
        }

        ReelTranscoder.EnsureReadable(path);
        using (ReelTranscoder.StartMediaFoundation())
        {
            var reader = ReelTranscoder.OpenReader(path, advancedVideoProcessing: false);
            try
            {
                return Describe(reader, path, out _);
            }
            finally
            {
                MediaFoundationVideo.Release(reader);
            }
        }
    }

    internal static ReelMediaInfo Describe(IMFSourceReader reader, string path, out ReelStreams streams)
    {
        var video = -1;
        var audio = -1;
        for (var streamIndex = 0; video < 0 || audio < 0; streamIndex++)
        {
            IMFMediaType type;
            try
            {
                reader.GetNativeMediaType(streamIndex, 0, out type);
            }
            catch (COMException exception) when (exception.HResult == MediaFoundationVideo.InvalidStreamNumber)
            {
                break;
            }

            try
            {
                var major = MediaFoundationVideo.GetGuidOrEmpty(type, MediaFoundationAttributes.MF_MT_MAJOR_TYPE);
                if (major == MediaTypes.MFMediaType_Video && video < 0)
                {
                    video = streamIndex;
                }
                else if (major == MediaTypes.MFMediaType_Audio && audio < 0)
                {
                    audio = streamIndex;
                }
            }
            finally
            {
                MediaFoundationVideo.Release(type);
            }
        }

        streams = new ReelStreams(video, audio);
        var container = MediaFoundationVideo.ReadMimeType(reader) ?? Path.GetExtension(path);
        var fileBytes = File.Exists(path) ? new FileInfo(path).Length : 0L;
        var duration = MediaFoundationVideo.ReadDurationTicks(reader) / (double)ReelFormat.TicksPerSecond;
        var info = new ReelMediaInfo(container, fileBytes, duration, "none", 0, 0, 0, 0, 0, 0, 0, null, 0, 0, 0, 0);
        if (video >= 0)
        {
            info = DescribeVideo(reader, video, info);
        }

        if (audio >= 0)
        {
            info = DescribeAudio(reader, audio, info);
        }

        return info;
    }

    private static ReelMediaInfo DescribeVideo(IMFSourceReader reader, int stream, ReelMediaInfo info)
    {
        reader.GetNativeMediaType(stream, 0, out var type);
        try
        {
            var codec = MediaFoundationVideo.CodecName(
                MediaFoundationVideo.GetGuidOrEmpty(type, MediaFoundationAttributes.MF_MT_SUBTYPE));
            _ = MediaFoundationVideo.TryGetUInt64(type, MediaFoundationVideo.FrameSize, out var packedSize);
            var (codedWidth, codedHeight) = MediaFoundationVideo.Unpack(packedSize);
            _ = MediaFoundationVideo.TryGetUInt64(type, MediaFoundationVideo.FrameRate, out var packedRate);
            var (rateNumerator, rateDenominator) = MediaFoundationVideo.Unpack(packedRate);
            _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationVideo.VideoRotation, out var rotation);
            _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationAttributes.MF_MT_AVG_BITRATE, out var bitsPerSecond);
            var visible = MediaFoundationVideo.TryGetAperture(type, out var aperture)
                ? aperture
                : new PixelRect(0, 0, codedWidth, codedHeight);
            var swapped = rotation is 90 or 270;
            return info with
            {
                VideoCodec = codec,
                CodedWidth = codedWidth,
                CodedHeight = codedHeight,
                DisplayWidth = swapped ? visible.Height : visible.Width,
                DisplayHeight = swapped ? visible.Width : visible.Height,
                RotationAttribute = rotation,
                FrameRateNumerator = rateNumerator,
                FrameRateDenominator = rateDenominator,
                VideoBitsPerSecond = Math.Max(0, bitsPerSecond),
            };
        }
        finally
        {
            MediaFoundationVideo.Release(type);
        }
    }

    private static ReelMediaInfo DescribeAudio(IMFSourceReader reader, int stream, ReelMediaInfo info)
    {
        reader.GetNativeMediaType(stream, 0, out var type);
        try
        {
            _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND,
                out var sampleRate);
            _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS,
                out var channels);
            _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationAttributes.MF_MT_AUDIO_AVG_BYTES_PER_SECOND,
                out var bytesPerSecond);
            return info with
            {
                AudioCodec = MediaFoundationVideo.CodecName(
                    MediaFoundationVideo.GetGuidOrEmpty(type, MediaFoundationAttributes.MF_MT_SUBTYPE)),
                AudioSampleRate = sampleRate,
                AudioChannels = channels,
                AudioBitsPerSecond = Math.Max(0, bytesPerSecond) * 8,
            };
        }
        finally
        {
            MediaFoundationVideo.Release(type);
        }
    }
}
