using System;

namespace TransactionDataGenerator{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Responses;
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
                                  String ipaddress = "192.168.0.133";
                                  if (apiName == "EstateManagementApi"){
                                      return $"http://{ipaddress}:5000";
                                  }

                                  if (apiName == "SecurityService"){
                                      return $"https://{ipaddress}:5001";
                                  }

                                  if (apiName == "TransactionProcessorApi"){
                                      return $"http://{ipaddress}:5002";
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
            
            // Set the date range
            DateTime startDate = new DateTime(2023, 4, 27); //27/7
            DateTime endDate = new DateTime(2023, 4, 28); // This is the date of the last generated transaction
            
            // Get a token to talk to the estate service
            CancellationToken cancellationToken = new CancellationToken();
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";
            ITransactionDataGenerator g = new TransactionDataGenerator(Program.SecurityServiceClient,
                                                                       Program.EstateClient,
                                                                       Program.TransactionProcessorClient,
                                                                       Program.baseAddressFunc("FileProcessorApi"),
                                                                       Program.baseAddressFunc("TestHostApi"),
                                                                       clientId,
                                                                       clientSecret,
                                                                       RunningMode.Live);

            List<DateTime> dateRange = g.GenerateDateRange(startDate, endDate);

            List<MerchantResponse> merchants = await g.GetMerchants(estateId, cancellationToken);

            foreach (DateTime dateTime in dateRange){
                foreach (MerchantResponse merchant in merchants){
                    // Send a logon transaction
                    await g.PerformMerchantLogon(dateTime, merchant, cancellationToken);

                    // Get the merchants contracts
                    List<ContractResponse> contracts = await g.GetMerchantContracts(merchant, cancellationToken);

                    foreach (ContractResponse contract in contracts){
                        // Generate and send some sales
                        await g.SendSales(dateTime, merchant, contract, cancellationToken);

                        // Generate a file and upload
                        await g.SendUploadFile(dateTime, contract, merchant, cancellationToken);
                    }
                }

                // Settlement
                await g.PerformSettlement(dateTime, estateId, cancellationToken);
            }

            Console.WriteLine($"Process Complete");
        }
    }
}