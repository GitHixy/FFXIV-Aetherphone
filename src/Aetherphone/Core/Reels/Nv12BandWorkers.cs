using System.Runtime.ExceptionServices;

namespace Aetherphone.Core.Reels;

// Helpers are capped at half the cores (six at most) and run below normal priority, so a transcode leaves the game
// its CPU. Bands are even rows so a chroma row never straddles two workers.
internal sealed class Nv12BandWorkers : IDisposable
{
    private const int MaxWorkers = 6;

    private readonly Thread[] helpers;
    private readonly Barrier? barrier;
    private Nv12Planes source;
    private PixelRect visible;
    private Nv12Planes target;
    private int width;
    private int height;
    private ReelLayout layout;
    private int rotation;
    private int bandRows;
    private Exception? failure;
    private volatile bool stopping;

    internal Nv12BandWorkers(int workerCount)
    {
        WorkerCount = Math.Max(1, workerCount);
        helpers = new Thread[WorkerCount - 1];
        if (helpers.Length == 0)
        {
            return;
        }

        barrier = new Barrier(WorkerCount);
        for (var index = 0; index < helpers.Length; index++)
        {
            helpers[index] = new Thread(HelperLoop)
            {
                IsBackground = true,
                Name = "aetherphone-reel-scale-" + (index + 1),
                Priority = ThreadPriority.BelowNormal,
            };
            helpers[index].Start(index + 1);
        }
    }

    internal static int DefaultWorkerCount => Math.Clamp(Environment.ProcessorCount / 2, 1, MaxWorkers);

    internal int WorkerCount { get; }

    internal Nv12RenderPath Render(in Nv12Planes framePlanes, PixelRect frameVisible, in Nv12Planes targetPlanes,
        int targetWidth, int targetHeight, in ReelLayout frameLayout, int clockwiseRotation)
    {
        if (Nv12Scaler.IsPassThrough(frameLayout, frameVisible, clockwiseRotation))
        {
            Nv12Scaler.Copy(framePlanes.AsSource(frameVisible), targetPlanes.AsTarget(targetWidth, targetHeight));
            return Nv12RenderPath.Copy;
        }

        if (barrier is null)
        {
            Nv12Scaler.RenderBand(framePlanes.AsSource(frameVisible), targetPlanes.AsTarget(targetWidth, targetHeight),
                frameLayout, clockwiseRotation, 0, targetHeight);
            return Nv12RenderPath.Sampled;
        }

        source = framePlanes;
        visible = frameVisible;
        target = targetPlanes;
        width = targetWidth;
        height = targetHeight;
        layout = frameLayout;
        rotation = clockwiseRotation;
        bandRows = ((targetHeight + WorkerCount - 1) / WorkerCount + 1) & ~1;
        failure = null;
        barrier.SignalAndWait();
        RenderBand(0);
        barrier.SignalAndWait();
        if (failure is { } error)
        {
            ExceptionDispatchInfo.Throw(error);
        }

        return Nv12RenderPath.Sampled;
    }

    private void HelperLoop(object? state)
    {
        var band = (int)state!;
        while (true)
        {
            barrier!.SignalAndWait();
            if (stopping)
            {
                return;
            }

            RenderBand(band);
            barrier.SignalAndWait();
        }
    }

    private void RenderBand(int band)
    {
        var firstRow = band * bandRows;
        var endRow = Math.Min(height, firstRow + bandRows);
        if (firstRow >= endRow)
        {
            return;
        }

        try
        {
            Nv12Scaler.RenderBand(source.AsSource(visible), target.AsTarget(width, height), layout, rotation,
                firstRow, endRow);
        }
        catch (Exception exception)
        {
            _ = Interlocked.CompareExchange(ref failure, exception, null);
        }
    }

    public void Dispose()
    {
        if (barrier is null)
        {
            return;
        }

        stopping = true;
        barrier.SignalAndWait();
        for (var index = 0; index < helpers.Length; index++)
        {
            helpers[index].Join();
        }

        barrier.Dispose();
    }
}
