using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.Persistence;
using TransactionProcessing.MerchantFileProcessor.Services;

namespace TransactionProcessing.MerchantFileProcessor.Reporting;

public interface IOperationsDashboardService
{
    Task<OperationsDashboardModel> GetDashboardModelAsync(CancellationToken cancellationToken);

    Task<string> RenderDashboardHtmlAsync(CancellationToken cancellationToken);

    Task<string> RenderConfigurationHtmlAsync(CancellationToken cancellationToken);

    Task<string> RenderRunsHtmlAsync(CancellationToken cancellationToken);
}

public sealed record OperationsMetric(string Label, string Value, string Detail);

public sealed record MerchantOperationsRow(
    string MerchantId,
    string MerchantName,
    bool Enabled,
    string EstateId,
    string RunTimesUtc,
    DateTimeOffset? LastMerchantRunUtc,
    DateTimeOffset? NextScheduledUtc,
    int SuccessfulFiles,
    int FailedFiles,
    int PendingStatusChecks);

public sealed record FileProfileRow(
    string FileProfileId,
    string FileProcessorFileProfileId,
    string Format,
    string FileExtension,
    string? ContentType,
    string LayoutSummary,
    int FieldCount,
    int HeaderFieldCount,
    int TrailerFieldCount,
    string FieldMap);

public sealed record ContractMappingRow(
    string ContractId,
    string FileProfileId);

public sealed record RunHistoryRow(
    string MerchantId,
    string MerchantName,
    DateTimeOffset ScheduledRunUtc,
    DateTimeOffset CompletedUtc,
    string Status,
    string? ErrorMessage);

public sealed record OperationsDashboardModel(
    DateTimeOffset GeneratedUtc,
    string EnvironmentName,
    string ConnectionStringSummary,
    string AuthenticationSummary,
    string FileProcessingSummary,
    string TransactionGenerationSummary,
    string PollingSummary,
    string LoggingSummary,
    IReadOnlyList<OperationsMetric> Metrics,
    IReadOnlyList<MerchantOperationsRow> Merchants,
    IReadOnlyList<FileProfileRow> FileProfiles,
    IReadOnlyList<ContractMappingRow> Contracts,
    IReadOnlyList<RunHistoryRow> RecentRuns);

public sealed class OperationsDashboardService(
    IDbContextFactory<MerchantFileProcessorDbContext> dbContextFactory,
    IMerchantProcessingConfigurationState configurationState,
    FrameworkLoggingOptions frameworkLoggingOptions,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : IOperationsDashboardService
{
    private const string SectionEnd = "  </section>";
    private const string SectionPanelStart = "  <section class=\"panel\">";
    private const string DivEnd = "    </div>";
    private const string TableStart = "      <table>";
    private const string TableEnd = "      </table>";
    private const string TableBodyStart = "      <tbody>";
    private const string TableBodyEnd = "      </tbody>";
    private const string TableRowStart = "        <tr>";
    private const string TableRowEnd = "        </tr>";
    private const string NeutralBadgeTone = "neutral";

    public Task<OperationsDashboardModel> GetDashboardModelAsync(CancellationToken cancellationToken) =>
        this.BuildDashboardModelAsync(cancellationToken);

    public async Task<string> RenderDashboardHtmlAsync(CancellationToken cancellationToken)
    {
        var model = await this.BuildDashboardModelAsync(cancellationToken);
        var html = new StringBuilder();

        AppendDocumentStart(html, "Merchant File Processor Operations", "Management and operations");
        AppendNavigation(html, ("Overview", "/ops"), ("Configuration", "/ops/config"), ("Run history", "/ops/runs"), ("Status board", "/status"));

        html.AppendLine("  <section class=\"hero\">");
        html.AppendLine("    <div>");
        html.AppendLine("      <div class=\"eyebrow\">Operations console</div>");
        html.AppendLine("      <h1>Merchant File Processor</h1>");
        html.AppendLine("      <p>Live configuration, scheduling, and processing visibility for the worker, using the same runtime settings that power the service.</p>");
        html.AppendLine(DivEnd);
        html.AppendLine("    <div class=\"hero-panel\">");
        html.AppendLine($"      <div class=\"hero-label\">Environment</div><div class=\"hero-value\">{Encode(model.EnvironmentName)}</div>");
        html.AppendLine($"      <div class=\"hero-label\">Generated</div><div class=\"hero-value mono\">{Encode(model.GeneratedUtc.ToString("u"))}</div>");
        html.AppendLine($"      <div class=\"hero-label\">Connection</div><div class=\"hero-value\">{Encode(model.ConnectionStringSummary)}</div>");
        html.AppendLine(DivEnd);
        html.AppendLine(SectionEnd);

        AppendMetrics(html, model.Metrics);

        html.AppendLine(SectionPanelStart);
        html.AppendLine("    <div class=\"section-title\">Merchant schedule</div>");
        html.AppendLine(TableStart);
        html.AppendLine("      <thead><tr><th>Merchant</th><th>Enabled</th><th>Estate</th><th>Run times (UTC)</th><th>Last merchant run</th><th>Next run</th><th>Success</th><th>Failed</th><th>Pending checks</th><th>Status</th></tr></thead>");
        html.AppendLine(TableBodyStart);

        foreach (var merchant in model.Merchants)
        {
            html.AppendLine(TableRowStart);
            html.AppendLine($"          <td><strong>{Encode(merchant.MerchantName)}</strong><br /><span class=\"mono muted\">{Encode(merchant.MerchantId)}</span></td>");
            html.AppendLine($"          <td>{RenderBadge(merchant.Enabled ? "Enabled" : "Disabled", merchant.Enabled ? "good" : NeutralBadgeTone)}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(merchant.EstateId)}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(merchant.RunTimesUtc)}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(merchant.LastMerchantRunUtc?.ToString("u") ?? "Never")}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(merchant.NextScheduledUtc?.ToString("u") ?? "Disabled")}</td>");
            html.AppendLine($"          <td>{merchant.SuccessfulFiles}</td>");
            html.AppendLine($"          <td>{merchant.FailedFiles}</td>");
            html.AppendLine($"          <td>{merchant.PendingStatusChecks}</td>");
            html.AppendLine($"          <td><a href=\"/status/{Uri.EscapeDataString(merchant.MerchantId)}\">View status</a></td>");
            html.AppendLine(TableRowEnd);
        }

        if (model.Merchants.Count == 0)
        {
            html.AppendLine("        <tr><td colspan=\"10\">No merchants are configured.</td></tr>");
        }

        html.AppendLine(TableBodyEnd);
        html.AppendLine(TableEnd);
        html.AppendLine(SectionEnd);

        html.AppendLine("  <section class=\"grid-2\">");
        AppendFileProfilesSection(html, model.FileProfiles);
        AppendContractsSection(html, model.Contracts);
        html.AppendLine(SectionEnd);

        html.AppendLine(SectionPanelStart);
        html.AppendLine("    <div class=\"section-title\">Recent merchant runs</div>");
        html.AppendLine(TableStart);
        html.AppendLine("      <thead><tr><th>Merchant</th><th>Scheduled</th><th>Completed</th><th>Status</th><th>Error</th></tr></thead>");
        html.AppendLine(TableBodyStart);

        foreach (var run in model.RecentRuns)
        {
            html.AppendLine(TableRowStart);
            html.AppendLine($"          <td><strong>{Encode(run.MerchantName)}</strong><br /><span class=\"mono muted\">{Encode(run.MerchantId)}</span></td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(run.ScheduledRunUtc.ToString("u"))}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(run.CompletedUtc.ToString("u"))}</td>");
            html.AppendLine($"          <td>{RenderBadge(run.Status, RunTone(run.Status))}</td>");
            html.AppendLine($"          <td>{Encode(run.ErrorMessage ?? string.Empty)}</td>");
            html.AppendLine(TableRowEnd);
        }

        if (model.RecentRuns.Count == 0)
        {
            html.AppendLine("        <tr><td colspan=\"5\">No merchant runs have been recorded yet.</td></tr>");
        }

        html.AppendLine(TableBodyEnd);
        html.AppendLine("    </table>");
        html.AppendLine(SectionEnd);

        AppendDocumentEnd(html);
        return html.ToString();
    }

    public async Task<string> RenderConfigurationHtmlAsync(CancellationToken cancellationToken)
    {
        var model = await this.BuildDashboardModelAsync(cancellationToken);
        var html = new StringBuilder();

        AppendDocumentStart(html, "Merchant File Processor Configuration", "Appsettings-driven runtime inventory");
        AppendNavigation(html, ("Overview", "/ops"), ("Configuration", "/ops/config"), ("Run history", "/ops/runs"), ("Status board", "/status"));

        html.AppendLine("  <section class=\"hero\">");
        html.AppendLine("    <div>");
        html.AppendLine("      <div class=\"eyebrow\">Configuration inventory</div>");
        html.AppendLine("      <h1>Appsettings review</h1>");
        html.AppendLine("      <p>This page mirrors the effective settings loaded at startup, so operators can validate what the worker is using without opening the deployment package.</p>");
        html.AppendLine(DivEnd);
        html.AppendLine("    <div class=\"hero-panel\">");
        html.AppendLine($"      <div class=\"hero-label\">Authentication</div><div class=\"hero-value\">{Encode(model.AuthenticationSummary)}</div>");
        html.AppendLine($"      <div class=\"hero-label\">File processing</div><div class=\"hero-value\">{Encode(model.FileProcessingSummary)}</div>");
        html.AppendLine($"      <div class=\"hero-label\">Polling</div><div class=\"hero-value\">{Encode(model.PollingSummary)}</div>");
        html.AppendLine(DivEnd);
        html.AppendLine(SectionEnd);

        html.AppendLine("  <section class=\"grid-2\">");
        AppendSummaryPanel(html, "Runtime summary", new[]
        {
            ("Environment", model.EnvironmentName),
            ("Database", model.ConnectionStringSummary),
            ("Logging", model.LoggingSummary),
            ("Transaction generation", model.TransactionGenerationSummary),
            ("Authentication", model.AuthenticationSummary),
            ("File processing", model.FileProcessingSummary),
            ("Polling", model.PollingSummary)
        });

        AppendMerchantConfigPanel(html);
        html.AppendLine(SectionEnd);

        html.AppendLine("  <section class=\"grid-2\">");
        AppendFileProfilesSection(html, model.FileProfiles);
        AppendContractsSection(html, model.Contracts);
        html.AppendLine(SectionEnd);

        html.AppendLine("  <section class=\"panel\">");
        html.AppendLine("    <div class=\"section-title\">Merchant definitions</div>");
        html.AppendLine("    <table>");
        html.AppendLine("      <thead><tr><th>Merchant</th><th>Enabled</th><th>Estate</th><th>Merchant ID</th><th>Run schedule</th></tr></thead>");
        html.AppendLine(TableBodyStart);

        foreach (var merchant in model.Merchants)
        {
            html.AppendLine(TableRowStart);
            html.AppendLine($"          <td><strong>{Encode(merchant.MerchantName)}</strong></td>");
            html.AppendLine($"          <td>{RenderBadge(merchant.Enabled ? "Enabled" : "Disabled", merchant.Enabled ? "good" : NeutralBadgeTone)}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(merchant.EstateId)}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(merchant.MerchantId)}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(merchant.RunTimesUtc)}</td>");
            html.AppendLine(TableRowEnd);
        }

        if (model.Merchants.Count == 0)
        {
            html.AppendLine("        <tr><td colspan=\"5\">No merchants are configured.</td></tr>");
        }

        html.AppendLine(TableBodyEnd);
        html.AppendLine("    </table>");
        html.AppendLine(SectionEnd);

        AppendDocumentEnd(html);
        return html.ToString();
    }

    public async Task<string> RenderRunsHtmlAsync(CancellationToken cancellationToken)
    {
        var model = await this.BuildDashboardModelAsync(cancellationToken);
        var html = new StringBuilder();

        AppendDocumentStart(html, "Merchant File Processor Runs", "Recent run history");
        AppendNavigation(html, ("Overview", "/ops"), ("Configuration", "/ops/config"), ("Run history", "/ops/runs"), ("Status board", "/status"));

        html.AppendLine("  <section class=\"hero\">");
        html.AppendLine("    <div>");
        html.AppendLine("      <div class=\"eyebrow\">Run history</div>");
        html.AppendLine("      <h1>Merchant execution audit trail</h1>");
        html.AppendLine("      <p>Each record shows the scheduled slot, completion time, and final outcome for a merchant processing cycle.</p>");
        html.AppendLine(DivEnd);
        html.AppendLine(SectionEnd);

        html.AppendLine("  <section class=\"panel\">");
        html.AppendLine("    <table>");
        html.AppendLine("      <thead><tr><th>Merchant</th><th>Scheduled</th><th>Completed</th><th>Status</th><th>Error</th></tr></thead>");
        html.AppendLine(TableBodyStart);

        foreach (var run in model.RecentRuns)
        {
            html.AppendLine(TableRowStart);
            html.AppendLine($"          <td><strong>{Encode(run.MerchantName)}</strong><br /><span class=\"mono muted\">{Encode(run.MerchantId)}</span></td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(run.ScheduledRunUtc.ToString("u"))}</td>");
            html.AppendLine($"          <td class=\"mono\">{Encode(run.CompletedUtc.ToString("u"))}</td>");
            html.AppendLine($"          <td>{RenderBadge(run.Status, RunTone(run.Status))}</td>");
            html.AppendLine($"          <td>{Encode(run.ErrorMessage ?? string.Empty)}</td>");
            html.AppendLine(TableRowEnd);
        }

        if (model.RecentRuns.Count == 0)
        {
            html.AppendLine("        <tr><td colspan=\"5\">No merchant runs are recorded yet.</td></tr>");
        }

        html.AppendLine(TableBodyEnd);
        html.AppendLine("    </table>");
        html.AppendLine(SectionEnd);

        AppendDocumentEnd(html);
        return html.ToString();
    }

    private async Task<OperationsDashboardModel> BuildDashboardModelAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var options = configurationState.Current;
        var fileSendRows = await LoadFileSendRowsAsync(dbContext, cancellationToken);
        var merchantRunRows = await LoadMerchantRunRowsAsync(dbContext, cancellationToken);
        var pendingStatusChecks = await CountPendingStatusChecksAsync(dbContext, cancellationToken);

        return new OperationsDashboardModel(
            DateTimeOffset.UtcNow,
            hostEnvironment.EnvironmentName,
            ResolveConnectionStringSummary(),
            DescribeAuthentication(),
            DescribeFileProcessing(),
            DescribeTransactionGeneration(),
            DescribePolling(),
            DescribeLogging(),
            BuildMetrics(options, pendingStatusChecks),
            BuildMerchantRows(options, fileSendRows, merchantRunRows),
            BuildFileProfiles(options),
            BuildContracts(options),
            BuildRecentRuns(merchantRunRows));
    }

    private static async Task<IReadOnlyList<dynamic>> LoadFileSendRowsAsync(
        MerchantFileProcessorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.FileSendRecords
            .Select(record => new
            {
                record.MerchantId,
                MerchantName = record.MerchantName ?? record.MerchantId,
                record.Status,
                record.ProcessedUtc,
                record.ProcessingCompleted,
                record.EstateId,
                record.FileProcessorFileId
            })
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<dynamic>> LoadMerchantRunRowsAsync(
        MerchantFileProcessorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.MerchantRunRecords
            .Select(record => new
            {
                record.MerchantId,
                MerchantName = record.MerchantName ?? record.MerchantId,
                record.ScheduledRunUtc,
                record.CompletedUtc,
                record.Status,
                record.ErrorMessage
            })
            .ToListAsync(cancellationToken);
    }

    private static async Task<int> CountPendingStatusChecksAsync(
        MerchantFileProcessorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.FileSendRecords.CountAsync(record =>
            record.Status == FileSendStatuses.Succeeded &&
            !record.ProcessingCompleted &&
            !string.IsNullOrWhiteSpace(record.EstateId) &&
            !string.IsNullOrWhiteSpace(record.FileProcessorFileId),
            cancellationToken);
    }

    private static IReadOnlyList<OperationsMetric> BuildMetrics(MerchantProcessingOptions options, int pendingStatusChecks) =>
        new[]
        {
            new OperationsMetric("Merchants", options.Merchants.Count.ToString(), $"{options.Merchants.Count(merchant => merchant.Enabled)} enabled"),
            new OperationsMetric("Contracts", options.ContractDefinitions.Count.ToString(), "Contract to file-profile mappings"),
            new OperationsMetric("File profiles", options.FileProfiles.Count.ToString(), "Delimited and JSON builders"),
            new OperationsMetric("Pending checks", pendingStatusChecks.ToString(), "Files awaiting completion polling"),
            new OperationsMetric("Run interval", $"{options.FileStatusPolling.PollIntervalSeconds}s", "Status polling cadence"),
            new OperationsMetric("Transaction range", $"{options.TransactionGeneration.MinimumTransactionsPerContract}-{options.TransactionGeneration.MaximumTransactionsPerContract}", "Synthetic transaction batch size")
        };

    private static IReadOnlyList<MerchantOperationsRow> BuildMerchantRows(
        MerchantProcessingOptions options,
        IReadOnlyList<dynamic> fileSendRows,
        IReadOnlyList<dynamic> merchantRunRows)
    {
        var merchantLookup = fileSendRows
            .GroupBy(record => (string)record.MerchantId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    SuccessfulFiles = group.Count(record => record.Status == FileSendStatuses.Succeeded),
                    FailedFiles = group.Count(record => record.Status == FileSendStatuses.Failed),
                    LastProcessedUtc = group.Max(record => (DateTimeOffset?)record.ProcessedUtc)
                },
                StringComparer.OrdinalIgnoreCase);

        var runLookup = merchantRunRows
            .GroupBy(record => (string)record.MerchantId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(record => record.CompletedUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        var pendingLookup = fileSendRows
            .Where(record => (bool)record.ProcessingCompleted == false && !string.IsNullOrWhiteSpace((string)record.MerchantId))
            .GroupBy(record => (string)record.MerchantId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return options.Merchants
            .OrderBy(merchant => merchant.MerchantId, StringComparer.OrdinalIgnoreCase)
            .Select(merchant =>
            {
                merchantLookup.TryGetValue(merchant.MerchantId, out var fileSummary);
                runLookup.TryGetValue(merchant.MerchantId, out var runSummary);
                pendingLookup.TryGetValue(merchant.MerchantId, out var pendingCount);

                return new MerchantOperationsRow(
                    merchant.MerchantId,
                    string.IsNullOrWhiteSpace(merchant.Name) ? merchant.MerchantId : merchant.Name,
                    merchant.Enabled,
                    merchant.EstateId,
                    string.Join(", ", merchant.GetDailyRunTimesUtc().Select(runTime => runTime.ToString("HH:mm:ss"))),
                    runSummary?.CompletedUtc,
                    GetNextScheduledUtc(merchant, DateTimeOffset.UtcNow),
                    fileSummary?.SuccessfulFiles ?? 0,
                    fileSummary?.FailedFiles ?? 0,
                    pendingCount);
            })
            .ToArray();
    }

    private static IReadOnlyList<FileProfileRow> BuildFileProfiles(MerchantProcessingOptions options) =>
        options.FileProfiles
            .OrderBy(profile => profile.FileProfileId, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new FileProfileRow(
                profile.FileProfileId,
                profile.FileProcessorFileProfileId,
                profile.Format,
                profile.FileExtension,
                profile.ContentType,
                DescribeLayout(profile),
                profile.Fields.Count,
                profile.Delimited.HeaderFields.Count,
                profile.Delimited.TrailerFields.Count,
                DescribeFieldMap(profile.Fields)))
            .ToArray();

    private static IReadOnlyList<ContractMappingRow> BuildContracts(MerchantProcessingOptions options) =>
        options.ContractDefinitions
            .OrderBy(contract => contract.ContractId, StringComparer.OrdinalIgnoreCase)
            .Select(contract => new ContractMappingRow(contract.ContractId, contract.FileProfileId))
            .ToArray();

    private static IReadOnlyList<RunHistoryRow> BuildRecentRuns(IReadOnlyList<dynamic> merchantRunRows) =>
        merchantRunRows
            .OrderByDescending(record => record.CompletedUtc)
            .Take(50)
            .Select(record => new RunHistoryRow(
                record.MerchantId,
                record.MerchantName,
                record.ScheduledRunUtc,
                record.CompletedUtc,
                record.Status,
                record.ErrorMessage))
            .ToArray();

    private string ResolveConnectionStringSummary()
    {
        var connectionString = configuration.GetConnectionString("MerchantFileProcessor");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "merchant-file-processor.db";
        }

        const string dataSourcePrefix = "Data Source=";
        if (!connectionString.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var path = connectionString[dataSourcePrefix.Length..].Trim();
        return Path.IsPathRooted(path) ? path : Path.Combine(hostEnvironment.ContentRootPath, path);
    }

    private string DescribeAuthentication() =>
        string.IsNullOrWhiteSpace(configurationState.Current.Authentication.ClientId)
            ? "Not configured"
            : $"Client ID {Mask(configurationState.Current.Authentication.ClientId)} and secret {MaskSecret(configurationState.Current.Authentication.ClientSecret)}";

    private string DescribeFileProcessing() =>
        string.IsNullOrWhiteSpace(configurationState.Current.FileProcessing.UserId)
            ? "Not configured"
            : $"Processing user {Mask(configurationState.Current.FileProcessing.UserId)}";

    private string DescribeTransactionGeneration() =>
        $"{configurationState.Current.TransactionGeneration.MinimumTransactionsPerContract} to {configurationState.Current.TransactionGeneration.MaximumTransactionsPerContract} transactions per contract";

    private string DescribePolling() =>
        $"{configurationState.Current.FileStatusPolling.PollIntervalSeconds} second poll interval";

    private string DescribeLogging() =>
        $"EF Core trace {(frameworkLoggingOptions.EnableEfCoreCommandTrace ? "on" : "off")}, HTTP trace {(frameworkLoggingOptions.EnableHttpClientTrace ? "on" : "off")}";

    private static string DescribeLayout(FileProfileOptions profile)
    {
        var parts = new List<string>
        {
            $"{profile.Format} / {profile.FileExtension}"
        };

        if (profile.Format.Equals(FileProfileFormats.Delimited, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"delimiter '{profile.Delimited.Delimiter}'");
            parts.Add(profile.Delimited.IncludeHeader ? "header row" : "no header row");
        }

        if (profile.Format.Equals(FileProfileFormats.Json, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(profile.Json.WriteIndented ? "indented JSON" : "compact JSON");
            if (!string.IsNullOrWhiteSpace(profile.Json.RootPropertyName))
            {
                parts.Add($"root '{profile.Json.RootPropertyName}'");
            }
        }

        return string.Join(", ", parts);
    }

    private static string DescribeFieldMap(IEnumerable<FileFieldOptions> fields) =>
        string.Join(", ", fields.Select(field =>
        {
            if (!string.IsNullOrWhiteSpace(field.Value))
            {
                return $"{field.Name}={field.Value}";
            }

            var format = string.IsNullOrWhiteSpace(field.Format) ? string.Empty : $" ({field.Format})";
            return $"{field.Name}<-{field.Source}{format}";
        }));

    private static DateTimeOffset? GetNextScheduledUtc(MerchantOptions merchant, DateTimeOffset nowUtc)
    {
        if (!merchant.Enabled)
        {
            return null;
        }

        var runTimes = merchant.GetDailyRunTimesUtc();
        var nextRunDate = DateOnly.FromDateTime(nowUtc.UtcDateTime);

        foreach (var runTime in runTimes)
        {
            var nextRun = new DateTimeOffset(
                nextRunDate.Year,
                nextRunDate.Month,
                nextRunDate.Day,
                runTime.Hour,
                runTime.Minute,
                runTime.Second,
                TimeSpan.Zero);

            if (nextRun > nowUtc)
            {
                return nextRun;
            }
        }

        var firstRunTime = runTimes[0];
        var tomorrow = nextRunDate.AddDays(1);
        return new DateTimeOffset(
            tomorrow.Year,
            tomorrow.Month,
            tomorrow.Day,
            firstRunTime.Hour,
            firstRunTime.Minute,
            firstRunTime.Second,
            TimeSpan.Zero);
    }

    private static void AppendNavigation(StringBuilder html, params (string Label, string Href)[] links)
    {
        html.AppendLine("  <nav class=\"topbar\">");
        foreach (var link in links)
        {
            html.AppendLine($"    <a href=\"{Encode(link.Href)}\">{Encode(link.Label)}</a>");
        }
        html.AppendLine("  </nav>");
    }

    private static void AppendMetrics(StringBuilder html, IReadOnlyList<OperationsMetric> metrics)
    {
        html.AppendLine("  <section class=\"metrics\">");
        foreach (var metric in metrics)
        {
            html.AppendLine("    <div class=\"metric\">");
            html.AppendLine($"      <div class=\"metric-label\">{Encode(metric.Label)}</div>");
            html.AppendLine($"      <div class=\"metric-value\">{Encode(metric.Value)}</div>");
            html.AppendLine($"      <div class=\"metric-detail\">{Encode(metric.Detail)}</div>");
        html.AppendLine(DivEnd);
        }
        html.AppendLine(SectionEnd);
    }

    private static void AppendSummaryPanel(StringBuilder html, string title, IEnumerable<(string Label, string Value)> items)
    {
        html.AppendLine("    <section class=\"panel\">");
        html.AppendLine($"      <div class=\"section-title\">{Encode(title)}</div>");
        html.AppendLine("      <dl class=\"summary-list\">");

        foreach (var item in items)
        {
            html.AppendLine($"        <dt>{Encode(item.Label)}</dt>");
            html.AppendLine($"        <dd>{Encode(item.Value)}</dd>");
        }

        html.AppendLine("      </dl>");
        html.AppendLine(SectionEnd);
    }

    private static void AppendMerchantConfigPanel(StringBuilder html)
    {
        html.AppendLine("    <section class=\"panel\">");
        html.AppendLine("      <div class=\"section-title\">Configuration summary</div>");
        html.AppendLine("      <p class=\"muted\">This dashboard reads the effective runtime settings after binding appsettings, environment overrides, and command-line values.</p>");
        html.AppendLine("      <ul class=\"bullets\">");
        html.AppendLine("        <li>Authentication values are masked and only shown as configured or not configured.</li>");
        html.AppendLine("        <li>SQLite is used for the local audit trail and status tables.</li>");
        html.AppendLine("        <li>Merchant schedules are interpreted in UTC and backed by the worker loop.</li>");
        html.AppendLine("      </ul>");
        html.AppendLine(SectionEnd);
    }

    private static void AppendFileProfilesSection(StringBuilder html, IReadOnlyList<FileProfileRow> fileProfiles)
    {
        html.AppendLine(SectionPanelStart);
        html.AppendLine("      <div class=\"section-title\">File profiles</div>");
        html.AppendLine(TableStart);
        html.AppendLine("        <thead><tr><th>Profile</th><th>Format</th><th>Layout</th><th>Fields</th><th>Mapping</th></tr></thead>");
        html.AppendLine(TableBodyStart);

        foreach (var profile in fileProfiles)
        {
            html.AppendLine("          <tr>");
            html.AppendLine($"            <td><strong>{Encode(profile.FileProfileId)}</strong><br /><span class=\"mono muted\">{Encode(profile.FileProcessorFileProfileId)}</span></td>");
            html.AppendLine($"            <td>{RenderBadge(profile.Format, NeutralBadgeTone)}</td>");
            html.AppendLine($"            <td>{Encode(profile.LayoutSummary)}<br /><span class=\"mono muted\">.{Encode(profile.FileExtension)}{(string.IsNullOrWhiteSpace(profile.ContentType) ? string.Empty : $" / {Encode(profile.ContentType)}")}</span></td>");
            html.AppendLine($"            <td>{profile.FieldCount} total<br /><span class=\"mono muted\">{profile.HeaderFieldCount} header, {profile.TrailerFieldCount} trailer</span></td>");
            html.AppendLine($"            <td class=\"mono smallwrap\">{Encode(profile.FieldMap)}</td>");
            html.AppendLine("          </tr>");
        }

        if (fileProfiles.Count == 0)
        {
            html.AppendLine("          <tr><td colspan=\"5\">No file profiles are configured.</td></tr>");
        }

        html.AppendLine(TableBodyEnd);
        html.AppendLine(TableEnd);
        html.AppendLine(SectionEnd);
    }

    private static void AppendContractsSection(StringBuilder html, IReadOnlyList<ContractMappingRow> contracts)
    {
        html.AppendLine(SectionPanelStart);
        html.AppendLine("      <div class=\"section-title\">Contract mappings</div>");
        html.AppendLine(TableStart);
        html.AppendLine("        <thead><tr><th>Contract</th><th>File profile</th></tr></thead>");
        html.AppendLine(TableBodyStart);

        foreach (var contract in contracts)
        {
            html.AppendLine("          <tr>");
            html.AppendLine($"            <td class=\"mono\">{Encode(contract.ContractId)}</td>");
            html.AppendLine($"            <td>{Encode(contract.FileProfileId)}</td>");
            html.AppendLine("          </tr>");
        }

        if (contracts.Count == 0)
        {
            html.AppendLine("          <tr><td colspan=\"2\">No contract definitions are configured.</td></tr>");
        }

        html.AppendLine(TableBodyEnd);
        html.AppendLine(TableEnd);
        html.AppendLine(SectionEnd);
    }

    private static void AppendDocumentStart(StringBuilder html, string title, string subtitle)
    {
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\" />");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.AppendLine("  <meta http-equiv=\"refresh\" content=\"30\" />");
        html.AppendLine($"  <title>{WebUtility.HtmlEncode(title)}</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    :root { color-scheme: dark; --bg: #07111f; --panel: rgba(10, 18, 32, 0.88); --panel-2: rgba(13, 24, 42, 0.98); --border: rgba(153, 180, 230, 0.18); --text: #ecf2ff; --muted: #9cb0cf; --accent: #7cc4ff; --accent-2: #8bffc6; --danger: #ff7a8a; --warn: #ffd37a; --good: #68e29b; }");
        html.AppendLine("    * { box-sizing: border-box; }");
        html.AppendLine("    body { margin: 0; font-family: Segoe UI, Arial, sans-serif; background: radial-gradient(circle at top left, rgba(65, 123, 255, 0.24), transparent 35%), radial-gradient(circle at top right, rgba(46, 205, 160, 0.15), transparent 28%), linear-gradient(180deg, #081120 0%, #07111f 60%, #050b15 100%); color: var(--text); }");
        html.AppendLine("    a { color: var(--accent); text-decoration: none; }");
        html.AppendLine("    a:hover { text-decoration: underline; }");
        html.AppendLine("    .shell { max-width: 1480px; margin: 0 auto; padding: 24px; }");
        html.AppendLine("    .topbar { display: flex; flex-wrap: wrap; gap: 12px; margin-bottom: 20px; }");
        html.AppendLine("    .topbar a { display: inline-flex; align-items: center; padding: 10px 14px; border: 1px solid var(--border); border-radius: 999px; background: rgba(255,255,255,0.04); color: var(--text); }");
        html.AppendLine("    .hero { display: grid; grid-template-columns: minmax(0, 1.8fr) minmax(320px, 0.9fr); gap: 20px; align-items: stretch; margin-bottom: 22px; }");
        html.AppendLine("    .hero h1 { margin: 6px 0 10px; font-size: clamp(2rem, 3vw, 3.25rem); line-height: 1.04; }");
        html.AppendLine("    .hero p { margin: 0; max-width: 70ch; color: var(--muted); font-size: 1.02rem; }");
        html.AppendLine("    .eyebrow { text-transform: uppercase; letter-spacing: 0.2em; color: var(--accent-2); font-size: 0.75rem; font-weight: 700; }");
        html.AppendLine("    .hero-panel, .panel, .metric { border: 1px solid var(--border); background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%); box-shadow: 0 18px 55px rgba(0, 0, 0, 0.24); backdrop-filter: blur(10px); }");
        html.AppendLine("    .hero-panel { border-radius: 22px; padding: 20px; display: grid; gap: 8px; }");
        html.AppendLine("    .hero-label { color: var(--muted); font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.12em; margin-top: 6px; }");
        html.AppendLine("    .hero-value { font-size: 1rem; word-break: break-word; }");
        html.AppendLine("    .metrics { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 14px; margin: 0 0 22px; }");
        html.AppendLine("    .metric { border-radius: 18px; padding: 16px; min-height: 112px; }");
        html.AppendLine("    .metric-label { color: var(--muted); text-transform: uppercase; letter-spacing: 0.12em; font-size: 0.72rem; }");
        html.AppendLine("    .metric-value { font-size: 1.8rem; font-weight: 700; margin: 8px 0 6px; }");
        html.AppendLine("    .metric-detail { color: var(--muted); font-size: 0.92rem; line-height: 1.35; }");
        html.AppendLine("    .panel { border-radius: 20px; padding: 18px; margin-bottom: 18px; }");
        html.AppendLine("    .section-title { font-size: 1rem; font-weight: 700; margin-bottom: 12px; }");
        html.AppendLine("    .grid-2 { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; }");
        html.AppendLine("    .summary-list { margin: 0; display: grid; grid-template-columns: minmax(180px, 240px) minmax(0, 1fr); gap: 10px 14px; }");
        html.AppendLine("    .summary-list dt { color: var(--muted); }");
        html.AppendLine("    .summary-list dd { margin: 0; }");
        html.AppendLine("    .bullets { margin: 0; padding-left: 20px; color: var(--muted); }");
        html.AppendLine("    .bullets li { margin-bottom: 8px; }");
        html.AppendLine("    table { width: 100%; border-collapse: collapse; }");
        html.AppendLine("    th, td { border-top: 1px solid rgba(153, 180, 230, 0.14); padding: 11px 10px; text-align: left; vertical-align: top; }");
        html.AppendLine("    th { color: var(--muted); font-size: 0.76rem; text-transform: uppercase; letter-spacing: 0.12em; border-top: none; }");
        html.AppendLine("    tbody tr:hover { background: rgba(255,255,255,0.03); }");
        html.AppendLine("    .mono { font-family: Consolas, Menlo, Monaco, monospace; }");
        html.AppendLine("    .smallwrap { white-space: normal; word-break: break-word; }");
        html.AppendLine("    .muted { color: var(--muted); }");
        html.AppendLine("    .badge { display: inline-flex; align-items: center; padding: 5px 10px; border-radius: 999px; font-size: 0.82rem; font-weight: 700; border: 1px solid transparent; }");
        html.AppendLine("    .badge.good { background: rgba(104, 226, 155, 0.14); color: var(--good); border-color: rgba(104, 226, 155, 0.24); }");
        html.AppendLine("    .badge.bad { background: rgba(255, 122, 138, 0.14); color: var(--danger); border-color: rgba(255, 122, 138, 0.24); }");
        html.AppendLine("    .badge.warn { background: rgba(255, 211, 122, 0.14); color: var(--warn); border-color: rgba(255, 211, 122, 0.24); }");
        html.AppendLine("    .badge.neutral { background: rgba(124, 196, 255, 0.12); color: var(--accent); border-color: rgba(124, 196, 255, 0.2); }");
        html.AppendLine("    @media (max-width: 1200px) { .metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); } .hero, .grid-2 { grid-template-columns: 1fr; } }");
        html.AppendLine("    @media (max-width: 760px) { .shell { padding: 16px; } .metrics { grid-template-columns: 1fr; } .summary-list { grid-template-columns: 1fr; } th, td { font-size: 0.92rem; } }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("  <div class=\"shell\">");
        html.AppendLine($"    <div class=\"eyebrow\">{WebUtility.HtmlEncode(subtitle)}</div>");
        html.AppendLine($"    <div class=\"muted\">{WebUtility.HtmlEncode(title)}</div>");
    }

    private static void AppendDocumentEnd(StringBuilder html)
    {
        html.AppendLine("  </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
    }

    private static string RenderBadge(string value, string tone) =>
        $"<span class=\"badge {tone}\">{Encode(value)}</span>";

    private static string RunTone(string status)
    {
        if (status.Equals(MerchantRunStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            return "good";
        }

        if (status.Equals(MerchantRunStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return "bad";
        }

        return NeutralBadgeTone;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Mask(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 8 ? trimmed : $"{trimmed[..4]}...{trimmed[^4..]}";
    }

    private static string MaskSecret(string value) =>
        string.IsNullOrWhiteSpace(value) ? "not set" : "configured";
}
