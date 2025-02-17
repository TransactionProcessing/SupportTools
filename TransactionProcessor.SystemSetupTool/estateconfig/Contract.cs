using Newtonsoft.Json;

namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    public class Contract
    {
        [JsonProperty("operator_name")]
        public string OperatorName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("products")]
        public List<Product> Products { get; set; }
    }
}