namespace Aetherphone.Core.Reels;

// Re-encoding never adds detail, so the target follows the source down to a floor where H.264 at reel sizes still
// holds up. A low bitrate source otherwise came out several times larger than it went in.
internal static class ReelBitrate
{
    internal const int FloorBitsPerSecond = 500_000;
    internal const int AssumedAudioBitsPerSecond = 128_000;

    internal static int Cap(long sourceVideoBitsPerSecond)
    {
        if (sourceVideoBitsPerSecond <= 0)
        {
            return ReelFormat.VideoBitsPerSecond;
        }

        return (int)Math.Clamp(sourceVideoBitsPerSecond, FloorBitsPerSecond, ReelFormat.VideoBitsPerSecond);
    }

    internal static long SourceVideoBitsPerSecond(in ReelMediaInfo input)
    {
        if (input.VideoBitsPerSecond > 0)
        {
            return input.VideoBitsPerSecond;
        }

        if (input.FileBytes <= 0 || input.DurationSeconds <= 0d)
        {
            return 0;
        }

        var overall = input.FileBytes * 8d / input.DurationSeconds;
        var audio = input.AudioCodec is null
            ? 0
            : input.AudioBitsPerSecond > 0
                ? input.AudioBitsPerSecond
                : AssumedAudioBitsPerSecond;
        return (long)Math.Max(0d, overall - audio);
    }
}
