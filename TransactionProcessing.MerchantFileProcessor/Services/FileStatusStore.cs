using System.Text;
using Microsoft.EntityFrameworkCore;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.FileBuilding;
using TransactionProcessing.MerchantFileProcessor.Persistence;

namespace TransactionProcessing.MerchantFileProcessor.Services;

public interface IFileStatusStore
{
    Task<bool> HasSuccessfulUploadAsync(
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset scheduledRunUtc,
        CancellationToken cancellationToken);

    Task<bool> IsMerchantRunCompleteAsync(
        MerchantOptions merchant,
        DateTimeOffset scheduledRunUtc,
        CancellationToken cancellationToken);

    Task RecordSuccessAsync(
        Guid runId,
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset scheduledRunUtc,
        Guid fileProcessorFileId,
        GeneratedFile file,
        CancellationToken cancellationToken);

    Task RecordFailureAsync(
        Guid runId,
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset scheduledRunUtc,
        GeneratedFile? file,
        string errorMessage,
        CancellationToken cancellationToken);

    Task RecordMerchantRunResultAsync(
        Guid runId,
        MerchantOptions merchant,
        DateTimeOffset scheduledRunUtc,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileStatusPollTarget>> GetPendingStatusPollTargetsAsync(CancellationToken cancellationToken);

    Task UpdateFileStatusAsync(
        long fileSendRecordId,
        FileProcessingStatusSnapshot snapshot,
        CancellationToken cancellationToken);
}

public static class FileSendStatuses
{
    public const string Succeeded = "Succeeded";

    public const string Failed = "Failed";
}

public static class MerchantRunStatuses
{
    public const string Succeeded = "Succeeded";

    public const string Failed = "Failed";
}

public static class FileLineStatuses
{
    public const string Unknown = "Unknown";

    public const string NotProcessed = "NotProcessed";

    public const string Successful = "Successful";

    public const string Failed = "Failed";

    public const string Ignored = "Ignored";

    public const string Rejected = "Rejected";
}

public sealed record FileStatusPollTarget(
    long FileSendRecordId,
    string MerchantId,
    string EstateId,
    Guid FileProcessorFileId,
    string FileName);

public sealed class FileStatusStore(
    IDbContextFactory<MerchantFileProcessorDbContext> dbContextFactory) : IFileStatusStore
{
    public async Task<bool> HasSuccessfulUploadAsync(
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset scheduledRunUtc,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.FileSendRecords.AnyAsync(record =>
            record.MerchantId == merchant.MerchantId &&
            record.ContractId == contract.ContractId &&
            record.ScheduledRunUtc == scheduledRunUtc &&
            record.Status == FileSendStatuses.Succeeded,
            cancellationToken);
    }

    public async Task<bool> IsMerchantRunCompleteAsync(
        MerchantOptions merchant,
        DateTimeOffset scheduledRunUtc,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var latestRunResult = await dbContext.MerchantRunRecords
            .Where(record =>
                record.MerchantId == merchant.MerchantId &&
                record.ScheduledRunUtc == scheduledRunUtc)
            .Select(record => new
            {
                record.Status,
                record.CompletedUtc
            })
            .ToListAsync(cancellationToken);

        return string.Equals(
            latestRunResult
                .OrderByDescending(record => record.CompletedUtc)
                .Select(record => record.Status)
                .FirstOrDefault(),
            MerchantRunStatuses.Succeeded,
            StringComparison.OrdinalIgnoreCase);
    }

    public Task RecordSuccessAsync(
        Guid runId,
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset scheduledRunUtc,
        Guid fileProcessorFileId,
        GeneratedFile file,
        CancellationToken cancellationToken) =>
        this.RecordAsync(runId, merchant, contract, scheduledRunUtc, file, FileSendStatuses.Succeeded, null, fileProcessorFileId, cancellationToken);

    public Task RecordFailureAsync(
        Guid runId,
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset scheduledRunUtc,
        GeneratedFile? file,
        string errorMessage,
        CancellationToken cancellationToken) =>
        this.RecordAsync(runId, merchant, contract, scheduledRunUtc, file, FileSendStatuses.Failed, errorMessage, null, cancellationToken);

    public async Task RecordMerchantRunResultAsync(
        Guid runId,
        MerchantOptions merchant,
        DateTimeOffset scheduledRunUtc,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        dbContext.MerchantRunRecords.Add(new MerchantRunRecord
        {
            RunId = runId,
            MerchantId = merchant.MerchantId,
            MerchantName = ResolveMerchantName(merchant),
            ScheduledRunUtc = scheduledRunUtc,
            Status = status,
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : Truncate(errorMessage, 2048),
            CompletedUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileStatusPollTarget>> GetPendingStatusPollTargetsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var candidates = await dbContext.FileSendRecords
            .Where(record =>
                record.Status == FileSendStatuses.Succeeded &&
                !record.ProcessingCompleted &&
                !string.IsNullOrWhiteSpace(record.EstateId) &&
                !string.IsNullOrWhiteSpace(record.FileProcessorFileId))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(candidate =>
                Guid.TryParse(candidate.FileProcessorFileId, out _) &&
                !string.IsNullOrWhiteSpace(candidate.EstateId))
            .OrderBy(candidate => candidate.ProcessedUtc)
            .Select(candidate => new FileStatusPollTarget(
                candidate.Id,
                candidate.MerchantId,
                candidate.EstateId!,
                Guid.Parse(candidate.FileProcessorFileId!),
                candidate.FileName ?? string.Empty))
            .ToArray();
    }

    public async Task UpdateFileStatusAsync(
        long fileSendRecordId,
        FileProcessingStatusSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var fileRecord = await dbContext.FileSendRecords
            .Include(record => record.LineStatuses)
            .FirstOrDefaultAsync(record => record.Id == fileSendRecordId, cancellationToken);

        if (fileRecord is null)
        {
            return;
        }

        var updateUtc = DateTimeOffset.UtcNow;
        var existingLineLookup = fileRecord.LineStatuses.ToDictionary(line => line.LineNumber);

        foreach (var line in snapshot.Lines)
        {
            if (!existingLineLookup.TryGetValue(line.LineNumber, out var entity))
            {
                entity = new FileSendRecordLineStatus
                {
                    FileSendRecordId = fileRecord.Id,
                    LineNumber = line.LineNumber
                };

                dbContext.FileSendRecordLineStatuses.Add(entity);
                existingLineLookup.Add(line.LineNumber, entity);
            }

            entity.LineData = string.IsNullOrWhiteSpace(line.LineData) ? entity.LineData : Truncate(line.LineData, 4096);
            entity.ProcessingStatus = ResolveLineStatus(line.ProcessingStatus);
            entity.RejectionReason = string.IsNullOrWhiteSpace(line.RejectionReason) ? null : Truncate(line.RejectionReason, 2048);
            entity.TransactionId = line.TransactionId?.ToString();
            entity.UpdatedUtc = updateUtc;
        }

        fileRecord.LastStatusCheckUtc = updateUtc;
        fileRecord.ProcessingCompleted = snapshot.ProcessingCompleted || AreAllLinesResolved(existingLineLookup.Values);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordAsync(
        Guid runId,
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset scheduledRunUtc,
        GeneratedFile? file,
        string status,
        string? errorMessage,
        Guid? fileProcessorFileId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var fileSendRecord = new FileSendRecord
        {
            RunId = runId,
            MerchantId = merchant.MerchantId,
            EstateId = merchant.EstateId,
            MerchantName = ResolveMerchantName(merchant),
            ContractId = contract.ContractId,
            ContractName = ResolveContractName(contract),
            FileName = file?.FileName,
            FileProfileId = file?.FileProfileId,
            Format = file?.Format,
            FileProcessorFileId = fileProcessorFileId?.ToString(),
            ScheduledRunUtc = scheduledRunUtc,
            FileContent = ResolveFileContent(file),
            RecordCount = file?.RecordCount,
            TotalAmount = file?.TotalAmount,
            Status = status,
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : Truncate(errorMessage, 2048),
            ProcessingCompleted = status == FileSendStatuses.Failed,
            ProcessedUtc = DateTimeOffset.UtcNow
        };

        dbContext.FileSendRecords.Add(fileSendRecord);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (file is null)
        {
            return;
        }

        var fileLines = GetFileLines(file);
        if (fileLines.Count == 0)
        {
            return;
        }

        if (status == FileSendStatuses.Succeeded)
        {
            dbContext.FileSendRecordLineStatuses.AddRange(
                fileLines.Select((line, index) => new FileSendRecordLineStatus
                {
                    FileSendRecordId = fileSendRecord.Id,
                    LineNumber = index + 1,
                    LineData = Truncate(line, 4096),
                    ProcessingStatus = FileLineStatuses.Unknown,
                    UpdatedUtc = fileSendRecord.ProcessedUtc
                }));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string ResolveMerchantName(MerchantOptions merchant) =>
        string.IsNullOrWhiteSpace(merchant.Name) ? merchant.MerchantId : Truncate(merchant.Name.Trim(), 256);

    private static string ResolveContractName(ContractOptions contract) =>
        string.IsNullOrWhiteSpace(contract.ContractName) ? contract.ContractId : Truncate(contract.ContractName.Trim(), 256);

    private static string? ResolveFileContent(GeneratedFile? file) =>
        file is null ? null : Encoding.UTF8.GetString(file.Content);

    private static IReadOnlyList<string> GetFileLines(GeneratedFile file) =>
        ResolveFileContent(file)?
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToArray() ?? [];

    private static string ResolveLineStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? FileLineStatuses.Unknown : Truncate(value.Trim(), 32);

    private static bool AreAllLinesResolved(IEnumerable<FileSendRecordLineStatus> lines)
    {
        var hasLines = false;

        foreach (var line in lines)
        {
            hasLines = true;

            if (line.ProcessingStatus.Equals(FileLineStatuses.Unknown, StringComparison.OrdinalIgnoreCase) ||
                line.ProcessingStatus.Equals(FileLineStatuses.NotProcessed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return hasLines;
    }
}
