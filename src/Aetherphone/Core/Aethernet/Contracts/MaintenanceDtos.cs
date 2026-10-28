namespace Aetherphone.Core.Aethernet.Contracts;

internal sealed record MaintenanceStatusDto(MaintenanceInfoDto? Maintenance = null);

internal sealed record MaintenanceInfoDto(
    bool Active = false,
    long? StartedAtUnix = null,
    long? EndsAtUnix = null,
    string? Message = null);

internal sealed record MaintenanceSignalDto(string Type = "", long? EndsAtUnix = null, string? Message = null);
