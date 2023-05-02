namespace TransactionProcessing.DataGeneration;

using EstateManagement.DataTransferObjects.Responses;

public interface ITransactionDataGenerator{
    #region Methods

    List<DateTime> GenerateDateRange(DateTime startDate, DateTime endDate);
    Task<List<ContractResponse>> GetMerchantContracts(MerchantResponse merchant, CancellationToken cancellationToken);
    Task<List<MerchantResponse>> GetMerchants(Guid estateId, CancellationToken cancellationToken);
    Task PerformMerchantLogon(DateTime dateTime, MerchantResponse merchant, CancellationToken cancellationToken);
    Task PerformSettlement(DateTime dateTime, Guid estateId, CancellationToken cancellationToken);
    Task SendSales(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, CancellationToken cancellationToken);
    Task SendUploadFile(DateTime dateTime, ContractResponse contract, MerchantResponse merchant, CancellationToken cancellationToken);
    Task<MerchantResponse> GetMerchant(Guid estateId, Guid merchantId, CancellationToken cancellationToken);
    Task GenerateMerchantStatement(Guid estateId, Guid merchantId, DateTime statementDateTime, CancellationToken cancellationToken);

    #endregion
}