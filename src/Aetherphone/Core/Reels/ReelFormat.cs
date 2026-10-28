namespace Aetherphone.Core.Reels;

internal static class ReelFormat
{
    internal const double MaxDurationSeconds = 90d;
    internal const int MaxLongSide = 1280;
    internal const int FrameRate = 30;
    internal const double FrameRateTolerance = 0.5d;
    internal const int VideoBitsPerSecond = 2_500_000;
    internal const float TallestAspect = 9f / 16f;
    internal const float WidestAspect = 1.91f;
    internal const long TicksPerSecond = 10_000_000;

    // 128 kbps; the Windows AAC encoder only accepts 12000, 16000, 20000 or 24000 bytes per second.
    internal const int AudioBytesPerSecond = 16_000;
}
