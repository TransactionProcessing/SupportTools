namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    public class Product
    {
        [JsonPropertyName("display_text")]
        public string DisplayText { get; set; }

        [JsonPropertyName("product_name")]
        public string ProductName { get; set; }

        [JsonPropertyName("value")]
        public decimal? Value { get; set; }

        [JsonPropertyName("transaction_fees")]
        public List<TransactionFee> TransactionFees { get; set; }
    }
}