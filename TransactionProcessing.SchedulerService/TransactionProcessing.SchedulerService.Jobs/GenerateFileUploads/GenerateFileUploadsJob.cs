namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using Quartz;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    [DisallowConcurrentExecution]
    public class GenerateFileUploadsJob : IJob
    {
        #region Fields

        /// <summary>
        /// The base address function
        /// </summary>
        private Func<String, String> baseAddressFunc;

        /// <summary>
        /// The bootstrapper
        /// </summary>
        private readonly IBootstrapper Bootstrapper;

        /// <summary>
        /// The estate client
        /// </summary>
        private IEstateClient EstateClient;

        /// <summary>
        /// The security service client
        /// </summary>
        private ISecurityServiceClient SecurityServiceClient;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="GenerateFileUploadsJob" /> class.
        /// </summary>
        /// <param name="bootstrapperResolver">The bootstrapper resolver.</param>
        public GenerateFileUploadsJob(Func<String, IBootstrapper> bootstrapperResolver)
        {
            this.Bootstrapper = bootstrapperResolver(nameof(GenerateFileUploadsJob));
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called by the <see cref="T:Quartz.IScheduler" /> when a <see cref="T:Quartz.ITrigger" />
        /// fires that is associated with the <see cref="T:Quartz.IJob" />.
        /// </summary>
        /// <param name="context">The execution context.</param>
        /// <remarks>
        /// The implementation may wish to set a  result object on the
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to
        /// <see cref="T:Quartz.IJobListener" />s or
        /// <see cref="T:Quartz.ITriggerListener" />s that are watching the job's
        /// execution.
        /// </remarks>
        public async Task Execute(IJobExecutionContext context)
        {
            this.Bootstrapper.ConfigureServices(context);

            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
            Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
            String contractsToSkip = context.MergedJobDataMap.GetString("contractsToSkip");

            this.SecurityServiceClient = this.Bootstrapper.GetService<ISecurityServiceClient>();
            this.EstateClient = this.Bootstrapper.GetService<IEstateClient>();
            this.baseAddressFunc = this.Bootstrapper.GetService<Func<String, String>>();

            await this.GenerateFileUploads(estateId, merchantId,contractsToSkip, context.CancellationToken);
        }

        /// <summary>
        /// Generates the file uploads.
        /// </summary>
        /// <param name="estateId">The estate identifier.</param>
        /// <param name="merchantId">The merchant identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async Task GenerateFileUploads(Guid estateId,
                                               Guid merchantId,
                                               String contractsToSkip,
                                               CancellationToken cancellationToken)
        {
            DateTime fileDate = DateTime.Now;

            // get a token
            String accessToken = await this.GetToken(cancellationToken);

            MerchantResponse merchant = await this.EstateClient.GetMerchant(accessToken, estateId, merchantId, cancellationToken);

            Random r = new Random();

            // get a number of transactions to generate
            Int32 numberOfSales = r.Next(5, 15);

            List<ContractResponse> contracts = await this.EstateClient.GetMerchantContracts(accessToken, merchant.EstateId, merchant.MerchantId, cancellationToken);

            if (String.IsNullOrEmpty(contractsToSkip) == false)
            {
                String[] skipContracts = contractsToSkip.Split('|');
                contracts = contracts.Where(c => skipContracts.Contains(c.Description) == false).ToList();
            }

            EstateResponse estate = await this.EstateClient.GetEstate(accessToken, merchant.EstateId, cancellationToken);

            SecurityUserResponse estateUser = estate.SecurityUsers.FirstOrDefault();

            foreach (MerchantOperatorResponse merchantOperator in merchant.Operators)
            {
                List<String> fileData = null;
                // get the contract 
                var contract = contracts.SingleOrDefault(c => c.OperatorId == merchantOperator.OperatorId);

                if (merchantOperator.Name == "Voucher")
                {
                    // Generate a voucher file
                    var voucherFile = this.GenerateVoucherFile(fileDate, contract.Description.Replace("Contract", ""), numberOfSales);
                    fileData = voucherFile.fileLines;
                    // Need to make a deposit for this amount - last sale
                    Decimal depositAmount = voucherFile.totalValue - voucherFile.lastSale;
                    await this.MakeMerchantDeposit(accessToken, merchant, depositAmount, fileDate.AddSeconds(1), cancellationToken);
                }
                else
                {
                    // generate a topup file
                    var topupFile = this.GenerateTopupFile(fileDate, numberOfSales);
                    fileData = topupFile.fileLines;
                    // Need to make a deposit for this amount - last sale
                    Decimal depositAmount = topupFile.totalValue - topupFile.lastSale;
                    await this.MakeMerchantDeposit(accessToken, merchant, depositAmount, fileDate.AddSeconds(2), cancellationToken);
                }

                // Write this file to disk
                Directory.CreateDirectory($"/home/txnproc/txngenerator/{merchantOperator.Name}");
                using(StreamWriter sw =
                      new
                          StreamWriter($"/home/txnproc/txngenerator/{merchantOperator.Name}/{contract.Description.Replace("Contract", "")}-{fileDate:yyyy-MM-dd-HH-mm-ss}"))
                {
                    foreach (String fileLine in fileData)
                    {
                        sw.WriteLine(fileLine);
                    }
                }

                // Upload the generated files for this merchant/operator
                // Get the files
                var files = Directory.GetFiles($"/home/txnproc/txngenerator/{merchantOperator.Name}");

                var fileDateTime = fileDate.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute).AddSeconds(DateTime.Now.Second);

                foreach (String file in files)
                {
                    var fileProfileId = this.GetFileProfileIdFromOperator(merchantOperator.Name, cancellationToken);

                    await this.UploadFile(accessToken,
                                          file,
                                          merchant.EstateId,
                                          merchant.MerchantId,
                                          fileProfileId,
                                          estateUser.SecurityUserId,
                                          fileDateTime,
                                          cancellationToken);

                    // Remove file once uploaded
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Generates the topup file.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <param name="numberOfLines">The number of lines.</param>
        /// <returns></returns>
        private (List<String> fileLines, Decimal totalValue, Decimal lastSale) GenerateTopupFile(DateTime dateTime,
                                                                                                 Int32 numberOfLines)
        {
            List<String> fileLines = new List<String>();
            Decimal totalValue = 0;
            Decimal lastSale = 0;
            String mobileNumber = "07777777305";
            Random r = new Random();

            fileLines.Add($"H,{dateTime:yyyy-MM-dd-HH-mm-ss}");

            for (Int32 i = 0; i < numberOfLines; i++)
            {
                Int32 amount = r.Next(75, 250);
                totalValue += amount;
                lastSale = amount;
                fileLines.Add($"D,{mobileNumber},{amount}");
            }

            fileLines.Add($"T,{numberOfLines}");

            return (fileLines, totalValue, lastSale);
        }

        /// <summary>
        /// Generates the voucher file.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <param name="issuerName">Name of the issuer.</param>
        /// <param name="numberOfLines">The number of lines.</param>
        /// <returns></returns>
        private (List<String> fileLines, Decimal totalValue, Decimal lastSale) GenerateVoucherFile(DateTime dateTime,
                                                                                                   String issuerName,
                                                                                                   Int32 numberOfLines)
        {
            // Build the header
            List<String> fileLines = new List<String>();
            fileLines.Add($"H,{dateTime:yyyy-MM-dd-HH-mm-ss}");
            String emailAddress = "testrecipient@email.com";
            String mobileNumber = "07777777305";
            Random r = new Random();
            Decimal totalValue = 0;
            Decimal lastSale = 0;

            for (Int32 i = 0; i < numberOfLines; i++)
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
        /// Gets the file profile identifier from operator.
        /// </summary>
        /// <param name="operatorName">Name of the operator.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private Guid GetFileProfileIdFromOperator(String operatorName,
                                                  CancellationToken cancellationToken)
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

        /// <summary>
        /// Gets the token.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task<String> GetToken(CancellationToken cancellationToken)
        {
            // Get a token to talk to the estate service
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";

            TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);

            return token.AccessToken;
        }

        /// <summary>
        /// Makes the merchant deposit.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <param name="merchant">The merchant.</param>
        /// <param name="depositAmount">The deposit amount.</param>
        /// <param name="dateTime">The date time.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async Task MakeMerchantDeposit(String accessToken,
                                               MerchantResponse merchant,
                                               Decimal depositAmount,
                                               DateTime dateTime,
                                               CancellationToken cancellationToken)
        {
            await this.EstateClient.MakeMerchantDeposit(accessToken,
                                                        merchant.EstateId,
                                                        merchant.MerchantId,
                                                        new MakeMerchantDepositRequest
                                                        {
                                                            Amount = depositAmount,
                                                            DepositDateTime = dateTime.AddSeconds(55),
                                                            Reference = "Test Data Gen Deposit"
                                                        },
                                                        cancellationToken);
            Console.WriteLine($"Deposit made for Merchant [{merchant.MerchantName}]");
        }

        /// <summary>
        /// Uploads the file.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="estateId">The estate identifier.</param>
        /// <param name="merchantId">The merchant identifier.</param>
        /// <param name="fileProfileId">The file profile identifier.</param>
        /// <param name="userId">The user identifier.</param>
        /// <param name="fileDateTime">The file date time.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task<HttpResponseMessage> UploadFile(String accessToken,
                                                           String filePath,
                                                           Guid estateId,
                                                           Guid merchantId,
                                                           Guid fileProfileId,
                                                           Guid userId,
                                                           DateTime fileDateTime,
                                                           CancellationToken cancellationToken)
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

            var request = new HttpRequestMessage(HttpMethod.Post, $"{this.baseAddressFunc("FileProcessorApi")}/api/files")
                          {
                              Content = formData,
                          };
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
            var response = await client.SendAsync(request, cancellationToken);

            return response;
        }

        #endregion
    }
}