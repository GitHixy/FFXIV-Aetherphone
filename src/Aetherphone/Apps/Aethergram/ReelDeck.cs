using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;
using Aetherphone.Core.Reels;
using Aetherphone.Core.Social;

namespace Aetherphone.Apps.Aethergram;

// The Reels tab and a reel opened from elsewhere each keep their own server session, so paging one never takes
// reels out of the other.
internal sealed class ReelDeck
{
    private readonly object gate = new();
    private ReelPaging paging;
    private int generation;

    internal FeedLane<PostDto> Lane { get; } = FeedLane<PostDto>.ServerOrdered();

    internal ReelWatchQueue Watches { get; } = new();

    internal ReelWatchLedger Play;

    internal int Position;

    internal string? CurrentId;

    internal PostDto[] Items => Lane.Items;

    internal bool Loading
    {
        get
        {
            lock (gate)
            {
                return paging.Loading;
            }
        }
    }

    internal bool Exhausted
    {
        get
        {
            lock (gate)
            {
                return paging.Exhausted;
            }
        }
    }

    internal string? Session
    {
        get
        {
            lock (gate)
            {
                return paging.Session;
            }
        }
    }

    internal bool TryBeginFetch(long nowMilliseconds, out int fetchGeneration, out string? session, out string? cursor)
    {
        lock (gate)
        {
            fetchGeneration = generation;
            session = paging.Session;
            cursor = paging.Cursor;
            if (!paging.Wants(Position, Lane.Items.Length, nowMilliseconds))
            {
                return false;
            }

            paging.Begin();
            return true;
        }
    }

    internal void AcceptPage(int fetchGeneration, ReelFeedPage page)
    {
        lock (gate)
        {
            if (fetchGeneration != generation)
            {
                return;
            }

            var before = Lane.Items.Length;
            Lane.ApplyMore(page.Items, null);
            paging.Accept(page.Session, page.NextCursor, Lane.Items.Length - before);
        }
    }

    internal void FailPage(int fetchGeneration, AepFailure failure, long nowMilliseconds)
    {
        lock (gate)
        {
            if (fetchGeneration != generation)
            {
                return;
            }

            paging.Fail(nowMilliseconds);
            Lane.RecordFailure(failure);
        }
    }

    internal void Reset()
    {
        lock (gate)
        {
            generation++;
            paging.Reset();
            Lane.Clear();
            Lane.ClearFailure();
            Position = 0;
            CurrentId = null;
            Play = default;
            Watches.Clear();
        }
    }

    internal void Seed(PostDto[] reels)
    {
        lock (gate)
        {
            generation++;
            paging.Reset();
            Lane.Clear();
            Lane.ClearFailure();
            Lane.Items = reels;
            Position = 0;
            CurrentId = reels.Length > 0 ? reels[0].Id : null;
        }
    }

    internal void Remove(string postId)
    {
        Lane.Items = CopyOnWrite.RemoveById(Lane.Items, postId);
    }

    // Keeps the place on the reel being watched when the list under it changes; a removed reel gives way to the one
    // that followed it.
    internal PostDto? Resolve()
    {
        var items = Lane.Items;
        if (items.Length == 0)
        {
            Position = 0;
            CurrentId = null;
            return null;
        }

        if ((uint)Position < (uint)items.Length && string.Equals(items[Position].Id, CurrentId, StringComparison.Ordinal))
        {
            return items[Position];
        }

        if (CurrentId is { } currentId)
        {
            for (var index = 0; index < items.Length; index++)
            {
                if (string.Equals(items[index].Id, currentId, StringComparison.Ordinal))
                {
                    Position = index;
                    return items[index];
                }
            }
        }

        Position = Math.Clamp(Position, 0, items.Length - 1);
        CurrentId = items[Position].Id;
        return items[Position];
    }

    internal void MoveTo(int position)
    {
        var items = Lane.Items;
        if (items.Length == 0)
        {
            return;
        }

        Position = Math.Clamp(position, 0, items.Length - 1);
        CurrentId = items[Position].Id;
    }
}
