namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class Operator
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("require_custom_merchant_number")]
        public bool RequireCustomMerchantNumber { get; set; }

        [JsonPropertyName("require_custom_terminal_number")]
        public bool RequireCustomTerminalNumber { get; set; }
    }
}