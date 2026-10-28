using Aetherphone.Core.Runtime;
using Aetherphone.Core.Video;
using Dalamud.Plugin.Services;

namespace Aetherphone.Core.Reels;

// Nothing draws while the phone is hidden, so the framework tick pauses the shared player once no surface has
// presented for a moment, and closes it after a minute away so three idle mpv instances never sit in memory.
internal sealed class ReelPlayback : IDisposable
{
    private const int RetiredWaitMilliseconds = 3000;

    private readonly MediaDependencies dependencies;
    private readonly PhoneVisibility visibility;
    private readonly Func<bool> otherVideoPlaying;
    private readonly ReelSource[] shown = new ReelSource[ReelPlayer.Depth];
    private readonly List<ReelPlayer> retired = [];
    private readonly List<Task> retiring = [];
    private int shownCount;
    private ReelPlayer? player;
    private long lastPresentedMilliseconds;
    private bool presenting;

    internal ReelPlayback(MediaDependencies dependencies, PhoneVisibility visibility, Func<bool> otherVideoPlaying)
    {
        this.dependencies = dependencies;
        this.visibility = visibility;
        this.otherVideoPlaying = otherVideoPlaying;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    internal event Action? Hidden;

    internal MediaDependencies Dependencies => dependencies;

    internal bool LibraryMissing => dependencies.VideoLibraryPath is null;

    internal bool CanPlay => !LibraryMissing && DxHandler.Device is not null;

    internal bool OtherVideoPlaying => otherVideoPlaying();

    internal bool PhoneVisible => visibility.IsVisible;

    internal ReelProgress Progress => player?.CurrentProgress ?? default;

    internal ReelFrame FrameOf(string id) => player?.FrameOf(id) ?? default;

    internal string? FailureOf(string id) => player?.FailureOf(id);

    internal void Present(ReadOnlySpan<ReelSource> window, bool play, bool muted)
    {
        lastPresentedMilliseconds = Environment.TickCount64;
        presenting = true;
        if (!CanPlay)
        {
            return;
        }

        var reels = player ??= new ReelPlayer((int)MathF.Round(Plugin.Cfg.VideoVolume * 100f),
            Plugin.Cfg.VideoHardwareDecoding);
        if (reels.Paused == play)
        {
            reels.SetPaused(!play);
        }

        if (reels.Muted != muted)
        {
            reels.SetMuted(muted);
        }

        if (!Matches(window))
        {
            Remember(window);
            reels.Show(window);
        }

        reels.Update();
    }

    internal void Suspend()
    {
        if (player is { Paused: false } reels)
        {
            reels.SetPaused(true);
        }

        if (!presenting)
        {
            return;
        }

        presenting = false;
        Hidden?.Invoke();
    }

    // A fresh player reloads every slot, which also recovers from a stream mpv gave up on.
    internal void Restart()
    {
        Release();
    }

    private bool Matches(ReadOnlySpan<ReelSource> window)
    {
        if (window.Length != shownCount)
        {
            return false;
        }

        for (var index = 0; index < shownCount; index++)
        {
            if (!string.Equals(window[index].Id, shown[index].Id, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void Remember(ReadOnlySpan<ReelSource> window)
    {
        shownCount = Math.Min(window.Length, shown.Length);
        for (var index = 0; index < shownCount; index++)
        {
            shown[index] = window[index];
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (presenting && !ReelPauseRules.SurfaceShown(now, lastPresentedMilliseconds))
        {
            Suspend();
        }

        if (player is not null && !presenting && ReelPauseRules.ShouldRelease(now, lastPresentedMilliseconds))
        {
            Release();
        }

        PruneRetired();
    }

    private void Release()
    {
        if (player is not { } closing)
        {
            return;
        }

        player = null;
        shownCount = 0;
        retired.Add(closing);
        retiring.Add(closing.CloseAsync());
    }

    private void PruneRetired()
    {
        for (var index = retiring.Count - 1; index >= 0; index--)
        {
            if (!retiring[index].IsCompleted)
            {
                continue;
            }

            retiring.RemoveAt(index);
            retired.RemoveAt(index);
        }
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        player?.Dispose();
        player = null;
        if (retiring.Count > 0)
        {
            _ = Task.WaitAll(retiring.ToArray(), RetiredWaitMilliseconds);
        }

        for (var index = 0; index < retired.Count; index++)
        {
            retired[index].Dispose();
        }

        retired.Clear();
        retiring.Clear();
    }
}
