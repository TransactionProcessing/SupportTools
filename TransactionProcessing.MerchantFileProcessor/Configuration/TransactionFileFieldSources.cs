namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public static class TransactionFileFieldSources
{
    public const string MerchantId = "merchantid";
    public const string ContractId = "contractid";
    public const string ProductCode = "productcode";
    public const string Description = "description";
    public const string Quantity = "quantity";
    public const string UnitAmount = "unitamount";
    public const string TotalAmount = "totalamount";
    public const string Currency = "currency";
    public const string TransactionDateUtc = "transactiondateutc";
    public const string RecipientMobileNumber = "recipientmobilenumber";
    public const string ContractIssuer = "contractissuer";
    public const string ProcessingDateUtc = "processingdateutc";
    public const string RecordCount = "recordcount";
    public const string FileTotalAmount = "filetotalamount";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        MerchantId,
        ContractId,
        ProductCode,
        Description,
        Quantity,
        UnitAmount,
        TotalAmount,
        Currency,
        TransactionDateUtc,
        RecipientMobileNumber,
        ContractIssuer,
        ProcessingDateUtc,
        RecordCount,
        FileTotalAmount
    };
}