using TransactionProcessorACL.DataTransferObjects.Responses;

namespace TransactionProcessing.MerchantPos.Runtime;

public class Product
{
    public string Name { get; set; }
    public Guid OperatorId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ContractId { get; set; }
    public decimal Value { get; set; }
    public ProductType ProductType { get; set; }
    public ProductSubType ProductSubType { get; set; }
}