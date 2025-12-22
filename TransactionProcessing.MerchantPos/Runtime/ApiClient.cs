using System.Net.Http.Headers;
using System.Text;
using MerchantPos.EF.Models;
using Newtonsoft.Json;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Logger;
using SimpleResults;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects;
using TransactionProcessor.DataTransferObjects.Requests.Merchant;
using TransactionProcessorACL.DataTransferObjects;
using TransactionProcessorACL.DataTransferObjects.Responses;
using OperatorTotalRequest = TransactionProcessorACL.DataTransferObjects.OperatorTotalRequest;

namespace TransactionProcessing.MerchantPos.Runtime;

public interface IApiClient
{
    Task<Result<TokenResponse>> GetToken(String clientId, String clientSecret, MerchantConfig cfg, CancellationToken cancellationToken);
    Task SendLogon(MerchantConfig cfg, TokenResponse token, 
                   Int32 transactionNumber, CancellationToken cancellationToken);
    Task<List<Product>> GetProductList(MerchantConfig cfg, TokenResponse token, CancellationToken cancellationToken);
    Task<decimal> GetBalance(MerchantConfig cfg, TokenResponse token, CancellationToken cancellationToken);
    Task<SaleResponse> SendSale(MerchantConfig cfg, TokenResponse token, Product product, Decimal value,
                                Int32 transactionNumber, CancellationToken cancellationToken);
    Task SendDeposit(MerchantConfig cfg, TokenResponse token, decimal amount, CancellationToken cancellationToken);
    Task SendReconciliation(MerchantConfig cfg, TokenResponse token, List<OperatorTotal> totals, CancellationToken cancellationToken);
}

public class ApiClient : ClientProxyBase.ClientProxyBase, IApiClient {
    private readonly ISecurityServiceClient SecurityClient;
    private readonly ITransactionProcessorClient TransactionProcessorClient;
    private readonly Func<String, String> BaseAddressResolver;

    public ApiClient(ISecurityServiceClient securityClient,
                     ITransactionProcessorClient transactionProcessorClient,
                     HttpClient httpClient,
                     Func<String, String> baseAddressResolver) : base(httpClient) {
        this.SecurityClient = securityClient;
        this.TransactionProcessorClient = transactionProcessorClient;
        this.BaseAddressResolver = baseAddressResolver;
    }

    public async Task<Result<TokenResponse>> GetToken(String clientId,
                                                      String clientSecret,
                                                      MerchantConfig cfg,
                                                      CancellationToken cancellationToken) {
        return await this.SecurityClient.GetToken(cfg.Username, cfg.Password, clientId, clientSecret, cancellationToken);
    }

    public async Task SendLogon(MerchantConfig cfg,
                                TokenResponse token,
                                Int32 transactionNumber,
                                CancellationToken cancellationToken) {

        LogonTransactionRequestMessage logonTransactionRequest = new() {
            ApplicationVersion = cfg.ApplicationVersion,
            DeviceIdentifier = cfg.DeviceIdentifier,
            TransactionDateTime = DateTime.Now, 
            TransactionNumber = transactionNumber.ToString("D4")
        };

        Result<LogonTransactionResponseMessage> result = await this.SendTransactionRequest<LogonTransactionRequestMessage, LogonTransactionResponseMessage>(logonTransactionRequest, "api/logontransactions", token, cancellationToken);

        if (result.IsFailed)
            Logger.LogWarning($"Logon failed for Merchant {cfg.MerchantName}");
    }

    public async Task<List<Product>> GetProductList(MerchantConfig cfg,
                                                    TokenResponse token,
                                                    CancellationToken cancellationToken) {
        List<Product> products = new();

        Guid estateId = cfg.EstateId;
        Guid merchantId = cfg.MerchantId;

        String requestUri = this.BuildRequestUrl($"api/merchants/contracts?applicationVersion={cfg.ApplicationVersion}");

        Logger.LogInformation("About to request merchant contracts");
        Logger.LogDebug($"Merchant Contract Request details:  Estate Id {estateId} Merchant Id {merchantId} Access Token {token.AccessToken}");

        HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var httpResponse = await this.HttpClient.SendAsync(request, cancellationToken);

        // Process the response
        Result<String> content = await this.HandleResponseX(httpResponse, cancellationToken);

        if (content.IsFailed)
        {
            Logger.LogInformation($"GetMerchantContracts failed {content.Status}");
        }

        Logger.LogDebug($"Transaction Response details:  Status {httpResponse.StatusCode} Payload {content.Data}");

        List<ContractResponseX>? responseData = JsonConvert.DeserializeObject<List<ContractResponseX>>(content.Data);

        responseData = responseData.Where(r => r.ContractId == Guid.Parse("881f5e96-deac-45a5-a9cf-69977a5af559")).ToList();

        Logger.LogInformation($"{responseData.Count} for merchant requested successfully");
        Logger.LogDebug($"Merchant Contract Response: [{JsonConvert.SerializeObject(responseData)}]");

        foreach (ContractResponseX contractResponse in responseData)
        {
            foreach (ContractProductX contractResponseProduct in contractResponse.Products)
            {
                products.Add(new Product {
                    ContractId = contractResponse.ContractId,
                    OperatorId = contractResponse.OperatorId,
                    ProductId = contractResponseProduct.ProductId,
                    ProductType = GetProductType(contractResponse.OperatorName),
                    Value = contractResponseProduct.Value ?? 0,
                    Name = contractResponseProduct.Name,
                    ProductSubType = GetProductSubType(contractResponse.OperatorName),
                });
            }
        }

        return products;
    }

    public static ProductType GetProductType(String operatorName)
    {
        return operatorName switch
        {
            "Safaricom" => ProductType.MobileTopup,
            "Voucher" => ProductType.Voucher,
            "PataPawa PostPay" => ProductType.BillPayment,
            "PataPawa PrePay" => ProductType.BillPayment,
            _ => ProductType.NotSet,
        };
    }

    public static ProductSubType GetProductSubType(String operatorName)
    {
        return operatorName switch
        {
            "Safaricom" => ProductSubType.MobileTopup,
            "Voucher" => ProductSubType.Voucher,
            "PataPawa PostPay" => ProductSubType.BillPaymentPostPay,
            "PataPawa PrePay" => ProductSubType.BillPaymentPrePay,
            _ => ProductSubType.NotSet,
        };
    }

    public async Task<Decimal> GetBalance(MerchantConfig cfg,
                                          TokenResponse token,
                                          CancellationToken cancellationToken) {
        Result<MerchantBalanceResponse>? response = await this.TransactionProcessorClient.GetMerchantBalance(token.AccessToken, cfg.EstateId, cfg.MerchantId, cancellationToken);
        
        if (response.IsFailed) {
            Logger.LogWarning($"Error retrieving merchant balance for merchant {cfg.MerchantName}");
        }
        
        return response.Data.Balance;
    }

    public async Task<SaleResponse> SendSale(MerchantConfig cfg,
                                             TokenResponse token,
                                             Product product,
                                             Decimal value,
                                             Int32 transactionNumber,
                                             CancellationToken cancellationToken) {


        List<SaleTransactionRequestMessage> requests = product.ProductType switch {
            ProductType.MobileTopup => BuildMobileSaleTransactionRequestMessage(cfg, product, value, transactionNumber),
            ProductType.Voucher => BuildVoucherTransactionRequestMessage(cfg, product, value, transactionNumber),
            ProductType.BillPayment => await BuildBillPaymentTransactionRequestMessages(cfg, product, value, transactionNumber),
            _ => throw new NotImplementedException($"Product Type {product.ProductType} not implemented")
        };
        Boolean approved = false;
        foreach (SaleTransactionRequestMessage saleTransactionRequestMessage in requests) {
            Result<SaleTransactionResponseMessage> response = await this.SendTransactionRequest<SaleTransactionRequestMessage, SaleTransactionResponseMessage>(saleTransactionRequestMessage, "api/saletransactions", token, cancellationToken);
            if (response.IsSuccess == false) {
                // Exit the loop on failure
                approved = false;
                break;
            }
            approved = response.Data.ResponseCode == "0000";
        }

        return new SaleResponse(approved);
    }

    public async Task SendDeposit(MerchantConfig cfg,
                                  TokenResponse token,
                                  Decimal amount,
                                  CancellationToken cancellationToken) {
        var result = await this.TransactionProcessorClient.MakeMerchantDeposit(token.AccessToken, cfg.EstateId, cfg.MerchantId, new MakeMerchantDepositRequest {
            Amount = amount,
            DepositDateTime = DateTime.Now,
            Reference = $"AutoDeposit{DateTime.Now:yyyy-MM-dd}"
        }, cancellationToken);

        if (result.IsFailed)
            Logger.LogWarning($"Error performing deposit for merchant {cfg.MerchantName}");
    }

    public async Task SendReconciliation(MerchantConfig cfg,
                                         TokenResponse token,
                                         List<OperatorTotal> totals,
                                         CancellationToken cancellationToken) {
        ReconciliationRequestMessage reconciliationRequest = new ReconciliationRequestMessage
        {
            ApplicationVersion = cfg.ApplicationVersion,
            TransactionDateTime = DateTime.Now,
            DeviceIdentifier = cfg.DeviceIdentifier,
            TransactionCount = totals.Sum(t=> t.TotalCount),
            TransactionValue = totals.Sum(t => t.Total),
            OperatorTotals = new List<OperatorTotalRequest>()
        };
        foreach (var modelOperatorTotal in totals)
        {
            reconciliationRequest.OperatorTotals.Add(new OperatorTotalRequest
            {
                OperatorId = modelOperatorTotal.OperatorId,
                TransactionValue = modelOperatorTotal.Total,
                ContractId = modelOperatorTotal.ContractId,
                TransactionCount = modelOperatorTotal.TotalCount
            });
        }

        Result<ReconciliationResponseMessage> result = await this.SendTransactionRequest<ReconciliationRequestMessage, ReconciliationResponseMessage>(reconciliationRequest, "api/reconciliationtransactions", token, cancellationToken);

        if (result.IsFailed)
        Logger.LogWarning($"Error during reconciliation for merchant {cfg.MerchantName}");
    }

    private async Task<List<SaleTransactionRequestMessage>> BuildBillPaymentTransactionRequestMessages(MerchantConfig cfg,
                                                                                           Product product,
                                                                                           Decimal value,
                                                                                           Int32 transactionNumber) {
        // We need data setup at test host here for both post pay and pre pay bill payment products
        (String accountNumber, String accountName, String mobileNumber) extraDetails = product.ProductSubType switch {
            ProductSubType.BillPaymentPostPay => await this.CreateBillPaymentBill(value, CancellationToken.None),
            ProductSubType.BillPaymentPrePay => await this.CreateBillPaymentMeter(CancellationToken.None),
        };
        
        List<SaleTransactionRequestMessage> requestMessages = new();
        SaleTransactionRequestMessage getTransactionRequestMessage = new() {
            ProductId = product.ProductId,
            OperatorId = product.OperatorId,
            ApplicationVersion = cfg.ApplicationVersion,
            DeviceIdentifier = cfg.DeviceIdentifier,
            ContractId = product.ContractId,
            TransactionDateTime = DateTime.Now,
            TransactionNumber = transactionNumber.ToString("D4")
        };

        if (product.ProductSubType == ProductSubType.BillPaymentPostPay) {
            getTransactionRequestMessage.AdditionalRequestMetadata = new Dictionary<String, String> { { "CustomerAccountNumber", extraDetails.accountNumber }, { "PataPawaPostPaidMessageType", "VerifyAccount" } };

        }
        else if (product.ProductSubType == ProductSubType.BillPaymentPrePay) {
            getTransactionRequestMessage.AdditionalRequestMetadata = new Dictionary<String, String> { { "MeterNumber", extraDetails.accountNumber }, { "PataPawaPrePayMessageType", "meter" } };
        }
        requestMessages.Add(getTransactionRequestMessage);

        // Now the actual payment request
        SaleTransactionRequestMessage paymentRequestMessage = new() {
            ProductId = product.ProductId,
            OperatorId = product.OperatorId,
            ApplicationVersion = cfg.ApplicationVersion,
            DeviceIdentifier = cfg.DeviceIdentifier,
            ContractId = product.ContractId,
            TransactionDateTime = DateTime.Now,
            TransactionNumber = transactionNumber.ToString("D4")
        };

        if (product.ProductSubType == ProductSubType.BillPaymentPostPay) {
            // Add the additional request data
            paymentRequestMessage.AdditionalRequestMetadata = new Dictionary<String, String> {
                { "CustomerAccountNumber", extraDetails.accountNumber },
                { "CustomerName", extraDetails.accountName },
                { "MobileNumber", extraDetails.mobileNumber },
                { "Amount", value.ToString() },
                { "PataPawaPostPaidMessageType", "ProcessBill" }
            };
        }
        else {
            paymentRequestMessage.AdditionalRequestMetadata = new Dictionary<String, String> {
                { "MeterNumber", extraDetails.accountNumber  }, { "CustomerName", extraDetails.accountName }, { "PataPawaPrePayMessageType", "vend" }, { "Amount", value.ToString() },
            };
        }
        requestMessages.Add(paymentRequestMessage);
        return requestMessages;
    }
    
    private List<SaleTransactionRequestMessage> BuildMobileSaleTransactionRequestMessage(MerchantConfig cfg,
                                                                                         Product product,
                                                                                         Decimal value, 
                                                                                         Int32 transactionNumber) {
        List<SaleTransactionRequestMessage> requestMessages = new();
        SaleTransactionRequestMessage saleTransactionRequest = new() {
            ProductId = product.ProductId,
            OperatorId = product.OperatorId,
            ApplicationVersion = cfg.ApplicationVersion,
            DeviceIdentifier = cfg.DeviceIdentifier,
            ContractId = product.ContractId,
            TransactionDateTime = DateTime.Now,
            TransactionNumber = transactionNumber.ToString("D4")
        };
        
        saleTransactionRequest.AdditionalRequestMetadata = new Dictionary<String, String> { { "Amount", value.ToString() }, { "CustomerAccountNumber", "07777777705" } };
        requestMessages.Add(saleTransactionRequest);
        return requestMessages;
    }

    private List<SaleTransactionRequestMessage> BuildVoucherTransactionRequestMessage(MerchantConfig cfg,
                                                                                         Product product,
                                                                                         Decimal value,
                                                                                         Int32 transactionNumber)
    {
        List<SaleTransactionRequestMessage> requestMessages = new();
        SaleTransactionRequestMessage saleTransactionRequest = new()
        {
            ProductId = product.ProductId,
            OperatorId = product.OperatorId,
            ApplicationVersion = cfg.ApplicationVersion,
            DeviceIdentifier = cfg.DeviceIdentifier,
            ContractId = product.ContractId,
            TransactionDateTime = DateTime.Now,
            TransactionNumber = transactionNumber.ToString("D4")
        };

        saleTransactionRequest.AdditionalRequestMetadata = new Dictionary<String, String> { 
            { "Amount", value.ToString() }, 
            { "RecipientMobile", "07777777705" }
        };
        requestMessages.Add(saleTransactionRequest);
        return requestMessages;
    }

    private async Task<Result<TResponse>> SendTransactionRequest<TRequest, TResponse>(TRequest request,
                                                                                      String route,
                                                                                      TokenResponse tokenResponse,
                                                                                      CancellationToken cancellationToken) {
        String requestUri = this.BuildRequestUrl(route);
        try {
            String requestSerialised = JsonConvert.SerializeObject(request);

            StringContent httpContent = new StringContent(requestSerialised, Encoding.UTF8, "application/json");

            this.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);

            // Make the Http Call here
            HttpResponseMessage httpResponse = await this.HttpClient.PostAsync(requestUri, httpContent, cancellationToken);

            // Process the response
            Result<String> result = await this.HandleResponseX(httpResponse, cancellationToken);

            if (result.IsSuccess == false) {
                return Result.Failure("Error performing Voucher transaction");
            }

            TResponse? responseData = JsonConvert.DeserializeObject<TResponse>(result.Data);

            return Result.Success(responseData);
        }
        catch (Exception ex) {
            // An exception has occurred, add some additional information to the message

            return ResultExtensions.FailureExtended("Error posting transaction", ex);
        }
    }

    private String BuildRequestUrl(String route) {
        String baseAddress = this.BaseAddressResolver("TransactionProcessorACL");

        String requestUri = $"{baseAddress}/{route}";

        return requestUri;
    }
    private readonly Random _rng = new();
    private async Task<(String accountNumber, String accountName, String mobileNumber)> CreateBillPaymentBill(Decimal billAmount, CancellationToken cancellationToken)
    {
        Int32 accountNumber = this._rng.Next(1, 100000);
        String baseAddress = this.BaseAddressResolver("TestHost");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{baseAddress}/api/developer/patapawapostpay/createbill");
        var body = new
        {
            due_date = DateTime.Now.AddDays(1),
            amount = billAmount,
            account_number = accountNumber,
            account_name = $"Test Account {accountNumber}"
        };
        request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");


        using (HttpClient client = new HttpClient())
        {
            await client.SendAsync(request, cancellationToken);
        }

        return (body.account_number.ToString(), body.account_name, "07777777705");
    
    }

    private async Task<(String accountNumber, String accountName, String mobileNumber)> CreateBillPaymentMeter(CancellationToken cancellationToken)
    {
            Int32 meterNumber = this._rng.Next(1, 100000);
            String baseAddress = this.BaseAddressResolver("TestHost");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{baseAddress}/api/developer/patapawaprepay/createmeter");
            var body = new
            {
                due_date = DateTime.Now.AddDays(1),
                meter_number = meterNumber,
                customer_name = $"Customer {meterNumber}"
            };
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            using (HttpClient client = new HttpClient())
            {
                await client.SendAsync(request, cancellationToken);
            }

            return (body.meter_number.ToString(), body.customer_name, null);
    }

}

public class ContractResponseX
{
    [JsonProperty("contract_id")]
    public Guid ContractId { get; set; }

    [JsonProperty("contract_reporting_id")]
    public int ContractReportingId { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("estate_id")]
    public Guid EstateId { get; set; }

    [JsonProperty("estate_reporting_id")]
    public int EstateReportingId { get; set; }

    [JsonProperty("operator_id")]
    public Guid OperatorId { get; set; }

    [JsonProperty("operator_name")]
    public string OperatorName { get; set; }

    [JsonProperty("products")]
    public List<ContractProductX> Products { get; set; }
}

public class ContractProductX
{
    [JsonProperty("display_text")]
    public string DisplayText { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("product_id")]
    public Guid ProductId { get; set; }

    [JsonProperty("product_reporting_id")]
    public int ProductReportingId { get; set; }

    [JsonProperty("transaction_fees")]
    public List<ContractProductTransactionFeeX> TransactionFees { get; set; }

    [JsonProperty("value")]
    public Decimal? Value { get; set; }

    [JsonProperty("product_type")]
    public ProductType ProductType { get; set; }
}

public class ContractProductTransactionFeeX
{
    [JsonProperty("calculation_type")]
    public CalculationType CalculationType { get; set; }

    [JsonProperty("fee_type")]
    public FeeType FeeType { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("transaction_fee_id")]
    public Guid TransactionFeeId { get; set; }

    [JsonProperty("transaction_fee_reporting_id")]
    public int TransactionFeeReportingId { get; set; }

    [JsonProperty("value")]
    public Decimal Value { get; set; }
}

public enum ProductSubType
{
    NotSet = 0,
    MobileTopup,
    MobileWallet,
    BillPaymentPostPay,
    BillPaymentPrePay,
    Voucher
}