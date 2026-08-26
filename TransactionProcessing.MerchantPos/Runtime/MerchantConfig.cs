using TransactionProcessing.MerchantPos.Runtime;

public class MerchantConfig
{
    public bool Enabled { get; set; } = false; // new flag
    public Guid EstateId { get; set; }
    public Guid MerchantId { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DeviceIdentifier { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public int SaleIntervalSeconds { get; set; } = 30;

    public double FailureInjectionProbability { get; set; } = 0.02;

    public decimal DepositThreshold { get; set; } = 100;
    public decimal DepositAmount { get; set; } = 500;

    public TimeOnly ClosingTime { get; set; } = new(23, 50);
    public TimeOnly OpeningTime { get; set; } = new(8, 0);
    public List<Product> Products { get; set; } = [];
    public Boolean RequiresEndOfDay { get; set; } = true;
    public MerchantResponse Merchant { get; set; } = null!;
}
