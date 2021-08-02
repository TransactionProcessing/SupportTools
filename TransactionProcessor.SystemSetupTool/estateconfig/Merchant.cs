namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class Merchant
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("address")]
        public Address Address { get; set; }

        [JsonPropertyName("contact")]
        public Contact Contact { get; set; }

        [JsonPropertyName("user")]
        public User User { get; set; }

        [JsonPropertyName("device")]
        public Device Device { get; set; }
    }
}