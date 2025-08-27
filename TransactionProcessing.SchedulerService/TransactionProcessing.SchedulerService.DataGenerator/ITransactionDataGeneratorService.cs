using SimpleResults;
using System.Text.Json;
using TransactionProcessor.DataTransferObjects;
using TransactionProcessor.DataTransferObjects.Responses.Contract;
using TransactionProcessor.DataTransferObjects.Responses.Merchant;

namespace TransactionProcessing.SchedulerService.DataGenerator;

public interface ITransactionDataGeneratorService
{
    #region Methods

    Result<List<DateTime>> GenerateDateRange(DateTime startDate, DateTime endDate);

    Task<Result<List<ContractResponse>>> GetEstateContracts(Guid estateId, CancellationToken cancellationToken);
    Task<Result<List<ContractResponse>>> GetMerchantContracts(MerchantResponse merchant, CancellationToken cancellationToken);
    Task<Result<List<MerchantResponse>>> GetMerchants(Guid estateId, CancellationToken cancellationToken);
    Task<Result<SerialisedMessage>> PerformMerchantLogon(DateTime dateTime, MerchantResponse merchant, CancellationToken cancellationToken);
    Task<Result> PerformSettlement(DateTime dateTime, Guid estateId, CancellationToken cancellationToken);

    Task<Result> PerformMerchantSettlement(DateTime dateTime,
                                                Guid estateId,
                                                Guid merchantId,
                                                CancellationToken cancellationToken);
    Task<Result> SendSales(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, Int32 numberOfSales, CancellationToken cancellationToken);
    Task<Result> SendUploadFile(DateTime dateTime, ContractResponse contract, MerchantResponse merchant, Guid userId, CancellationToken cancellationToken);
    Task<Result<MerchantResponse>> GetMerchant(Guid estateId, Guid merchantId, CancellationToken cancellationToken);
    Task<Result> GenerateMerchantStatement(Guid estateId, Guid merchantId, DateTime statementDateTime, CancellationToken cancellationToken);
    Task<Result> MakeFloatDeposit(DateTime dateTime, Guid estateId, Guid contractId, Guid contractProductId, Decimal amount, CancellationToken cancellationToken);

    event TraceHandler TraceGenerated;

    #endregion
}