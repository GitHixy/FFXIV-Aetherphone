using Aetherphone.Core.Media;

namespace Aetherphone.Core.Reels;

internal sealed class WindowsReelMedia : IReelMedia
{
    private const int PosterQuality = 90;
    private const double Megabit = 1_000_000d;
    private const double Megabyte = 1024d * 1024d;

    public Task<ReelProbeResult> ProbeAsync(string path, CancellationToken token) =>
        Task.Run(() =>
        {
            try
            {
                return new ReelProbeResult(ReelTranscodeFailure.None, ReelMediaProbe.Read(path));
            }
            catch (ReelTranscodeException exception)
            {
                AepLog.Info($"[Reels] probe refused {Path.GetFileName(path)} ({exception.Failure}): {exception.Message}");
                return new ReelProbeResult(exception.Failure, default);
            }
            catch (Exception exception)
            {
                AepLog.Warning(exception, $"[Reels] probe failed for {Path.GetFileName(path)}");
                return new ReelProbeResult(ReelTranscodeFailure.UnreadableInput, default);
            }
        }, CancellationToken.None);

    public Task<ReelFramesResult> ThumbnailsAsync(string path, double[] seconds, int longSide, bool exact,
        CancellationToken token) =>
        Task.Run(() =>
        {
            try
            {
                return new ReelFramesResult(ReelTranscodeFailure.None,
                    ReelFrameGrabber.Thumbnails(path, seconds, longSide, exact, token));
            }
            catch (OperationCanceledException)
            {
                return new ReelFramesResult(ReelTranscodeFailure.Cancelled, Array.Empty<PixelImage>());
            }
            catch (ReelTranscodeException exception)
            {
                AepLog.Info($"[Reels] frames refused ({exception.Failure}): {exception.Message}");
                return new ReelFramesResult(exception.Failure, Array.Empty<PixelImage>());
            }
            catch (Exception exception)
            {
                AepLog.Warning(exception, "[Reels] frames failed");
                return new ReelFramesResult(ReelTranscodeFailure.UnreadableInput, Array.Empty<PixelImage>());
            }
        }, CancellationToken.None);

    public Task<ReelPosterResult> PosterAsync(string path, double seconds, ReelCrop? crop, CancellationToken token) =>
        Task.Run(() =>
        {
            try
            {
                var frame = ReelFrameGrabber.Poster(path, seconds, crop, token);
                if (frame.IsEmpty)
                {
                    return new ReelPosterResult(ReelTranscodeFailure.EncodeFailed, Array.Empty<byte>(), 0, 0);
                }

                var jpeg = ScalarJpegEncoder.Encode(frame.Pixels, frame.Width, frame.Height, PosterQuality,
                    JpegChromaSubsampling.Ratio420);
                return new ReelPosterResult(ReelTranscodeFailure.None, jpeg, frame.Width, frame.Height);
            }
            catch (OperationCanceledException)
            {
                return new ReelPosterResult(ReelTranscodeFailure.Cancelled, Array.Empty<byte>(), 0, 0);
            }
            catch (ReelTranscodeException exception)
            {
                AepLog.Info($"[Reels] cover refused ({exception.Failure}): {exception.Message}");
                return new ReelPosterResult(exception.Failure, Array.Empty<byte>(), 0, 0);
            }
            catch (Exception exception)
            {
                AepLog.Warning(exception, "[Reels] cover failed");
                return new ReelPosterResult(ReelTranscodeFailure.EncodeFailed, Array.Empty<byte>(), 0, 0);
            }
        }, CancellationToken.None);

    public async Task<ReelTranscodeResult> TranscodeAsync(ReelTranscodeRequest request, IProgress<float>? progress,
        CancellationToken token)
    {
        var result = await ReelTranscoder.TranscodeAsync(request, progress, token).ConfigureAwait(false);
        var report = result.Report;
        if (!result.Succeeded)
        {
            AepLog.Info($"[Reels] conversion stopped ({result.Failure}) after {report.ElapsedSeconds:F1} s: {result.Detail}");
            return result;
        }

        AepLog.Info($"[Reels] converted {report.TrimSeconds:F1} s to {report.Layout.Width}x{report.Layout.Height} "
            + $"in {report.ElapsedSeconds:F1} s, video target {report.VideoBitsPerSecond / Megabit:F2} Mbps "
            + $"(source {report.SourceVideoBitsPerSecond / Megabit:F2} Mbps), {report.OutputBytes / Megabyte:F1} MB, "
            + $"encoder {report.EncoderChain}");
        return result;
    }
}
