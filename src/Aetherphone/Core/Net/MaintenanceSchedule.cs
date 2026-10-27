namespace Aetherphone.Core.Net;

internal enum MaintenancePhase : byte
{
    Off,
    Active,
    Lifting,
}

internal sealed record MaintenanceView(MaintenancePhase Phase, long EndsAtUnix, string? Message)
{
    public static readonly MaintenanceView Off = new(MaintenancePhase.Off, 0L, null);

    public bool Holds => Phase != MaintenancePhase.Off;

    public long SecondsLeft(long nowUnix) => Math.Max(0L, EndsAtUnix - nowUnix);
}

internal sealed class MaintenanceSchedule
{
    public const long SpreadMilliseconds = 30_000L;
    public const long OverdueProbeMilliseconds = 60_000L;
    public const long CountdownProbeMilliseconds = 5L * 60_000L;
    private const long MillisecondsPerSecond = 1000L;

    private readonly Func<double> unitRandom;

    public MaintenanceSchedule(Func<double> unitRandom)
    {
        this.unitRandom = unitRandom;
    }

    public MaintenanceView View { get; private set; } = MaintenanceView.Off;

    public long ProbeAtMilliseconds { get; private set; } = long.MaxValue;

    public long LiftAtMilliseconds { get; private set; } = long.MaxValue;

    public bool Enter(long endsAtUnix, string? message, bool messageKnown, long nowMilliseconds)
    {
        var holding = View.Holds;
        var endsAt = endsAtUnix > 0 ? endsAtUnix : holding ? View.EndsAtUnix : 0L;
        var text = messageKnown ? Clean(message) : holding ? View.Message : null;
        var next = new MaintenanceView(MaintenancePhase.Active, endsAt, text);
        if (next == View)
        {
            return false;
        }

        View = next;
        LiftAtMilliseconds = long.MaxValue;
        ProbeAtMilliseconds = !messageKnown && !holding ? nowMilliseconds + Spread() : NextProbe(nowMilliseconds);
        return true;
    }

    public bool ApplyStatus(bool active, long endsAtUnix, string? message, long nowMilliseconds)
    {
        if (active)
        {
            var changed = Enter(endsAtUnix, message, true, nowMilliseconds);
            ProbeAtMilliseconds = NextProbe(nowMilliseconds);
            return changed;
        }

        if (View.Phase != MaintenancePhase.Active)
        {
            return false;
        }

        View = View with { Phase = MaintenancePhase.Lifting };
        ProbeAtMilliseconds = long.MaxValue;
        LiftAtMilliseconds = nowMilliseconds + Spread();
        return true;
    }

    public bool ProbeDue(long nowMilliseconds) =>
        View.Phase == MaintenancePhase.Active && nowMilliseconds >= ProbeAtMilliseconds;

    public void BeginProbe()
    {
        ProbeAtMilliseconds = long.MaxValue;
    }

    public void ProbeFailed(long nowMilliseconds)
    {
        if (View.Phase != MaintenancePhase.Active)
        {
            return;
        }

        ProbeAtMilliseconds = NextProbe(nowMilliseconds);
    }

    public bool TryLift(long nowMilliseconds)
    {
        if (View.Phase != MaintenancePhase.Lifting || nowMilliseconds < LiftAtMilliseconds)
        {
            return false;
        }

        View = MaintenanceView.Off;
        LiftAtMilliseconds = long.MaxValue;
        ProbeAtMilliseconds = long.MaxValue;
        return true;
    }

    private long NextProbe(long nowMilliseconds)
    {
        var endsAtMilliseconds = View.EndsAtUnix * MillisecondsPerSecond;
        var due = endsAtMilliseconds > nowMilliseconds
            ? Math.Min(endsAtMilliseconds, nowMilliseconds + CountdownProbeMilliseconds)
            : nowMilliseconds + OverdueProbeMilliseconds;
        return due + Spread();
    }

    private long Spread() => (long)(Math.Clamp(unitRandom(), 0d, 1d) * SpreadMilliseconds);

    private static string? Clean(string? message) => string.IsNullOrWhiteSpace(message) ? null : message.Trim();
}
