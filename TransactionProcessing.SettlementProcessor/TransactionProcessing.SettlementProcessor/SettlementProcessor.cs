namespace TransactionProcessing.SettlementProcessor
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using EstateManagement.Client;
    using Microsoft.Extensions.Configuration;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using Shared.General;
    using TransactionProcessing.DataGeneration;
    using TransactionProcessor.Client;

    public class SettlementProcessor
    {
        #region Fields

        private SecurityServiceClient SecurityServiceClient;

        private TokenResponse TokenResponse;

        private TransactionProcessorClient TransactionProcessorClient;
        
        private EstateClient EstateClient;

        private ITransactionDataGenerator TransactionDataGenerator;
        #endregion

        #region Properties

        public IConfigurationRoot Configuration { get; set; }

        #endregion

        #region Methods

        public void LoadConfiguration()
        {
            IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("/home/txnproc/config/appsettings.json", true, true)
                                                                      .AddJsonFile("appsettings.json", optional:true, reloadOnChange:true)
                                                                      .AddJsonFile("appsettings.development.json", optional:true, reloadOnChange:true);

            this.Configuration = builder.Build();

            ConfigurationReader.Initialise(this.Configuration);

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

            Func<String, String> baseAddressFunc = apiName =>
                                                   {
                                                       String url = ConfigurationReader.GetBaseServerUri(apiName).AbsoluteUri;
                                                       url = url.Remove(url.Length - 1);
                                                       return url;
                                                   };

            this.SecurityServiceClient = new SecurityServiceClient(baseAddressFunc, httpClient);
            this.TransactionProcessorClient = new TransactionProcessorClient(baseAddressFunc, httpClient);
            this.EstateClient = new EstateClient(baseAddressFunc, httpClient);

            this.TransactionDataGenerator = new TransactionDataGenerator(this.SecurityServiceClient,
                                                                         this.EstateClient,
                                                                         this.TransactionProcessorClient,
                                                                         baseAddressFunc("EstateManagementApi"),
                                                                         baseAddressFunc("FileProcessorApi"),
                                                                         baseAddressFunc("TestHostApi"),
                                                                         "serviceClient",
                                                                         "d192cbc46d834d0da90e8a9d50ded543",
                                                                         RunningMode.Live);

        }

        public async Task ProcessSettlement(Guid estateId,
                                            DateTime? startDate,
                                            DateTime? endDate)
        {


            // Get a token
            //await this.GetToken(CancellationToken.None);
            List<DateTime> dates = new List<DateTime>();
            if (startDate.HasValue && endDate.HasValue)
            {
                dates = this.GenerateDateRange(startDate.Value, endDate.Value);
            }
            else
            {
                dates.Add(DateTime.Now.Date);
            }

            //var merchantList = await this.EstateClient.GetMerchants(this.TokenResponse.AccessToken, estateId, CancellationToken.None);

            //foreach (var merchant in merchantList){

                foreach (DateTime settlementDate in dates){
                //        await this.TransactionProcessorClient.ProcessSettlement(this.TokenResponse.AccessToken, settlementDate, estateId, merchant.MerchantId, CancellationToken.None);
                await this.TransactionDataGenerator.PerformSettlement(settlementDate, estateId, CancellationToken.None);
                }
            //}

            
        }

        /// <summary>
        /// Generates the date range.
        /// </summary>
        /// <param name="startDate">The start date.</param>
        /// <param name="endDate">The end date.</param>
        /// <returns></returns>
        private List<DateTime> GenerateDateRange(DateTime startDate,
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

        private async Task GetToken(CancellationToken cancellationToken)
        {
            // Get a token to talk to the estate service
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";

            if (this.TokenResponse == null)
            {
                TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);
                this.TokenResponse = token;
            }

            if (this.TokenResponse.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2))
            {
                TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);
                this.TokenResponse = token;
            }
        }


        //protected static ITransactionDataGenerator CreateTransactionDataGenerator(String clientId, String clientSecret, RunningMode runningMode)
        //{
        //    ISecurityServiceClient securityServiceClient = Bootstrapper.GetService<ISecurityServiceClient>();
        //    IEstateClient estateClient = Bootstrapper.GetService<IEstateClient>();
        //    ITransactionProcessorClient transactionProcessorClient = Bootstrapper.GetService<ITransactionProcessorClient>();
        //    Func<String, String> baseAddressFunc = Bootstrapper.GetService<Func<String, String>>();

        //    ITransactionDataGenerator g = new TransactionDataGenerator(securityServiceClient,
        //                                                               estateClient,
        //                                                               transactionProcessorClient,
        //                                                               baseAddressFunc("EstateManagementApi"),
        //                                                               baseAddressFunc("FileProcessorApi"),
        //                                                               baseAddressFunc("TestHostApi"),
        //                                                               clientId,
        //                                                               clientSecret,
        //                                                               runningMode);
        //    return g;
        //}

        #endregion
    }
}