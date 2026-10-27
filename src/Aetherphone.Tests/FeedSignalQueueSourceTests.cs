using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Social;
using Xunit;

namespace Aetherphone.Tests;

public sealed class FeedSignalQueueSourceTests
{
    [Fact]
    public async Task EachImpressionGoesOutWithTheSurfaceThatFirstReportedIt()
    {
        var batches = new List<(string[] PostIds, int[] Sources)>();
        using var work = new StoreWork("FeedSignalQueueSourceTests");
        var queue = new FeedSignalQueue(
            (postIds, sources, _) =>
            {
                batches.Add((postIds, sources));
                return Task.FromResult(true);
            },
            (_, _, _, _) => Task.FromResult(true),
            work);

        queue.MarkSeen("post-feed", PostViewSource.Following);
        queue.MarkSeen("post-grid", PostViewSource.Explore);
        queue.MarkSeen("post-feed", PostViewSource.Profile);

        await queue.FlushAsync(CancellationToken.None);

        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.PostIds.Length);
        Assert.Equal(batch.PostIds.Length, batch.Sources.Length);
        Assert.Equal((int)PostViewSource.Following, SourceOf(batch, "post-feed"));
        Assert.Equal((int)PostViewSource.Explore, SourceOf(batch, "post-grid"));
    }

    [Fact]
    public async Task AFailedBatchIsQueuedAgainWithItsSources()
    {
        var batches = new List<(string[] PostIds, int[] Sources)>();
        var fail = true;
        using var work = new StoreWork("FeedSignalQueueSourceTests");
        var queue = new FeedSignalQueue(
            (postIds, sources, _) =>
            {
                batches.Add((postIds, sources));
                return Task.FromResult(!fail);
            },
            (_, _, _, _) => Task.FromResult(true),
            work);

        queue.MarkSeen("post-search", PostViewSource.Search);
        await queue.FlushAsync(CancellationToken.None);
        fail = false;
        await queue.FlushAsync(CancellationToken.None);

        Assert.Equal(2, batches.Count);
        Assert.Equal((int)PostViewSource.Search, SourceOf(batches[1], "post-search"));
    }

    [Fact]
    public async Task ASignalCarriesTheSurfaceItWasTappedOn()
    {
        var sent = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var work = new StoreWork("FeedSignalQueueSourceTests");
        var queue = new FeedSignalQueue(
            (_, _, _) => Task.FromResult(true),
            (_, _, source, _) =>
            {
                sent.TrySetResult(source);
                return Task.FromResult(true);
            },
            work);

        queue.Signal("post-one", FeedSignalKinds.DetailOpen, PostViewSource.Hashtag);

        var finished = await Task.WhenAny(sent.Task, Task.Delay(5000));
        Assert.Same(sent.Task, finished);
        Assert.Equal((int)PostViewSource.Hashtag, await sent.Task);
    }

    private static int SourceOf((string[] PostIds, int[] Sources) batch, string postId)
    {
        for (var index = 0; index < batch.PostIds.Length; index++)
        {
            if (batch.PostIds[index] == postId)
            {
                return batch.Sources[index];
            }
        }

        return -1;
    }
}
