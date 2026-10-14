using System.Text.Json;
using HealthMonitoring.Domain;

namespace HealthMonitoring.Reporting;

public sealed record ServerStatusDashboardModel(
    string ServerId,
    string ServerName,
    string OverallStatus,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ServerCheckDashboardModel> Checks);

public sealed record ServerCheckDashboardModel(
    string Name,
    string Status,
    string Summary,
    double DurationMs,
    JsonElement? Details,
    string? Error);

public static class ServerStatusDashboardMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ServerStatusDashboardModel Map(ServerStatus status)
    {
        var checks = JsonSerializer.Deserialize<List<ServerCheckDashboardModel>>(status.ChecksJson, JsonOptions) ?? [];
        return new ServerStatusDashboardModel(status.ServerId, status.ServerName, status.OverallStatus, status.UpdatedAtUtc, checks);
    }
}
