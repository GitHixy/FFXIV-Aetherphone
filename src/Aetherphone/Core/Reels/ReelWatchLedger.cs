using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Reels;

// One reel's play while it is the current reel, read from mpv's observed position every frame, so a stall, a hold
// or a load never counts as watching. Watched time adds up replays, since the server checks a claimed loop against it.
internal struct ReelWatchLedger
{
    internal const int QuickSkipMilliseconds = 2000;
    internal const int ReelsSource = 10;

    // A loop is the position jumping from the last second back into the first after the reel played forward. Any
    // other jump (a rewind of a parked slot, a stale reading, a resume) only moves the baseline, so it never counts
    // as a loop or as watching.
    private const double WrapWindowSeconds = 1d;
    private const double EndToleranceSeconds = 0.35d;
    private const double LargestStepSeconds = 1d;

    private string? postId;
    private int durationMs;
    private double lastPosition;
    private double watchedSeconds;
    private int loops;
    private int reportedLoops;
    private int reportedWatchedMs;
    private bool completed;
    private bool reportedCompleted;
    private bool started;
    private bool checkpointed;

    internal readonly string? PostId => postId;

    internal readonly bool Started => started;

    internal readonly int WatchedMilliseconds => (int)(watchedSeconds * 1000d);

    internal readonly int Loops => loops;

    internal readonly bool Completed => completed;

    internal void Begin(string id, int durationMilliseconds)
    {
        postId = id;
        durationMs = Math.Max(0, durationMilliseconds);
        lastPosition = -1d;
        watchedSeconds = 0d;
        loops = 0;
        reportedLoops = 0;
        reportedWatchedMs = 0;
        completed = false;
        reportedCompleted = false;
        started = false;
        checkpointed = false;
    }

    internal void Rebase()
    {
        lastPosition = -1d;
    }

    internal void Observe(double positionSeconds, double durationSeconds)
    {
        if (postId is null || positionSeconds < 0d)
        {
            return;
        }

        var duration = durationSeconds > 0d ? durationSeconds : durationMs / 1000d;
        if (lastPosition >= 0d)
        {
            var step = positionSeconds - lastPosition;
            if (step > 0d && step <= LargestStepSeconds)
            {
                started = true;
                watchedSeconds += step;
            }
            else if (started && step < 0d && duration > WrapWindowSeconds * 2d
                     && lastPosition >= duration - WrapWindowSeconds && positionSeconds <= WrapWindowSeconds)
            {
                started = true;
                watchedSeconds += Math.Max(0d, duration - lastPosition) + positionSeconds;
                loops++;
                completed = true;
            }
        }

        lastPosition = positionSeconds;
        if (started && duration > 0d && positionSeconds >= duration - EndToleranceSeconds)
        {
            completed = true;
        }
    }

    // Reports the play so far without ending it, for a pause, a hide or a tab change. A play still under the
    // quick-skip bar stays open, so stepping away a second after a swipe does not read as a skip.
    internal bool TryCheckpoint(out ReelWatchItem item)
    {
        item = default;
        if (postId is null || !started || (!completed && WatchedMilliseconds < QuickSkipMilliseconds) || !HasNews())
        {
            return false;
        }

        item = Report(quickSkip: false);
        checkpointed = true;
        return true;
    }

    internal bool TryEnd(bool swiped, out ReelWatchItem item)
    {
        item = default;
        if (postId is null)
        {
            return false;
        }

        var watched = WatchedMilliseconds;
        var quickSkip = swiped && !completed && watched < QuickSkipMilliseconds;
        var reportable = started
            && (checkpointed ? HasNews() : quickSkip || completed || watched >= QuickSkipMilliseconds);
        if (reportable)
        {
            item = Report(quickSkip);
        }

        postId = null;
        return reportable;
    }

    private readonly bool HasNews() =>
        loops > reportedLoops || completed != reportedCompleted || WatchedMilliseconds > reportedWatchedMs;

    private ReelWatchItem Report(bool quickSkip)
    {
        var watched = WatchedMilliseconds;
        var item = new ReelWatchItem(postId!, watched, durationMs, loops - reportedLoops, completed, quickSkip,
            ReelsSource);
        reportedLoops = loops;
        reportedWatchedMs = watched;
        reportedCompleted = completed;
        return item;
    }
}
