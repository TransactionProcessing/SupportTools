using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.Persistence;
using TransactionProcessing.MerchantFileProcessor.Services;

namespace TransactionProcessing.MerchantFileProcessor.Reporting;

public interface IFileStatusReportService
{
    Task<FileStatusReport> GetReportAsync(CancellationToken cancellationToken);

    Task<string> RenderHtmlAsync(CancellationToken cancellationToken);

    Task<string?> RenderMerchantHtmlAsync(string merchantId, CancellationToken cancellationToken);

    Task<string?> RenderFileHtmlAsync(string merchantId, long fileId, CancellationToken cancellationToken);
}

public sealed record MerchantFileSummary(string MerchantId,
                                         string MerchantName,
                                         bool Enabled,
                                         int SuccessfulFilesSent,
                                         int FailedFiles,
                                         DateTimeOffset? LastProcessedUtc,
                                         DateTimeOffset? NextScheduledUtc);

public sealed record FileStatusRow(long Id,
                                   DateTimeOffset ProcessedUtc,
                                   string MerchantId,
                                   string MerchantName,
                                   string ContractId,
                                   string ContractName,
                                   string Status,
                                   string FileName,
                                   string FileProfileId,
                                   string Format,
                                   string? FileContent,
                                   int RecordCount,
                                   decimal TotalAmount,
                                   string? ErrorMessage,
                                   bool ProcessingCompleted,
                                   DateTimeOffset? LastStatusCheckUtc);

public sealed record FileStatusReport(DateTimeOffset GeneratedUtc,
                                      IReadOnlyList<MerchantFileSummary> MerchantSummaries,
                                      IReadOnlyList<FileStatusRow> RecentFiles);

public sealed record MerchantDetailReport(DateTimeOffset GeneratedUtc,
                                          MerchantFileSummary Merchant,
                                          IReadOnlyList<FileStatusRow> RecentFiles);

public sealed record FileDetailReport(DateTimeOffset GeneratedUtc,
                                      MerchantFileSummary Merchant,
                                      FileStatusRow File,
                                      IReadOnlyList<FileLineStatusRow> FileLines);

public sealed record FileLineStatusRow(int LineNumber,
                                       string Content,
                                       string ProcessingStatus,
                                       string? RejectionReason,
                                       string? TransactionId);

public sealed class FileStatusReportService(
    IDbContextFactory<MerchantFileProcessorDbContext> dbContextFactory,
    MerchantProcessingOptions options) : IFileStatusReportService
{
    public async Task<FileStatusReport> GetReportAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var aggregateSource = await dbContext.FileSendRecords
            .Select(record => new
            {
                record.MerchantId,
                record.Status,
                record.ProcessedUtc
            })
            .ToListAsync(cancellationToken);

        var aggregates = aggregateSource
            .GroupBy(record => record.MerchantId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                MerchantId = group.Key,
                SuccessfulFilesSent = group.Count(record => record.Status == FileSendStatuses.Succeeded),
                FailedFiles = group.Count(record => record.Status == FileSendStatuses.Failed),
                LastProcessedUtc = group.Max(record => (DateTimeOffset?)record.ProcessedUtc)
            })
            .ToList();

        var aggregateLookup = aggregates.ToDictionary(item => item.MerchantId, StringComparer.OrdinalIgnoreCase);

        var merchantSummaries = options.Merchants
            .OrderBy(merchant => merchant.MerchantId, StringComparer.OrdinalIgnoreCase)
            .Select(merchant =>
            {
                aggregateLookup.TryGetValue(merchant.MerchantId, out var aggregate);

                return new MerchantFileSummary(
                    merchant.MerchantId,
                    string.IsNullOrWhiteSpace(merchant.Name) ? merchant.MerchantId : merchant.Name,
                    merchant.Enabled,
                    aggregate?.SuccessfulFilesSent ?? 0,
                    aggregate?.FailedFiles ?? 0,
                    aggregate?.LastProcessedUtc,
                    GetNextScheduledUtc(merchant, DateTimeOffset.UtcNow));
            })
            .ToArray();

        var recentFiles = (await dbContext.FileSendRecords
            .Select(record => new FileStatusRow(
                record.Id,
                record.ProcessedUtc,
                record.MerchantId,
                record.MerchantName ?? record.MerchantId,
                record.ContractId,
                record.ContractName ?? record.ContractId,
                record.Status,
                record.FileName ?? string.Empty,
                record.FileProfileId ?? string.Empty,
                record.Format ?? string.Empty,
                record.FileContent,
                record.RecordCount ?? 0,
                record.TotalAmount ?? 0m,
                record.ErrorMessage,
                record.ProcessingCompleted,
                record.LastStatusCheckUtc))
            .ToListAsync(cancellationToken))
            .OrderByDescending(record => record.ProcessedUtc)
            .Take(100)
            .ToList();

        return new FileStatusReport(DateTimeOffset.UtcNow, merchantSummaries, recentFiles);
    }

    public async Task<string> RenderHtmlAsync(CancellationToken cancellationToken)
    {
        var report = await this.GetReportAsync(cancellationToken);
        var html = new StringBuilder();

        AppendDocumentStart(html, "Merchant File Status");
        html.AppendLine("  <h1>Merchant File Status</h1>");
        html.AppendLine($"  <p>Generated at {Encode(report.GeneratedUtc.ToString("u"))}. Auto-refreshes every 30 seconds.</p>");
        html.AppendLine("  <h2>Merchants</h2>");
        html.AppendLine("  <table>");
        html.AppendLine("    <thead><tr><th>Merchant</th><th>Enabled</th><th>Successful Files</th><th>Failed Files</th><th>Last Processed (UTC)</th><th>Next Scheduled Send (UTC)</th><th>Details</th></tr></thead>");
        html.AppendLine("    <tbody>");

        foreach (var merchant in report.MerchantSummaries)
        {
            html.AppendLine(
                $"      <tr><td>{Encode(merchant.MerchantName)}<br /><span class=\"mono\">{Encode(merchant.MerchantId)}</span></td><td>{(merchant.Enabled ? "Yes" : "No")}</td><td>{merchant.SuccessfulFilesSent}</td><td>{merchant.FailedFiles}</td><td>{Encode(merchant.LastProcessedUtc?.ToString("u") ?? "Never")}</td><td>{Encode(merchant.NextScheduledUtc?.ToString("u") ?? "Disabled")}</td><td><a href=\"/status/{Uri.EscapeDataString(merchant.MerchantId)}\">View details</a></td></tr>");
        }

        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
        AppendDocumentEnd(html);

        return html.ToString();
    }

    public async Task<string?> RenderMerchantHtmlAsync(string merchantId, CancellationToken cancellationToken)
    {
        var report = await this.GetMerchantReportAsync(merchantId, cancellationToken);
        if (report is null)
        {
            return null;
        }

        var html = new StringBuilder();
        AppendDocumentStart(html, $"Merchant {report.Merchant.MerchantName} Details");
        html.AppendLine("  <p><a href=\"/status\">&larr; Back to merchants</a></p>");
        html.AppendLine($"  <h1>{Encode(report.Merchant.MerchantName)}</h1>");
        html.AppendLine($"  <p class=\"mono\">{Encode(report.Merchant.MerchantId)}</p>");
        html.AppendLine($"  <p>Generated at {Encode(report.GeneratedUtc.ToString("u"))}. Auto-refreshes every 30 seconds.</p>");
        html.AppendLine("  <h2>Summary</h2>");
        html.AppendLine("  <table>");
        html.AppendLine("    <thead><tr><th>Enabled</th><th>Successful Files</th><th>Failed Files</th><th>Last Processed (UTC)</th><th>Next Scheduled Send (UTC)</th></tr></thead>");
        html.AppendLine("    <tbody>");
        html.AppendLine(
            $"      <tr><td>{(report.Merchant.Enabled ? "Yes" : "No")}</td><td>{report.Merchant.SuccessfulFilesSent}</td><td>{report.Merchant.FailedFiles}</td><td>{Encode(report.Merchant.LastProcessedUtc?.ToString("u") ?? "Never")}</td><td>{Encode(report.Merchant.NextScheduledUtc?.ToString("u") ?? "Disabled")}</td></tr>");
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");

        html.AppendLine("  <h2>Recent File Activity</h2>");
        html.AppendLine("  <table>");
        html.AppendLine("    <thead><tr><th>Processed (UTC)</th><th>Contract</th><th>Status</th><th>Profile</th><th>Details</th></tr></thead>");
        html.AppendLine("    <tbody>");

        foreach (var file in report.RecentFiles)
        {
            html.AppendLine(
                $"      <tr><td>{Encode(file.ProcessedUtc.ToString("u"))}</td><td>{Encode(file.ContractName)}<br /><span class=\"mono\">{Encode(file.ContractId)}</span></td><td class=\"status-{Encode(file.Status)}\">{Encode(RenderStatus(file))}</td><td>{Encode(file.FileProfileId)}</td><td><a href=\"/status/{Uri.EscapeDataString(report.Merchant.MerchantId)}/files/{file.Id}\">View file</a></td></tr>");
        }

        if (report.RecentFiles.Count == 0)
        {
            html.AppendLine("      <tr><td colspan=\"5\">No file activity recorded yet.</td></tr>");
        }

        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
        AppendDocumentEnd(html);

        return html.ToString();
    }

    public async Task<string?> RenderFileHtmlAsync(string merchantId, long fileId, CancellationToken cancellationToken)
    {
        var report = await this.GetFileReportAsync(merchantId, fileId, cancellationToken);
        if (report is null)
        {
            return null;
        }

        var html = new StringBuilder();
        AppendDocumentStart(html, $"Merchant {report.Merchant.MerchantName} File {report.File.Id}");
        html.AppendLine($"  <p><a href=\"/status/{Uri.EscapeDataString(report.Merchant.MerchantId)}\">&larr; Back to merchant</a></p>");
        html.AppendLine("  <h1>File Details</h1>");
        html.AppendLine($"  <p>Merchant {Encode(report.Merchant.MerchantName)}<br /><span class=\"mono\">{Encode(report.Merchant.MerchantId)}</span></p>");
        html.AppendLine("  <table>");
        html.AppendLine("    <thead><tr><th>Processed (UTC)</th><th>Contract</th><th>Upload Status</th><th>Processing</th><th>Profile</th><th>Format</th></tr></thead>");
        html.AppendLine("    <tbody>");
        html.AppendLine(
            $"      <tr><td>{Encode(report.File.ProcessedUtc.ToString("u"))}</td><td>{Encode(report.File.ContractName)}<br /><span class=\"mono\">{Encode(report.File.ContractId)}</span></td><td class=\"status-{Encode(report.File.Status)}\">{Encode(report.File.Status)}</td><td>{Encode(report.File.ProcessingCompleted ? "Complete" : "Pending")}{(report.File.LastStatusCheckUtc.HasValue ? $"<br /><span class=\"mono\">Last checked {Encode(report.File.LastStatusCheckUtc.Value.ToString("u"))}</span>" : string.Empty)}</td><td>{Encode(report.File.FileProfileId)}</td><td>{Encode(report.File.Format)}</td></tr>");
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");

        html.AppendLine("  <h2>File Data</h2>");
        html.AppendLine("  <table>");
        html.AppendLine("    <thead><tr><th>File Name</th><th>Records</th><th>Total Amount</th></tr></thead>");
        html.AppendLine("    <tbody>");
        html.AppendLine(
            $"      <tr><td>{Encode(string.IsNullOrWhiteSpace(report.File.FileName) ? "(not generated)" : report.File.FileName)}</td><td>{report.File.RecordCount}</td><td>{report.File.TotalAmount:0.00}</td></tr>");
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");

        html.AppendLine("  <h2>File Lines</h2>");
        if (report.FileLines.Count == 0)
        {
            html.AppendLine("  <p>No file content recorded.</p>");
        }
        else
        {
            html.AppendLine("  <table>");
            html.AppendLine("    <thead><tr><th>Line</th><th>Content</th><th>Status</th><th>Transaction</th><th>Rejection</th></tr></thead>");
            html.AppendLine("    <tbody>");

            foreach (var line in report.FileLines)
            {
                html.AppendLine($"      <tr><td>{line.LineNumber}</td><td class=\"mono\">{Encode(line.Content)}</td><td>{Encode(line.ProcessingStatus)}</td><td class=\"mono\">{Encode(line.TransactionId ?? string.Empty)}</td><td>{Encode(line.RejectionReason ?? string.Empty)}</td></tr>");
            }

            html.AppendLine("    </tbody>");
            html.AppendLine("  </table>");
        }

        html.AppendLine("  <h2>Error Details</h2>");
        html.AppendLine($"  <pre>{Encode(string.IsNullOrWhiteSpace(report.File.ErrorMessage) ? "No error recorded." : report.File.ErrorMessage)}</pre>");
        AppendDocumentEnd(html);

        return html.ToString();
    }

    private async Task<MerchantDetailReport?> GetMerchantReportAsync(string merchantId, CancellationToken cancellationToken)
    {
        var summary = (await this.GetReportAsync(cancellationToken))
            .MerchantSummaries
            .FirstOrDefault(merchant => merchant.MerchantId.Equals(merchantId, StringComparison.OrdinalIgnoreCase));

        if (summary is null)
        {
            return null;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var recentFiles = (await dbContext.FileSendRecords
            .Where(record => record.MerchantId == summary.MerchantId)
            .Select(record => new FileStatusRow(
                record.Id,
                record.ProcessedUtc,
                record.MerchantId,
                record.MerchantName ?? record.MerchantId,
                record.ContractId,
                record.ContractName ?? record.ContractId,
                record.Status,
                record.FileName ?? string.Empty,
                record.FileProfileId ?? string.Empty,
                record.Format ?? string.Empty,
                record.FileContent,
                record.RecordCount ?? 0,
                record.TotalAmount ?? 0m,
                record.ErrorMessage,
                record.ProcessingCompleted,
                record.LastStatusCheckUtc))
            .ToListAsync(cancellationToken))
            .OrderByDescending(record => record.ProcessedUtc)
            .Take(100)
            .ToList();

        return new MerchantDetailReport(DateTimeOffset.UtcNow, summary, recentFiles);
    }

    private async Task<FileDetailReport?> GetFileReportAsync(string merchantId, long fileId, CancellationToken cancellationToken)
    {
        var merchantReport = await this.GetMerchantReportAsync(merchantId, cancellationToken);
        if (merchantReport is null)
        {
            return null;
        }

        var file = merchantReport.RecentFiles.FirstOrDefault(row => row.Id == fileId);
        if (file is null)
        {
            return null;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var storedLineStatuses = await dbContext.FileSendRecordLineStatuses
            .Where(line => line.FileSendRecordId == file.Id)
            .OrderBy(line => line.LineNumber)
            .Select(line => new FileLineStatusRow(
                line.LineNumber,
                line.LineData ?? string.Empty,
                line.ProcessingStatus,
                line.RejectionReason,
                line.TransactionId))
            .ToListAsync(cancellationToken);

        var fileLines = storedLineStatuses.Count > 0
            ? storedLineStatuses
            : BuildFileLineRows(file.FileContent);

        return new FileDetailReport(DateTimeOffset.UtcNow, merchantReport.Merchant, file, fileLines);
    }

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

    private static void AppendDocumentStart(StringBuilder html, string title)
    {
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\" />");
        html.AppendLine("  <meta http-equiv=\"refresh\" content=\"30\" />");
        html.AppendLine($"  <title>{WebUtility.HtmlEncode(title)}</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    body { font-family: Arial, sans-serif; margin: 20px; color: #222; }");
        html.AppendLine("    h1, h2 { margin-bottom: 8px; }");
        html.AppendLine("    p { margin-top: 0; color: #555; }");
        html.AppendLine("    table { border-collapse: collapse; width: 100%; margin-bottom: 24px; }");
        html.AppendLine("    th, td { border: 1px solid #d0d7de; padding: 8px; text-align: left; vertical-align: top; }");
        html.AppendLine("    th { background: #f6f8fa; }");
        html.AppendLine("    .status-Succeeded { color: #1a7f37; font-weight: bold; }");
        html.AppendLine("    .status-Failed { color: #cf222e; font-weight: bold; }");
        html.AppendLine("    .mono { font-family: Consolas, monospace; }");
        html.AppendLine("    pre { background: #f6f8fa; border: 1px solid #d0d7de; padding: 12px; white-space: pre-wrap; }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
    }

    private static void AppendDocumentEnd(StringBuilder html)
    {
        html.AppendLine("</body>");
        html.AppendLine("</html>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static IReadOnlyList<FileLineStatusRow> BuildFileLineRows(string? fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return Array.Empty<FileLineStatusRow>();
        }

        return fileContent
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select((line, index) => new FileLineStatusRow(
                index + 1,
                line,
                FileLineStatuses.Unknown,
                null,
                null))
            .ToArray();
    }

    private static string RenderStatus(FileStatusRow file) =>
        file.Status == FileSendStatuses.Succeeded
            ? $"{file.Status} / {(file.ProcessingCompleted ? "Complete" : "Pending")}"
            : file.Status;
}
