namespace Aetherphone.Core.Net;

internal sealed class AethernetMaintenance
{
    private static volatile bool holdingTraffic;

    private readonly object gate = new();
    private readonly MaintenanceSchedule schedule = new(Random.Shared.NextDouble);
    private volatile MaintenanceView view = MaintenanceView.Off;
    private TaskCompletionSource lifted = Released();

    public static bool HoldsTraffic => holdingTraffic;

    public MaintenanceView View => view;

    public bool Holds => view.Holds;

    public event Action? Lifted;

    public Task WhenLifted
    {
        get
        {
            lock (gate)
            {
                return lifted.Task;
            }
        }
    }

    public void EnterFromResponse(long endsAtUnix) => Enter(endsAtUnix, null, false);

    public void EnterFromSignal(long endsAtUnix, string? message) => Enter(endsAtUnix, message, true);

    public void ApplyStatus(bool active, long endsAtUnix, string? message)
    {
        lock (gate)
        {
            var wasHolding = schedule.View.Holds;
            if (!schedule.ApplyStatus(active, endsAtUnix, message, NowMilliseconds()))
            {
                return;
            }

            Publish(wasHolding);
        }
    }

    public bool TryBeginProbe()
    {
        if (view.Phase != MaintenancePhase.Active)
        {
            return false;
        }

        lock (gate)
        {
            if (!schedule.ProbeDue(NowMilliseconds()))
            {
                return false;
            }

            schedule.BeginProbe();
            return true;
        }
    }

    public void ProbeFailed()
    {
        lock (gate)
        {
            schedule.ProbeFailed(NowMilliseconds());
        }
    }

    public void Tick()
    {
        if (view.Phase != MaintenancePhase.Lifting)
        {
            return;
        }

        TaskCompletionSource released;
        lock (gate)
        {
            if (!schedule.TryLift(NowMilliseconds()))
            {
                return;
            }

            released = lifted;
            Publish(true);
        }

        AepLog.Info("Aethernet maintenance is over; resuming online features.");
        released.TrySetResult();
        Lifted?.Invoke();
    }

    private void Enter(long endsAtUnix, string? message, bool messageKnown)
    {
        lock (gate)
        {
            var wasHolding = schedule.View.Holds;
            if (!schedule.Enter(endsAtUnix, message, messageKnown, NowMilliseconds()))
            {
                return;
            }

            Publish(wasHolding);
        }
    }

    private void Publish(bool wasHolding)
    {
        var next = schedule.View;
        if (!wasHolding && next.Holds)
        {
            lifted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AepLog.Info($"Aethernet is under maintenance (ends at {next.EndsAtUnix}); holding online features.");
        }

        view = next;
        holdingTraffic = next.Holds;
    }

    private static TaskCompletionSource Released()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        released.SetResult();
        return released;
    }

    private static long NowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
