using Aetherphone.Core.Media;

namespace Aetherphone.Core.Reels;

internal readonly record struct ReelProbeResult(ReelTranscodeFailure Failure, ReelMediaInfo Info)
{
    // The editor needs a known length to trim against and a picture to frame, so either missing refuses the file
    // before any conversion work starts.
    internal ReelTranscodeFailure Verdict
    {
        get
        {
            if (Failure != ReelTranscodeFailure.None)
            {
                return Failure;
            }

            if (Info.DisplayWidth <= 0 || Info.DisplayHeight <= 0)
            {
                return ReelTranscodeFailure.NoVideo;
            }

            return Info.DurationSeconds > 0d ? ReelTranscodeFailure.None : ReelTranscodeFailure.UnknownDuration;
        }
    }
}

internal readonly record struct ReelFramesResult(ReelTranscodeFailure Failure, PixelImage[] Frames);

internal readonly record struct ReelPosterResult(ReelTranscodeFailure Failure, byte[] Jpeg, int Width, int Height)
{
    internal bool Succeeded => Failure == ReelTranscodeFailure.None && Jpeg.Length > 0;
}

// The seam between the reel flow and Windows Media Foundation, so the composer and the share pipeline run against a
// fake where Media Foundation does not exist. Implementations never throw; every failure comes back typed.
internal interface IReelMedia
{
    Task<ReelProbeResult> ProbeAsync(string path, CancellationToken token);

    Task<ReelFramesResult> ThumbnailsAsync(string path, double[] seconds, int longSide, bool exact,
        CancellationToken token);

    Task<ReelPosterResult> PosterAsync(string path, double seconds, ReelCrop? crop, CancellationToken token);

    Task<ReelTranscodeResult> TranscodeAsync(ReelTranscodeRequest request, IProgress<float>? progress,
        CancellationToken token);
}
