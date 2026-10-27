using Aetherphone.Core.Net;
using Xunit;

namespace Aetherphone.Tests;

public sealed class MaintenanceScheduleTests
{
    private const long NowMilliseconds = 1_800_000_000_000L;
    private const long NowUnix = NowMilliseconds / 1000L;
    private const long Spread = MaintenanceSchedule.SpreadMilliseconds;
    private const double Lowest = 0d;
    private const double Highest = 0.999999d;

    [Fact]
    public void AnErrorAnswerChecksStatusWithinTheSpreadToLearnTheMessage()
    {
        var earliest = Schedule(Lowest);
        var latest = Schedule(Highest);

        Assert.True(earliest.Enter(NowUnix + 3600, null, false, NowMilliseconds));
        Assert.True(latest.Enter(NowUnix + 3600, null, false, NowMilliseconds));

        Assert.Equal(MaintenancePhase.Active, earliest.View.Phase);
        Assert.Equal(NowUnix + 3600, earliest.View.EndsAtUnix);
        Assert.Equal(NowMilliseconds, earliest.ProbeAtMilliseconds);
        Assert.InRange(latest.ProbeAtMilliseconds, NowMilliseconds, NowMilliseconds + Spread);
    }

    [Fact]
    public void AShortCountdownChecksStatusWhenItReachesZero()
    {
        var endsAt = NowUnix + 120;
        var schedule = Schedule(Highest);

        schedule.Enter(endsAt, "Big update", true, NowMilliseconds);

        Assert.Equal("Big update", schedule.View.Message);
        Assert.InRange(schedule.ProbeAtMilliseconds, endsAt * 1000L, endsAt * 1000L + Spread);
    }

    [Fact]
    public void ALongCountdownStillChecksForAnEarlyEnd()
    {
        var schedule = Schedule(Lowest);

        schedule.Enter(NowUnix + 3 * 3600, null, true, NowMilliseconds);

        Assert.Equal(NowMilliseconds + MaintenanceSchedule.CountdownProbeMilliseconds,
            schedule.ProbeAtMilliseconds);
    }

    [Fact]
    public void APassedEndChecksEverySixtySecondsPlusTheSpread()
    {
        var earliest = Schedule(Lowest);
        var latest = Schedule(Highest);

        earliest.Enter(NowUnix - 10, null, true, NowMilliseconds);
        latest.Enter(NowUnix - 10, null, true, NowMilliseconds);
        latest.ProbeFailed(NowMilliseconds);

        Assert.Equal(NowMilliseconds + MaintenanceSchedule.OverdueProbeMilliseconds, earliest.ProbeAtMilliseconds);
        Assert.InRange(latest.ProbeAtMilliseconds, NowMilliseconds + MaintenanceSchedule.OverdueProbeMilliseconds,
            NowMilliseconds + MaintenanceSchedule.OverdueProbeMilliseconds + Spread);
    }

    [Fact]
    public void AnUnknownEndCountsAsFinishingUp()
    {
        var schedule = Schedule(Lowest);

        schedule.Enter(0L, null, false, NowMilliseconds);

        Assert.Equal(0L, schedule.View.SecondsLeft(NowUnix));
    }

    [Fact]
    public void AnExtendedEndUpdatesTheCountdown()
    {
        var schedule = Schedule(Lowest);
        schedule.Enter(NowUnix + 60, "Patching", true, NowMilliseconds);

        Assert.True(schedule.ApplyStatus(true, NowUnix + 1800, "Patching, almost there", NowMilliseconds));

        Assert.Equal(NowUnix + 1800, schedule.View.EndsAtUnix);
        Assert.Equal("Patching, almost there", schedule.View.Message);
        Assert.Equal(1800L, schedule.View.SecondsLeft(NowUnix));
    }

    [Fact]
    public void AnAnswerWithoutAnEndKeepsTheKnownEndAndMessage()
    {
        var schedule = Schedule(Lowest);
        schedule.Enter(NowUnix + 600, "Moving servers", true, NowMilliseconds);

        Assert.False(schedule.Enter(0L, null, false, NowMilliseconds + 5000));

        Assert.Equal(NowUnix + 600, schedule.View.EndsAtUnix);
        Assert.Equal("Moving servers", schedule.View.Message);
    }

    [Fact]
    public void ABlankMessageShowsNoMessage()
    {
        var schedule = Schedule(Lowest);

        schedule.Enter(NowUnix + 600, "   ", true, NowMilliseconds);

        Assert.Null(schedule.View.Message);
    }

    [Fact]
    public void TheEndOfMaintenanceKeepsHoldingUntilTheJitteredLift()
    {
        var schedule = Schedule(Highest);
        schedule.Enter(NowUnix - 5, null, true, NowMilliseconds);

        Assert.True(schedule.ApplyStatus(false, 0L, null, NowMilliseconds));

        Assert.Equal(MaintenancePhase.Lifting, schedule.View.Phase);
        Assert.True(schedule.View.Holds);
        Assert.False(schedule.ProbeDue(long.MaxValue - 1));
        Assert.InRange(schedule.LiftAtMilliseconds, NowMilliseconds, NowMilliseconds + Spread);
        Assert.False(schedule.TryLift(schedule.LiftAtMilliseconds - 1));
        Assert.True(schedule.TryLift(schedule.LiftAtMilliseconds));
        Assert.Same(MaintenanceView.Off, schedule.View);
        Assert.False(schedule.View.Holds);
    }

    [Fact]
    public void AnImmediateLiftHappensWithAZeroDraw()
    {
        var schedule = Schedule(Lowest);
        schedule.Enter(NowUnix + 60, null, true, NowMilliseconds);

        schedule.ApplyStatus(false, 0L, null, NowMilliseconds);

        Assert.True(schedule.TryLift(NowMilliseconds));
    }

    [Fact]
    public void AnInactiveAnswerOutsideMaintenanceChangesNothing()
    {
        var schedule = Schedule(Lowest);

        Assert.False(schedule.ApplyStatus(false, 0L, null, NowMilliseconds));

        Assert.Same(MaintenanceView.Off, schedule.View);
        Assert.False(schedule.TryLift(long.MaxValue));
    }

    [Fact]
    public void MaintenanceStartingAgainWhileLiftingCancelsTheLift()
    {
        var schedule = Schedule(Lowest);
        schedule.Enter(NowUnix + 60, "First pass", true, NowMilliseconds);
        schedule.ApplyStatus(false, 0L, null, NowMilliseconds);

        schedule.ApplyStatus(true, NowUnix + 900, "Second pass", NowMilliseconds);

        Assert.Equal(MaintenancePhase.Active, schedule.View.Phase);
        Assert.Equal(long.MaxValue, schedule.LiftAtMilliseconds);
        Assert.False(schedule.TryLift(long.MaxValue));
    }

    [Fact]
    public void AProbeIsDueOnceAndThenWaitsForItsAnswer()
    {
        var schedule = Schedule(Lowest);
        schedule.Enter(NowUnix - 1, null, true, NowMilliseconds);
        var due = schedule.ProbeAtMilliseconds;

        Assert.False(schedule.ProbeDue(due - 1));
        Assert.True(schedule.ProbeDue(due));
        schedule.BeginProbe();
        Assert.False(schedule.ProbeDue(due));
        schedule.ApplyStatus(true, 0L, null, due);
        Assert.InRange(schedule.ProbeAtMilliseconds, due + MaintenanceSchedule.OverdueProbeMilliseconds,
            due + MaintenanceSchedule.OverdueProbeMilliseconds + Spread);
    }

    [Fact]
    public void TheSpreadNeverLeavesItsThirtySecondWindow()
    {
        var draws = new[] { -1d, 0d, 0.25d, 0.5d, 0.999999d, 1d, 2d };
        for (var drawIndex = 0; drawIndex < draws.Length; drawIndex++)
        {
            var draw = draws[drawIndex];
            var schedule = Schedule(draw);
            schedule.Enter(NowUnix + 60, null, true, NowMilliseconds);
            schedule.ApplyStatus(false, 0L, null, NowMilliseconds);

            Assert.InRange(schedule.LiftAtMilliseconds, NowMilliseconds, NowMilliseconds + Spread);
        }
    }

    private static MaintenanceSchedule Schedule(double draw) => new(() => draw);
}
