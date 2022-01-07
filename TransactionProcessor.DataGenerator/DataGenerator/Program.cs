using System;

namespace TransactionDataGenerator
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
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

        private static Func<String, String> baseAddressFunc;
        /// <summary>
        /// Defines the entry point of the application.
        /// </summary>
        /// <param name="args">The arguments.</param>
        static async Task Main(string[] args)
        {

            HttpClientHandler handler = new HttpClientHandler
                                        {
                                            ServerCertificateCustomValidationCallback = (message,
                                                                                         cert,
                                                                                         chain,
                                                                                         errors) =>
                                                                                        {
                                                                                            return true;
                                                                                        }
                                        };
            HttpClient httpClient = new HttpClient(handler);

            baseAddressFunc = (apiName) =>
                              {
                                  String ipaddress = "192.168.0.133";
                                  if (apiName == "EstateManagementApi")
                                  {
                                      return $"http://{ipaddress}:5000";
                                  }

                                  if (apiName == "SecurityService")
                                  {
                                      return $"https://{ipaddress}:5001";
                                  }

                                  if (apiName == "TransactionProcessorApi")
                                  {
                                      return $"http://{ipaddress}:5002";
                                  }

                                  if (apiName == "FileProcessorApi")
                                  {
                                      return $"http://{ipaddress}:5009";
                                  }

                                  return null;
                              };

            Program.SecurityServiceClient = new SecurityServiceClient(baseAddressFunc, httpClient);

            Program.EstateClient = new EstateClient(baseAddressFunc, httpClient);

            Program.TransactionProcessorClient = new TransactionProcessorClient(baseAddressFunc, httpClient);

            // Set an estate
            Guid estateId = Guid.Parse("0f7040a6-e3c1-48ad-9d1b-39c1536fa688");

            // Get a token
            await Program.GetToken(CancellationToken.None);

            // Get the the merchant list for the estate
            List<MerchantResponse> merchants = await Program.EstateClient.GetMerchants(Program.TokenResponse.AccessToken, estateId, CancellationToken.None);
            
            // Set the date range
            DateTime startDate = new DateTime(2022,1,5); //27/7
            DateTime endDate = new DateTime(2022,1,5);  // This is the date of te last generated transaction
            List<DateTime> dateRange = Program.GenerateDateRange(startDate, endDate);

            // Only use merchants that have a device
            merchants = merchants.Where(m => m.Devices != null && m.Devices.Any()).ToList();

            foreach (DateTime dateTime in dateRange)
            {
                await Program.GenerateTransactions(merchants, dateTime, CancellationToken.None);
                //await Program.GenerateFileUploads(merchants, dateTime, CancellationToken.None);
            }
            
            Console.WriteLine($"Process Complete");
        }

        private static async Task GenerateFileUploads(List<MerchantResponse> merchants,
                                                      DateTime dateTime,
                                                      CancellationToken cancellationToken)
        {
            foreach (MerchantResponse merchant in merchants)
            {
                Random r = new Random();
                
                // get a number of transactions to generate
                Int32 numberOfSales = r.Next(5, 15);

                List<ContractResponse> contracts =
                    await Program.EstateClient.GetMerchantContracts(Program.TokenResponse.AccessToken, merchant.EstateId, merchant.MerchantId, cancellationToken);

                EstateResponse estate = await Program.EstateClient.GetEstate(Program.TokenResponse.AccessToken, merchant.EstateId, cancellationToken);

                var estateUser = estate.SecurityUsers.FirstOrDefault();
                
                foreach (MerchantOperatorResponse merchantOperator in merchant.Operators)
                {
                    List<String> fileData = null;
                    // get the contract 
                    var contract = contracts.SingleOrDefault(c => c.OperatorId == merchantOperator.OperatorId);

                    if (merchantOperator.Name == "Voucher")
                    {
                        // Generate a voucher file
                        var voucherFile = GenerateVoucherFile(dateTime, contract.Description.Replace("Contract", ""), numberOfSales);
                        fileData = voucherFile.fileLines;
                        // Need to make a deposit for this amount - last sale
                        Decimal depositAmount = voucherFile.totalValue - voucherFile.lastSale;
                        await MakeMerchantDeposit(merchant, depositAmount, dateTime.AddSeconds(1));
                    }
                    else
                    {
                        // generate a topup file
                        var topupFile = GenerateTopupFile(dateTime, numberOfSales);
                        fileData = topupFile.fileLines;
                        // Need to make a deposit for this amount - last sale
                        Decimal depositAmount = topupFile.totalValue - topupFile.lastSale;
                        await MakeMerchantDeposit(merchant, depositAmount, dateTime.AddSeconds(2));
                    }

                    // Write this file to disk
                    Directory.CreateDirectory($"/home/txnproc/txngenerator/{merchantOperator.Name}");
                    using(StreamWriter sw = new StreamWriter($"/home/txnproc/txngenerator/{merchantOperator.Name}/{contract.Description.Replace("Contract", "")}-{dateTime:yyyy-MM-dd-HH-mm-ss}"))
                    {
                        foreach (String fileLine in fileData)  
                        {
                            sw.WriteLine(fileLine);
                        }
                    }

                    // Upload the generated files for this merchant/operatorcd 
                    // Get the files
                    var files = Directory.GetFiles($"/home/txnproc/txngenerator/{merchantOperator.Name}");

                    var fileDateTime = dateTime.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute).AddSeconds(DateTime.Now.Second);

                    foreach (String file in files)
                    {
                        var fileProfileId = GetFileProfileIdFromOperator(merchantOperator.Name, cancellationToken);
                        
                        await UploadFile(file, merchant.EstateId, merchant.MerchantId, fileProfileId, estateUser.SecurityUserId, fileDateTime, cancellationToken);
                        // Remove file onece uploaded
                        File.Delete(file);
                    }
                }
            }
        }

        private static Guid GetFileProfileIdFromOperator(String operatorName, CancellationToken cancellationToken)
        {
            // TODO: get this profile list from API

            switch(operatorName)
            {
                case "Safaricom":
                    return Guid.Parse("B2A59ABF-293D-4A6B-B81B-7007503C3476");
                case "Voucher":
                    return Guid.Parse("8806EDBC-3ED6-406B-9E5F-A9078356BE99");
                default:
                    return Guid.Empty;
            }
        }

        private static async Task<HttpResponseMessage> UploadFile(String filePath, Guid estateId, Guid merchantId, Guid fileProfileId, Guid userId, DateTime fileDateTime, CancellationToken cancellationToken)
        {
            var client = new HttpClient();
            var formData = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
            formData.Add(fileContent, "file", Path.GetFileName(filePath));
            formData.Add(new StringContent(estateId.ToString()), "request.EstateId");
            formData.Add(new StringContent(merchantId.ToString()), "request.MerchantId");
            formData.Add(new StringContent(fileProfileId.ToString()), "request.FileProfileId");
            formData.Add(new StringContent(userId.ToString()), "request.UserId");
            formData.Add(new StringContent(fileDateTime.ToString("yyyy-MM-dd HH:mm:ss")), "request.UploadDateTime");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseAddressFunc("FileProcessorApi")}/api/files")
                          {
                              Content = formData,
                          };
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", Program.TokenResponse.AccessToken);
            var response = await client.SendAsync(request, cancellationToken);

            return response;
        }

        private static (List<String> fileLines, Decimal totalValue, Decimal lastSale) GenerateTopupFile(DateTime dateTime,
                                                                                                        Int32 numberOfLines)
        {
            List<String> fileLines = new List<String>();
            Decimal totalValue = 0;
            Decimal lastSale = 0;
            String mobileNumber = "07777777305";
            Random r = new Random();

            fileLines.Add($"H,{dateTime:yyyy-MM-dd-HH-mm-ss}");

            for (int i = 0; i < numberOfLines; i++)
            {
                Int32 amount = r.Next(75, 250);
                totalValue += amount;
                lastSale = amount;
                fileLines.Add($"D,{mobileNumber},{amount}");
            }

            fileLines.Add($"T,{numberOfLines}");

            return (fileLines, totalValue, lastSale);
        }

        private static (List<String> fileLines, Decimal totalValue, Decimal lastSale) GenerateVoucherFile(DateTime dateTime, String issuerName, Int32 numberOfLines)
        {
            // Build the header
            List<String> fileLines = new List<String>();
            fileLines.Add($"H,{dateTime:yyyy-MM-dd-HH-mm-ss}");
            String emailAddress = "testrecipient@email.com";
            String mobileNumber = "07777777305";
            Random r = new Random();
            Decimal totalValue = 0;
            Decimal lastSale = 0;

            for (int i = 0; i < numberOfLines; i++)
            {
                Int32 amount = r.Next(75, 250);
                totalValue += amount;
                lastSale = amount;
                if (i % 2 == 0)
                {
                    fileLines.Add($"D,{issuerName},{emailAddress},{amount}");
                }
                else
                {
                    fileLines.Add($"D,{issuerName},{mobileNumber},{amount}");
                }
            }

            fileLines.Add($"T,{numberOfLines}");

            return (fileLines, totalValue, lastSale);
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
            Int32 maxDegreeOfParallelism = 1;
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
                                                               Reference = "Test Data Gen Deposit"
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
