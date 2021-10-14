namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    public class EstateConfig
    {
        [JsonPropertyName("estates")]
        public List<Estate> Estates { get; set; }
    }
}