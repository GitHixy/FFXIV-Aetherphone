using Aetherphone.Core.Aethernet;

namespace Aetherphone.Core.Social;

internal sealed class FeedSignalQueue
{
    public const int BatchMax = 40;

    public const int PendingCap = 200;

    private const int FlushAtCount = 12;

    private const double FlushSeconds = 4d;

    private readonly Func<string[], int[], CancellationToken, Task<bool>> sendSeen;
    private readonly Func<string, int, int, CancellationToken, Task<bool>> sendSignal;
    private readonly StoreWork work;
    private readonly Dictionary<string, PostViewSource> pending = new(StringComparer.Ordinal);
    private readonly HashSet<(string PostId, int Kind)> signalled = new();
    private readonly object gate = new();
    private DateTime lastFlush = DateTime.UtcNow;
    private bool flushing;

    public FeedSignalQueue(
        Func<string[], int[], CancellationToken, Task<bool>> sendSeen,
        Func<string, int, int, CancellationToken, Task<bool>> sendSignal,
        StoreWork work)
    {
        this.sendSeen = sendSeen;
        this.sendSignal = sendSignal;
        this.work = work;
    }

    public int PendingCount
    {
        get
        {
            lock (gate)
            {
                return pending.Count;
            }
        }
    }

    // The first surface to report a post keeps it: the server attributes a view to where it was
    // first seen, so a later surface must not overwrite the source of one still waiting to go out.
    public void MarkSeen(string postId, PostViewSource source)
    {
        if (postId.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            if (pending.Count >= PendingCap && !pending.ContainsKey(postId))
            {
                return;
            }

            pending.TryAdd(postId, source);
        }
    }

    public void Signal(string postId, int kind, PostViewSource source)
    {
        if (postId.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            if (!signalled.Add((postId, kind)))
            {
                return;
            }
        }

        work.Run("feed signal", token => sendSignal(postId, kind, (int)source, token));
    }

    public void Tick(DateTime now)
    {
        Dispatch(now, false);
    }

    public void Flush(DateTime now)
    {
        Dispatch(now, true);
    }

    public async Task FlushAsync(CancellationToken token)
    {
        while (true)
        {
            SeenBatch batch;
            lock (gate)
            {
                if (pending.Count == 0)
                {
                    return;
                }

                batch = Take();
                lastFlush = DateTime.UtcNow;
            }

            if (await sendSeen(batch.PostIds, batch.Sources, token).ConfigureAwait(false))
            {
                continue;
            }

            lock (gate)
            {
                Requeue(batch);
            }

            return;
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            pending.Clear();
            signalled.Clear();
        }
    }

    private void Dispatch(DateTime now, bool force)
    {
        SeenBatch batch;
        lock (gate)
        {
            if (flushing || pending.Count == 0)
            {
                return;
            }

            if (!force && pending.Count < FlushAtCount && (now - lastFlush).TotalSeconds < FlushSeconds)
            {
                return;
            }

            batch = Take();
            flushing = true;
            lastFlush = now;
        }

        work.Run("feed seen", token => sendSeen(batch.PostIds, batch.Sources, token), sent =>
        {
            lock (gate)
            {
                flushing = false;
                if (!sent)
                {
                    Requeue(batch);
                }
            }
        });
    }

    private void Requeue(SeenBatch batch)
    {
        for (var index = 0; index < batch.PostIds.Length; index++)
        {
            if (pending.Count < PendingCap)
            {
                pending.TryAdd(batch.PostIds[index], (PostViewSource)batch.Sources[index]);
            }
        }
    }

    private SeenBatch Take()
    {
        var count = pending.Count < BatchMax ? pending.Count : BatchMax;
        var postIds = new string[count];
        var sources = new int[count];
        var index = 0;
        foreach (var entry in pending)
        {
            postIds[index] = entry.Key;
            sources[index] = (int)entry.Value;
            index++;
            if (index == count)
            {
                break;
            }
        }

        for (var removed = 0; removed < count; removed++)
        {
            pending.Remove(postIds[removed]);
        }

        return new SeenBatch(postIds, sources);
    }

    private readonly record struct SeenBatch(string[] PostIds, int[] Sources);
}
