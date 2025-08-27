    public record BaseConfiguration(
        String ClientId,
        String ClientSecret,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi,
        String EventStoreAddress);


    public class ReplayParkedQueueJobConfiguration {
        
    }

    public class MakeFloatCreditsJobConfiguration{

        public Guid EstateId { get; set; }
        public List<DepositAmount> DepositAmounts { get; set; } = new List<DepositAmount>();
    }


    public class DepositAmount{
        public Guid ContractId { get; set; }
        public Guid ProductId { get; set; }
        public Decimal Amount { get; set; }
    }

    public class UploadTransactionFileJobConfiguration
    {
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
        public Guid UserId { get; set; }
        public List<String> ContractsToInclude { get; set; }
}

    public class GenerateTransactionsJobConfiguration
    {
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
    }

    public class ProcessSettlementJobConfiguration
    {
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
    }

    public class MerchantStatementJobConfiguration
    {
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
    }
