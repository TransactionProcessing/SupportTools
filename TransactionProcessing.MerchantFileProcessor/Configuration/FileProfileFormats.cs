namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public static class FileProfileFormats
{
    public static string Delimited { get; } = "delimited";

    public static string Json { get; } = "json";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Delimited,
        Json
    };
}
