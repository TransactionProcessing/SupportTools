namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class User
    {
        [JsonPropertyName("email_address")]
        public string EmailAddress { get; set; }

        [JsonPropertyName("password")]
        public string Password { get; set; }

        [JsonPropertyName("given_name")]
        public string GivenName { get; set; }

        [JsonPropertyName("middle_name")]
        public string MiddleName { get; set; }

        [JsonPropertyName("family_name")]
        public string FamilyName { get; set; }
    }
}