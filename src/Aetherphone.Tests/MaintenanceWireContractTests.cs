using System.Text;
using System.Text.Json;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Net;
using Xunit;

namespace Aetherphone.Tests;

public sealed class MaintenanceWireContractTests
{
    private const long NowUnix = 1_800_000_000L;

    [Fact]
    public void TheStatusRouteAndErrorShapeMatchTheServer()
    {
        Assert.Equal("/status", MaintenanceWire.StatusPath);
        Assert.Equal("X-Aep-Maintenance-Ends", MaintenanceWire.EndsHeader);
        Assert.Equal("maintenance", MaintenanceWire.CloseReason);
        Assert.True(MaintenanceWire.IsMaintenance(
            new AepFailure(AepFailureKind.Server, 503, "maintenance", "Aethernet is under maintenance.", "r1", "1800003600")));
        Assert.False(MaintenanceWire.IsMaintenance(
            new AepFailure(AepFailureKind.Server, 503, "app_disabled", null, null, null)));
        Assert.False(MaintenanceWire.IsMaintenance(
            new AepFailure(AepFailureKind.Server, 500, "maintenance", null, null, null)));
    }

    [Fact]
    public void TheErrorBodyCarriesTheEndAsADecimalString()
    {
        const string json = """
        {"error":"Aethernet is under maintenance.","code":"maintenance","requestId":"abc123","value":"1800003600"}
        """;

        var body = JsonSerializer.Deserialize(json, AepErrorJsonContext.Default.AepErrorBody)!;

        Assert.Equal("maintenance", body.Code);
        Assert.Equal(1_800_003_600L, MaintenanceWire.EndsAtUnix(null, body.Value, null, NowUnix));
    }

    [Fact]
    public void TheEndPrefersTheHeaderThenTheBodyThenRetryAfter()
    {
        Assert.Equal(1_800_000_900L, MaintenanceWire.EndsAtUnix("1800000900", "1800003600", null, NowUnix));
        Assert.Equal(1_800_003_600L, MaintenanceWire.EndsAtUnix("soon", "1800003600", null, NowUnix));
        Assert.Equal(NowUnix + 120, MaintenanceWire.EndsAtUnix(null, null, TimeSpan.FromSeconds(119.2), NowUnix));
        Assert.Equal(0L, MaintenanceWire.EndsAtUnix(null, "", null, NowUnix));
        Assert.Equal(0L, MaintenanceWire.EndsAtUnix("0", "-5", TimeSpan.Zero, NowUnix));
    }

    [Fact]
    public void TheWebSocketHeaderIsFoundInAnyCase()
    {
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            ["retry-after"] = new[] { "600" },
            ["x-aep-maintenance-ends"] = new[] { "1800000600" },
        };

        Assert.True(MaintenanceWire.TryHeader(headers, out var value));
        Assert.Equal("1800000600", value);
        Assert.False(MaintenanceWire.TryHeader(new Dictionary<string, IEnumerable<string>>(), out _));
        Assert.False(MaintenanceWire.TryHeader(null, out _));
    }

    [Fact]
    public void TheStatusReadsActiveMaintenance()
    {
        const string json = """
        {"maintenance":{"active":true,"startedAtUnix":1799999000,"endsAtUnix":1800003600,"message":"Moving to new servers"}}
        """;

        var reading = Read(200, json);

        Assert.True(reading.Known);
        Assert.True(reading.Active);
        Assert.Equal(1_800_003_600L, reading.EndsAtUnix);
        Assert.Equal("Moving to new servers", reading.Message);
    }

    [Fact]
    public void TheStatusReadsNullsAsAnUnknownEnd()
    {
        const string json = """
        {"maintenance":{"active":true,"startedAtUnix":null,"endsAtUnix":null,"message":null}}
        """;

        var reading = Read(200, json);

        Assert.True(reading.Active);
        Assert.Equal(0L, reading.EndsAtUnix);
        Assert.Null(reading.Message);
    }

    [Fact]
    public void AnInactiveOrBareStatusMeansNoMaintenance()
    {
        Assert.Equal(MaintenanceReading.Inactive,
            Read(200, """{"maintenance":{"active":false,"startedAtUnix":null,"endsAtUnix":null,"message":null}}"""));
        Assert.Equal(MaintenanceReading.Inactive, Read(200, "{}"));
    }

    [Fact]
    public void AnOldServerWithoutStatusIsNotInMaintenance()
    {
        Assert.Equal(MaintenanceReading.Inactive,
            MaintenanceWatch.Read(new JsonProbe<MaintenanceStatusDto>(404, null)));
    }

    [Fact]
    public void AFailedCheckLeavesTheStateAlone()
    {
        Assert.False(MaintenanceWatch.Read(new JsonProbe<MaintenanceStatusDto>(0, null)).Known);
        Assert.False(MaintenanceWatch.Read(new JsonProbe<MaintenanceStatusDto>(502, null)).Known);
        Assert.False(MaintenanceWatch.Read(new JsonProbe<MaintenanceStatusDto>(200, null)).Known);
    }

    [Fact]
    public void TheRealtimeFrameIsRecognisedWhereverItsTypeSits()
    {
        Assert.True(IsFrame("""{"type":"server.maintenance","endsAtUnix":1800003600,"message":"Back soon"}"""));
        Assert.True(IsFrame("""{"endsAtUnix":1800003600,"message":null,"type":"server.maintenance"}"""));
    }

    [Fact]
    public void OtherFramesAreNotMistakenForMaintenance()
    {
        Assert.False(IsFrame("""{"type":"server.restarting"}"""));
        Assert.False(IsFrame("""{"type":"chat.ping","message":{"body":"server.maintenance"}}"""));
        Assert.False(IsFrame("""{"call":{"type":"server.maintenance"},"type":"call.roster"}"""));
        Assert.False(IsFrame("""{"text":"server.maintenance"}"""));
        Assert.False(IsFrame("[]"));
    }

    [Fact]
    public void TheRealtimeFrameCarriesTheEndAndTheMessage()
    {
        var frame = JsonSerializer.Deserialize(
            """{"type":"server.maintenance","endsAtUnix":1800003600,"message":"Back soon"}""",
            AethernetJsonContext.Default.MaintenanceSignalDto)!;

        Assert.Equal("server.maintenance", frame.Type);
        Assert.Equal(1_800_003_600L, frame.EndsAtUnix);
        Assert.Equal("Back soon", frame.Message);
    }

    [Fact]
    public void AMaintenanceFailureReadsAsMaintenanceNotAsAnError()
    {
        var expected = Loc.T(L.Failure.Maintenance);

        Assert.Equal(expected, FailureText.Resolve(AepFailure.Transport(AepFailureKind.Maintenance)));
        Assert.Equal(expected, FailureText.Resolve(
            new AepFailure(AepFailureKind.Server, 503, "maintenance", null, "r1", "1800003600")));
    }

    [Fact]
    public void OnlyServerBackedAppsAreCoveredByTheMaintenanceScreen()
    {
        var covered = new[]
        {
            "message", "chirper", "aethergram", "velvet", "muster", "yellowpages", "announcements", "polls",
            "casino", "coin", "prism", "loveisland", "feedback",
        };
        var open = new[]
        {
            "messages", "games", "music", "aetherstream", "calendar", "settings", "clock", "camera", "photos",
            "notes", "calculator", "inventory", "fishing", "housing", "maps", "market", "news", "notifications",
        };

        for (var index = 0; index < covered.Length; index++)
        {
            Assert.True(ServerBackedApps.Contains(covered[index]), covered[index]);
        }

        for (var index = 0; index < open.Length; index++)
        {
            Assert.False(ServerBackedApps.Contains(open[index]), open[index]);
        }
    }

    private static MaintenanceReading Read(int status, string json) =>
        MaintenanceWatch.Read(new JsonProbe<MaintenanceStatusDto>(status,
            JsonSerializer.Deserialize(json, AethernetJsonContext.Default.MaintenanceStatusDto)));

    private static bool IsFrame(string json) => MaintenanceWire.IsMaintenanceFrame(Encoding.UTF8.GetBytes(json));
}
