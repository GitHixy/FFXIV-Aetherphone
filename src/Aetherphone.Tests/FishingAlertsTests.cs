using Aetherphone.Core.Fishing;
using Xunit;

namespace Aetherphone.Tests;

public sealed class FishingAlertsTests
{
    private const long Start = 10_000;
    private const long End = 12_000;

    private static readonly FishWindow Window = new(Start, End);

    [Fact]
    public void AnOpenWindowCountsAsRunning()
    {
        Assert.True(FishingAlerts.AlertWindowRunning(Window, Start));
        Assert.True(FishingAlerts.AlertWindowRunning(Window, End - 1));
    }

    [Fact]
    public void TheLeadTimeBeforeAWindowCountsAsRunning()
    {
        Assert.True(FishingAlerts.AlertWindowRunning(Window, Start - FishingAlerts.FishLeadSeconds));
        Assert.False(FishingAlerts.AlertWindowRunning(Window, Start - FishingAlerts.FishLeadSeconds - 1));
    }

    [Fact]
    public void AClosedOrMissingWindowIsNotRunning()
    {
        Assert.False(FishingAlerts.AlertWindowRunning(Window, End));
        Assert.False(FishingAlerts.AlertWindowRunning(FishWindow.None, Start));
    }
}
