namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class Contact
    {
        [JsonPropertyName("contact_name")]
        public string ContactName { get; set; }

        [JsonPropertyName("email_address")]
        public string EmailAddress { get; set; }
    }
}