    public record ServiceConfiguration(
        String ClientId,
        String ClientSecret,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi,
        String EventStoreAddress);


    public class BaseConfiguration {
        public Boolean IsEnabled { get; set; } = true;
    }

    public class ReplayParkedQueueJobConfiguration : BaseConfiguration
    {
        
    }

    public class MakeFloatCreditsJobConfiguration : BaseConfiguration
    {

        public Guid EstateId { get; set; }
        public List<DepositAmount> DepositAmounts { get; set; } = new List<DepositAmount>();
    }


    public class DepositAmount : BaseConfiguration
    {
        public Guid ContractId { get; set; }
        public Guid ProductId { get; set; }
        public Decimal Amount { get; set; }
    }

    public class UploadTransactionFileJobConfiguration : BaseConfiguration
{
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
        public Guid UserId { get; set; }
        public List<String> ContractsToInclude { get; set; }
}

    public class GenerateTransactionsJobConfiguration : BaseConfiguration
{
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
    }

    public class ProcessSettlementJobConfiguration : BaseConfiguration
{
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
    }

    public class MerchantStatementJobConfiguration : BaseConfiguration
{
        public Guid EstateId { get; set; }
        public Guid MerchantId { get; set; }
    }
