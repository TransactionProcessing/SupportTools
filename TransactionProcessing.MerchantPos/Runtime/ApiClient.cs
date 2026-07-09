using MerchantPos.EF.Models;
using SecurityService.Client;
using Shared.Logger;
using SimpleResults;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Newtonsoft.Json;
using SecurityService.DataTransferObjects;
using Shared.Results;
using Shared.Serialisation;
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

    Task<Result<MerchantResponse>> GetMerchant(MerchantConfig cfg, TokenResponse token, CancellationToken cancellationToken);

    Task<Result> SendLogon(MerchantConfig cfg, TokenResponse token, 
                           Int32 transactionNumber, CancellationToken cancellationToken);
    Task<Result<List<Product>>> GetProductList(MerchantConfig cfg, TokenResponse token, CancellationToken cancellationToken);
    Task<Result<Decimal>> GetBalance(MerchantConfig cfg, TokenResponse token, CancellationToken cancellationToken);
    Task<Result<SaleResponse>> SendSale(MerchantConfig cfg, TokenResponse token, Product product, Decimal value,
                                        Int32 transactionNumber, CancellationToken cancellationToken);
    Task<Result> SendDeposit(MerchantConfig cfg, TokenResponse token, decimal amount, CancellationToken cancellationToken);
    Task<Result> SendReconciliation(MerchantConfig cfg, TokenResponse token, List<OperatorTotal> totals, CancellationToken cancellationToken);
}

public class ApiClient : ClientProxyBase.ClientProxyBase, IApiClient {
    private readonly ISecurityServiceClient SecurityClient;
    private readonly ITransactionProcessorClient TransactionProcessorClient;
    private readonly Func<String, String> BaseAddressResolver;
    
    public ApiClient(ISecurityServiceClient securityClient,
                     ITransactionProcessorClient transactionProcessorClient,
                     HttpClient httpClient,
                     Func<String, String> baseAddressResolver,
                     Func<object, String> serialise,
                     Func<String, Type, Object> deserialise) : base(httpClient, serialise, deserialise) {
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

    public async Task<Result<MerchantResponse>> GetMerchant(MerchantConfig cfg,
                                                            TokenResponse token,
                                                            CancellationToken cancellationToken) {

        Guid estateId = cfg.EstateId;
        Guid merchantId = cfg.MerchantId;

        String requestUri = this.BuildRequestUrl($"api/merchants?applicationVersion={cfg.ApplicationVersion}");

        Logger.LogInformation("About to request merchant details");
        Logger.LogDebug($"Merchant Request details:  Estate Id {estateId} Merchant Id {merchantId} Access Token {token.AccessToken}");
        
        // Process the response
        Result<MerchantResponse>? result = await this.Get<MerchantResponse>(requestUri, token.AccessToken, cancellationToken);
        
        if (result.IsFailed) {
            Logger.LogInformation($"GetMerchant failed {result.Message}");
            return ResultHelpers.CreateFailure(result);
        }

        return result;
    }

    public async Task<Result> SendLogon(MerchantConfig cfg,
                                        TokenResponse token,
                                        Int32 transactionNumber,
                                        CancellationToken cancellationToken) {

        LogonTransactionRequestMessage logonTransactionRequest = new() { ApplicationVersion = cfg.ApplicationVersion, DeviceIdentifier = cfg.DeviceIdentifier, TransactionDateTime = DateTime.Now, TransactionNumber = transactionNumber.ToString("D4") };

        Result<LogonTransactionResponseMessage> result = await this.SendTransactionRequest<LogonTransactionRequestMessage, LogonTransactionResponseMessage>(logonTransactionRequest, "api/logontransactions", token, cancellationToken);

        if (result.IsFailed) {
            Logger.LogWarning($"Logon failed for Merchant {cfg.MerchantName}");
            return ResultHelpers.CreateFailure(result);
        }

        return Result.Success();
    }

    public async Task<Result<List<Product>>> GetProductList(MerchantConfig cfg,
                                                            TokenResponse token,
                                                            CancellationToken cancellationToken) {
        List<Product> products = new();

        Guid estateId = cfg.EstateId;
        Guid merchantId = cfg.MerchantId;

        String requestUri = this.BuildRequestUrl($"api/merchants/contracts?applicationVersion={cfg.ApplicationVersion}");

        Logger.LogInformation("About to request merchant contracts");
        Logger.LogDebug($"Merchant Contract Request details:  Estate Id {estateId} Merchant Id {merchantId} Access Token {token.AccessToken}");

        Result<List<ContractResponseX>?>? result = await this.Get<List<ContractResponseX>?>(requestUri, token.AccessToken, cancellationToken);

        // Process the response
        if (result.IsFailed) {
            Logger.LogInformation($"GetMerchantContracts failed {result.Message}");
            return ResultHelpers.CreateFailure(result);
        }
        
        Logger.LogInformation($"{result.Data.Count} for merchant requested successfully");

        foreach (ContractResponseX contractResponse in result.Data) {
            foreach (ContractProductX contractResponseProduct in contractResponse.Products) {
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

        return Result.Success(products);
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

    public async Task<Result<Decimal>> GetBalance(MerchantConfig cfg,
                                          TokenResponse token,
                                          CancellationToken cancellationToken) {
        Result<MerchantBalanceResponse>? result = await this.TransactionProcessorClient.GetMerchantBalance(token.AccessToken, cfg.EstateId, cfg.MerchantId, cancellationToken);
        
        if (result.IsFailed) {
            Logger.LogWarning($"Error retrieving merchant balance for merchant {cfg.MerchantName}");
            return ResultHelpers.CreateFailure(result);
        }
        
        return Result.Success(result.Data.Balance);
    }

    public async Task<Result<SaleResponse>> SendSale(MerchantConfig cfg,
                                             TokenResponse token,
                                             Product product,
                                             Decimal value,
                                             Int32 transactionNumber,
                                             CancellationToken cancellationToken) {


        Result<List<SaleTransactionRequestMessage>> requests = product.ProductType switch {
            ProductType.MobileTopup => BuildMobileSaleTransactionRequestMessage(cfg, product, value, transactionNumber),
            ProductType.Voucher => BuildVoucherTransactionRequestMessage(cfg, product, value, transactionNumber),
            ProductType.BillPayment => await BuildBillPaymentTransactionRequestMessages(cfg, product, value, transactionNumber),
            _ => throw new NotImplementedException($"Product Type {product.ProductType} not implemented")
        };

        if (requests.IsFailed) {
            Logger.LogWarning($"Error building transaction request for merchant {cfg.MerchantName} product {product.Name}");
            return ResultHelpers.CreateFailure(requests);
        }

        Boolean approved = false;
        foreach (SaleTransactionRequestMessage saleTransactionRequestMessage in requests.Data) {
            Result<SaleTransactionResponseMessage> response = await this.SendTransactionRequest<SaleTransactionRequestMessage, SaleTransactionResponseMessage>(saleTransactionRequestMessage, "api/saletransactions", token, cancellationToken);
            if (response.IsSuccess == false) {
                // Exit the loop on failure
                approved = false;
                break;
            }
            approved = response.Data.ResponseCode == "0000";
        }

        return  Result.Success(new SaleResponse(approved));
    }

    public async Task<Result> SendDeposit(MerchantConfig cfg,
                                  TokenResponse token,
                                  Decimal amount,
                                  CancellationToken cancellationToken) {
        var result = await this.TransactionProcessorClient.MakeMerchantDeposit(token.AccessToken, cfg.EstateId, cfg.MerchantId, new MakeMerchantDepositRequest {
            Amount = amount,
            DepositDateTime = DateTime.Now,
            Reference = $"AutoDeposit{DateTime.Now:yyyy-MM-dd}"
        }, cancellationToken);

        if (result.IsFailed) {
            Logger.LogWarning($"Error performing deposit for merchant {cfg.MerchantName}");
            return ResultHelpers.CreateFailure(result);
        }
        return Result.Success();

    }

    public async Task<Result> SendReconciliation(MerchantConfig cfg,
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

        if (result.IsFailed) {
            Logger.LogWarning($"Error during reconciliation for merchant {cfg.MerchantName}");
            return ResultHelpers.CreateFailure(result);
        }
        return Result.Success();
    }

    private async Task<Result<List<SaleTransactionRequestMessage>>> BuildBillPaymentTransactionRequestMessages(MerchantConfig cfg,
                                                                                           Product product,
                                                                                           Decimal value,
                                                                                           Int32 transactionNumber) {
        // We need data setup at test host here for both post pay and pre pay bill payment products
        Result<(String accountNumber, String accountName, String mobileNumber)> extraDetails = product.ProductSubType switch {
            ProductSubType.BillPaymentPostPay => await this.CreateBillPaymentBill(value, CancellationToken.None),
            ProductSubType.BillPaymentPrePay => await this.CreateBillPaymentMeter(CancellationToken.None),
        };

        if (extraDetails.IsFailed) {
            return ResultHelpers.CreateFailure(extraDetails);
        }
        
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
            getTransactionRequestMessage.AdditionalRequestMetadata = new Dictionary<String, String> { { "CustomerAccountNumber", extraDetails.Data.accountNumber }, { "PataPawaPostPaidMessageType", "VerifyAccount" } };

        }
        else if (product.ProductSubType == ProductSubType.BillPaymentPrePay) {
            getTransactionRequestMessage.AdditionalRequestMetadata = new Dictionary<String, String> { { "MeterNumber", extraDetails.Data.accountNumber }, { "PataPawaPrePayMessageType", "meter" } };
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
                { "CustomerAccountNumber", extraDetails.Data.accountNumber },
                { "CustomerName", extraDetails.Data.accountName },
                { "MobileNumber", extraDetails.Data.mobileNumber },
                { "Amount", value.ToString() },
                { "PataPawaPostPaidMessageType", "ProcessBill" }
            };
        }
        else {
            paymentRequestMessage.AdditionalRequestMetadata = new Dictionary<String, String> {
                { "MeterNumber", extraDetails.Data.accountNumber  }, 
                { "CustomerName", extraDetails.Data.accountName }, 
                { "PataPawaPrePayMessageType", "vend" }, 
                { "Amount", value.ToString() },
            };
        }
        requestMessages.Add(paymentRequestMessage);
        
        return Result.Success(requestMessages);
    }
    
    private Result<List<SaleTransactionRequestMessage>> BuildMobileSaleTransactionRequestMessage(MerchantConfig cfg,
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
        return Result.Success(requestMessages);
    }

    private Result<List<SaleTransactionRequestMessage>> BuildVoucherTransactionRequestMessage(MerchantConfig cfg,
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
        return Result.Success(requestMessages);
    }

    private async Task<Result<TResponse>> SendTransactionRequest<TRequest, TResponse>(TRequest request,
                                                                                      String route,
                                                                                      TokenResponse tokenResponse,
                                                                                      CancellationToken cancellationToken) {
        String requestUri = this.BuildRequestUrl(route);
        try {
            Result<TResponse>? result = await this.Post<TRequest, TResponse>(requestUri, request, tokenResponse.AccessToken, cancellationToken);
            
            return Result.Success(result.Data);
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
    private async Task<Result<(String accountNumber, String accountName, String? mobileNumber)>> CreateBillPaymentBill(Decimal billAmount, CancellationToken cancellationToken)
    {
        Int32 accountNumber = this._rng.Next(1, 100000);
        String baseAddress = this.BaseAddressResolver("TestHost");
        
        var body = new
        {
            due_date = DateTime.Now.AddDays(1),
            amount = billAmount,
            account_number = accountNumber,
            account_name = $"Test Account {accountNumber}"
        };
        var content = new StringContent(StringSerialiser.Serialise(body), Encoding.UTF8, "application/json");

        var result = await this.Post($"{baseAddress}/api/developer/patapawapostpay/createbill", content, cancellationToken);

        if (result.IsFailed) {
            return ResultHelpers.CreateFailure(result);
        }

        return Result.Success((body.account_number.ToString(), body.account_name, "07777777705"));
    
    }

    private async Task<Result<(String accountNumber, String accountName, String mobileNumber)>> CreateBillPaymentMeter(CancellationToken cancellationToken)
    {
            Int32 meterNumber = this._rng.Next(1, 100000);
            String baseAddress = this.BaseAddressResolver("TestHost");
            var body = new
            {
                due_date = DateTime.Now.AddDays(1),
                meter_number = meterNumber,
                customer_name = $"Customer {meterNumber}"
            };
            var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        
            var result = await this.Post($"{baseAddress}/api/developer/patapawaprepay/createmeter", content, cancellationToken);

            if (result.IsFailed) {
                return ResultHelpers.CreateFailure(result);
            }

            return Result.Success<(String, String, String?)>((body.meter_number.ToString(), body.customer_name, null));
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

[ExcludeFromCodeCoverage]
public class MerchantResponse
{
    [JsonProperty("estate_id")]
    public Guid EstateId { get; set; }

    [JsonProperty("merchant_id")]
    public Guid MerchantId { get; set; }

    [JsonProperty("merchant_name")]
    public string MerchantName { get; set; }

    [JsonProperty("opening_hours")]
    public Dictionary<DayOfWeek, OpeningHoursResponse> OpeningHours { get; set; }
}

public class OpeningHoursResponse
{
    [JsonProperty("opening")]
    public string Opening { get; set; }

    [JsonProperty("closing")]
    public string Closing { get; set; }
}
