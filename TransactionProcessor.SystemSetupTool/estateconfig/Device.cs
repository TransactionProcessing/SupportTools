namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class Device
    {
        [JsonPropertyName("device_identifier")]
        public string DeviceIdentifier { get; set; }
    }
}