
namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;

    public class Contract
    {
        public string OperatorName { get; set; }

        public string Description { get; set; }

        public List<Product> Products { get; set; }
    }
}