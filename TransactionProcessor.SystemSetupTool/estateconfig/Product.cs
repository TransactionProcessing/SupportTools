namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;

    public class Product
    {
        public string DisplayText { get; set; }

        public string ProductName { get; set; }

        public decimal? Value { get; set; }

        public List<TransactionFee> TransactionFees { get; set; }

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