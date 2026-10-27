using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Aetherphone.Core.Video;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace Aetherphone.Core.Reels;

internal readonly record struct ReelSource(string Id, string Location, int Width, int Height);

internal readonly record struct ReelFrame(nint TextureHandle, int Width, int Height)
{
    internal bool HasFrame => TextureHandle != nint.Zero;
}

internal readonly record struct ReelProgress(double PositionSeconds, double DurationSeconds);

internal readonly record struct ReelSlotStatus(
    string? Id,
    bool Current,
    long LoadStartedTimestamp,
    long FirstFrameTimestamp,
    long ActivatedTimestamp,
    long MotionTimestamp,
    int FrameVersion,
    string? Failure);

// Call every member from the UI draw thread: mpv commands and texture work run on per-slot workers, so the draw
// never waits on mpv, and a surface is only swapped or released between frames.
internal sealed class ReelPlayer : IDisposable
{
    internal const int Depth = 3;

    private const int DefaultWidth = 720;
    private const int DefaultHeight = 1280;
    private const double MotionThresholdSeconds = 0.05d;
    private const int RetireWaitMilliseconds = 3000;
    private static readonly TimeSpan RenderThreadTimeout = TimeSpan.FromSeconds(5);

    private static int surfaceCount;

    private readonly ReelSlot[] slots = new ReelSlot[Depth];
    private readonly ConcurrentQueue<ReelSlotEvent> events = new();
    private readonly List<Task> retiring = [];
    private readonly int volume;
    private readonly bool hardwareDecoding;
    private int currentSlot = -1;
    private bool paused;
    private bool muted;
    private volatile bool disposed;

    internal ReelPlayer(int volume, bool hardwareDecoding)
    {
        this.volume = Math.Clamp(volume, 0, 100);
        this.hardwareDecoding = hardwareDecoding;
        for (var index = 0; index < Depth; index++)
        {
            slots[index] = new ReelSlot(index);
        }
    }

    internal bool Paused => paused;

    internal bool Muted => muted;

    internal ReelFrame CurrentFrame
    {
        get
        {
            if (currentSlot < 0)
            {
                return default;
            }

            var slot = slots[currentSlot];
            return slot.Surface is { } surface && slot.FirstFrameTimestamp != 0
                ? new ReelFrame(surface.Handle, surface.Width, surface.Height)
                : default;
        }
    }

    internal ReelProgress CurrentProgress
    {
        get
        {
            if (currentSlot < 0 || slots[currentSlot].Surface is not { } surface)
            {
                return default;
            }

            return new ReelProgress(surface.Renderer.ObservedPositionSeconds, surface.Renderer.ObservedDurationSeconds);
        }
    }

    internal int CurrentFrameVersion =>
        currentSlot >= 0 && slots[currentSlot].Surface is { } surface ? surface.Renderer.FrameVersion : 0;

    internal ReelSlotStatus SlotStatus(int slotIndex)
    {
        var slot = slots[slotIndex];
        return new ReelSlotStatus(slot.Generation > 0 ? slot.Source.Id : null, slotIndex == currentSlot,
            slot.LoadStartedTimestamp, slot.FirstFrameTimestamp, slot.ActivatedTimestamp, slot.MotionTimestamp,
            slot.Surface?.Renderer.FrameVersion ?? 0, slot.Failure);
    }

    internal void Show(ReadOnlySpan<ReelSource> window)
    {
        var count = Math.Min(window.Length, Depth);
        Span<int> assigned = stackalloc int[Depth];
        Span<bool> claimed = stackalloc bool[Depth];
        Span<bool> reused = stackalloc bool[Depth];
        assigned.Fill(-1);
        for (var position = 0; position < count; position++)
        {
            var match = FindSlot(window[position].Id, claimed);
            if (match < 0)
            {
                continue;
            }

            assigned[position] = match;
            claimed[match] = true;
            reused[position] = true;
        }

        for (var position = 0; position < count; position++)
        {
            if (assigned[position] >= 0)
            {
                continue;
            }

            var free = FirstUnclaimed(claimed);
            assigned[position] = free;
            claimed[free] = true;
            Load(slots[free], window[position], position == 0);
        }

        for (var position = 0; position < count; position++)
        {
            if (!reused[position])
            {
                continue;
            }

            if (position == 0)
            {
                Activate(slots[assigned[position]]);
            }
            else
            {
                Park(slots[assigned[position]]);
            }
        }

        for (var slotIndex = 0; slotIndex < Depth; slotIndex++)
        {
            if (!claimed[slotIndex] && slots[slotIndex].Generation > 0)
            {
                Park(slots[slotIndex]);
            }
        }

        currentSlot = count > 0 ? assigned[0] : -1;
    }

    internal void SetPaused(bool value)
    {
        paused = value;
        if (currentSlot >= 0)
        {
            Command(slots[currentSlot], !paused, muted, rewind: false);
        }
    }

    internal void SetMuted(bool value)
    {
        muted = value;
        if (currentSlot >= 0)
        {
            Command(slots[currentSlot], !paused, muted, rewind: false);
        }
    }

    internal void Update()
    {
        var now = Stopwatch.GetTimestamp();
        for (var index = 0; index < Depth; index++)
        {
            var slot = slots[index];
            AdoptPending(slot);
            Stamp(slot, now);
        }

        while (events.TryDequeue(out var reelEvent))
        {
            var slot = slots[reelEvent.SlotIndex];
            if (reelEvent.Generation == slot.Generation)
            {
                slot.Failure = reelEvent.Detail;
            }
        }

        for (var index = retiring.Count - 1; index >= 0; index--)
        {
            if (retiring[index].IsCompleted)
            {
                retiring.RemoveAt(index);
            }
        }
    }

    private int FindSlot(string id, Span<bool> claimed)
    {
        for (var slotIndex = 0; slotIndex < Depth; slotIndex++)
        {
            var slot = slots[slotIndex];
            if (!claimed[slotIndex] && slot.Generation > 0 && string.Equals(slot.Source.Id, id, StringComparison.Ordinal))
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private static int FirstUnclaimed(Span<bool> claimed)
    {
        for (var slotIndex = 0; slotIndex < claimed.Length; slotIndex++)
        {
            if (!claimed[slotIndex])
            {
                return slotIndex;
            }
        }

        return 0;
    }

    private void Load(ReelSlot slot, in ReelSource source, bool active)
    {
        var now = Stopwatch.GetTimestamp();
        slot.Source = source;
        slot.Generation++;
        slot.Failure = null;
        slot.LoadStartedTimestamp = now;
        slot.FirstFrameTimestamp = 0;
        slot.ActivatedTimestamp = active ? now : 0;
        slot.MotionTimestamp = 0;
        slot.Work = LoadAsync(slot.Work, slot, source, slot.Generation, active, paused, muted);
    }

    private void Activate(ReelSlot slot)
    {
        slot.ActivatedTimestamp = Stopwatch.GetTimestamp();
        slot.MotionTimestamp = 0;
        Command(slot, !paused, muted, rewind: false);
    }

    private void Park(ReelSlot slot)
    {
        slot.ActivatedTimestamp = 0;
        slot.MotionTimestamp = 0;
        Command(slot, playing: false, silent: true, rewind: true);
    }

    private void Command(ReelSlot slot, bool playing, bool silent, bool rewind) =>
        slot.Work = CommandAsync(slot.Work, slot, slot.Generation, playing, silent, rewind);

    private async Task CommandAsync(Task previous, ReelSlot slot, int generation, bool playing, bool silent,
        bool rewind)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.SuppressThrowing);
        if (disposed || generation != Volatile.Read(ref slot.Generation) || LatestSurface(slot) is not { } surface)
        {
            return;
        }

        surface.Renderer.Pause(!playing);
        surface.Renderer.SetMuted(silent);
        if (rewind)
        {
            surface.Renderer.Seek(0d);
        }
    }

    private async Task LoadAsync(Task previous, ReelSlot slot, ReelSource source, int generation, bool active,
        bool pausedNow, bool mutedNow)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.SuppressThrowing);
        if (disposed || generation != Volatile.Read(ref slot.Generation))
        {
            return;
        }

        try
        {
            var width = DefaultWidth;
            var height = DefaultHeight;
            if (source.Width > 0 && source.Height > 0)
            {
                var shrink = MathF.Min(1f, (float)ReelFormat.MaxLongSide / Math.Max(source.Width, source.Height));
                width = ReelLayout.Even(source.Width * shrink);
                height = ReelLayout.Even(source.Height * shrink);
            }

            var surface = LatestSurface(slot);
            if (surface is null || surface.Width != width || surface.Height != height)
            {
                surface = await CreateSurfaceAsync(slot, width, height).ConfigureAwait(false);
                if (!Publish(slot, surface))
                {
                    return;
                }
            }

            surface.Renderer.SetMuted(!active || mutedNow);
            Volatile.Write(ref slot.FrameVersionAtPlay, surface.Renderer.FrameVersion);
            if (!surface.Renderer.Play(source.Location, 0d, active && !pausedNow))
            {
                throw new InvalidOperationException("mpv refused to load the reel.");
            }

            Volatile.Write(ref slot.PlayedGeneration, generation);
        }
        catch (Exception exception)
        {
            events.Enqueue(new ReelSlotEvent(slot.Index, generation, exception.Message));
        }
    }

    private bool Publish(ReelSlot slot, ReelSurface surface)
    {
        ReelSurface? displaced;
        lock (slot)
        {
            if (disposed)
            {
                displaced = surface;
            }
            else
            {
                displaced = slot.Pending;
                slot.Pending = surface;
            }
        }

        if (displaced is not null)
        {
            displaced.Retire();
        }

        return displaced != surface;
    }

    private void AdoptPending(ReelSlot slot)
    {
        ReelSurface? previous;
        lock (slot)
        {
            if (slot.Pending is not { } pending)
            {
                return;
            }

            previous = slot.Surface;
            slot.Surface = pending;
            slot.Pending = null;
        }

        if (previous is not null)
        {
            retiring.Add(Task.Run(previous.Retire));
        }
    }

    private static ReelSurface? LatestSurface(ReelSlot slot)
    {
        lock (slot)
        {
            return slot.Pending ?? slot.Surface;
        }
    }

    private void Stamp(ReelSlot slot, long now)
    {
        if (slot.Surface is not { } surface || Volatile.Read(ref slot.PlayedGeneration) != slot.Generation)
        {
            return;
        }

        if (slot.FirstFrameTimestamp == 0 && surface.Renderer.FrameVersion != Volatile.Read(ref slot.FrameVersionAtPlay))
        {
            slot.FirstFrameTimestamp = now;
        }

        if (slot.ActivatedTimestamp != 0 && slot.MotionTimestamp == 0
            && surface.Renderer.ObservedPositionSeconds > MotionThresholdSeconds)
        {
            slot.MotionTimestamp = now;
        }
    }

    private async Task<ReelSurface> CreateSurfaceAsync(ReelSlot slot, int width, int height)
    {
        var key = "reel-" + Interlocked.Increment(ref surfaceCount).ToString(CultureInfo.InvariantCulture);
        var created = new TaskCompletionSource<(Texture2D Texture, ShaderResourceView View)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DxHandler.RunOnRenderThread(key + "-create", () => CreateTexture(created, width, height));
        Texture2D texture;
        ShaderResourceView view;
        try
        {
            (texture, view) = await created.Task.WaitAsync(RenderThreadTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = created.Task.ContinueWith(late => ReelSurface.ReleaseOnRenderThread(key, late.Result.Texture,
                late.Result.View), TaskContinuationOptions.OnlyOnRanToCompletion);
            throw new TimeoutException("The render thread did not create the reel texture in time.");
        }

        var renderer = new MpvRenderer(key);
        try
        {
            renderer.Initialize(width, height, texture, null, hardwareDecoding, ReelFormat.MaxLongSide, false,
                volume);
            renderer.SetLooping(true);
            renderer.ObserveProgress();
            renderer.FileEnded += ending => OnFileEnded(slot, ending);
            return new ReelSurface(key, renderer, texture, view, width, height);
        }
        catch
        {
            renderer.Dispose();
            ReelSurface.ReleaseOnRenderThread(key, texture, view);
            throw;
        }
    }

    private static void CreateTexture(TaskCompletionSource<(Texture2D Texture, ShaderResourceView View)> created,
        int width, int height)
    {
        try
        {
            var device = DxHandler.Device ?? throw new InvalidOperationException("Direct3D is not ready.");
            var texture = new Texture2D(device, new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                OptionFlags = ResourceOptionFlags.None,
            });
            created.SetResult((texture, new ShaderResourceView(device, texture)));
        }
        catch (Exception exception)
        {
            created.SetException(exception);
        }
    }

    private void OnFileEnded(ReelSlot slot, PlaybackEnding ending)
    {
        if (ending.Reason != MpvEndReason.Failed)
        {
            return;
        }

        events.Enqueue(new ReelSlotEvent(slot.Index, Volatile.Read(ref slot.PlayedGeneration),
            ending.Detail ?? "mpv could not play the reel."));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        for (var index = 0; index < Depth; index++)
        {
            var slot = slots[index];
            ReelSurface? surface;
            ReelSurface? pending;
            lock (slot)
            {
                surface = slot.Surface;
                pending = slot.Pending;
                slot.Surface = null;
                slot.Pending = null;
            }

            surface?.DisposeNow();
            pending?.DisposeNow();
        }

        if (retiring.Count > 0)
        {
            _ = Task.WaitAll(retiring.ToArray(), RetireWaitMilliseconds);
        }

        retiring.Clear();
        currentSlot = -1;
    }

    private readonly record struct ReelSlotEvent(int SlotIndex, int Generation, string Detail);

    private sealed class ReelSlot
    {
        internal ReelSlot(int index)
        {
            Index = index;
        }

        internal int Index { get; }

        internal ReelSource Source;
        internal ReelSurface? Surface;
        internal ReelSurface? Pending;
        internal Task Work = Task.CompletedTask;
        internal int Generation;
        internal int PlayedGeneration;
        internal int FrameVersionAtPlay;
        internal long LoadStartedTimestamp;
        internal long FirstFrameTimestamp;
        internal long ActivatedTimestamp;
        internal long MotionTimestamp;
        internal string? Failure;
    }

    private sealed class ReelSurface
    {
        private readonly string key;
        private readonly Texture2D texture;
        private readonly ShaderResourceView view;

        internal ReelSurface(string key, MpvRenderer renderer, Texture2D texture, ShaderResourceView view, int width,
            int height)
        {
            this.key = key;
            this.texture = texture;
            this.view = view;
            Renderer = renderer;
            Width = width;
            Height = height;
            Handle = view.NativePointer;
        }

        internal MpvRenderer Renderer { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal nint Handle { get; }

        internal void Retire()
        {
            Renderer.Dispose();
            ReleaseOnRenderThread(key, texture, view);
        }

        internal void DisposeNow()
        {
            Renderer.Dispose();
            view.Dispose();
            texture.Dispose();
        }

        internal static void ReleaseOnRenderThread(string key, Texture2D texture, ShaderResourceView view) =>
            DxHandler.RunOnRenderThread(key + "-release", () =>
            {
                view.Dispose();
                texture.Dispose();
            });
    }
}
