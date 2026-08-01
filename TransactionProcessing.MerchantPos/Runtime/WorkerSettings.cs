public class WorkerSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ServiceClientId { get; set; } = string.Empty;
    public string ServiceClientSecret { get; set; } = string.Empty;
    public List<MerchantConfig> Merchants { get; set; } = new();
}
