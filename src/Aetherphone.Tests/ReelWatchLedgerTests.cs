using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReelWatchLedgerTests
{
    private const string PostId = "0f8b7c";
    private const int DurationMs = 10_000;
    private const double Duration = 10d;
    private const double Frame = 1d / 30d;

    private static void Play(ref ReelWatchLedger ledger, double from, double to)
    {
        for (var position = from; position <= to + 1e-9; position += Frame)
        {
            ledger.Observe(position, Duration);
        }
    }

    [Fact]
    public void LeavingWithinTwoSecondsBySwipeIsAQuickSkip()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 1.5d);

        Assert.True(ledger.TryEnd(swiped: true, out var item));
        Assert.True(item.QuickSkip);
        Assert.False(item.Completed);
        Assert.Equal(0, item.Loops);
        Assert.InRange(item.WatchedMs, 1400, 1550);
        Assert.Equal(ReelWatchLedger.ReelsSource, item.Source);
        Assert.Equal(DurationMs, item.DurationMs);
    }

    [Fact]
    public void WatchingPastTwoSecondsIsNotAQuickSkip()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 4d);

        Assert.True(ledger.TryEnd(swiped: true, out var item));
        Assert.False(item.QuickSkip);
        Assert.False(item.Completed);
        Assert.InRange(item.WatchedMs, 3900, 4050);
    }

    [Fact]
    public void ReachingTheEndCompletesThePlay()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 9.8d);

        Assert.True(ledger.TryEnd(swiped: true, out var item));
        Assert.True(item.Completed);
        Assert.False(item.QuickSkip);
        Assert.Equal(0, item.Loops);
    }

    [Fact]
    public void WrappingFromTheLastSecondToTheFirstCountsALoop()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 9.97d);
        Play(ref ledger, 0.02d, 9.97d);
        Play(ref ledger, 0.02d, 3d);

        Assert.Equal(2, ledger.Loops);
        Assert.True(ledger.TryEnd(swiped: true, out var item));
        Assert.Equal(2, item.Loops);
        Assert.True(item.Completed);
        Assert.InRange(item.WatchedMs, 22_800, 23_100);
    }

    [Fact]
    public void AJumpBackFromTheMiddleIsNotALoop()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 6d);
        Play(ref ledger, 0d, 1d);

        Assert.Equal(0, ledger.Loops);
        Assert.False(ledger.Completed);
        Assert.InRange(ledger.WatchedMilliseconds, 6900, 7100);
    }

    [Fact]
    public void AStaleFirstReadingDoesNotCountAsWatching()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        ledger.Observe(9.9d, Duration);
        Play(ref ledger, 0d, 1d);

        Assert.Equal(0, ledger.Loops);
        Assert.InRange(ledger.WatchedMilliseconds, 950, 1050);
    }

    [Fact]
    public void APlayThatNeverStartedIsNotReported()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        ledger.Observe(0d, Duration);

        Assert.False(ledger.TryEnd(swiped: true, out _));
        Assert.Null(ledger.PostId);
    }

    [Fact]
    public void LeavingShortlyWithoutASwipeIsNotReported()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 1d);

        Assert.False(ledger.TryEnd(swiped: false, out _));
    }

    [Fact]
    public void ACheckpointUnderTheQuickSkipBarWaits()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 1d);

        Assert.False(ledger.TryCheckpoint(out _));
        Assert.Equal(PostId, ledger.PostId);
    }

    [Fact]
    public void ACheckpointSendsOnlyTheLoopsSinceTheLastReport()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 9.97d);
        Play(ref ledger, 0.02d, 2d);

        Assert.True(ledger.TryCheckpoint(out var first));
        Assert.Equal(1, first.Loops);
        Assert.False(first.QuickSkip);

        ledger.Rebase();
        Play(ref ledger, 2d, 9.97d);
        Play(ref ledger, 0.02d, 1d);

        Assert.True(ledger.TryEnd(swiped: true, out var second));
        Assert.Equal(1, second.Loops);
        Assert.True(second.WatchedMs > first.WatchedMs);
    }

    [Fact]
    public void NothingNewAfterACheckpointSendsNothing()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 3d);

        Assert.True(ledger.TryCheckpoint(out _));
        Assert.False(ledger.TryEnd(swiped: true, out _));
    }

    [Fact]
    public void ARebaseAfterAResumeDoesNotCountTheGap()
    {
        var ledger = new ReelWatchLedger();
        ledger.Begin(PostId, DurationMs);
        Play(ref ledger, 0d, 9.97d);
        ledger.Rebase();
        Play(ref ledger, 0d, 1d);

        Assert.Equal(0, ledger.Loops);
        Assert.InRange(ledger.WatchedMilliseconds, 10_850, 11_050);
    }
}

public sealed class ReelWatchQueueTests
{
    private static ReelWatchItem Item(int index) =>
        new("post" + index, 3000, 10_000, 0, false, false, ReelWatchLedger.ReelsSource);

    [Fact]
    public void ASwipeWaitsForAFewPlaysOrAFewSeconds()
    {
        var queue = new ReelWatchQueue();
        queue.Drain(0);
        queue.Add(Item(0));

        Assert.False(queue.ShouldFlush(1000, urgent: false));
        Assert.True(queue.ShouldFlush(ReelWatchQueue.FlushIntervalMilliseconds, urgent: false));
    }

    [Fact]
    public void FivePlaysSendAtOnce()
    {
        var queue = new ReelWatchQueue();
        queue.Drain(0);
        for (var index = 0; index < ReelWatchQueue.FlushCount; index++)
        {
            queue.Add(Item(index));
        }

        Assert.True(queue.ShouldFlush(1, urgent: false));
    }

    [Fact]
    public void APauseOrAHideSendsAtOnce()
    {
        var queue = new ReelWatchQueue();
        queue.Drain(0);
        queue.Add(Item(0));

        Assert.True(queue.ShouldFlush(1, urgent: true));
    }

    [Fact]
    public void AnEmptyQueueNeverSends()
    {
        var queue = new ReelWatchQueue();

        Assert.False(queue.ShouldFlush(long.MaxValue, urgent: true));
    }

    [Fact]
    public void DrainingHandsOverEveryPlayAndEmpties()
    {
        var queue = new ReelWatchQueue();
        queue.Add(Item(0));
        queue.Add(Item(1));

        var drained = queue.Drain(5);

        Assert.Equal(2, drained.Length);
        Assert.Equal("post1", drained[1].PostId);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void AFullQueueKeepsTheNewestTwenty()
    {
        var queue = new ReelWatchQueue();
        for (var index = 0; index < ReelWatchQueue.Capacity + 3; index++)
        {
            queue.Add(Item(index));
        }

        var drained = queue.Drain(0);

        Assert.Equal(ReelWatchQueue.Capacity, drained.Length);
        Assert.Equal("post3", drained[0].PostId);
    }
}
