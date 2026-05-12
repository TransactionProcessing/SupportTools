namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System;
    public class Merchant
    {
        public Guid MerchantId { get; set; }

        public DateTime CreateDate { get; set; }

        public String Name { get; set; }

        public Address Address { get; set; }

        public Contact Contact { get; set; }

        public User User { get; set; }

        public Device Device { get; set; }

        public String SettlementSchedule { get; set; }
    }
}