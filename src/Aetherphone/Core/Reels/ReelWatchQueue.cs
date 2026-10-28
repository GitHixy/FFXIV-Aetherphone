using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Reels;

// Ended plays wait here and leave together, since the server shares one rate bucket between seen marks, signals and
// watch reports. A swipe sends once a few have gathered or a few seconds have passed; a pause, a hide or a tab change
// sends at once.
internal sealed class ReelWatchQueue
{
    internal const int Capacity = 20;
    internal const int FlushCount = 5;
    internal const long FlushIntervalMilliseconds = 4000;

    private readonly ReelWatchItem[] items = new ReelWatchItem[Capacity];
    private int count;
    private long lastFlushMilliseconds;

    internal int Count => count;

    internal void Add(in ReelWatchItem item)
    {
        if (count == Capacity)
        {
            Array.Copy(items, 1, items, 0, Capacity - 1);
            count--;
        }

        items[count] = item;
        count++;
    }

    internal bool ShouldFlush(long nowMilliseconds, bool urgent) =>
        count > 0 && (urgent || count >= FlushCount || nowMilliseconds - lastFlushMilliseconds >= FlushIntervalMilliseconds);

    internal ReelWatchItem[] Drain(long nowMilliseconds)
    {
        var drained = new ReelWatchItem[count];
        Array.Copy(items, drained, count);
        Array.Clear(items, 0, count);
        count = 0;
        lastFlushMilliseconds = nowMilliseconds;
        return drained;
    }

    internal void Clear()
    {
        Array.Clear(items, 0, count);
        count = 0;
    }
}
