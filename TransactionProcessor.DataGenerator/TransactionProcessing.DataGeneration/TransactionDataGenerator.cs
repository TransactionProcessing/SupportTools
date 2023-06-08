﻿namespace TransactionProcessing.DataGeneration;

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Net.Http.Headers;
using System.Text;
using EstateManagement.Client;
using EstateManagement.DataTransferObjects;
using EstateManagement.DataTransferObjects.Requests;
using EstateManagement.DataTransferObjects.Responses;
using Newtonsoft.Json;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects;

public class TransactionDataGenerator : ITransactionDataGenerator{
    #region Fields

    private readonly String ClientId;

    private readonly String ClientSecret;

    private readonly IEstateClient EstateClient;

    private readonly String FileProcessorApi;

    private readonly Random r = new Random();

    private readonly RunningMode RunningMode;

    private readonly ISecurityServiceClient SecurityServiceClient;

    private String TestHostApi;

    private TokenResponse TokenResponse;

    private Int32 TransactionNumber;

    private readonly ITransactionProcessorClient TransactionProcessorClient;

    private readonly String EstateManagementApi;

    #endregion

    #region Constructors

    public TransactionDataGenerator(ISecurityServiceClient securityServiceClient,
                                    IEstateClient estateClient,
                                    ITransactionProcessorClient transactionProcessorClient,
                                    String estateManagementApi,
                                    String fileProcessorApi,
                                    String testHostApi,
                                    String clientId,
                                    String clientSecret,
                                    RunningMode runningMode = RunningMode.WhatIf){
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

    #endregion

    #region Methods

    private void WriteTrace(String message)
    {
        if (TraceGenerated != null){
            TraceEventArgs args = new(){
                                           TraceLevel = TraceEventArgs.Level.Trace,
                                           Message = message
                                       };

            TraceGenerated.Invoke(args);
        }
    }

    private void WriteWarning(String message)
    {
        if (TraceGenerated != null)
        {
            TraceEventArgs args = new()
                                  {
                                      TraceLevel = TraceEventArgs.Level.Warning,
                                      Message = message
                                  };

            TraceGenerated.Invoke(args);
        }
    }
    private void WriteError(String message)
    {
        if (TraceGenerated != null){
            TraceEventArgs args = new(){
                                           TraceLevel = TraceEventArgs.Level.Error,
                                           Message = message
                                       };

            TraceGenerated.Invoke(args);
        }
    }

    private void WriteError(Exception ex)
    {
        if (TraceGenerated != null)
        {
            TraceEventArgs args = new()
                                  {
                                      TraceLevel = TraceEventArgs.Level.Error,
                                      Message = ex.ToString()
                                  };

            TraceGenerated.Invoke(args);
        }
    }

    public List<DateTime> GenerateDateRange(DateTime startDate, DateTime endDate){
        
        this.WriteTrace($"Generating date range between {startDate:dd-MM-yyyy} and {endDate:dd-MM-yyyy}");
        List<DateTime> dateRange = new List<DateTime>();

        if (endDate.Subtract(startDate).Days == 0){
            dateRange.Add(startDate);
        }
        else{
            while (endDate.Subtract(startDate).Days >= 0){
                dateRange.Add(startDate);
                startDate = startDate.AddDays(1);
            }
        }

        this.WriteTrace($"{dateRange.Count} dates generated");

        return dateRange;
    }

    public async Task<List<ContractResponse>> GetMerchantContracts(MerchantResponse merchant, CancellationToken cancellationToken){

        List<ContractResponse> contracts = new List<ContractResponse>();

        if (merchant == null){
            this.WriteError("Merchant is null");
            return contracts;
        }

        String token = await this.GetAuthToken(cancellationToken);
        
        try{
            this.WriteTrace($"About to get contracts for Merchant [{merchant.MerchantId}] Estate Id [{merchant.EstateId}]");
            contracts = await this.EstateClient.GetMerchantContracts(token, merchant.EstateId, merchant.MerchantId, cancellationToken);
            this.WriteTrace($"{contracts.Count} contracts returned for Merchant");
        }
        catch(Exception ex){
            this.WriteError("Error getting merchant contracts");
            this.WriteError(ex);
        }

        return contracts;
    }

    public async Task<List<MerchantResponse>> GetMerchants(Guid estateId, CancellationToken cancellationToken){
        List<MerchantResponse> merchants = new List<MerchantResponse>();

        String token = await this.GetAuthToken(cancellationToken);
        
        try
        {
            this.WriteTrace($"About to get merchants for Estate Id [{estateId}]");
            merchants = await this.EstateClient.GetMerchants(token, estateId, cancellationToken);
            this.WriteTrace($"{merchants.Count} merchants returned for Estate");
        }
        catch (Exception ex)
        {
            this.WriteError("Error getting merchant contracts");
            this.WriteError(ex);
        }

        return merchants;
    }

    public async Task PerformMerchantLogon(DateTime dateTime, MerchantResponse merchant, CancellationToken cancellationToken){

        if (merchant == null)
        {
            this.WriteError("Merchant is null");
            return;
        }

        // Build logon message
        String deviceIdentifier = merchant.Devices.Single().Value;
        LogonTransactionRequest logonTransactionRequest = new LogonTransactionRequest
                                                          {
                                                              DeviceIdentifier = deviceIdentifier,
                                                              EstateId = merchant.EstateId,
                                                              MerchantId = merchant.MerchantId,
                                                              TransactionDateTime = dateTime.Date.AddMinutes(1),
                                                              TransactionNumber = "1",
                                                              TransactionType = "Logon"
                                                          };

        try{

            this.WriteTrace($"About to send Logon Transaction for Merchant [{merchant.MerchantName}]");

            await this.SendLogonTransaction(merchant, logonTransactionRequest, cancellationToken);

            this.WriteTrace($"Logon Transaction for Merchant [{merchant.MerchantName}] sent");

        }
        catch (Exception ex)
        {
            this.WriteError($"Error sending logon transaction for Merchant {merchant.MerchantId} Estate [{merchant.EstateId}]");
            this.WriteError(ex);
        }
    }

    public async Task PerformSettlement(DateTime dateTime, Guid estateId, CancellationToken cancellationToken){
        try
        {
            this.WriteTrace($"About to send Process Settlement Request for Date [{dateTime:dd-MM-yyyy}] and Estate [{estateId}]");

            await this.SendProcessSettlementRequest(dateTime, estateId, cancellationToken);

            this.WriteTrace($"Process Settlement Request sent for Date [{dateTime:dd-MM-yyyy}] and Estate [{estateId}]");

        }
        catch (Exception ex)
        {
            this.WriteError($"Error sending Process Settlement Request for Date [{dateTime:dd-MM-yyyy}] and Estate [{estateId}]");
            this.WriteError(ex);
        }
    }

    public async Task SendSales(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, CancellationToken cancellationToken){
        List<SaleTransactionRequest> salesToSend = new List<SaleTransactionRequest>();

        Decimal depositAmount = 0;
        (Int32 accountNumber, String accountName, Decimal balance) billDetails = default;
        foreach (ContractProduct contractProduct in contract.Products){
            this.WriteTrace($"product [{contractProduct.DisplayText}]");

            List<(SaleTransactionRequest request, Decimal amount)> saleRequests = null;
            // Get a number of sales to be sent
            Int32 numberOfSales = this.r.Next(5, 15);
            for (Int32 i = 1; i <= numberOfSales; i++){
                ProductType productType = this.GetProductType(contract.OperatorName);

                if (productType == ProductType.BillPayment){
                    // Create a bill for this sale
                    Decimal amount = this.GetAmount(contractProduct);
                    billDetails = await this.CreateBillPaymentBill(contract.OperatorName, contractProduct, cancellationToken);
                }

                saleRequests = productType switch{
                    ProductType.MobileTopup => this.BuildMobileTopupSaleRequests(dateTime, merchant, contract, contractProduct),
                    ProductType.Voucher => this.BuildVoucherSaleRequests(dateTime, merchant, contract, contractProduct),
                    ProductType.BillPayment => this.BuildBillPaymentSaleRequests(dateTime, merchant, contract, contractProduct, billDetails),
                    _ => throw new Exception($"Product Type [{productType}] not yet supported")
                };

                // Add the value of the sale to the deposit amount
                Boolean addToDeposit = i switch{
                    _ when i == numberOfSales => false,
                    _ => true
                };

                if (addToDeposit){
                    depositAmount += saleRequests.Sum(sr => sr.amount);
                }

                salesToSend.AddRange(saleRequests.Select(s => s.request));
            }
        }

        // Build up a deposit (minus the last sale amount)
        MakeMerchantDepositRequest depositRequest = this.CreateMerchantDepositRequest(depositAmount, dateTime);

        // Send the deposit
        await this.SendMerchantDepositRequest(merchant, depositRequest, cancellationToken);

        IOrderedEnumerable<SaleTransactionRequest> orderedSales = salesToSend.OrderBy(s => s.TransactionDateTime);
        // Send the sales to the host
        foreach (SaleTransactionRequest sale in orderedSales){
            sale.TransactionNumber = this.GetTransactionNumber().ToString();
            await this.SendSaleTransaction(merchant, sale, cancellationToken);
        }
    }

    public async Task SendUploadFile(DateTime dateTime, ContractResponse contract, MerchantResponse merchant, CancellationToken cancellationToken){
        Int32 numberOfSales = this.r.Next(5, 15);
        (Decimal, UploadFile) uploadFile = await this.BuildUploadFile(dateTime, merchant, contract, numberOfSales, cancellationToken);

        if (uploadFile.Item2 == null){
            return;
        }
        if (this.RunningMode == RunningMode.WhatIf){
            this.WriteTrace($"Send File for Merchant [{merchant.MerchantName}] Contract [{contract.OperatorName}] Lines [{uploadFile.Item2.GetNumberOfLines()}]");
            return;
        }

        // Build up a deposit (minus the last sale amount)
        MakeMerchantDepositRequest depositRequest = this.CreateMerchantDepositRequest(uploadFile.Item1, dateTime);

        // Send the deposit
        await this.SendMerchantDepositRequest(merchant, depositRequest, cancellationToken);
        
        await this.UploadFile(uploadFile.Item2, Guid.Empty, dateTime, cancellationToken);
    }

    public async Task<MerchantResponse> GetMerchant(Guid estateId, Guid merchantId, CancellationToken cancellationToken){
        MerchantResponse merchant = new MerchantResponse();
        String token = await this.GetAuthToken(cancellationToken);

        try
        {
            this.WriteTrace($"About to get Merchant [{merchant.MerchantId}] Estate Id [{merchant.EstateId}]");
            merchant = await this.EstateClient.GetMerchant(token, estateId, merchantId, cancellationToken);
            this.WriteTrace($"Merchant retrieved successfully");
        }
        catch (Exception ex)
        {
            this.WriteError("Error getting merchant");
            this.WriteError(ex);
        }

        return merchant;

    }

    public async Task GenerateMerchantStatement(Guid estateId, Guid merchantId, DateTime statementDateTime, CancellationToken cancellationToken)
    {
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
            String token = await this.GetAuthToken(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using (HttpClient client = new HttpClient())
            {
                await client.SendAsync(request, cancellationToken);
            }

            this.WriteTrace($"Generate Merchant Statement Request sent for Estate [{estateId}] and Merchant [{merchantId}] StatementDate [{statementDateTime:dd-MM-yyyy}]");

        }
        catch (Exception ex)
        {
            this.WriteError($"Error sending Generate Merchant Statement Request for Date [{statementDateTime:dd-MM-yyyy}] and Estate [{estateId}] and Merchant [{merchantId}]");
            this.WriteError(ex);
        }

    }

    public event TraceHandler? TraceGenerated;

    private List<(SaleTransactionRequest request, Decimal amount)> BuildBillPaymentSaleRequests(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, ContractProduct product, (Int32 accountNumber, String accountName, Decimal balance) billDetails){
        List<(SaleTransactionRequest request, Decimal amount)> requests = new List<(SaleTransactionRequest request, Decimal amount)>();

        // Create the requests required
        String deviceIdentifier = merchant.Devices.Single().Value;

        // First request is Get Account
        Dictionary<String, String> getAccountRequestMetaData = new Dictionary<String, String>{
                                                                                                 { "CustomerAccountNumber", billDetails.accountNumber.ToString() },
                                                                                                 { "PataPawaPostPaidMessageType", "VerifyAccount" }
                                                                                             };

        var transactionDateTime = this.GetTransactionDateTime(dateTime);

        SaleTransactionRequest getAccountRequest = new SaleTransactionRequest{
                                                                                 AdditionalTransactionMetadata = getAccountRequestMetaData,
                                                                                 ContractId = contract.ContractId,
                                                                                 CustomerEmailAddress = String.Empty,
                                                                                 DeviceIdentifier = deviceIdentifier,
                                                                                 MerchantId = merchant.MerchantId,
                                                                                 EstateId = merchant.EstateId,
                                                                                 TransactionType = "Sale",
                                                                                 TransactionDateTime = transactionDateTime,
                                                                                 OperatorIdentifier = contract.OperatorName,
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

        SaleTransactionRequest makePaymentRequest = new SaleTransactionRequest{
                                                                                  AdditionalTransactionMetadata = makePaymentRequestMetaData,
                                                                                  ContractId = contract.ContractId,
                                                                                  CustomerEmailAddress = String.Empty,
                                                                                  DeviceIdentifier = deviceIdentifier,
                                                                                  MerchantId = merchant.MerchantId,
                                                                                  EstateId = merchant.EstateId,
                                                                                  TransactionType = "Sale",
                                                                                  TransactionDateTime = transactionDateTime.AddSeconds(30),
                                                                                  OperatorIdentifier = contract.OperatorName,
                                                                                  ProductId = product.ProductId
                                                                              };

        requests.Add((makePaymentRequest, billDetails.balance));

        return requests;
    }

    private List<(SaleTransactionRequest request, Decimal amount)> BuildMobileTopupSaleRequests(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, ContractProduct contractProduct){
        Decimal amount = this.GetAmount(contractProduct);

        Dictionary<String, String> requestMetaData = new Dictionary<String, String>{
                                                                                       { "Amount", amount.ToString() },
                                                                                       { "CustomerAccountNumber", "1234567890" }
                                                                                   };

        List<(SaleTransactionRequest request, Decimal amount)> requests = new List<(SaleTransactionRequest request, Decimal amount)>();

        String deviceIdentifier = merchant.Devices.Single().Value;

        SaleTransactionRequest request = new SaleTransactionRequest{
                                                                       AdditionalTransactionMetadata = requestMetaData,
                                                                       ContractId = contract.ContractId,
                                                                       CustomerEmailAddress = String.Empty,
                                                                       DeviceIdentifier = deviceIdentifier,
                                                                       MerchantId = merchant.MerchantId,
                                                                       EstateId = merchant.EstateId,
                                                                       TransactionType = "Sale",
                                                                       TransactionDateTime = this.GetTransactionDateTime(dateTime),
                                                                       OperatorIdentifier = contract.OperatorName,
                                                                       ProductId = contractProduct.ProductId
                                                                   };
        requests.Add((request, amount));

        return requests;
    }

    private async Task<(Decimal, UploadFile)> BuildUploadFile(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, Int32 numberOfLines, CancellationToken cancellationToken){
        ProductType productType = this.GetProductType(contract.OperatorName);
        Guid fileProfileId = await TransactionDataGenerator.GetFileProfileIdFromOperator(contract.OperatorName, cancellationToken);
        String token = await this.GetAuthToken(cancellationToken);
        EstateResponse estate = await this.EstateClient.GetEstate(token, merchant.EstateId, cancellationToken);
        Guid userId = estate.SecurityUsers.First().SecurityUserId;
        Decimal depositAmount = 0;
        if (productType == ProductType.MobileTopup){
            MobileTopupUploadFile mobileTopupUploadFile = new MobileTopupUploadFile(contract.EstateId, merchant.MerchantId, fileProfileId, userId);
            mobileTopupUploadFile.AddHeader(dateTime);

            for (Int32 i = 1; i <= numberOfLines; i++){
                Decimal amount = this.GetAmount();
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
            return (depositAmount,mobileTopupUploadFile);
        }

        if (productType == ProductType.Voucher){
            VoucherTopupUploadFile voucherTopupUploadFile = new VoucherTopupUploadFile(contract.EstateId, merchant.MerchantId, fileProfileId, userId);
            voucherTopupUploadFile.AddHeader(dateTime);

            for (Int32 i = 1; i <= numberOfLines; i++){
                Decimal amount = this.GetAmount();
                String mobileNumber = String.Format($"077777777{i.ToString().PadLeft(2, '0')}");
                String emailAddress = String.Format($"testrecipient{i.ToString().PadLeft(2, '0')}@testing.com");
                String recipient = mobileNumber;
                if (i % 2 == 0){
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
            return (depositAmount, voucherTopupUploadFile);
        }

        // Not supported product type for file upload
        return (0,null);
    }

    private List<(SaleTransactionRequest request, Decimal amount)> BuildVoucherSaleRequests(DateTime dateTime, MerchantResponse merchant, ContractResponse contract, ContractProduct contractProduct){
        Decimal amount = this.GetAmount(contractProduct);

        Dictionary<String, String> requestMetaData = new Dictionary<String, String>{
                                                                                       { "Amount", amount.ToString() },
                                                                                       { "RecipientMobile", "1234567890" }
                                                                                   };

        List<(SaleTransactionRequest request, Decimal amount)> requests = new List<(SaleTransactionRequest request, Decimal amount)>();

        String deviceIdentifier = merchant.Devices.Single().Value;

        SaleTransactionRequest request = new SaleTransactionRequest{
                                                                       AdditionalTransactionMetadata = requestMetaData,
                                                                       ContractId = contract.ContractId,
                                                                       CustomerEmailAddress = String.Empty,
                                                                       DeviceIdentifier = deviceIdentifier,
                                                                       MerchantId = merchant.MerchantId,
                                                                       EstateId = merchant.EstateId,
                                                                       TransactionType = "Sale",
                                                                       TransactionDateTime = this.GetTransactionDateTime(dateTime),
                                                                       OperatorIdentifier = contract.OperatorName,
                                                                       ProductId = contractProduct.ProductId
                                                                   };
        requests.Add((request, amount));

        return requests;
    }

    private async Task<(Int32 accountNumber, String accountName, Decimal balance)> CreateBillPaymentBill(String contractOperatorName, ContractProduct contractProduct, CancellationToken cancellationToken){
        if (contractOperatorName == "PataPawa PostPay"){
            Int32 accountNumber = this.r.Next(1, 100000);
            Decimal amount = this.GetAmount(contractProduct);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{this.TestHostApi}/api/developer/patapawapostpay/createbill");
            var body = new{
                              due_date = DateTime.Now.AddDays(1),
                              amount = amount * 100,
                              account_number = accountNumber,
                              account_name = "Test Account 1"
                          };
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            if (this.RunningMode == RunningMode.WhatIf){
                this.WriteTrace($"Bill Created Account [{body.account_number}] Balance [{body.amount}]");
                return (body.account_number, body.account_name, body.amount);
            }

            using(HttpClient client = new HttpClient()){
                await client.SendAsync(request, cancellationToken);
            }

            return (body.account_number, body.account_name, body.amount);
        }

        return default;
    }

    private MakeMerchantDepositRequest CreateMerchantDepositRequest(Decimal depositAmount, DateTime dateTime){
        // TODO: generate a reference

        MakeMerchantDepositRequest request = new MakeMerchantDepositRequest{
                                                                               Amount = depositAmount,
                                                                               DepositDateTime = dateTime,
                                                                               Reference = "ABC"
                                                                           };
        return request;
    }

    private Decimal GetAmount(ContractProduct product = null){
        if (product == null){
            return this.r.Next(9, 250);
        }

        return product.Value.HasValue switch{
            true => product.Value.Value,
            _ => this.r.Next(9, 250)
        };
    }

    private async Task<String> GetAuthToken(CancellationToken cancellationToken){
        
        this.WriteTrace($"About to get auth token");
        
        if (this.TokenResponse == null){
            this.WriteTrace($"TokenResponse was null");
            TokenResponse token = await this.SecurityServiceClient.GetToken(this.ClientId, this.ClientSecret, cancellationToken);
            this.TokenResponse = token;
        }

        if (this.TokenResponse.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2)){
            this.WriteTrace($"TokenResponse was expired");
            TokenResponse token = await this.SecurityServiceClient.GetToken(this.ClientId, this.ClientSecret, cancellationToken);
            this.TokenResponse = token;
        }

        this.WriteTrace($"Auth token retrieved");

        return this.TokenResponse.AccessToken;
    }

    private static async Task<Guid> GetFileProfileIdFromOperator(String operatorName, CancellationToken cancellationToken){
        // TODO: get this profile list from API

        switch(operatorName){
            case "Safaricom":
                return Guid.Parse("B2A59ABF-293D-4A6B-B81B-7007503C3476");
            case "Voucher":
                return Guid.Parse("8806EDBC-3ED6-406B-9E5F-A9078356BE99");
            default:
                return Guid.Empty;
        }
    }

    private ProductType GetProductType(String operatorName){
        ProductType productType = ProductType.NotSet;
        switch(operatorName){
            case "Safaricom":
                productType = ProductType.MobileTopup;
                break;
            case "Voucher":
                productType = ProductType.Voucher;
                break;
            case "PataPawa PostPay":
                productType = ProductType.BillPayment;
                break;
        }

        return productType;
    }

    private DateTime GetTransactionDateTime(DateTime dateTime){
        // Generate the time
        Int32 hours = this.r.Next(0, 23);
        Int32 minutes = this.r.Next(0, 59);
        Int32 seconds = this.r.Next(0, 59);

        return dateTime.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
    }
    private Int32 GetTransactionNumber(){
        this.TransactionNumber++;
        return this.TransactionNumber;
    }

    private async Task SendMerchantDepositRequest(MerchantResponse merchant, MakeMerchantDepositRequest request, CancellationToken cancellationToken){
        if (this.RunningMode == RunningMode.WhatIf){
            this.WriteTrace($"Make Deposit [{request.Amount}] for Merchant [{merchant.MerchantName}]");
            return;
        }
        String token = await this.GetAuthToken(cancellationToken);
        try{

            this.WriteTrace($"About to make Deposit [{request.Amount}] for Merchant [{merchant.MerchantName}]");
            MakeMerchantDepositResponse response = await this.EstateClient.MakeMerchantDeposit(token, merchant.EstateId, merchant.MerchantId, request, cancellationToken);
            this.WriteTrace($"Deposit [{request.Amount}] made for Merchant [{merchant.MerchantName}]");
        }
        catch (Exception ex)
        {
            this.WriteError($"Error making merchant deposit for merchant [{merchant.MerchantName}]");
            this.WriteError(ex);
        }
    }

    private async Task SendSaleTransaction(MerchantResponse merchant, SaleTransactionRequest request, CancellationToken cancellationToken){
        if (this.RunningMode == RunningMode.WhatIf){
            this.WriteTrace($"Send Sale for Merchant [{merchant.MerchantName}] - {request.TransactionNumber} - {request.OperatorIdentifier} - {request.GetAmount()}");
            return;
        }

        String token = await this.GetAuthToken(cancellationToken);
        SerialisedMessage requestSerialisedMessage = request.CreateSerialisedMessage();
        SerialisedMessage responseSerialisedMessage = null;

        this.WriteTrace($"About to Send sale for Merchant [{merchant.MerchantName}]");
        for (int i = 0; i < 3; i++){
            try
            {
                responseSerialisedMessage =
                    await this.TransactionProcessorClient.PerformTransaction(token, requestSerialisedMessage, CancellationToken.None);
                break;
            }
            catch(TaskCanceledException e){
                this.WriteError(e);
            }
        }

        SaleTransactionResponse saleTransactionResponse = responseSerialisedMessage.GetSerialisedMessageResponseDTO<SaleTransactionResponse>();

        this.WriteTrace($"Sale Transaction for Merchant [{merchant.MerchantName}] sent");
    }

    private async Task SendLogonTransaction(MerchantResponse merchant, LogonTransactionRequest request, CancellationToken cancellationToken)
    {
        if (this.RunningMode == RunningMode.WhatIf)
        {
            this.WriteTrace($"Send Logon Transaction for Merchant [{merchant.MerchantName}]");
            return;
        }

        String token = await this.GetAuthToken(cancellationToken);
        SerialisedMessage requestSerialisedMessage = request.CreateSerialisedMessage();
            
        SerialisedMessage responseSerialisedMessage =
            await this.TransactionProcessorClient.PerformTransaction(token, requestSerialisedMessage, CancellationToken.None);

        SaleTransactionResponse saleTransactionResponse = responseSerialisedMessage.GetSerialisedMessageResponseDTO<SaleTransactionResponse>();
    }

    private async Task UploadFile(UploadFile uploadFile, Guid userId, DateTime fileDateTime, CancellationToken cancellationToken){
        var formData = new MultipartFormDataContent();
        String token = await this.GetAuthToken(cancellationToken);

        var fileContent = new ByteArrayContent(uploadFile.GetFileContents());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
        formData.Add(fileContent, "file", $"bulkfile{fileDateTime:yyyy-MM-dd}");
        formData.Add(new StringContent(uploadFile.EstateId.ToString()), "request.EstateId");
        formData.Add(new StringContent(uploadFile.MerchantId.ToString()), "request.MerchantId");
        formData.Add(new StringContent(uploadFile.FileProfileId.ToString()), "request.FileProfileId");
        formData.Add(new StringContent(userId.ToString()), "request.UserId");
        formData.Add(new StringContent(fileDateTime.ToString("yyyy-MM-dd HH:mm:ss")), "request.UploadDateTime");

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{this.FileProcessorApi}/api/files"){
                                                                                                                      Content = formData,
                                                                                                                  };

        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
        HttpResponseMessage response = null;
        
        try
        {

            this.WriteTrace($"About to upload file for Merchant [{uploadFile.MerchantId}]");
            using (HttpClient client = new HttpClient())
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            this.WriteTrace($"File uploaded for Merchant [{uploadFile.MerchantId}]");
        }
        catch (Exception ex)
        {
            this.WriteError($"Error uploading file for merchant [{uploadFile.MerchantId}]");
            this.WriteError(ex);
        }
    }

    private async Task SendProcessSettlementRequest(DateTime dateTime, Guid estateId, CancellationToken cancellationToken)
    {
        if (this.RunningMode == RunningMode.WhatIf)
        {
            this.WriteTrace($"Sending Settlement for Date [{dateTime.Date}] Estate [{estateId}]");
            return;
        }

        String token = await this.GetAuthToken(cancellationToken);
        await this.TransactionProcessorClient.ProcessSettlement(token, dateTime, estateId, cancellationToken);
    }

    #endregion
}

public enum RunningMode
{
    WhatIf,

    Live
}