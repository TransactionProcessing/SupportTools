using System;

namespace TransactionProcessing.SettlementProcessor
{
    using EstateManagement.Client;
    using SecurityService.Client;
    using System.Threading.Tasks;
    using TransactionProcessing.DataGeneration;
    using TransactionProcessor.Client;

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

            startDate = new DateTime(2023,10,10);
            endDate = new DateTime(2023,10,10);

            String clientId = "";
            String clientSecret = "";


            SettlementProcessor processor = new SettlementProcessor();
            processor.LoadConfiguration();
            await processor.ProcessSettlement(estateId, startDate, endDate);
        }

        
    }
}
