namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using Newtonsoft.Json;
    using System.Text.Json.Serialization;

    public class Device
    {
        [JsonProperty("device_identifier")]
        public string DeviceIdentifier { get; set; }
    }
}