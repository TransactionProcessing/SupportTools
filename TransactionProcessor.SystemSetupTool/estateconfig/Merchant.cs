using Newtonsoft.Json;

namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System;
    using System.Text.Json.Serialization;

    public class Merchant
    {
        [JsonProperty("merchant_id")]
        public Guid MerchantId { get; set; }

        [JsonProperty("createdate")]
        public DateTime CreateDate { get; set; }

        [JsonProperty("name")]
        public String Name { get; set; }

        [JsonProperty("address")]
        public Address Address { get; set; }

        [JsonProperty("contact")]
        public Contact Contact { get; set; }

        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("device")]
        public Device Device { get; set; }

        [JsonProperty("settlementschedule")]
        public String SettlementSchedule { get; set; }
    }
}