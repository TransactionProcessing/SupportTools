namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Text.Json.Serialization;

    public class Address
    {
        public string AddressLine1 { get; set; }

        public string Country { get; set; }

        public string Region { get; set; }

        public string Town { get; set; }
    }
}