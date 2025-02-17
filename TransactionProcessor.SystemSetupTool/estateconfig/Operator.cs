namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using Newtonsoft.Json;
    using System.Text.Json.Serialization;

    public class Operator
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("require_custom_merchant_number")]
        public bool RequireCustomMerchantNumber { get; set; }

        [JsonProperty("require_custom_terminal_number")]
        public bool RequireCustomTerminalNumber { get; set; }
    }
}