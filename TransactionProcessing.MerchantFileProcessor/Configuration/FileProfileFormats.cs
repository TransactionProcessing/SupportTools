namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public static class FileProfileFormats
{
    public const string Delimited = "delimited";

    public const string Json = "json";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Delimited,
        Json
    };
}