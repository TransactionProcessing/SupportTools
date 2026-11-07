using System.Text.Json;
using HealthMonitoring.Domain;

namespace HealthMonitoring.Api;

public sealed class ServerStatusRequest
{
    public string? OverallStatus { get; set; }
    public List<SupportCheckResultRequest> Checks { get; set; } = [];
}

public sealed class SupportCheckResultRequest
{
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? Summary { get; set; }
    public JsonElement? Details { get; set; }
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public double DurationMs { get; set; }
    public string? Error { get; set; }
}

public sealed record ServerStatusResponse(
    string ServerName,
    string OverallStatus,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<SupportCheckResultRequest> Checks);

public sealed record ServerStatusValidationResult(IReadOnlyList<string> Errors, ServerStatus? Status);

public static class ServerStatusValidator
{
    private static readonly HashSet<string> AllowedStatuses = ["Passed", "Warning", "Failed"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ServerStatusValidationResult Create(string serverName, ServerStatusRequest request, DateTimeOffset now)
    {
        serverName = serverName?.Trim() ?? string.Empty;
        var errors = ValidateRequest(serverName, request);

        if (errors.Count > 0) return new ServerStatusValidationResult(errors, null);

        var checks = request.Checks!;
        return new ServerStatusValidationResult(errors, new ServerStatus
        {
            Id = Guid.NewGuid(),
            ServerName = serverName,
            OverallStatus = CalculateOverallStatus(checks),
            ChecksJson = JsonSerializer.Serialize(checks, JsonOptions),
            ReceivedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private static List<string> ValidateRequest(string serverName, ServerStatusRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(serverName)) errors.Add("ServerName is required.");
        if (serverName.Length > 300) errors.Add("ServerName cannot exceed 300 characters.");
        if (request.Checks is null || request.Checks.Count == 0) errors.Add("Checks must contain at least one check.");

        foreach (var check in request.Checks ?? [])
            AddCheckValidationErrors(errors, check);

        return errors;
    }

    private static void AddCheckValidationErrors(List<string> errors, SupportCheckResultRequest check)
    {
        if (string.IsNullOrWhiteSpace(check.Name)) errors.Add("Checks.Name is required.");
        if (string.IsNullOrWhiteSpace(check.Status) || !AllowedStatuses.Contains(check.Status))
            errors.Add($"Checks.Status must be one of: {string.Join(", ", AllowedStatuses)}.");
        if (string.IsNullOrWhiteSpace(check.Summary)) errors.Add("Checks.Summary is required.");
        if (check.DurationMs < 0) errors.Add("Checks.DurationMs cannot be negative.");
    }

    private static string CalculateOverallStatus(IReadOnlyList<SupportCheckResultRequest> checks) =>
        checks.Any(check => check.Status == "Failed")
            ? "Failed"
            : checks.Any(check => check.Status == "Warning")
                ? "Warning"
                : "Passed";
}
