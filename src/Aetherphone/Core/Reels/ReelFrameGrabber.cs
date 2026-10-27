using Aetherphone.Core.Media;
using NAudio.MediaFoundation;

namespace Aetherphone.Core.Reels;

// Decodes single frames through the same NV12 path as the transcoder, so a cover drawn from the source lands on
// exactly the pixels and frame shape the reel will have.
internal static class ReelFrameGrabber
{
    private const int MaxSamplesPerSeek = 900;
    private const long FrameSlackTicks = ReelFormat.TicksPerSecond / 100;

    internal static PixelImage[] Thumbnails(string path, ReadOnlySpan<double> seconds, int longSide, bool exact,
        CancellationToken token)
    {
        var frames = new PixelImage[seconds.Length];
        Decode(path, seconds, exact, new FramePlan(longSide, null, false), frames, token);
        return frames;
    }

    internal static PixelImage Poster(string path, double seconds, ReelCrop? crop, CancellationToken token)
    {
        var frames = new PixelImage[1];
        Span<double> times = stackalloc double[1];
        times[0] = seconds;
        Decode(path, times, true, new FramePlan(0, crop, true), frames, token);
        return frames[0];
    }

    internal static ReelLayout Fit(int displayWidth, int displayHeight, int longSide)
    {
        var shrink = MathF.Min(1f, (float)longSide / Math.Max(displayWidth, displayHeight));
        var width = ReelLayout.Even(displayWidth * shrink);
        var height = ReelLayout.Even(displayHeight * shrink);
        return new ReelLayout(width, height, new PixelRect(0, 0, width, height), 0f, 0f, displayWidth, displayHeight);
    }

    private static void Decode(string path, ReadOnlySpan<double> seconds, bool exact, FramePlan plan,
        PixelImage[] frames, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.MediaFoundationMissing,
                "Media Foundation only exists on Windows.");
        }

        ReelTranscoder.EnsureReadable(path);
        using (ReelTranscoder.StartMediaFoundation())
        {
            var reader = ReelTranscoder.OpenReader(path, advancedVideoProcessing: true);
            try
            {
                var input = ReelMediaProbe.Describe(reader, path, out var streams);
                if (streams.Video < 0)
                {
                    throw new ReelTranscodeException(ReelTranscodeFailure.NoVideo, "The file has no video track.");
                }

                reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
                reader.SetStreamSelection(streams.Video, true);
                ReelVideoGeometry.RequestNv12(reader, streams.Video, input.VideoCodec);
                var geometry = ReelVideoGeometry.Read(reader, streams.Video, input, out _);
                var layout = plan.AsOutput
                    ? ReelLayout.Plan(geometry.DisplayWidth, geometry.DisplayHeight, plan.Crop)
                    : Fit(geometry.DisplayWidth, geometry.DisplayHeight, plan.LongSide);
                var rec709 = Nv12Color.UsesRec709(geometry.DisplayWidth, geometry.DisplayHeight);
                var target = new byte[layout.Width * layout.Height * 3 / 2];
                for (var index = 0; index < seconds.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var ticks = (long)Math.Round(Math.Max(0d, seconds[index]) * ReelFormat.TicksPerSecond);
                    MediaFoundationVideo.Seek(reader, ticks);
                    if (!ReadFrameAt(reader, streams.Video, input, ticks, exact, ref geometry, layout, target, token))
                    {
                        frames[index] = index > 0 ? frames[index - 1] : PixelImage.Empty;
                        continue;
                    }

                    frames[index] = ToImage(target, layout, rec709);
                }
            }
            finally
            {
                MediaFoundationVideo.Release(reader);
            }
        }
    }

    // Only the frame that lands is scaled; the one before it is held so a time past the last frame still shows the
    // clip's final picture.
    private static bool ReadFrameAt(IMFSourceReader reader, int stream, in ReelMediaInfo input, long ticks,
        bool exact, ref ReelVideoGeometry geometry, in ReelLayout layout, byte[] target, CancellationToken token)
    {
        IMFSample? held = null;
        try
        {
            for (var attempt = 0; attempt < MaxSamplesPerSeek; attempt++)
            {
                token.ThrowIfCancellationRequested();
                reader.ReadSample(stream, 0, out _, out var flags, out var timestamp, out var sample);
                var status = (int)flags;
                if ((status & MediaFoundationVideo.ReaderError) != 0)
                {
                    MediaFoundationVideo.Release(sample);
                    throw new ReelTranscodeException(ReelTranscodeFailure.UnreadableInput,
                        "The source reader stopped with an error while decoding a frame.");
                }

                if ((status & MediaFoundationVideo.ReaderCurrentTypeChanged) != 0)
                {
                    geometry = ReelVideoGeometry.Read(reader, stream, input, out _);
                }

                if (sample is null)
                {
                    if ((status & MediaFoundationVideo.ReaderEndOfStream) == 0)
                    {
                        continue;
                    }

                    if (held is null)
                    {
                        return false;
                    }

                    Render(held, geometry, layout, target);
                    return true;
                }

                if (!exact || (long)timestamp + FrameSlackTicks >= ticks)
                {
                    try
                    {
                        Render(sample, geometry, layout, target);
                    }
                    finally
                    {
                        MediaFoundationVideo.Release(sample);
                    }

                    return true;
                }

                MediaFoundationVideo.Release(held);
                held = sample;
            }

            if (held is null)
            {
                return false;
            }

            Render(held, geometry, layout, target);
            return true;
        }
        finally
        {
            MediaFoundationVideo.Release(held);
        }
    }

    private static unsafe void Render(IMFSample sample, in ReelVideoGeometry geometry, in ReelLayout layout,
        byte[] target)
    {
        IMFMediaBuffer? input = null;
        try
        {
            sample.GetBufferCount(out var bufferCount);
            if (bufferCount == 1)
            {
                sample.GetBufferByIndex(0, out input);
            }
            else
            {
                sample.ConvertToContiguousBuffer(out input);
            }

            using var locked = new ReelLockedFrame(input, geometry);
            var decoded = locked.Planes(geometry);
            var lumaBytes = layout.Width * layout.Height;
            fixed (byte* pointer = target)
            {
                var planes = new Nv12Planes((nint)pointer, lumaBytes, (nint)pointer + lumaBytes, lumaBytes / 2,
                    layout.Width);
                _ = Nv12Scaler.Render(decoded.AsSource(geometry.Visible), planes.AsTarget(layout.Width, layout.Height),
                    layout, geometry.ClockwiseRotation);
            }
        }
        finally
        {
            MediaFoundationVideo.Release(input);
        }
    }

    private static PixelImage ToImage(byte[] nv12, in ReelLayout layout, bool rec709)
    {
        var lumaBytes = layout.Width * layout.Height;
        var pixels = new byte[lumaBytes * PixelImage.BytesPerPixel];
        Nv12Color.ToRgba(nv12.AsSpan(0, lumaBytes), nv12.AsSpan(lumaBytes), layout.Width, layout.Height, rec709,
            pixels);
        return new PixelImage(pixels, layout.Width, layout.Height);
    }

    private readonly record struct FramePlan(int LongSide, ReelCrop? Crop, bool AsOutput);
}
