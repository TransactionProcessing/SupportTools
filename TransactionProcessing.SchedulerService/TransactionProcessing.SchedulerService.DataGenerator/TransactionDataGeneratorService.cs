using System.Net.Http.Headers;
using System.Text;
using EstateManagement.Client;
using EstateManagement.DataTransferObjects.Requests.Merchant;
using EstateManagement.DataTransferObjects.Responses.Contract;
using EstateManagement.DataTransferObjects.Responses.Estate;
using EstateManagement.DataTransferObjects.Responses.Merchant;
using Newtonsoft.Json;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Results;
using SimpleResults;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects;

namespace TransactionProcessing.SchedulerService.DataGenerator;

public class TransactionDataGeneratorService : ITransactionDataGeneratorService {
    private readonly ISecurityServiceClient SecurityServiceClient;
    private readonly IEstateClient EstateClient;
    private readonly ITransactionProcessorClient TransactionProcessorClient;
    private readonly String EstateManagementApi;
    private readonly String FileProcessorApi;

    public TransactionDataGeneratorService(ISecurityServiceClient securityServiceClient,
                                           IEstateClient estateClient,
                                           ITransactionProcessorClient transactionProcessorClient,
                                           String estateManagementApi,
                                           String fileProcessorApi,
                                           String testHostApi,
                                           String clientId,
                                           String clientSecret,
                                           RunningMode runningMode = RunningMode.WhatIf) {
        this.SecurityServiceClient = securityServiceClient;
        this.EstateClient = estateClient;
        this.TransactionProcessorClient = transactionProcessorClient;
        this.EstateManagementApi = estateManagementApi;
        this.FileProcessorApi = fileProcessorApi;
        this.TestHostApi = testHostApi;
        this.ClientId = clientId;
        this.ClientSecret = clientSecret;
        this.RunningMode = runningMode;
    }

    public Result<List<DateTime>> GenerateDateRange(DateTime startDate,
                                                    DateTime endDate) {
        this.WriteTrace($"Generating date range between {startDate:dd-MM-yyyy} and {endDate:dd-MM-yyyy}");
        List<DateTime> dateRange = new List<DateTime>();

        if (endDate.Subtract(startDate).Days == 0) {
            dateRange.Add(startDate);
        }
        else {
            while (endDate.Subtract(startDate).Days >= 0) {
                dateRange.Add(startDate);
                startDate = startDate.AddDays(1);
            }
        }

        this.WriteTrace($"{dateRange.Count} dates generated");

        return Result.Success(dateRange);
    }

    public async Task<Result<List<ContractResponse>>> GetEstateContracts(Guid estateId,
                                                                         CancellationToken cancellationToken) {
        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);

        this.WriteTrace($"About to get contracts for Estate Id [{estateId}]");
        Result<List<ContractResponse>>? result = await this.EstateClient.GetContracts(tokenResult.Data, estateId, cancellationToken);
        if (result.IsFailed) {
            this.WriteError("Error getting contracts");
            this.WriteError(result.Message);
            return result;
        }

        this.WriteTrace($"{result.Data.Count} contracts returned for Estate");

        return result;
    }

    public async Task<Result<List<ContractResponse>>> GetMerchantContracts(MerchantResponse merchant,
                                                                           CancellationToken cancellationToken) {
        // TODO: not sure if we even need this
        if (merchant == null) {
            this.WriteError("Merchant is null");
            return Result.Invalid("Merchant is null");
        }

        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);

        this.WriteTrace($"About to get contracts for Merchant [{merchant.MerchantId}] Estate Id [{merchant.EstateId}]");
        Result<List<ContractResponse>>? result = await this.EstateClient.GetMerchantContracts(tokenResult.Data, merchant.EstateId, merchant.MerchantId, cancellationToken);
        if (result.IsFailed) {
            this.WriteError("Error getting merchant contracts");
            this.WriteError(result.Message);
            return result;
        }

        this.WriteTrace($"{result.Data.Count} contracts returned for Merchant");

        return result;
    }

    public async Task<Result<List<MerchantResponse>>> GetMerchants(Guid estateId,
                                                                   CancellationToken cancellationToken) {
        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);
        this.WriteTrace($"About to get merchants for Estate Id [{estateId}]");
        Result<List<MerchantResponse>>? result = await this.EstateClient.GetMerchants(tokenResult.Data, estateId, cancellationToken);
        if (result.IsFailed) {
            this.WriteError("Error getting merchants");
            this.WriteError(result.Message);
            return result;
        }

        this.WriteTrace($"{result.Data.Count} merchants returned for Estate");

        return result;
    }

    public async Task<Result<SerialisedMessage>> PerformMerchantLogon(DateTime dateTime,
                                                                      MerchantResponse merchant,
                                                                      CancellationToken cancellationToken) {
        if (merchant == null) {
            this.WriteError("Merchant is null");
            return Result.Invalid("Merchant is null");
        }

        // Build logon message
        String deviceIdentifier = merchant.Devices.Single().Value;
        LogonTransactionRequest logonTransactionRequest = new LogonTransactionRequest {
            DeviceIdentifier = deviceIdentifier,
            EstateId = merchant.EstateId,
            MerchantId = merchant.MerchantId,
            TransactionDateTime = dateTime.Date.AddMinutes(1),
            TransactionNumber = "1",
            TransactionType = "Logon"
        };


        this.WriteTrace($"About to send Logon Transaction for Merchant [{merchant.MerchantName}]");

        Result<SerialisedMessage> result = await this.SendLogonTransaction(merchant, logonTransactionRequest, cancellationToken);
        if (result.IsFailed) {
            this.WriteError("Error getting merchants");
            this.WriteError(result.Message);
            return Result.Failure(result.Message);
        }

        this.WriteTrace($"Logon Transaction for Merchant [{merchant.MerchantName}] sent");
        return result;
    }

    public async Task<Result> PerformSettlement(DateTime dateTime,
                                                Guid estateId,
                                                CancellationToken cancellationToken) {
        List<MerchantResponse> merchants = await this.GetMerchants(estateId, cancellationToken);
        List<String> errors = new List<String>();
        foreach (MerchantResponse merchantResponse in merchants) {
            this.WriteTrace($"About to send Process Settlement Request for Date [{dateTime:dd-MM-yyyy}] and Estate [{estateId}] and Merchant [{merchantResponse.MerchantId}]");
            Result result = await this.SendProcessSettlementRequest(dateTime, estateId, merchantResponse.MerchantId, cancellationToken);
            if (result.IsFailed) {
                this.WriteError($"Error sending Process Settlement Request for Date [{dateTime:dd-MM-yyyy}] and Estate [{estateId}]");
                this.WriteError(result.Message);
                errors.Add($"Error sending Process Settlement Request for Date [{dateTime:dd-MM-yyyy}] and Estate [{estateId}]");
            }

            this.WriteTrace($"Process Settlement Request sent for Date [{dateTime:dd-MM-yyyy}] and Estate [{estateId}] and Merchant [{merchantResponse.MerchantId}]");
        }

        if (errors.Any()) {
            return Result.Failure(String.Join(",", errors));
        }
        return Result.Success();
    }

    public async Task<Result> SendSales(DateTime dateTime,
                                        MerchantResponse merchant,
                                        ContractResponse contract,
                                        Int32 numberOfSales,
                                        CancellationToken cancellationToken) {
        List<SaleTransactionRequest> salesToSend = new List<SaleTransactionRequest>();
        Decimal depositAmount = 0;
        (Int32 accountNumber, String accountName, Decimal balance) billDetails = default;
        (Int32 meterNumber, String customerName, Decimal amount) meterDetails = default;

        foreach (ContractProduct contractProduct in contract.Products)
        {
            this.WriteTrace($"product [{contractProduct.DisplayText}]");

            List<(SaleTransactionRequest request, Decimal amount)> saleRequests = null;
            // Get a number of sales to be sent
            if (numberOfSales == 0)
            {
                numberOfSales = this.r.Next(5, 15);
            }

            for (Int32 i = 1; i <= numberOfSales; i++)
            {
                ProductSubType productSubType = this.GetProductSubType(contract.OperatorName);

                if (productSubType == ProductSubType.BillPaymentPostPay)
                {
                    // Create a bill for this sale
                    billDetails = await this.CreateBillPaymentBill(contract.OperatorName, contractProduct, cancellationToken);
                }

                if (productSubType == ProductSubType.BillPaymentPrePay)
                {
                    // Create a meter
                    meterDetails = await this.CreateBillPaymentMeter(contract.OperatorName, contractProduct, cancellationToken);
                }

                saleRequests = productSubType switch
                {
                    ProductSubType.MobileTopup => this.BuildMobileTopupSaleRequests(dateTime, merchant, contract, contractProduct),
                    ProductSubType.Voucher => this.BuildVoucherSaleRequests(dateTime, merchant, contract, contractProduct),
                    ProductSubType.BillPaymentPostPay => this.BuildPataPawaPostPayBillPaymentSaleRequests(dateTime, merchant, contract, contractProduct, billDetails),
                    ProductSubType.BillPaymentPrePay => this.BuildPataPawaPrePayBillPaymentSaleRequests(dateTime, merchant, contract, contractProduct, meterDetails),
                    _ => throw new Exception($"Product Sub Type [{productSubType}] not yet supported")
                };

                // Add the value of the sale to the deposit amount
                Boolean addToDeposit = i switch
                {
                    _ when i == numberOfSales => false,
                    _ => true
                };

                if (addToDeposit)
                {
                    depositAmount += saleRequests.Sum(sr => sr.amount);
                }

                salesToSend.AddRange(saleRequests.Select(s => s.request));
            }
        }

        // Build up a deposit (minus the last sale amount)
        MakeMerchantDepositRequest depositRequest = this.CreateMerchantDepositRequest(depositAmount, dateTime);

        // Send the deposit
        Result result = await this.SendMerchantDepositRequest(merchant, depositRequest, cancellationToken);

        if (result.IsFailed)
        {
            return result;
        }

        Int32 salesSent = 0;
        IOrderedEnumerable<SaleTransactionRequest> orderedSales = salesToSend.OrderBy(s => s.TransactionDateTime);
        // Send the sales to the host
        foreach (SaleTransactionRequest sale in orderedSales)
        {
            sale.TransactionNumber = this.GetTransactionNumber().ToString();
            Result<SerialisedMessage> saleResult = await this.SendSaleTransaction(merchant, sale, cancellationToken);
            if (saleResult.IsFailed)
            {
                salesSent++;
            }
        }

        if (salesSent == 0)
        {
            // All sales failed
            return Result.Failure("All sales have failed");
        }

        return Result.Success();
    }

    public async Task<Result> SendUploadFile(DateTime dateTime,
                                             ContractResponse contract,
                                             MerchantResponse merchant,
                                             Guid userId,
                                             CancellationToken cancellationToken) {
        Int32 numberOfSales = this.r.Next(5, 15);
        var builtFileResult = await this.BuildUploadFile(dateTime, merchant, contract, numberOfSales, cancellationToken);

        if (builtFileResult.IsFailed) {
            return ResultHelpers.CreateFailure(builtFileResult);
        }

        Decimal fileValue = builtFileResult.Data.Item1;
        UploadFile uploadFile = builtFileResult.Data.Item2;

        if (this.RunningMode == RunningMode.WhatIf) {
            this.WriteTrace($"Send File for Merchant [{merchant.MerchantName}] Contract [{contract.OperatorName}] Lines [{uploadFile.GetNumberOfLines()}]");
            return Result.Success();
        }

        // Build up a deposit (minus the last sale amount)
        MakeMerchantDepositRequest depositRequest = this.CreateMerchantDepositRequest(fileValue, dateTime);

        // Send the deposit
        var result = await this.SendMerchantDepositRequest(merchant, depositRequest, cancellationToken);

        if (result.IsFailed)
        {
            return result;
        }

        Result fileSendResult = await this.UploadFile(uploadFile, uploadFile.UserId, dateTime, cancellationToken);

        if (fileSendResult.IsFailed)
        {
            return fileSendResult;
        }

        return Result.Success();
    }

    private async Task<Result> UploadFile(UploadFile uploadFile, Guid userId, DateTime fileDateTime, CancellationToken cancellationToken)
    {
        var formData = new MultipartFormDataContent();
        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);

        var fileContent = new ByteArrayContent(uploadFile.GetFileContents());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
        formData.Add(fileContent, "file", $"bulkfile{fileDateTime:yyyy-MM-dd}");
        formData.Add(new StringContent(uploadFile.EstateId.ToString()), "request.EstateId");
        formData.Add(new StringContent(uploadFile.MerchantId.ToString()), "request.MerchantId");
        formData.Add(new StringContent(uploadFile.FileProfileId.ToString()), "request.FileProfileId");
        formData.Add(new StringContent(userId.ToString()), "request.UserId");
        formData.Add(new StringContent(fileDateTime.ToString("yyyy-MM-dd HH:mm:ss")), "request.UploadDateTime");

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{this.FileProcessorApi}/api/files")
        {
            Content = formData,
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", tokenResult.Data);
        HttpResponseMessage response = null;

        try
        {

            this.WriteTrace($"About to upload file for Merchant [{uploadFile.MerchantId}]");
            using (HttpClient client = new HttpClient())
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            this.WriteTrace($"File uploaded for Merchant [{uploadFile.MerchantId}]");
            return Result.Success();
        }
        catch (Exception ex)
        {
            this.WriteError($"Error uploading file for merchant [{uploadFile.MerchantId}]");
            this.WriteError(ex);
            return Result.Failure(ex.Message);
        }
    }

    private static async Task<Guid> GetFileProfileIdFromOperator(String operatorName, CancellationToken cancellationToken)
    {
        // TODO: get this profile list from API

        switch (operatorName)
        {
            case "Safaricom":
                return Guid.Parse("B2A59ABF-293D-4A6B-B81B-7007503C3476");
            case "Voucher":
                return Guid.Parse("8806EDBC-3ED6-406B-9E5F-A9078356BE99");
            default:
                return Guid.Empty;
        }
    }

    private async Task<Result<(Decimal, UploadFile)>> BuildUploadFile(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, Int32 numberOfLines, CancellationToken cancellationToken)
    {
        ProductType productType = this.GetProductType(contract.OperatorName);
        Guid fileProfileId = await TransactionDataGeneratorService.GetFileProfileIdFromOperator(contract.OperatorName, cancellationToken);
        Guid variableProductId = contract.Products.Single(p => p.Value == null).ProductId;
        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);

        EstateResponse estate = await this.EstateClient.GetEstate(tokenResult.Data, merchant.EstateId, cancellationToken);
        Guid userId = estate.SecurityUsers.First().SecurityUserId;
        Decimal depositAmount = 0;
        if (productType == ProductType.MobileTopup)
        {
            MobileTopupUploadFile mobileTopupUploadFile = new MobileTopupUploadFile(contract.EstateId, merchant.MerchantId, contract.ContractId, variableProductId, fileProfileId, userId);
            mobileTopupUploadFile.AddHeader(dateTime);

            for (Int32 i = 1; i <= numberOfLines; i++)
            {
                Decimal amount = GetAmount(this.r);
                String mobileNumber = String.Format($"077777777{i.ToString().PadLeft(2, '0')}");
                mobileTopupUploadFile.AddLine(amount, mobileNumber);

                // Add the value of the sale to the deposit amount
                Boolean addToDeposit = i switch
                {
                    _ when i == numberOfLines => false,
                    _ => true
                };

                if (addToDeposit)
                {
                    depositAmount += amount;
                }
            }

            mobileTopupUploadFile.AddTrailer();
            return  Result.Success<(Decimal, UploadFile)>((depositAmount, mobileTopupUploadFile));
        }

        if (productType == ProductType.Voucher)
        {
            VoucherTopupUploadFile voucherTopupUploadFile = new VoucherTopupUploadFile(contract.EstateId, merchant.MerchantId, contract.ContractId, variableProductId, fileProfileId, userId);
            voucherTopupUploadFile.AddHeader(dateTime);

            for (Int32 i = 1; i <= numberOfLines; i++)
            {
                Decimal amount = GetAmount(this.r);
                String mobileNumber = String.Format($"077777777{i.ToString().PadLeft(2, '0')}");
                String emailAddress = String.Format($"testrecipient{i.ToString().PadLeft(2, '0')}@testing.com");
                String recipient = mobileNumber;
                if (i % 2 == 0)
                {
                    recipient = emailAddress;
                }

                voucherTopupUploadFile.AddLine(amount, recipient, contract.Description.Replace("Contract", ""));

                // Add the value of the sale to the deposit amount
                Boolean addToDeposit = i switch
                {
                    _ when i == numberOfLines => false,
                    _ => true
                };

                if (addToDeposit)
                {
                    depositAmount += amount;
                }
            }



            voucherTopupUploadFile.AddTrailer();
            return Result.Success<(Decimal, UploadFile)>((depositAmount, voucherTopupUploadFile));
        }

        // Not supported product type for file upload
        return Result.Invalid($"Product Type {productType} not supported");
    }


    private String TestHostApi;

    public static Decimal GetAmount(Random r, ContractProduct product = null)
    {
        return product switch
        {
            null => r.Next(9, 250),
            _ when product.Value.HasValue == false => r.Next(9, 250),
            _ => product.Value.Value
        };
    }
    private Random r = new Random();

    private async Task<(Int32 accountNumber, String accountName, Decimal balance)> CreateBillPaymentBill(String contractOperatorName, ContractProduct contractProduct, CancellationToken cancellationToken)
    {
        if (contractOperatorName == "PataPawa PostPay")
        {
            Int32 accountNumber = this.r.Next(1, 100000);
            Decimal amount = GetAmount(this.r, contractProduct);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{this.TestHostApi}/api/developer/patapawapostpay/createbill");
            var body = new
            {
                due_date = DateTime.Now.AddDays(1),
                amount = amount,
                account_number = accountNumber,
                account_name = "Test Account 1"
            };
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            if (this.RunningMode == RunningMode.WhatIf)
            {
                this.WriteTrace($"Bill Created Account [{body.account_number}] Balance [{body.amount}]");
                return (body.account_number, body.account_name, body.amount);
            }

            using (HttpClient client = new HttpClient())
            {
                await client.SendAsync(request, cancellationToken);
            }

            return (body.account_number, body.account_name, body.amount);
        }

        return default;
    }

    private async Task<(Int32 meterNumber, String CustomerName, Decimal balance)> CreateBillPaymentMeter(String contractOperatorName, ContractProduct contractProduct, CancellationToken cancellationToken)
    {
        if (contractOperatorName == "PataPawa PrePay")
        {
            Int32 meterNumber = this.r.Next(1, 100000);
            Decimal amount = GetAmount(this.r, contractProduct);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{this.TestHostApi}/api/developer/patapawaprepay/createmeter");
            var body = new
            {
                due_date = DateTime.Now.AddDays(1),
                meter_number = meterNumber,
                customer_name = $"Customer {meterNumber}"
            };
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            if (this.RunningMode == RunningMode.WhatIf)
            {
                this.WriteTrace($"Meter Created Customer [{body.customer_name}] Meter # [{body.meter_number}]");
                return (body.meter_number, body.customer_name, amount);
            }

            using (HttpClient client = new HttpClient())
            {
                await client.SendAsync(request, cancellationToken);
            }

            return (body.meter_number, body.customer_name, amount);
        }

        return default;
    }

    private Int32 TransactionNumber;
    private Int32 GetTransactionNumber()
    {
        this.TransactionNumber++;
        return this.TransactionNumber;
    }

    private async Task<Result> SendMerchantDepositRequest(MerchantResponse merchant,
                                                          MakeMerchantDepositRequest request,
                                                          CancellationToken cancellationToken) {
        if (this.RunningMode == RunningMode.WhatIf) {
            this.WriteTrace($"Make Deposit [{request.Amount}] for Merchant [{merchant.MerchantName}]");
            return Result.Success();
        }

        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);

        this.WriteTrace($"About to make Deposit [{request.Amount}] for Merchant [{merchant.MerchantName}]");
        Result result = await this.EstateClient.MakeMerchantDeposit(tokenResult.Data, merchant.EstateId, merchant.MerchantId, request, cancellationToken);
        if (result.IsFailed) {
            this.WriteError($"Error making merchant deposit for merchant [{merchant.MerchantName}]");
            this.WriteError(result.Message);
            return result;
        }

        this.WriteTrace($"Deposit [{request.Amount}] made for Merchant [{merchant.MerchantName}]");
        return Result.Success();
    }

    private MakeMerchantDepositRequest CreateMerchantDepositRequest(Decimal depositAmount, DateTime dateTime)
    {
        // TODO: generate a reference

        MakeMerchantDepositRequest request = new MakeMerchantDepositRequest
        {
            Amount = depositAmount,
            DepositDateTime = dateTime,
            Reference = "ABC"
        };
        return request;
    }

    private async Task<Result> SendProcessSettlementRequest(DateTime dateTime, Guid estateId, Guid merchantId, CancellationToken cancellationToken)
    {
        if (this.RunningMode == RunningMode.WhatIf)
        {
            this.WriteTrace($"Sending Settlement for Date [{dateTime.Date}] Estate [{estateId}]");
            return Result.Success();
        }

        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);
        return await this.TransactionProcessorClient.ProcessSettlement(tokenResult.Data, dateTime, estateId, merchantId, cancellationToken);
    }

    public async Task<Result<MerchantResponse>> GetMerchant(Guid estateId,
                                                            Guid merchantId,
                                                            CancellationToken cancellationToken) {
        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);
        this.WriteTrace($"About to get merchant {merchantId} for Estate Id [{estateId}]");
        Result<MerchantResponse>? result = await this.EstateClient.GetMerchant(tokenResult.Data, estateId, merchantId, cancellationToken);
        if (result.IsFailed) {
            this.WriteError("Error getting merchant");
            this.WriteError(result.Message);
            return result;
        }

        this.WriteTrace($"Merchant retrieved successfully");

        return result;
    }

    public async Task<Result> GenerateMerchantStatement(Guid estateId,
                                                        Guid merchantId,
                                                        DateTime statementDateTime,
                                                        CancellationToken cancellationToken) {
        try
        {
            this.WriteTrace($"About to send Generate Merchant Statement Request for Estate [{estateId}] and Merchant [{merchantId}] StatementDate [{statementDateTime:dd-MM-yyyy}]");

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{this.EstateManagementApi}/api/estates/{estateId}/merchants/{merchantId}/statements");
            var body = new
            {
                merchant_statement_date = statementDateTime,
            };
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            if (this.RunningMode == RunningMode.WhatIf)
            {
                this.WriteTrace($"Merchant Statement Generated for merchant [{merchantId}] Statement Date [{body.merchant_statement_date}]");
            }
            Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Data);

            using (HttpClient client = new HttpClient())
            {
                await client.SendAsync(request, cancellationToken);
            }

            this.WriteTrace($"Generate Merchant Statement Request sent for Estate [{estateId}] and Merchant [{merchantId}] StatementDate [{statementDateTime:dd-MM-yyyy}]");
            return Result.Success();

        }
        catch (Exception ex)
        {
            this.WriteError($"Error sending Generate Merchant Statement Request for Date [{statementDateTime:dd-MM-yyyy}] and Estate [{estateId}] and Merchant [{merchantId}]");
            this.WriteError(ex);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> MakeFloatDeposit(DateTime dateTime,
                                               Guid estateId,
                                               Guid contractId,
                                               Guid contractProductId,
                                               Decimal amount,
                                               CancellationToken cancellationToken) {
        Guid floatId = IdGenerationService.GenerateFloatAggregateId(estateId, contractId, contractProductId);

        Decimal costPrice = amount * 0.85m;

        RecordFloatCreditPurchaseRequest request = new RecordFloatCreditPurchaseRequest { FloatId = floatId, CreditAmount = amount, CostPrice = costPrice, PurchaseDateTime = dateTime };

        return await this.SendFloatDepositRequest(estateId, request, cancellationToken);
    }

    private async Task<Result> SendFloatDepositRequest(Guid estateId,
                                                       RecordFloatCreditPurchaseRequest request,
                                                       CancellationToken cancellationToken) {
        if (this.RunningMode == RunningMode.WhatIf) {
            this.WriteTrace($"Send Float Credit Request for {request.CreditAmount}");
            return Result.Success();
        }

        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);
        this.WriteTrace($"About to Float Credit Request");
        Result? result = await this.TransactionProcessorClient.RecordFloatCreditPurchase(tokenResult.Data, estateId, request, cancellationToken);
        if (result.IsFailed) {
            this.WriteError($"Error Float Credit Request");
            this.WriteError(result.Message);
            return result;
        }

        this.WriteTrace($"Float Credit Request sent");

        return Result.Success();
    }

    private async Task<Result<SerialisedMessage>> SendLogonTransaction(MerchantResponse merchant,
                                                                       LogonTransactionRequest request,
                                                                       CancellationToken cancellationToken) {
        if (this.RunningMode == RunningMode.WhatIf) {
            this.WriteTrace($"Send Logon Transaction for Merchant [{merchant.MerchantName}]");
            return Result.Success();
        }

        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);
        SerialisedMessage requestSerialisedMessage = request.CreateSerialisedMessage();

        return await this.TransactionProcessorClient.PerformTransaction(tokenResult.Data, requestSerialisedMessage, CancellationToken.None);
    }

    private async Task<Result<SerialisedMessage>> SendSaleTransaction(MerchantResponse merchant,
                                                                      SaleTransactionRequest request,
                                                                      CancellationToken cancellationToken) {
        if (this.RunningMode == RunningMode.WhatIf) {
            this.WriteTrace($"Send Sale for Merchant [{merchant.MerchantName}] - {request.TransactionNumber} - {request.OperatorId} - {request.GetAmount()}");
            return Result.Success();
        }

        Result<String> tokenResult = await this.GetAuthToken(cancellationToken);
        if (tokenResult.IsFailed)
            return ResultHelpers.CreateFailure(tokenResult);

        SerialisedMessage requestSerialisedMessage = request.CreateSerialisedMessage();
        SerialisedMessage responseSerialisedMessage = null;

        this.WriteTrace($"About to Send sale for Merchant [{merchant.MerchantName}]");
        Result<SerialisedMessage> result = new Result<SerialisedMessage>();
        for (int i = 0; i < 3; i++) {
            try {
                result = await this.TransactionProcessorClient.PerformTransaction(tokenResult.Data, requestSerialisedMessage, CancellationToken.None);
                break;
            }
            catch (TaskCanceledException e) {
                this.WriteError(e.Message);
            }
        }

        if (result.IsFailed)
            return result;

        this.WriteTrace($"Sale Transaction for Merchant [{merchant.MerchantName}] sent");

        return result;
    }

    public event TraceHandler? TraceGenerated;
    private TokenResponse TokenResponse;
    private readonly RunningMode RunningMode;
    private readonly String ClientId;

    private readonly String ClientSecret;
    private readonly String ClientToken;

    private async Task<Result<String>> GetAuthToken(CancellationToken cancellationToken) {
        this.WriteTrace($"About to get auth token");

        if (this.TokenResponse == null) {
            this.WriteTrace($"TokenResponse was null");
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(this.ClientId, this.ClientSecret, cancellationToken);

            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            this.TokenResponse = tokenResult.Data;
        }

        if (this.TokenResponse.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2)) {
            this.WriteTrace($"TokenResponse was expired");
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(this.ClientId, this.ClientSecret, cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            this.TokenResponse = tokenResult.Data;
        }

        this.WriteTrace($"Auth token retrieved");

        return Result.Success<String>(this.TokenResponse.AccessToken);
    }

    private void WriteMessage(String message,
                              TraceEventArgs.Level traceLevel) {
        if (this.TraceGenerated != null) {
            TraceEventArgs args = new() { TraceLevel = traceLevel, Message = message };

            this.TraceGenerated.Invoke(args);
        }
    }

    private void WriteTrace(String message) => this.WriteMessage(message, TraceEventArgs.Level.Trace);
    private void WriteWarning(String message) => this.WriteMessage(message, TraceEventArgs.Level.Warning);
    private void WriteError(String message) => this.WriteMessage(message, TraceEventArgs.Level.Error);
    private void WriteError(Exception ex) => this.WriteMessage(ex.ToString(), TraceEventArgs.Level.Error);

    private List<(SaleTransactionRequest request, Decimal amount)> BuildVoucherSaleRequests(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, ContractProduct contractProduct)
    {
        Decimal amount = GetAmount(this.r, contractProduct);

        Dictionary<String, String> requestMetaData = new Dictionary<String, String>{
            { "Amount", amount.ToString() },
            { "RecipientMobile", "1234567890" }
        };

        List<(SaleTransactionRequest request, Decimal amount)> requests = new List<(SaleTransactionRequest request, Decimal amount)>();

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
            TransactionDateTime = GetTransactionDateTime(this.r, dateTime),
            OperatorId = contract.OperatorId,
            ProductId = contractProduct.ProductId
        };
        requests.Add((request, amount));

        return requests;
    }

    public static DateTime GetTransactionDateTime(Random r, DateTime dateTime)
    {

        if (dateTime.Hour != 0)
        {
            // Already have a time only change the seconds
            Int32 seconds = r.Next(0, 59);
            return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, seconds);
        }
        else
        {
            // Generate the time
            Int32 hours = r.Next(0, 23);
            Int32 minutes = r.Next(0, 59);
            Int32 seconds = r.Next(0, 59);

            return dateTime.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
        }
    }

    private List<(SaleTransactionRequest request, Decimal amount)> BuildMobileTopupSaleRequests(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, ContractProduct contractProduct)
    {
        Decimal amount = GetAmount(this.r, contractProduct);

        Dictionary<String, String> requestMetaData = new Dictionary<String, String>{
            { "Amount", amount.ToString() },
            { "CustomerAccountNumber", "1234567890" }
        };

        List<(SaleTransactionRequest request, Decimal amount)> requests = new List<(SaleTransactionRequest request, Decimal amount)>();

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
            TransactionDateTime = GetTransactionDateTime(this.r, dateTime),
            OperatorId = contract.OperatorId,
            ProductId = contractProduct.ProductId
        };
        requests.Add((request, amount));

        return requests;
    }

    private List<(SaleTransactionRequest request, Decimal amount)> BuildPataPawaPostPayBillPaymentSaleRequests(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, ContractProduct product, (Int32 accountNumber, String accountName, Decimal balance) billDetails)
    {
        List<(SaleTransactionRequest request, Decimal amount)> requests = new List<(SaleTransactionRequest request, Decimal amount)>();

        // Create the requests required
        String deviceIdentifier = merchant.Devices.Single().Value;

        // First request is Get Account
        Dictionary<String, String> getAccountRequestMetaData = new Dictionary<String, String>{
            { "CustomerAccountNumber", billDetails.accountNumber.ToString() },
            { "PataPawaPostPaidMessageType", "VerifyAccount" }
        };

        DateTime transactionDateTime = GetTransactionDateTime(this.r, dateTime);

        SaleTransactionRequest getAccountRequest = new SaleTransactionRequest
        {
            AdditionalTransactionMetadata = getAccountRequestMetaData,
            ContractId = contract.ContractId,
            CustomerEmailAddress = String.Empty,
            DeviceIdentifier = deviceIdentifier,
            MerchantId = merchant.MerchantId,
            EstateId = merchant.EstateId,
            TransactionType = "Sale",
            TransactionDateTime = transactionDateTime,
            OperatorId = contract.OperatorId,
            ProductId = product.ProductId
        };

        requests.Add((getAccountRequest, 0));

        // Second request is Make Payment
        Dictionary<String, String> makePaymentRequestMetaData = new Dictionary<String, String>{
            { "CustomerAccountNumber", billDetails.accountNumber.ToString() },
            { "CustomerName", billDetails.accountName },
            { "MobileNumber", "1234567890" },
            { "Amount", billDetails.balance.ToString() },
            { "PataPawaPostPaidMessageType", "ProcessBill" }
        };

        SaleTransactionRequest makePaymentRequest = new SaleTransactionRequest
        {
            AdditionalTransactionMetadata = makePaymentRequestMetaData,
            ContractId = contract.ContractId,
            CustomerEmailAddress = String.Empty,
            DeviceIdentifier = deviceIdentifier,
            MerchantId = merchant.MerchantId,
            EstateId = merchant.EstateId,
            TransactionType = "Sale",
            TransactionDateTime = transactionDateTime.AddSeconds(30),
            OperatorId = contract.OperatorId,
            ProductId = product.ProductId
        };

        requests.Add((makePaymentRequest, billDetails.balance));

        return requests;
    }

    private List<(SaleTransactionRequest request, Decimal amount)> BuildPataPawaPrePayBillPaymentSaleRequests(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, ContractProduct product, (Int32 meterNumber, String CustomerName, Decimal amount) meterDetails)
    {
        List<(SaleTransactionRequest request, Decimal amount)> requests = new List<(SaleTransactionRequest request, Decimal amount)>();

        // Create the requests required
        String deviceIdentifier = merchant.Devices.Single().Value;

        // First request is Get Account
        Dictionary<String, String> getAccountRequestMetaData = new Dictionary<String, String>{
            { "MeterNumber", meterDetails.meterNumber.ToString() },
            { "PataPawaPrePayMessageType", "meter" }
        };

        DateTime transactionDateTime = GetTransactionDateTime(this.r, dateTime);

        SaleTransactionRequest getAccountRequest = new SaleTransactionRequest
        {
            AdditionalTransactionMetadata = getAccountRequestMetaData,
            ContractId = contract.ContractId,
            CustomerEmailAddress = String.Empty,
            DeviceIdentifier = deviceIdentifier,
            MerchantId = merchant.MerchantId,
            EstateId = merchant.EstateId,
            TransactionType = "Sale",
            TransactionDateTime = transactionDateTime,
            OperatorId = contract.OperatorId,
            ProductId = product.ProductId
        };

        requests.Add((getAccountRequest, 0));

        // Second request is Make Payment
        Dictionary<String, String> makePaymentRequestMetaData = new Dictionary<String, String>{
            { "MeterNumber", meterDetails.meterNumber.ToString() },
            { "CustomerName", meterDetails.CustomerName },
            { "Amount", meterDetails.amount.ToString() },
            { "PataPawaPrePayMessageType", "vend" }
        };

        SaleTransactionRequest makePaymentRequest = new SaleTransactionRequest
        {
            AdditionalTransactionMetadata = makePaymentRequestMetaData,
            ContractId = contract.ContractId,
            CustomerEmailAddress = String.Empty,
            DeviceIdentifier = deviceIdentifier,
            MerchantId = merchant.MerchantId,
            EstateId = merchant.EstateId,
            TransactionType = "Sale",
            TransactionDateTime = transactionDateTime.AddSeconds(30),
            OperatorId = contract.OperatorId,
            ProductId = product.ProductId
        };

        requests.Add((makePaymentRequest, meterDetails.amount));

        return requests;
    }

    private ProductType GetProductType(String operatorName)
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
            case "PataPawa PostPay":
            case "PataPawa PrePay":
                productType = ProductType.BillPayment;
                break;
        }

        return productType;
    }

    private ProductSubType GetProductSubType(String operatorName)
    {
        ProductSubType productType = ProductSubType.NotSet;
        switch (operatorName)
        {
            case "Safaricom":
                productType = ProductSubType.MobileTopup;
                break;
            case "Voucher":
                productType = ProductSubType.Voucher;
                break;
            case "PataPawa PostPay":
                productType = ProductSubType.BillPaymentPostPay;
                break;
            case "PataPawa PrePay":
                productType = ProductSubType.BillPaymentPrePay;
                break;
        }

        return productType;
    }
}