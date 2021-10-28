namespace TransactionProcessing.SettlementProcessor
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Configuration;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using Shared.General;
    using TransactionProcessor.Client;

    public class SettlementProcessor
    {
        #region Fields

        /// <summary>
        /// The security service client
        /// </summary>
        private SecurityServiceClient SecurityServiceClient;

        /// <summary>
        /// The token response
        /// </summary>
        private TokenResponse TokenResponse;

        /// <summary>
        /// The transaction processor client
        /// </summary>
        private TransactionProcessorClient TransactionProcessorClient;

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
                                                       var url = ConfigurationReader.GetBaseServerUri(apiName).AbsoluteUri;
                                                       url = url.Remove(url.Length - 1);
                                                       return url;
                                                   };

            this.SecurityServiceClient = new SecurityServiceClient(baseAddressFunc, httpClient);
            this.TransactionProcessorClient = new TransactionProcessorClient(baseAddressFunc, httpClient);
        }

        public async Task ProcessSettlement(Guid estateId,
                                            DateTime? startDate,
                                            DateTime? endDate)
        {
            // Get a token
            await this.GetToken(CancellationToken.None);
            List<DateTime> dates = new List<DateTime>();
            if (startDate.HasValue && endDate.HasValue)
            {
                dates = this.GenerateDateRange(startDate.Value, endDate.Value);
            }
            else
            {
                dates.Add(DateTime.Now.Date);
            }

            foreach (DateTime settlementDate in dates)
            {
                await this.TransactionProcessorClient.ProcessSettlement(this.TokenResponse.AccessToken, settlementDate, estateId, CancellationToken.None);
            }
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

        #endregion
    }
}