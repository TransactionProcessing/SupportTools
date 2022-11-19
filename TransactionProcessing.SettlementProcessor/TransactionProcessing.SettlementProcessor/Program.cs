using System;

namespace TransactionProcessing.SettlementProcessor
{
    using System.Threading.Tasks;

    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Set an estate
            Guid estateId = Guid.Parse(args[0]);
            DateTime? startDate = null;
            DateTime? endDate = null;
            if (args.Length == 3)
            {
                // We have been provided a date range
                startDate = DateTime.ParseExact(args[1], "yyyy-MM-dd", null);
                endDate = DateTime.ParseExact(args[2], "yyyy-MM-dd", null);
            }

            startDate = new DateTime(2022,11,3);
            endDate = new DateTime(2022,11,7);

            SettlementProcessor processor = new SettlementProcessor();
            processor.LoadConfiguration();
            await processor.ProcessSettlement(estateId, startDate, endDate);
        }

        
    }
}
