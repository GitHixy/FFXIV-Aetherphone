using System.Net;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;
using Dalamud.Plugin.Services;

namespace Aetherphone.Core.Aethernet;

internal sealed class MaintenanceWatch : IDisposable
{
    private readonly AethernetSession session;
    private readonly HttpService http;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Func<Task> probe;
    private volatile bool checkRequested = true;
    private int probing;

    public MaintenanceWatch(AethernetSession session, HttpService http, IFramework framework,
        IClientState clientState)
    {
        this.session = session;
        this.http = http;
        this.framework = framework;
        this.clientState = clientState;
        probe = ProbeAsync;
        framework.Update += OnFrameworkUpdate;
        clientState.Login += OnLogin;
    }

    private AethernetMaintenance Maintenance => session.Maintenance;

    private void OnLogin()
    {
        checkRequested = true;
    }

    private void OnFrameworkUpdate(IFramework source)
    {
        Maintenance.Tick();
        if (Volatile.Read(ref probing) == 1)
        {
            return;
        }

        if (!checkRequested && !Maintenance.TryBeginProbe())
        {
            return;
        }

        checkRequested = false;
        Volatile.Write(ref probing, 1);
        _ = Task.Run(probe);
    }

    private async Task ProbeAsync()
    {
        try
        {
            var url = string.Concat(session.BaseUrl.TrimEnd('/'), MaintenanceWire.StatusPath);
            var answer = await http.ProbeJsonAsync(url, AethernetJsonContext.Default.MaintenanceStatusDto,
                cancellation.Token).ConfigureAwait(false);
            var reading = Read(answer);
            if (!reading.Known)
            {
                Maintenance.ProbeFailed();
                return;
            }

            Maintenance.ApplyStatus(reading.Active, reading.EndsAtUnix, reading.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AepLog.Warning(exception, "Aethernet status check failed");
            Maintenance.ProbeFailed();
        }
        finally
        {
            Volatile.Write(ref probing, 0);
        }
    }

    internal static MaintenanceReading Read(JsonProbe<MaintenanceStatusDto> answer)
    {
        if (answer.Status == (int)HttpStatusCode.NotFound)
        {
            return MaintenanceReading.Inactive;
        }

        if (answer.Status != (int)HttpStatusCode.OK || answer.Body is null)
        {
            return MaintenanceReading.Unknown;
        }

        if (answer.Body.Maintenance is not { } info)
        {
            return MaintenanceReading.Inactive;
        }

        return new MaintenanceReading(true, info.Active, info.EndsAtUnix ?? 0L, info.Message);
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        clientState.Login -= OnLogin;
        cancellation.Cancel();
        cancellation.Dispose();
    }
}

internal readonly record struct MaintenanceReading(bool Known, bool Active, long EndsAtUnix, string? Message)
{
    public static readonly MaintenanceReading Unknown = new(false, false, 0L, null);
    public static readonly MaintenanceReading Inactive = new(true, false, 0L, null);
}
