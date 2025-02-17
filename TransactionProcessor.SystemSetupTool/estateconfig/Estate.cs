using Newtonsoft.Json;

namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    public class Estate
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("merchants")]
        public List<Merchant> Merchants { get; set; }

        [JsonProperty("operators")]
        public List<Operator> Operators { get; set; }

        [JsonProperty("contracts")]
        public List<Contract> Contracts { get; set; }
    }
}