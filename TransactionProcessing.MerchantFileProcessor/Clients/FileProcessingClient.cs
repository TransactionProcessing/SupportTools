using FileProcessor.Client;
using FileProcessor.DataTransferObjects;
using FileProcessor.DataTransferObjects.Responses;
using SimpleResults;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.FileBuilding;
using TransactionProcessing.MerchantFileProcessor.Services;

namespace TransactionProcessing.MerchantFileProcessor.Clients;

public interface IFileProcessingClient
{
    Task<Result<Guid>> Upload(MerchantOptions merchant,
                              ContractOptions contract,
                              string accessToken,
                              GeneratedFile file,
                              CancellationToken cancellationToken);

    Task<Result<FileProcessingStatusSnapshot>> GetFileStatus(string accessToken,
                                                             Guid estateId,
                                                             Guid fileId,
                                                             CancellationToken cancellationToken);
}

public sealed class FileProcessingClient(IFileProcessorClient fileProcessorClient, MerchantProcessingOptions options) : IFileProcessingClient {
    public async Task<Result<Guid>> Upload(MerchantOptions merchant,
                                           ContractOptions contract,
                                           string accessToken,
                                           GeneratedFile file,
                                           CancellationToken cancellationToken) {
        FileProfileOptions? fileProfile = options.FileProfiles.FirstOrDefault(profile => profile.FileProfileId.Equals(file.FileProfileId, StringComparison.OrdinalIgnoreCase));

        if (fileProfile is null) {
            return new Result<Guid> { IsSuccess = false, Status = ResultStatus.Failure, Message = $"Generated file references unknown file profile '{file.FileProfileId}'." };
        }

        UploadFileRequest request = new UploadFileRequest {
            EstateId = merchant.GetEstateGuid(),
            MerchantId = merchant.GetMerchantGuid(),
            UserId = options.FileProcessing.GetUserGuid(),
            FileProfileId = fileProfile.GetFileProcessorFileProfileGuid(),
            UploadDateTime = DateTime.UtcNow
        };

        Result<Guid>? result = await fileProcessorClient.UploadFile(accessToken, file.FileName, file.Content, request, cancellationToken);

        if (result.IsFailed) {
            return new Result<Guid> { IsSuccess = false, Status = ResultStatus.Failure, Message = $"File processor client failed to upload file '{file.FileName}'." };
        }

        return Result.Success(result.Data);
    }

    public async Task<Result<FileProcessingStatusSnapshot>> GetFileStatus(string accessToken,
                                                                          Guid estateId,
                                                                          Guid fileId,
                                                                          CancellationToken cancellationToken) {
        Result<FileDetails>? result = await fileProcessorClient.GetFile(accessToken, estateId, fileId, cancellationToken);

        if (result.IsFailed || result.Data is null) {
            return new Result<FileProcessingStatusSnapshot> { IsSuccess = false, Status = ResultStatus.Failure, Message = $"File processor client failed to retrieve status for file '{fileId}'." };
        }

        FileDetails? fileDetails = result.Data;
        FileProcessingLineStatusSnapshot[] lineStatuses = fileDetails.FileLines?.OrderBy(line => line.LineNumber).Select(MapLineStatus).ToArray() ?? [];

        return Result.Success(new FileProcessingStatusSnapshot(fileDetails.ProcessingCompleted || AreAllLinesResolved(lineStatuses), lineStatuses));
    }

    private static FileProcessingLineStatusSnapshot MapLineStatus(FileLine line) => new(line.LineNumber, line.LineData, line.ProcessingResult.ToString(), string.IsNullOrWhiteSpace(line.RejectionReason) ? null : line.RejectionReason, line.TransactionId == Guid.Empty ? null : line.TransactionId);

    private static bool AreAllLinesResolved(IEnumerable<FileProcessingLineStatusSnapshot> lines) {
        bool hasLines = false;

        foreach (FileProcessingLineStatusSnapshot line in lines) {
            hasLines = true;

            if (line.ProcessingStatus.Equals(FileLineStatuses.Unknown, StringComparison.OrdinalIgnoreCase) || line.ProcessingStatus.Equals(FileLineStatuses.NotProcessed, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return hasLines;
    }
}
