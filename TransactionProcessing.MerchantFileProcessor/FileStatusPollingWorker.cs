using TransactionProcessing.MerchantFileProcessor.Clients;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.Services;

namespace TransactionProcessing.MerchantFileProcessor;

public sealed class FileStatusPollingWorker(
    MerchantProcessingOptions options,
    IAccessTokenProvider accessTokenProvider,
    IFileProcessingClient fileProcessingClient,
    IFileStatusStore fileStatusStore) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = LocalLogger.For();
        var pollInterval = options.FileStatusPolling.GetPollInterval();
        logger.LogInformation($"File status polling worker started with interval of {pollInterval.TotalSeconds:0} seconds");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.PollPendingFilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("File status polling iteration failed", ex);
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PollPendingFilesAsync(CancellationToken cancellationToken)
    {
        var pendingFiles = await fileStatusStore.GetPendingStatusPollTargetsAsync(cancellationToken);
        if (pendingFiles.Count == 0)
        {
            return;
        }

        var accessTokenResult = await accessTokenProvider.GetAccessToken(cancellationToken);
        if (accessTokenResult.IsFailed)
        {
            throw new InvalidOperationException(string.Join("; ", accessTokenResult.Errors));
        }

        var accessToken = accessTokenResult.Data.AccessToken;

        foreach (var pendingFile in pendingFiles)
        {
            try
            {
                var statusResult = await fileProcessingClient.GetFileStatus(
                    accessToken,
                    Guid.Parse(pendingFile.EstateId),
                    pendingFile.FileProcessorFileId,
                    cancellationToken);

                if (statusResult.IsFailed || statusResult.Data is null)
                {
                    LocalLogger.For(pendingFile.MerchantId)
                        .LogWarning($"Unable to retrieve status for file {pendingFile.FileProcessorFileId} ({pendingFile.FileName}).");
                    continue;
                }

                await fileStatusStore.UpdateFileStatusAsync(
                    pendingFile.FileSendRecordId,
                    statusResult.Data,
                    cancellationToken);

                if (statusResult.Data.ProcessingCompleted)
                {
                    LocalLogger.For(pendingFile.MerchantId)
                        .LogInformation($"Completed line status tracking for file {pendingFile.FileProcessorFileId} ({pendingFile.FileName}).");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LocalLogger.For(pendingFile.MerchantId)
                    .LogError($"File status polling failed for file {pendingFile.FileProcessorFileId} ({pendingFile.FileName})", ex);
            }
        }
    }
}
