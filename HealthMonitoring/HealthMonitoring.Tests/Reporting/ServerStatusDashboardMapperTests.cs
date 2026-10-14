using System.Text.Json;
using HealthMonitoring.Domain;
using HealthMonitoring.Reporting;

namespace HealthMonitoring.Tests.Reporting;

public sealed class ServerStatusDashboardMapperTests
{
    [Fact]
    public void Maps_latest_server_status_and_preserves_structured_sub_checks()
    {
        var status = new ServerStatus
        {
            ServerId = "transaction-processor",
            ServerName = "TRANSACTION-SERVER-01",
            OverallStatus = "Warning",
            UpdatedAtUtc = DateTimeOffset.Parse("2026-10-14T06:00:00Z"),
            ChecksJson = "[{\"name\":\"Disk Space\",\"status\":\"Warning\",\"summary\":\"Drive C is low.\",\"durationMs\":12.5,\"details\":[{\"drive\":\"C:\",\"freePercent\":12,\"thresholdPercent\":15}]}]"
        };

        var result = ServerStatusDashboardMapper.Map(status);

        Assert.Equal("TRANSACTION-SERVER-01", result.ServerName);
        Assert.Equal("Warning", result.OverallStatus);
        var check = Assert.Single(result.Checks);
        Assert.Equal("Disk Space", check.Name);
        Assert.Equal(JsonValueKind.Array, check.Details!.Value.ValueKind);
        Assert.Equal("C:", check.Details.Value[0].GetProperty("drive").GetString());
    }

    [Fact]
    public void Maps_disk_sub_checks_to_progress_bar_values_without_byte_fields()
    {
        using var document = JsonDocument.Parse("""[{"drive":"C:","sizeBytes":999,"freeBytes":500,"freePercent":50.5,"status":"Passed"}]""");

        var result = ServerCheckDetailsMapper.MapDisk(document.RootElement);

        var disk = Assert.Single(result);
        Assert.Equal("C:", disk.Drive);
        Assert.Equal(50.5, disk.FreePercent);
        Assert.Equal("Passed", disk.Status);
    }
}
