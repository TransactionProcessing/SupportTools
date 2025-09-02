using System;
using Microsoft.Extensions.Logging;
using SimpleResults;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessor.DataTransferObjects.Responses.Contract;
using TransactionProcessor.DataTransferObjects.Responses.Merchant;

namespace TransactionDataGenerator{
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using SecurityService.Client;
    using TransactionProcessor.Client;

    /// <summary>
    /// 
    /// </summary>
    class Program{
       
        private static SecurityServiceClient SecurityServiceClient;
        
        private static TransactionProcessorClient TransactionProcessorClient;
        
        private static Func<String, String> baseAddressFunc;
        
        static async Task Main(string[] args){


            HttpClientHandler handler = new HttpClientHandler{
                                                                 ServerCertificateCustomValidationCallback = (message,
                                                                                                              cert,
                                                                                                              chain,
                                                                                                              errors) => {
                                                                                                                 return true;
                                                                                                             }
                                                             };
            HttpClient httpClient = new HttpClient(handler);

            baseAddressFunc = (apiName) => {
                                  String ipaddress = "192.168.1.163";

                                  if (apiName == "SecurityService"){
                                      return $"https://{ipaddress}:5001";
                                  }

                                  if (apiName == "TransactionProcessorApi")
                                  {
                                      return $"http://{ipaddress}:5002";
                                      //return $"http://127.0.0.1:5002";
                                  }

                                  if (apiName == "FileProcessorApi"){
                                      return $"http://{ipaddress}:5009";
                                  }

                                  if (apiName == "TestHostApi"){
                                      return $"http://{ipaddress}:9000";
                                  }

                                  return null;
                              };
            Shared.Logger.Logger.Initialise(new ConsoleLogger());
            Program.SecurityServiceClient = new SecurityServiceClient(baseAddressFunc, httpClient);

            Program.TransactionProcessorClient = new TransactionProcessorClient(baseAddressFunc, httpClient);

            // Set an estate
            Guid estateId = Guid.Parse("435613ac-a468-47a3-ac4f-649d89764c22");
            
            // Get a token to talk to the estate service
            CancellationToken cancellationToken = new();
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";
            ITransactionDataGeneratorService g = new TransactionDataGeneratorService(Program.SecurityServiceClient,
                                                                       Program.TransactionProcessorClient,
                                                                       Program.baseAddressFunc("TransactionProcessorApi"),
                                                                       Program.baseAddressFunc("FileProcessorApi"),
                                                                       Program.baseAddressFunc("TestHostApi"),
                                                                       clientId,
                                                                       clientSecret,
                                                                       RunningMode.Live);

            g.TraceGenerated += arguments => {
                                    Console.WriteLine($"{arguments.TraceLevel}|{arguments.Message}");
                                };

            await Program.GenerateTransactions(g, estateId, cancellationToken);
            //await Program.GenerateStatements(g, estateId, cancellationToken);

            Console.WriteLine($"Process Complete");
        }

        private static async Task GenerateStatements(ITransactionDataGeneratorService g, Guid estateId, CancellationToken cancellationToken){
            Result<List<MerchantResponse>> getMerchantsResult = await g.GetMerchants(estateId, cancellationToken);
            if (getMerchantsResult.IsFailed)
                return;
            List<MerchantResponse>? merchants = getMerchantsResult.Data;
            foreach (MerchantResponse merchant in merchants){
                await g.GenerateMerchantStatement(merchant.EstateId, merchant.MerchantId, DateTime.Now.AddMonths(-2), cancellationToken);
            }
        }

        private static async Task GenerateTransactions(ITransactionDataGeneratorService g, Guid estateId, CancellationToken cancellationToken){
            // Set the date range
            DateTime startDate = new DateTime(2025, 8, 29); //27/7
            DateTime endDate = new DateTime(2025, 9,2); // This is the date of the last generated transaction

            Result<List<DateTime>> dateRangeResult = g.GenerateDateRange(startDate, endDate);
            if (dateRangeResult.IsFailed)
            {
                Console.WriteLine($"Failed to generate date range: {dateRangeResult.Message}");
                return;
            }
            var allContractsResult = await g.GetEstateContracts(estateId, cancellationToken);
            if (allContractsResult.IsFailed) {
                Console.WriteLine($"Failed to get estate contracts: {allContractsResult.Message}");
            }
            var merchantsResult = await g.GetMerchants(estateId, cancellationToken);
            if (merchantsResult.IsFailed)
            {
                Console.WriteLine($"Failed to get merchants: {merchantsResult.Message} ");
            }
            Dictionary<(String, String), Decimal> floatDeposits = new() {
                { ("Healthcare Centre 1 Contract", "10 KES Voucher"), 1400 },
                { ("Healthcare Centre 1 Contract", "Custom"), 27000 },
                { ("Safaricom Contract", "100 KES Topup"), 14000 },
                { ("Safaricom Contract", "200 KES Topup"), 28000 },
                { ("Safaricom Contract", "Custom"), 27000 },
                { ("PataPawa PostPay Contract", "Post Pay Bill Pay"), 18000 },
                { ("PataPawa prePay Contract", "Pre Pay Bill Pay"), 18000 }
            };

            DataToSend dataToSend = 0; 
            // Everything
            //dataToSend = DataToSend.FloatDeposits | DataToSend.Logons | DataToSend.Sales | DataToSend.Files | DataToSend.Settlement;
            
            // Everything (No settlement)
            dataToSend = DataToSend.FloatDeposits | DataToSend.Logons | DataToSend.Sales | DataToSend.Files;

            //  Floats
            //dataToSend = DataToSend.FloatDeposits;

            //  Logons and Sales
            //dataToSend = DataToSend.Logons | DataToSend.Sales;

            // Files
            //dataToSend = DataToSend.Files;

            // Settlement
            dataToSend = DataToSend.Settlement;

            if (dataToSend == 0) {
                Console.WriteLine("No data to send");
                return;
            }

            foreach (DateTime dateTime in dateRangeResult.Data){

                if ((dataToSend & DataToSend.FloatDeposits) == DataToSend.FloatDeposits)
                {
                    foreach (ContractResponse contractResponse in allContractsResult.Data) {
                        foreach (ContractProduct contractResponseProduct in contractResponse.Products) {
                            // Lookup the deposit amount here
                            KeyValuePair<(String, String), Decimal> depositAmount = floatDeposits.SingleOrDefault(f =>
                                f.Key.Item1 == contractResponse.Description &&
                                f.Key.Item2 == contractResponseProduct.Name);

                            await g.MakeFloatDeposit(dateTime, estateId, contractResponse.ContractId,
                                contractResponseProduct.ProductId, depositAmount.Value, cancellationToken);
                        }
                    }
                }

                if ((dataToSend & DataToSend.Logons) == DataToSend.Logons) {
                    foreach (MerchantResponse merchant in merchantsResult.Data) {

                        // Send a logon transaction
                        await g.PerformMerchantLogon(dateTime, merchant, cancellationToken);
                    }
                }

                if ((dataToSend & DataToSend.Sales) == DataToSend.Sales)
                {
                    foreach (MerchantResponse merchant in merchantsResult.Data) {
                        // Get the merchants contracts
                        Result<List<ContractResponse>> getMerchantContractsResult = await g.GetMerchantContracts(merchant, cancellationToken);
                        if (getMerchantContractsResult.IsFailed) {
                            Console.WriteLine($"Failed to get merchant contracts: {getMerchantContractsResult.Message}");
                            break;
                        }
                        foreach (ContractResponse contract in getMerchantContractsResult.Data) {
                            // Generate and send some sales

                            await g.SendSales(dateTime, merchant, contract, 0, cancellationToken);
                        }

                    }
                }

                if ((dataToSend & DataToSend.Files) == DataToSend.Files) {
                    foreach (MerchantResponse merchant in merchantsResult.Data) {
                        // Get the merchants contracts
                        Result<List<ContractResponse>> getMerchantContractsResult = await g.GetMerchantContracts(merchant, cancellationToken);
                        if (getMerchantContractsResult.IsFailed)
                        {
                            Console.WriteLine($"Failed to get merchant contracts: {getMerchantContractsResult.Message}");
                            break;
                        }
                        foreach (ContractResponse contract in getMerchantContractsResult.Data) {
                            // Generate a file and upload
                            await g.SendUploadFile(dateTime, contract, merchant, Guid.Parse("75e19f2e-2ce9-4296-930a-3bb4416375f4"), cancellationToken);

                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }

                // Settlement
                if ((dataToSend & DataToSend.Settlement) == DataToSend.Settlement) {
                    try {
                        await g.PerformSettlement(dateTime, estateId, cancellationToken);

                        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (Exception e) {
                        Console.WriteLine(e);
                    }
                }
            }
        }

        [Flags]
        enum DataToSend {
            FloatDeposits= 1,
            Logons = 2,
            Sales = 4,
            Files = 8,
            Settlement = 16
        }
    }

    public class ConsoleLogger : Shared.Logger.ILogger {
        public void LogCritical(Exception exception) {
            Console.WriteLine(exception);
        }

        public void LogCritical(String message,
                                Exception exception) {
            Console.WriteLine(message);
            Console.WriteLine(exception);
        }

        public void LogDebug(String message) {
            Console.WriteLine(message);
        }

        public void LogError(Exception exception) {
            Console.WriteLine(exception);
        }

        public void LogError(String message,
                             Exception exception) {
            Console.WriteLine(message);
            Console.WriteLine(exception);
        }

        public void LogInformation(String message) {
            Console.WriteLine(message);
        }

        public void LogTrace(String message) {
            Console.WriteLine(message);
        }

        public void LogWarning(String message) {
            Console.WriteLine(message);
        }

        public Boolean IsInitialised { get; set; }
    }
}