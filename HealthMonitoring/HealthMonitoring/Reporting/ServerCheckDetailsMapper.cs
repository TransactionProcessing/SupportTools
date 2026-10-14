using System.Text.Json;

namespace HealthMonitoring.Reporting;

public sealed record DiskSubcheckDashboardModel(string Drive, double FreePercent, string Status);

public static class ServerCheckDetailsMapper
{
    public static IReadOnlyList<DiskSubcheckDashboardModel> MapDisk(JsonElement? details)
    {
        if (details is not { ValueKind: JsonValueKind.Array }) return [];

        return details.Value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new DiskSubcheckDashboardModel(
                GetString(item, "drive") ?? "—",
                GetDouble(item, "freePercent"),
                GetString(item, "status") ?? "Unknown"))
            .ToArray();
    }

    private static string? GetString(JsonElement item, string propertyName) =>
        item.EnumerateObject()
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            .Value.ValueKind is JsonValueKind.String
                ? item.EnumerateObject().First(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)).Value.GetString()
                : null;

    private static double GetDouble(JsonElement item, string propertyName) =>
        item.EnumerateObject()
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            .Value.ValueKind == JsonValueKind.Number &&
        item.EnumerateObject().First(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)).Value.TryGetDouble(out var value)
            ? value
            : 0;
}
