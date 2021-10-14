namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class TransactionFee
    {
        [JsonPropertyName("calculation_type")]
        public int CalculationType { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("value")]
        public decimal Value { get; set; }

        [JsonPropertyName("fee_type")]
        public int FeeType { get; set; }
    }
}