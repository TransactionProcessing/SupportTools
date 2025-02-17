namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    public class Product
    {
        [JsonProperty("display_text")]
        public string DisplayText { get; set; }

        [JsonProperty("product_name")]
        public string ProductName { get; set; }

        [JsonProperty("value")]
        public decimal? Value { get; set; }

        [JsonProperty("transaction_fees")]
        public List<TransactionFee> TransactionFees { get; set; }

        [JsonProperty("product_type")]
        public ProductType ProductType { get; set; }
    }

    public enum ProductType
    {
        NotSet,
        MobileTopup,
        Voucher,
        BillPayment,
    }
}