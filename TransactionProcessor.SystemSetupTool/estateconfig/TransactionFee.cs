namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using Newtonsoft.Json;
    using System.Text.Json.Serialization;

    public class TransactionFee
    {
        [JsonProperty("calculation_type")]
        public int CalculationType { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("value")]
        public decimal Value { get; set; }

        [JsonProperty("fee_type")]
        public int FeeType { get; set; }
    }
}