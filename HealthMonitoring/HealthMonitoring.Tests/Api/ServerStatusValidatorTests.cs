using System.Text.Json;
using HealthMonitoring.Api;

namespace HealthMonitoring.Tests.Api;

public sealed class ServerStatusValidatorTests
{
    [Fact]
    public void Valid_request_calculates_overall_status_from_checks()
    {
        var request = new ServerStatusRequest
        {
            ServerName = "TRANSACTION-SERVER-01",
            Checks =
            [
                Check("PowerShell Runtime", "Passed"),
                Check("Disk Space", "Warning"),
                Check("HealthMonitoring", "Passed")
            ]
        };

        request.OverallStatus = "Passed";
        var result = ServerStatusValidator.Create("transaction-processor", request, DateTimeOffset.UtcNow);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Status);
        Assert.Equal("Warning", result.Status!.OverallStatus);
        Assert.Equal("transaction-processor", result.Status.ServerId);
        Assert.Equal("TRANSACTION-SERVER-01", result.Status.ServerName);
    }

    [Fact]
    public void Request_requires_server_name_and_at_least_one_check()
    {
        var result = ServerStatusValidator.Create(
            "transaction-processor",
            new ServerStatusRequest { ServerName = " ", Checks = [] },
            DateTimeOffset.UtcNow);

        Assert.Contains(result.Errors, error => error.Contains("ServerName", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Checks", StringComparison.Ordinal));
    }

    [Fact]
    public void Request_rejects_unknown_check_status()
    {
        var result = ServerStatusValidator.Create(
            "transaction-processor",
            new ServerStatusRequest
            {
                ServerName = "SERVER-01",
                Checks = [Check("Disk Space", "Unknown")]
            },
            DateTimeOffset.UtcNow);

        Assert.Contains(result.Errors, error => error.Contains("Status", StringComparison.Ordinal));
        Assert.Null(result.Status);
    }

    private static SupportCheckResultRequest Check(string name, string status) => new()
    {
        Name = name,
        Status = status,
        Summary = $"{name} is {status}.",
        Details = JsonDocument.Parse("{}").RootElement.Clone()
    };
}
