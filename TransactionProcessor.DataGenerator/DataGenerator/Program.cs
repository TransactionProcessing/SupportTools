using System;

namespace TransactionDataGenerator{
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Responses.Contract;
    using EstateManagement.DataTransferObjects.Responses.Merchant;
    using SecurityService.Client;
    using TransactionProcessing.DataGeneration;
    using TransactionProcessor.Client;

    /// <summary>
    /// 
    /// </summary>
    class Program{
        private static EstateClient EstateClient;
        
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
                                  String ipaddress = "192.168.1.167";
                                  if (apiName == "EstateManagementApi"){
                                      return $"http://{ipaddress}:5000";
                                  }

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

            Program.SecurityServiceClient = new SecurityServiceClient(baseAddressFunc, httpClient);

            Program.EstateClient = new EstateClient(baseAddressFunc, httpClient);

            Program.TransactionProcessorClient = new TransactionProcessorClient(baseAddressFunc, httpClient);

            // Set an estate
            Guid estateId = Guid.Parse("435613ac-a468-47a3-ac4f-649d89764c22");
            
            // Get a token to talk to the estate service
            CancellationToken cancellationToken = new CancellationToken();
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";
            ITransactionDataGenerator g = new TransactionDataGenerator(Program.SecurityServiceClient,
                                                                       Program.EstateClient,
                                                                       Program.TransactionProcessorClient,
                                                                       Program.baseAddressFunc("EstateManagementApi"),
                                                                       Program.baseAddressFunc("FileProcessorApi"),
                                                                       Program.baseAddressFunc("TestHostApi"),
                                                                       clientId,
                                                                       clientSecret,
                                                                       RunningMode.WhatIf);

            g.TraceGenerated += arguments => {
                                    Console.WriteLine($"{arguments.TraceLevel}|{arguments.Message}");
                                };

            await Program.GenerateTransactions(g, estateId, cancellationToken);
            //await Program.GenerateStatements(g, estateId, cancellationToken);

            Console.WriteLine($"Process Complete");
        }

        private static async Task GenerateStatements(ITransactionDataGenerator g, Guid estateId, CancellationToken cancellationToken){
            List<MerchantResponse> merchants = await g.GetMerchants(estateId, cancellationToken);
            foreach (MerchantResponse merchant in merchants){
                await g.GenerateMerchantStatement(merchant.EstateId, merchant.MerchantId, DateTime.Now.AddMonths(-2), cancellationToken);
            }
        }

        private static async Task GenerateTransactions(ITransactionDataGenerator g, Guid estateId, CancellationToken cancellationToken){
            // Set the date range
            DateTime startDate = new DateTime(2024, 7, 1); //27/7
            DateTime endDate = new DateTime(2024, 7,26); // This is the date of the last generated transaction

            List<DateTime> dateRange = g.GenerateDateRange(startDate, endDate);
            List<ContractResponse> allContracts = await g.GetEstateContracts(estateId, cancellationToken);
            List<MerchantResponse> merchants = await g.GetMerchants(estateId, cancellationToken);

            Dictionary<(String, String), Decimal> floatDeposits = new Dictionary<(String, String), Decimal>();
            floatDeposits.Add(("Healthcare Centre 1 Contract", "10 KES Voucher"), 1400);
            floatDeposits.Add(("Healthcare Centre 1 Contract", "Custom"), 27000);

            floatDeposits.Add(("Safaricom Contract", "100 KES Topup"), 14000);
            floatDeposits.Add(("Safaricom Contract", "200 KES Topup"), 28000);
            floatDeposits.Add(("Safaricom Contract", "Custom"), 27000);

            floatDeposits.Add(("PataPawa PostPay Contract", "Post Pay Bill Pay"), 18000);
            floatDeposits.Add(("PataPawa prePay Contract", "Pre Pay Bill Pay"), 18000);


            // Everything
            //DataToSend dataToSend = DataToSend.FloatDeposits | DataToSend.Logons | DataToSend.Sales | DataToSend.Files |
            //                        DataToSend.Settlement;
            //  Floats
            DataToSend dataToSend = DataToSend.FloatDeposits;

            //  Logons and Sales
            //DataToSend dataToSend = DataToSend.Logons | DataToSend.Sales;

            // Files
            //DataToSend dataToSend = DataToSend.Files;

            // Settlement
            //DataToSend dataToSend = DataToSend.Settlement;

            foreach (DateTime dateTime in dateRange){

                if ((dataToSend & DataToSend.FloatDeposits) == DataToSend.FloatDeposits)
                {
                    foreach (ContractResponse contractResponse in allContracts) {
                        foreach (ContractProduct contractResponseProduct in contractResponse.Products) {
                            // Lookup the deposit amount here
                            var depositAmount = floatDeposits.SingleOrDefault(f =>
                                f.Key.Item1 == contractResponse.Description &&
                                f.Key.Item2 == contractResponseProduct.Name);

                            await g.MakeFloatDeposit(dateTime, estateId, contractResponse.ContractId,
                                contractResponseProduct.ProductId, depositAmount.Value, cancellationToken);
                        }
                    }
                }

                if ((dataToSend & DataToSend.Logons) == DataToSend.Logons) {
                    foreach (MerchantResponse merchant in merchants) {

                        // Send a logon transaction
                        await g.PerformMerchantLogon(dateTime, merchant, cancellationToken);
                    }
                }

                if ((dataToSend & DataToSend.Sales) == DataToSend.Sales)
                {
                    foreach (MerchantResponse merchant in merchants) {
                        // Get the merchants contracts
                        List<ContractResponse> contracts = await g.GetMerchantContracts(merchant, cancellationToken);
                        foreach (ContractResponse contract in contracts) {
                            // Generate and send some sales

                            await g.SendSales(dateTime, merchant, contract, 0, cancellationToken);
                        }

                    }
                }

                if ((dataToSend & DataToSend.Files) == DataToSend.Files) {
                    foreach (MerchantResponse merchant in merchants) {
                        // Get the merchants contracts
                        List<ContractResponse> contracts = await g.GetMerchantContracts(merchant, cancellationToken);

                        foreach (ContractResponse contract in contracts) {
                            // Generate a file and upload
                            await g.SendUploadFile(dateTime, contract, merchant, Guid.Empty, cancellationToken);

                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }

                // Settlement
                if ((dataToSend & DataToSend.Settlement) == DataToSend.Settlement) {
                    await g.PerformSettlement(dateTime, estateId, cancellationToken);

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
        }

        [Flags]
        enum DataToSend {
            FloatDeposits,
            Logons,
            Sales,
            Files,
            Settlement
        }
    }
}