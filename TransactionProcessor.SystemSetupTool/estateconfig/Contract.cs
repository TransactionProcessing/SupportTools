namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    public class Contract
    {
        [JsonPropertyName("operator_name")]
        public string OperatorName { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("products")]
        public List<Product> Products { get; set; }
    }
}