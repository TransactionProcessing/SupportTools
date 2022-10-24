namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System;
    using System.Text.Json.Serialization;

    public class Merchant
    {
        [JsonPropertyName("merchant_id")]
        public Guid MerchantId { get; set; }

        [JsonPropertyName("createdate")]
        public DateTime CreateDate { get; set; }

        [JsonPropertyName("name")]
        public String Name { get; set; }

        [JsonPropertyName("address")]
        public Address Address { get; set; }

        [JsonPropertyName("contact")]
        public Contact Contact { get; set; }

        [JsonPropertyName("user")]
        public User User { get; set; }

        [JsonPropertyName("device")]
        public Device Device { get; set; }

        [JsonPropertyName("settlementschedule")]
        public String SettlementSchedule { get; set; }
    }
}