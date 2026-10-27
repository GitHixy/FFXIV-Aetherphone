using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Aetherphone.Core.Net;

internal static class MaintenanceWire
{
    public const int Status = 503;
    public const string Code = "maintenance";
    public const string EndsHeader = "X-Aep-Maintenance-Ends";
    public const string CloseReason = "maintenance";
    public const string StatusPath = "/status";

    private static ReadOnlySpan<byte> TypeProperty => "type"u8;
    private static ReadOnlySpan<byte> FrameType => "server.maintenance"u8;

    public static bool IsMaintenance(in AepFailure failure) =>
        failure.StatusCode == Status && string.Equals(failure.Code, Code, StringComparison.Ordinal);

    public static long EndsAtUnix(string? header, string? value, TimeSpan? retryAfter, long nowUnix)
    {
        if (TryParseUnix(header, out var fromHeader))
        {
            return fromHeader;
        }

        if (TryParseUnix(value, out var fromBody))
        {
            return fromBody;
        }

        if (retryAfter is { } delay && delay > TimeSpan.Zero)
        {
            return nowUnix + (long)Math.Ceiling(delay.TotalSeconds);
        }

        return 0L;
    }

    public static string? Header(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues(EndsHeader, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            return value;
        }

        return null;
    }

    public static bool TryHeader(IReadOnlyDictionary<string, IEnumerable<string>>? headers, out string? value)
    {
        value = null;
        if (headers is null)
        {
            return false;
        }

        foreach (var entry in headers)
        {
            if (!string.Equals(entry.Key, EndsHeader, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var item in entry.Value)
            {
                value = item;
                break;
            }

            return true;
        }

        return false;
    }

    public static bool IsMaintenanceFrame(ReadOnlySpan<byte> frame)
    {
        var reader = new Utf8JsonReader(frame);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isType = reader.ValueTextEquals(TypeProperty);
            if (!reader.Read())
            {
                return false;
            }

            if (isType)
            {
                return reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(FrameType);
            }

            reader.Skip();
        }

        return false;
    }

    private static bool TryParseUnix(string? text, out long unix) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out unix) && unix > 0;
}
