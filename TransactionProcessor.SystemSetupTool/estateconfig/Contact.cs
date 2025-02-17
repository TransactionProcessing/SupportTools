using Newtonsoft.Json;

namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class Contact
    {
        [JsonProperty("contact_name")]
        public string ContactName { get; set; }

        [JsonProperty("email_address")]
        public string EmailAddress { get; set; }
    }
}