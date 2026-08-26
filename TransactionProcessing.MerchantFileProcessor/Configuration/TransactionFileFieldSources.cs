namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public static class TransactionFileFieldSources
{
    public static string MerchantId { get; } = "merchantid";
    public static string ContractId { get; } = "contractid";
    public static string ProductCode { get; } = "productcode";
    public static string Description { get; } = "description";
    public static string Quantity { get; } = "quantity";
    public static string UnitAmount { get; } = "unitamount";
    public static string TotalAmount { get; } = "totalamount";
    public static string Currency { get; } = "currency";
    public static string TransactionDateUtc { get; } = "transactiondateutc";
    public static string RecipientMobileNumber { get; } = "recipientmobilenumber";
    public static string ContractIssuer { get; } = "contractissuer";
    public static string ProcessingDateUtc { get; } = "processingdateutc";
    public static string RecordCount { get; } = "recordcount";
    public static string FileTotalAmount { get; } = "filetotalamount";

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
