using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MerchantPos.EF.Models
{
    public class Merchant
    {
        [Key]
        public Guid MerchantId { get; set; }
        public String MerchantName { get; set; }
        public Decimal Balance { get; set; }
        public DateTime LastEndOfDayDateTime { get; set; }
        public DateTime LastLogonDateTime { get; set; }
        public Int32 TransactionNumber { get; set; }
    }

    public class OperatorTotal
    {
        [Key]
        public int Id { get; set; }
        public Guid MerchantId { get; set; }
        public Guid OperatorId { get; set; }
        public Guid ContractId { get; set; }
        public Decimal Total { get; set; }
        public Int32 TotalCount { get; set; }
    }
}
