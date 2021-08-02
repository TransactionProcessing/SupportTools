namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    public class Estate
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("user")]
        public User User { get; set; }

        [JsonPropertyName("merchants")]
        public List<Merchant> Merchants { get; set; }

        [JsonPropertyName("operators")]
        public List<Operator> Operators { get; set; }

        [JsonPropertyName("contracts")]
        public List<Contract> Contracts { get; set; }
    }
}