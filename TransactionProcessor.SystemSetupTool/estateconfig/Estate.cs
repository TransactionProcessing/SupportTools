namespace TransactionProcessor.SystemSetupTool.estateconfig
{
    using System.Collections.Generic;

    public class Estate
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public User User { get; set; }

        public List<Merchant> Merchants { get; set; }

        public List<Operator> Operators { get; set; }

        public List<Contract> Contracts { get; set; }
    }
}