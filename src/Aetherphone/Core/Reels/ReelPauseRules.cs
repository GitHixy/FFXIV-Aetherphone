namespace Aetherphone.Core.Reels;

internal enum ReelCover : byte
{
    None,
    Comments,
    Sheet,
}

internal readonly record struct ReelPlaybackInputs(
    bool PhoneVisible,
    bool SurfaceShown,
    ReelCover Cover,
    bool OtherVideoPlaying,
    bool Held);

internal static class ReelPauseRules
{
    internal const long HiddenAfterMilliseconds = 250;
    internal const long ReleaseAfterMilliseconds = 60_000;

    // Comments keep the reel playing behind them, as on Instagram; every other sheet, a hidden or minimized phone,
    // another app or tab, MogCast playing, or a held finger pauses it.
    internal static bool ShouldPlay(in ReelPlaybackInputs inputs) =>
        inputs.PhoneVisible && inputs.SurfaceShown && inputs.Cover != ReelCover.Sheet && !inputs.OtherVideoPlaying
        && !inputs.Held;

    internal static bool SurfaceShown(long nowMilliseconds, long lastPresentedMilliseconds) =>
        nowMilliseconds - lastPresentedMilliseconds < HiddenAfterMilliseconds;

    internal static bool ShouldRelease(long nowMilliseconds, long lastPresentedMilliseconds) =>
        nowMilliseconds - lastPresentedMilliseconds >= ReleaseAfterMilliseconds;
}
