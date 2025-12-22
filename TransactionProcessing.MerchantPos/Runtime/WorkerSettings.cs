public class WorkerSettings
{
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string ServiceClientId { get; set; }
    public string ServiceClientSecret { get; set; }
    public List<MerchantConfig> Merchants { get; set; } = new();
}