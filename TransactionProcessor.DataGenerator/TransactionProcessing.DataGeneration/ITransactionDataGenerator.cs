namespace TransactionProcessing.DataGeneration;

using EstateManagement.DataTransferObjects.Responses.Merchant;
using EstateManagement.DataTransferObjects.Responses.Contract;

public interface ITransactionDataGenerator{
    #region Methods

    List<DateTime> GenerateDateRange(DateTime startDate, DateTime endDate);
    Task<List<ContractResponse>> GetMerchantContracts(MerchantResponse merchant, CancellationToken cancellationToken);
    Task<List<MerchantResponse>> GetMerchants(Guid estateId, CancellationToken cancellationToken);
    Task<Boolean> PerformMerchantLogon(DateTime dateTime, MerchantResponse merchant, CancellationToken cancellationToken);
    Task<Boolean> PerformSettlement(DateTime dateTime, Guid estateId, CancellationToken cancellationToken);
    Task<Boolean> SendSales(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, Int32 numberOfSales, CancellationToken cancellationToken);
    Task<Boolean> SendUploadFile(DateTime dateTime, ContractResponse contract, MerchantResponse merchant, Guid userId, CancellationToken cancellationToken);
    Task<MerchantResponse> GetMerchant(Guid estateId, Guid merchantId, CancellationToken cancellationToken);
    Task<Boolean> GenerateMerchantStatement(Guid estateId, Guid merchantId, DateTime statementDateTime, CancellationToken cancellationToken);

    event TraceHandler TraceGenerated;
    
    #endregion
}

public delegate void TraceHandler(TraceEventArgs traceArguments);

public class TraceEventArgs : EventArgs
{
    public enum  Level{
        Trace,
        Warning,
        Error
    }

    #region Properties
    
    public String Message { get; set; }

    public Level TraceLevel { get; set; }

    #endregion
}