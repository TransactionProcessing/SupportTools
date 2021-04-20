using System;

namespace TransactionDataGenerator
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using Newtonsoft.Json;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using TransactionProcessor.Client;
    using TransactionProcessor.DataTransferObjects;

    public class Gimp
    {
        public class V1
        {
            public class GimpAddedEvent
            {

            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    class Program
    {
        /// <summary>
        /// The estate client
        /// </summary>
        private static EstateManagement.Client.EstateClient EstateClient;

        /// <summary>
        /// The security service client
        /// </summary>
        private static SecurityService.Client.SecurityServiceClient SecurityServiceClient;

        /// <summary>
        /// The transaction processor client
        /// </summary>
        private static TransactionProcessor.Client.TransactionProcessorClient TransactionProcessorClient;

        /// <summary>
        /// The token response
        /// </summary>
        private static TokenResponse TokenResponse;

        /// <summary>
        /// The transaction count
        /// </summary>
        private static Int32 TransactionCount = 0;
        /// <summary>
        /// Defines the entry point of the application.
        /// </summary>
        /// <param name="args">The arguments.</param>
        static async Task Main(string[] args)
        {
            HttpClient httpClient = new HttpClient();

            Func<String, String> baseAddressFunc = (apiName) =>
                                                   {
                                                       if (apiName == "EstateManagementApi")
                                                       {
                                                           return "http://192.168.1.133:5000";
                                                       }

                                                       if (apiName == "SecurityService")
                                                       {
                                                           return "http://192.168.1.133:5001";
                                                       }

                                                       if (apiName == "TransactionProcessorApi")
                                                       {
                                                           return "http://192.168.1.133:5002";
                                                       }

                                                       return null;
                                                   };

            Program.SecurityServiceClient = new SecurityServiceClient(baseAddressFunc, httpClient);

            Program.EstateClient = new EstateClient(baseAddressFunc, httpClient);

            Program.TransactionProcessorClient = new TransactionProcessorClient(baseAddressFunc, httpClient);

            // Set an estate
            Guid estateId = Guid.Parse("5b07ec2d-3856-4d05-ab31-e2dedb95ea27");

            // Get a token
            await Program.GetToken(CancellationToken.None);

            // Get the the merchant list for the estate
            List<MerchantResponse> merchants = await Program.EstateClient.GetMerchants(Program.TokenResponse.AccessToken, estateId, CancellationToken.None);
            
            // Set the date range
            DateTime startDate = new DateTime(2021,4,1);
            DateTime endDate = new DateTime(2021, 4, 19);  // This is the date of te last generated transaction
            List<DateTime> dateRange = Program.GenerateDateRange(startDate, endDate);

            // Only use merchants that have a device
            merchants = merchants.Where(m => m.Devices != null && m.Devices.Any()).ToList();

            foreach (DateTime dateTime in dateRange)
            {
                await Program.GenerateTransactions(merchants, dateTime, CancellationToken.None);
            }
            
            Console.WriteLine($"Process Complete");
        }

        /// <summary>
        /// Gets the token.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        private static async Task GetToken(CancellationToken cancellationToken)
        {
            // Get a token to talk to the estate service
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";

            if (Program.TokenResponse == null)
            {
                TokenResponse token = await Program.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);
                Program.TokenResponse = token;
            }

            if (Program.TokenResponse.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2))
            {
                TokenResponse token = await Program.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);
                Program.TokenResponse = token;
            }
        }

        /// <summary>
        /// Generates the transactions.
        /// </summary>
        /// <param name="merchants">The merchants.</param>
        /// <param name="dateRange">The date range.</param>
        private static async Task GenerateTransactions(List<MerchantResponse> merchants,
                                                       DateTime dateTime,
                                                       CancellationToken cancellationToken)
        {
            Int32 maxDegreeOfParallelism = 5;
            Int32 boundedCapacityForActionBlock = merchants.Count;

            ActionBlock<(MerchantResponse merchant, CancellationToken cancellationToken)> workerBlock =
                new ActionBlock<(MerchantResponse merchant, CancellationToken cancellationToken)>(async (message) =>
                                                                                                  {
                                                                                                      try
                                                                                                      {
                                                                                                          Int32 transactionCount = 0;

                                                                                                          // Do a logon transaction for each merchant
                                                                                                          await Program.DoLogonTransaction(message.merchant, dateTime);
                                                                                                          Console
                                                                                                              .WriteLine($"Logon sent for Merchant [{message.merchant.MerchantName}]");

                                                                                                          // Now generate some sales
                                                                                                          List<SaleTransactionRequest> saleRequests =
                                                                                                              await Program.CreateSaleRequests(message.merchant,
                                                                                                                  dateTime);

                                                                                                          // Work out how much of a deposit the merchant needs (minus 1 sale)
                                                                                                          IEnumerable<Dictionary<String, String>> metadata =
                                                                                                              saleRequests.Select(s => s.AdditionalTransactionMetadata);
                                                                                                          List<String> amounts = metadata.Select(m => m["Amount"])
                                                                                                              .ToList();

                                                                                                          Decimal depositAmount = amounts.TakeLast(amounts.Count - 1)
                                                                                                              .Sum(a => Decimal.Parse(a));

                                                                                                          await Program.MakeMerchantDeposit(message.merchant,
                                                                                                              depositAmount,
                                                                                                              dateTime);

                                                                                                          // Now send the sales
                                                                                                          saleRequests = saleRequests.OrderBy(s => s.TransactionDateTime)
                                                                                                              .ToList();
                                                                                                          foreach (SaleTransactionRequest saleTransactionRequest in
                                                                                                              saleRequests)
                                                                                                          {
                                                                                                              await Program.DoSaleTransaction(saleTransactionRequest);
                                                                                                              Console
                                                                                                                  .WriteLine($"Sale sent for Merchant [{message.merchant.MerchantName}]");
                                                                                                              transactionCount++;
                                                                                                          }

                                                                                                          Console.ForegroundColor = ConsoleColor.Green;
                                                                                                          Console
                                                                                                              .WriteLine($"{transactionCount} transactions generated for {message.merchant.MerchantName} on date {dateTime.ToLongDateString()}");
                                                                                                      }
                                                                                                      catch(Exception ex)
                                                                                                      {
                                                                                                          Console.ForegroundColor = ConsoleColor.Red;
                                                                                                          Console.WriteLine("Failed");
                                                                                                          Console.WriteLine(ex);
                                                                                                      }
                                                                                                  },
                                                                                                  new ExecutionDataflowBlockOptions
                                                                                                  {
                                                                                                      MaxDegreeOfParallelism = maxDegreeOfParallelism,
                                                                                                      BoundedCapacity = boundedCapacityForActionBlock
                                                                                                  });


            try
            {
                foreach (var merchant in merchants)
                {
                    await workerBlock.SendAsync((merchant, cancellationToken), cancellationToken);
                }

                workerBlock.Complete();

                await workerBlock.Completion;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Makes the merchant deposit.
        /// </summary>
        /// <param name="merchant">The merchant.</param>
        /// <param name="depositAmount">The deposit amount.</param>
        /// <param name="dateTime">The date time.</param>
        private static async Task MakeMerchantDeposit(MerchantResponse merchant,
                                                      Decimal depositAmount,
                                                      DateTime dateTime)
        {
            await Program.GetToken(CancellationToken.None);

            await Program.EstateClient.MakeMerchantDeposit(Program.TokenResponse.AccessToken,
                                                           merchant.EstateId,
                                                           merchant.MerchantId,
                                                           new MakeMerchantDepositRequest
                                                           {
                                                               Amount = depositAmount,
                                                               DepositDateTime = dateTime.AddSeconds(55),
                                                               Reference = "Test Data Gen Deposit",
                                                               Source = MerchantDepositSource.Manual
                                                           },
                                                           CancellationToken.None);
            Console.WriteLine($"Deposit made for Merchant [{merchant.MerchantName}]");
        }

        /// <summary>
        /// Does the sale transaction.
        /// </summary>
        /// <param name="saleTransactionRequest">The sale transaction request.</param>
        private static async Task DoSaleTransaction(SaleTransactionRequest saleTransactionRequest)
        {
            try
            {
                await Program.GetToken(CancellationToken.None);

                SerialisedMessage requestSerialisedMessage = new SerialisedMessage();
                requestSerialisedMessage.Metadata.Add("estate_id", saleTransactionRequest.EstateId.ToString());
                requestSerialisedMessage.Metadata.Add("merchant_id", saleTransactionRequest.MerchantId.ToString());
                requestSerialisedMessage.SerialisedData = JsonConvert.SerializeObject(saleTransactionRequest,
                                                                                      new JsonSerializerSettings
                                                                                      {
                                                                                          TypeNameHandling = TypeNameHandling.All
                                                                                      });

                SerialisedMessage responseSerialisedMessage =
                    await Program.TransactionProcessorClient.PerformTransaction(Program.TokenResponse.AccessToken, requestSerialisedMessage, CancellationToken.None);

                SaleTransactionResponse saleTransactionResponse = JsonConvert.DeserializeObject<SaleTransactionResponse>(responseSerialisedMessage.SerialisedData);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Creates the sale requests.
        /// </summary>
        /// <param name="merchant">The merchant.</param>
        /// <param name="dateTime">The date time.</param>
        /// <returns></returns>
        private static async Task<List<SaleTransactionRequest>> CreateSaleRequests(MerchantResponse merchant,
                                                                                   DateTime dateTime)
        {
            List<ContractResponse> contracts =
                await Program.EstateClient.GetMerchantContracts(Program.TokenResponse.AccessToken, merchant.EstateId, merchant.MerchantId, CancellationToken.None);

            List<SaleTransactionRequest> saleRequests = new List<SaleTransactionRequest>();

            Random r = new Random();
            Int32 transactionNumber = 1;
            // get a number of transactions to generate
            Int32 numberOfSales = r.Next(10, 50);
            //Int32 numberOfSales = 2;

            for (int i = 0; i < numberOfSales; i++)
            {
                // Pick a contract
                ContractResponse contract = contracts[r.Next(0, contracts.Count)];

                // Pick a product
                ContractProduct product = contract.Products[r.Next(0, contract.Products.Count)];

                Decimal amount = 0;
                if (product.Value.HasValue)
                {
                    amount = product.Value.Value;
                }
                else
                {
                    // generate an amount
                    amount = r.Next(9, 250);
                }

                // Generate the time
                Int32 hours = r.Next(0, 23);
                Int32 minutes = r.Next(0, 59);
                Int32 seconds = r.Next(0, 59);

                // Build the metadata
                Dictionary<String, String> requestMetaData = new Dictionary<String, String>();
                requestMetaData.Add("Amount", amount.ToString());

                var productType = Program.GetProductType(contract.OperatorName);
                String operatorName = Program.GetOperatorName(contract, product);
                if (productType == ProductType.MobileTopup)
                {
                    requestMetaData.Add("CustomerAccountNumber", "1234567890");
                }
                else if (productType == ProductType.Voucher)
                {
                    requestMetaData.Add("RecipientMobile", "1234567890");
                }

                String deviceIdentifier = merchant.Devices.Single().Value;

                SaleTransactionRequest request = new SaleTransactionRequest
                                                 {
                                                     AdditionalTransactionMetadata = requestMetaData,
                                                     ContractId = contract.ContractId,
                                                     CustomerEmailAddress = String.Empty,
                                                     DeviceIdentifier = deviceIdentifier,
                                                     MerchantId = merchant.MerchantId,
                                                     EstateId = merchant.EstateId,
                                                     TransactionType = "Sale",
                                                     TransactionDateTime = dateTime.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds),
                                                     TransactionNumber = transactionNumber.ToString(),
                                                     OperatorIdentifier = contract.OperatorName,
                                                     ProductId = product.ProductId
                                                 };

                saleRequests.Add(request);
                transactionNumber++;
            }

            return saleRequests;
        }
        
        /// <summary>
        /// Does the logon transaction.
        /// </summary>
        /// <param name="merchant">The merchant.</param>
        /// <param name="date">The date.</param>
        private static async Task DoLogonTransaction(MerchantResponse merchant,
                                                     DateTime date)
        {
            await Program.GetToken(CancellationToken.None);

            String deviceIdentifier = merchant.Devices.Single().Value;
            LogonTransactionRequest logonTransactionRequest = new LogonTransactionRequest
                                                              {
                                                                  DeviceIdentifier = deviceIdentifier,
                                                                  EstateId = merchant.EstateId,
                                                                  MerchantId = merchant.MerchantId,
                                                                  TransactionDateTime = date.AddMinutes(1),
                                                                  TransactionNumber = "1",
                                                                  TransactionType = "Logon"
                                                              };

            SerialisedMessage requestSerialisedMessage = new SerialisedMessage();
            requestSerialisedMessage.Metadata.Add("estate_id", merchant.EstateId.ToString());
            requestSerialisedMessage.Metadata.Add("merchant_id", merchant.MerchantId.ToString());
            requestSerialisedMessage.SerialisedData = JsonConvert.SerializeObject(logonTransactionRequest,
                                                                                  new JsonSerializerSettings
                                                                                  {
                                                                                      TypeNameHandling = TypeNameHandling.All
                                                                                  });

            SerialisedMessage responseSerialisedMessage =
                await Program.TransactionProcessorClient.PerformTransaction(Program.TokenResponse.AccessToken, requestSerialisedMessage, CancellationToken.None);

            LogonTransactionResponse logonTransactionResponse = JsonConvert.DeserializeObject<LogonTransactionResponse>(responseSerialisedMessage.SerialisedData);
        }

        /// <summary>
        /// Generates the date range.
        /// </summary>
        /// <param name="startDate">The start date.</param>
        /// <param name="endDate">The end date.</param>
        /// <returns></returns>
        private static List<DateTime> GenerateDateRange(DateTime startDate,
                                                        DateTime endDate)
        {
            List<DateTime> dateRange = new List<DateTime>();

            if (endDate.Subtract(startDate).Days == 0)
            {
                dateRange.Add(startDate);
            }
            else
            {
                while (endDate.Subtract(startDate).Days >= 0)
                {
                    dateRange.Add(startDate);
                    startDate = startDate.AddDays(1);
                }
            }

            return dateRange;
        }

        private static String GetOperatorName(ContractResponse contractResponse, ContractProduct contractProduct)
        {
            String operatorName = null;
            ProductType productType = Program.GetProductType(contractResponse.OperatorName);
            switch (productType)
            {
                case ProductType.Voucher:
                    operatorName = contractResponse.Description;
                    break;
                default:
                    operatorName = contractResponse.OperatorName;
                    break;

            }

            return operatorName;
        }

        private static ProductType GetProductType(String operatorName)
        {
            ProductType productType = ProductType.NotSet;
            switch (operatorName)
            {
                case "Safaricom":
                    productType = ProductType.MobileTopup;
                    break;
                case "Voucher":
                    productType = ProductType.Voucher;
                    break;
            }

            return productType;
        }
    }

    public enum ProductType
    {
        /// <summary>
        /// The not set
        /// </summary>
        NotSet = 0,

        /// <summary>
        /// The mobile topup
        /// </summary>
        MobileTopup,

        /// <summary>
        /// The mobile wallet
        /// </summary>
        MobileWallet,

        /// <summary>
        /// The bill payment
        /// </summary>
        BillPayment,

        /// <summary>
        /// The voucher
        /// </summary>
        Voucher
    }
}
