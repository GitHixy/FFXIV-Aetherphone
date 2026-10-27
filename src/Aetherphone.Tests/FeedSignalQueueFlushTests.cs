using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Social;
using Xunit;

namespace Aetherphone.Tests;

public sealed class FeedSignalQueueFlushTests
{
    [Fact]
    public async Task AnAwaitedFlushSendsEveryPendingImpressionBeforeItReturns()
    {
        var batches = new List<string[]>();
        using var work = new StoreWork("FeedSignalQueueFlushTests");
        var queue = new FeedSignalQueue(
            (postIds, _) =>
            {
                batches.Add(postIds);
                return Task.FromResult(true);
            },
            (_, _, _) => Task.FromResult(true),
            work);

        var total = FeedSignalQueue.BatchMax + 10;
        for (var index = 0; index < total; index++)
        {
            queue.MarkSeen("post-" + index);
        }

        await queue.FlushAsync(CancellationToken.None);

        Assert.Equal(2, batches.Count);
        Assert.Equal(FeedSignalQueue.BatchMax, batches[0].Length);
        Assert.Equal(10, batches[1].Length);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task AFailedAwaitedFlushKeepsTheImpressionsForTheNextOne()
    {
        var attempts = 0;
        using var work = new StoreWork("FeedSignalQueueFlushTests");
        var queue = new FeedSignalQueue(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(false);
            },
            (_, _, _) => Task.FromResult(true),
            work);

        queue.MarkSeen("post-one");
        queue.MarkSeen("post-two");

        await queue.FlushAsync(CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public async Task AnAwaitedFlushWithNothingPendingSendsNothing()
    {
        var attempts = 0;
        using var work = new StoreWork("FeedSignalQueueFlushTests");
        var queue = new FeedSignalQueue(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(true);
            },
            (_, _, _) => Task.FromResult(true),
            work);

        await queue.FlushAsync(CancellationToken.None);

        Assert.Equal(0, attempts);
    }
}
