using Microsoft.AspNetCore.DataProtection;
using SecurityService.Client;
using SecurityService.DataTransferObjects;
using TransactionProcessing.Shared.Exceptions;
using TransactionProcessing.UnionResults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects;
using TransactionProcessor.DataTransferObjects.Requests.Contract;
using TransactionProcessor.DataTransferObjects.Requests.Estate;
using TransactionProcessor.DataTransferObjects.Requests.Float;
using TransactionProcessor.DataTransferObjects.Requests.Merchant;
using TransactionProcessor.DataTransferObjects.Requests.Operator;
using TransactionProcessor.DataTransferObjects.Responses.Contract;
using TransactionProcessor.DataTransferObjects.Responses.Estate;
using TransactionProcessor.DataTransferObjects.Responses.Merchant;
using TransactionProcessor.DataTransferObjects.Responses.Operator;
using TransactionProcessor.SystemSetupTool.estateconfig;
using TransactionProcessor.SystemSetupTool.identityserverconfig;
using Address = TransactionProcessor.DataTransferObjects.Requests.Merchant.Address;
using AssignOperatorRequest = TransactionProcessor.DataTransferObjects.Requests.Estate.AssignOperatorRequest;
using Contact = TransactionProcessor.DataTransferObjects.Requests.Merchant.Contact;
using Contract = TransactionProcessor.SystemSetupTool.estateconfig.Contract;

namespace TransactionProcessor.SystemSetupTool;

public class EstateSetupFunctions {
    private readonly ISecurityServiceClient SecurityServiceClient;

    private readonly ITransactionProcessorClient TransactionProcessorClient;

    private readonly Estate EstateConfig;
    private readonly IdentityServerConfiguration IdentityServerConfiguration;

    private TokenResponse TokenResponse;

    private Guid EstateId;

    public EstateSetupFunctions(ISecurityServiceClient securityServiceClient,
                                ITransactionProcessorClient transactionProcessorClient,
                                Estate estateConfig,
                                IdentityServerConfiguration identityServerConfiguration) {
        this.SecurityServiceClient = securityServiceClient;
        this.TransactionProcessorClient = transactionProcessorClient;
        this.EstateConfig = estateConfig;
        this.IdentityServerConfiguration = identityServerConfiguration;
    }

    public async Task<Result> SetupEstate(CancellationToken cancellationToken) {
        var result = await this.SecurityServiceClient.GetToken("serviceClient", ResolveServiceClientSecret(), CancellationToken.None);
        if (result.Value is not Success<TokenResponse> { Value: { } token })
            return result.IsSuccess() ? Results.Fail("Security service returned no access token.") : result.WithoutPayload();
        this.TokenResponse = token;

        Result<Guid> createEstateResult = await this.CreateEstate(cancellationToken);
        if (createEstateResult.Value is not Success<Guid> { Value: var estateId })
            return createEstateResult.WithoutPayload();
        this.EstateId = estateId;

        Result<EstateResponse> getEstateResult = await this.GetEstate(this.EstateId, cancellationToken);
        if (!getEstateResult.IsSuccess())
            return getEstateResult.WithoutPayload();

        var createUserResult = await this.CreateEstateUser(cancellationToken);
        if (!createUserResult.IsSuccess())
            return createUserResult;

        var createOperatorsResult = await this.CreateOperators(cancellationToken);
        if (!createOperatorsResult.IsSuccess())
            return createOperatorsResult;

        var createContractsResult = await this.CreateContracts(cancellationToken);
        if (!createContractsResult.IsSuccess())
            return createContractsResult;

        var createMerchantsResult = await this.CreateMerchants(cancellationToken);
        if (!createMerchantsResult.IsSuccess())
            return createMerchantsResult;

        var createFloatsResult = await this.CreateFloats(cancellationToken);
        if (!createFloatsResult.IsSuccess())
            return createFloatsResult;

        // Will only get here is everything was OK
        return Results.Ok();
    }

    private static string ResolveUserPassword(String password) {
        if (!string.IsNullOrWhiteSpace(password))
        {
            return password;
        }
        return Environment.GetEnvironmentVariable("DEFAULT_USER_PASSWORD");
    }


    private static string ResolveServiceClientSecret()
    {
        string secret = Environment.GetEnvironmentVariable("TRANSACTIONPROCESSOR_SERVICECLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(secret))
        {
            return secret;
        }

        throw new InvalidOperationException("Missing required secret for service client. Set TRANSACTIONPROCESSOR_SERVICECLIENT_SECRET.");
    }

    private async Task<Result<EstateResponse>> GetEstate(Guid estateId,
                                                         CancellationToken cancellationToken) {
        var estateResponse = await this.TransactionProcessorClient.GetEstates(this.TokenResponse.AccessToken, estateId, cancellationToken);
        return estateResponse.Value switch {
            Success<List<EstateResponse>> success => Results.Ok(success.Value.Single()),
            Error error => error,
            ErrorList errors => errors,
            _ => new Error(ErrorType.Critical, "An uninitialized result has no outcome.")
        };
    }

    private async Task<Result<Guid>> CreateEstate(CancellationToken cancellationToken) {
        CreateEstateRequest createEstateRequest = new CreateEstateRequest { EstateName = this.EstateConfig.Name };

        return await this.TransactionProcessorClient.CreateEstate(this.TokenResponse.AccessToken, createEstateRequest, cancellationToken);
    }

    private async Task<Result> CreateEstateUser(CancellationToken cancellationToken) {
        var estateResponse = await this.GetEstate(this.EstateId, cancellationToken);
        if (estateResponse.Value is not Success<EstateResponse> { Value: var estate })
            return estateResponse.WithoutPayload();

        SecurityUserResponse existingUser = estate.SecurityUsers.SingleOrDefault(u => u.EmailAddress == this.EstateConfig.User.EmailAddress);

        if (existingUser == null) {
            // Need to create the user
            // Create Estate user
            CreateEstateUserRequest createEstateUserRequest = new CreateEstateUserRequest {
                EmailAddress = this.EstateConfig.User.EmailAddress,
                FamilyName = this.EstateConfig.User.FamilyName,
                GivenName = this.EstateConfig.User.GivenName,
                MiddleName = this.EstateConfig.User.MiddleName,
                Password = ResolveUserPassword(this.EstateConfig.User.Password)
            };
            return await this.TransactionProcessorClient.CreateEstateUser(this.TokenResponse.AccessToken, this.EstateId, createEstateUserRequest, cancellationToken);
        }

        return Results.Ok();
    }

    private async Task<Result> CreateOperators(CancellationToken cancellationToken) {
        var estateResponse = await this.GetEstate(this.EstateId, cancellationToken);
        if (estateResponse.Value is not Success<EstateResponse> { Value: var estate })
            return estateResponse.WithoutPayload();


        // Now do the operators
        foreach (Operator @operator in this.EstateConfig.Operators) {
            EstateOperatorResponse existingOperator = estate.Operators.SingleOrDefault(o => o.Name == @operator.Name);

            if (existingOperator != null) {
                continue;
            }

            CreateOperatorRequest createOperatorRequest = new CreateOperatorRequest {
                Name = @operator.Name, RequireCustomMerchantNumber = @operator.RequireCustomMerchantNumber, RequireCustomTerminalNumber = @operator.RequireCustomMerchantNumber,
            };
            Result<Guid> createResult = await this.TransactionProcessorClient.CreateOperator(this.TokenResponse.AccessToken, this.EstateId, createOperatorRequest, cancellationToken);
            if (createResult.Value is not Success<Guid> { Value: var operatorId })
                return createResult.WithoutPayload();

            // Now assign this to the estate
            Result assignResult = await this.TransactionProcessorClient.AssignOperatorToEstate(this.TokenResponse.AccessToken, this.EstateId, new AssignOperatorRequest { OperatorId = operatorId }, cancellationToken);
            if (!assignResult.IsSuccess())
                return assignResult;
        }

        return Results.Ok();
    }

    private async Task<Result<Guid>> GetContractId(String contractDescription,
                                                   CancellationToken cancellationToken) {
        Guid contractId = Guid.Empty;
        await Retry.For(async () => {
            var existingContractsResult = await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
            if (existingContractsResult.Value is not Success<List<ContractResponse>> { Value: var contracts })
                throw new InvalidOperationException("GetContracts failed");

            var c = contracts.SingleOrDefault(c => c.Description == contractDescription);
            if (c == null) {
                throw new NotFoundException($"Contract with description {contractDescription} not found");
            }

            contractId = c.ContractId;
        });
        if (contractId == Guid.Empty)
            return new Error(ErrorType.NotFound, $"Contract with description {contractDescription} not found");

        return Results.Ok(contractId);
    }

    private async Task<Result<Guid>> GetContractProductId(Guid contractId,
                                                          String productName,
                                                          CancellationToken cancellationToken) {


        Guid contractProductId = Guid.Empty;
        await Retry.For(async () => {

            var getContractResult = await this.TransactionProcessorClient.GetContract(this.TokenResponse.AccessToken, this.EstateId, contractId, cancellationToken);
            if (getContractResult.Value is not Success<ContractResponse> { Value: var contract })
                throw new InvalidOperationException("GetContract failed");

            var cp = contract.Products.SingleOrDefault(p => p.Name == productName);
            if (cp == null) {
                throw new NotFoundException($"Contract Product with name {productName} not found on Contract Id {contractId}");
            }

            contractProductId = cp.ProductId;
        });
        if (contractProductId == Guid.Empty)
            return new Error(ErrorType.NotFound, $"Contract Product with name {productName} not found on Contract Id {contractId}");

        return Results.Ok(contractProductId);
    }

    private async Task<Result> UpdateExistingContract(Contract contract,
                                                      ContractResponse existingContract,
                                                      CancellationToken cancellationToken) {
        var getContractResult = await this.TransactionProcessorClient.GetContract(this.TokenResponse.AccessToken, this.EstateId, existingContract.ContractId, cancellationToken);
        if (getContractResult.Value is not Success<ContractResponse> { Value: var contractDetails })
            return getContractResult.WithoutPayload();

        // Now we need to check if all the products are created
        foreach (Product contractProduct in contract.Products) {
            ContractProduct product = null;
            if (existingContract.Products != null){
                product = existingContract.Products.SingleOrDefault(p => p.Name == contractProduct.ProductName);
            }

            if (product == null) {
                var addContractProductResult = await this.TransactionProcessorClient.AddProductToContract(this.TokenResponse.AccessToken, this.EstateId, existingContract.ContractId, 
                    new AddProductToContractRequest { ProductName = contractProduct.ProductName, DisplayText = contractProduct.DisplayText, Value = contractProduct.Value, 
                        ProductType = Enum.Parse<DataTransferObjects.Responses.Contract.ProductType>(contractProduct.ProductType.ToString()) }, cancellationToken);

                if (!addContractProductResult.IsSuccess())
                    return addContractProductResult;

                Result<Guid> contractProductIdResult = await GetContractProductId(existingContract.ContractId, contractProduct.ProductName, cancellationToken);
                if (contractProductIdResult.Value is not Success<Guid> { Value: var contractProductId })
                    return contractProductIdResult.WithoutPayload();

                foreach (var transactionFee in contractProduct.TransactionFees) {
                    Result addTransactionFeeForProductToContractResult = await this.TransactionProcessorClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken, this.EstateId, existingContract.ContractId, contractProductId, new AddTransactionFeeForProductToContractRequest { Value = transactionFee.Value, Description = transactionFee.Description, CalculationType = (CalculationType)transactionFee.CalculationType, FeeType = (FeeType)transactionFee.FeeType }, cancellationToken);

                    if (!addTransactionFeeForProductToContractResult.IsSuccess())
                        return addTransactionFeeForProductToContractResult;
                }
            }
            else {
                var fullProduct = contractDetails.Products.Single(p => p.Name == contractProduct.ProductName);

                foreach (var transactionFee in contractProduct.TransactionFees) {
                    var feeExists = fullProduct.TransactionFees.Any(p => p.CalculationType == (CalculationType)transactionFee.CalculationType && p.FeeType == (FeeType)transactionFee.FeeType && p.Description == transactionFee.Description && p.Value == transactionFee.Value);
                    if (feeExists == false) {
                        Result addTransactionFeeForProductToContractResult = await this.TransactionProcessorClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken, this.EstateId, existingContract.ContractId, product.ProductId, new AddTransactionFeeForProductToContractRequest { Value = transactionFee.Value, Description = transactionFee.Description, CalculationType = (CalculationType)transactionFee.CalculationType, FeeType = (FeeType)transactionFee.FeeType }, cancellationToken);

                        if (!addTransactionFeeForProductToContractResult.IsSuccess())
                            return addTransactionFeeForProductToContractResult;
                    }
                }
            }
        }

        return Results.Ok();
    }

    private async Task<Result> CreateNewContract(EstateResponse estate,
                                                 Contract contract,
                                                 CancellationToken cancellationToken) {
        var @operator = estate.Operators.SingleOrDefault(o => o.Name == contract.OperatorName);

        CreateContractRequest createContractRequest = new CreateContractRequest { Description = contract.Description, OperatorId = @operator.OperatorId };
        Result<Guid> createResult = await this.TransactionProcessorClient.CreateContract(this.TokenResponse.AccessToken, this.EstateId, createContractRequest, cancellationToken);
        if (createResult.Value is not Success<Guid> { Value: var contractId })
            return createResult.WithoutPayload();

        foreach (Product contractProduct in contract.Products) {
            AddProductToContractRequest addProductToContractRequest = new AddProductToContractRequest { DisplayText = contractProduct.DisplayText, ProductName = contractProduct.ProductName, Value = contractProduct.Value };

            Result addProductToContractResult = await this.TransactionProcessorClient.AddProductToContract(this.TokenResponse.AccessToken, this.EstateId, contractId, addProductToContractRequest, cancellationToken);

            if (!addProductToContractResult.IsSuccess())
                return addProductToContractResult;

            Result<Guid> contractProductIdResult = await GetContractProductId(contractId, contractProduct.ProductName, cancellationToken);
            if (contractProductIdResult.Value is not Success<Guid> { Value: var contractProductId })
                return contractProductIdResult.WithoutPayload();

            foreach (TransactionFee contractProductTransactionFee in contractProduct.TransactionFees) {
                AddTransactionFeeForProductToContractRequest addTransactionFeeForProductToContractRequest = new AddTransactionFeeForProductToContractRequest { Description = contractProductTransactionFee.Description, Value = contractProductTransactionFee.Value, CalculationType = (CalculationType)contractProductTransactionFee.CalculationType, FeeType = (FeeType)contractProductTransactionFee.FeeType };

                Result addTransactionFeeForProductToContractResult = await this.TransactionProcessorClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken, this.EstateId, contractId, contractProductId, addTransactionFeeForProductToContractRequest, cancellationToken);

                if (!addTransactionFeeForProductToContractResult.IsSuccess())
                    return addTransactionFeeForProductToContractResult;
            }
        }

        return Results.Ok();
    }

    private async Task<Result> CreateContracts(CancellationToken cancellationToken) {
        var existingContractsResult= await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (existingContractsResult.Value is not Success<List<ContractResponse>> { Value: var existingContracts })
            return existingContractsResult.WithoutPayload();
        var estateResponse = await this.GetEstate(this.EstateId, cancellationToken);
        if (estateResponse.Value is not Success<EstateResponse> { Value: var estate })
            return estateResponse.WithoutPayload();

        foreach (Contract contract in this.EstateConfig.Contracts) {
            // Is the contact created 
            ContractResponse existingContract = existingContracts.SingleOrDefault(c => c.Description == contract.Description);
            if (existingContract == null) {
                // New contract
                Result createResult = await CreateNewContract(estate, contract, cancellationToken);
                if (!createResult.IsSuccess())
                    return createResult;
            }
            else {
                // Update contract
                var updateResult = await UpdateExistingContract(contract, existingContract, cancellationToken);
                if (!updateResult.IsSuccess())
                    return updateResult;
            }
        }

        return Results.Ok();
    }

    private async Task<Result<List<MerchantResponse>>> GetMerchants(CancellationToken cancellationToken) {
        var merchants = await this.TransactionProcessorClient.GetMerchants(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        return merchants;
    }

    public async Task<Result> CreateMerchant(Merchant merchant,
                                             CancellationToken cancellationToken) {
        SettlementSchedule settlementSchedule = Enum.Parse<SettlementSchedule>(merchant.SettlementSchedule);

        CreateMerchantRequest createMerchantRequest = new CreateMerchantRequest {
            Address = new Address {
                AddressLine1 = merchant.Address.AddressLine1, Country = merchant.Address.Country, Region = merchant.Address.Region, Town = merchant.Address.Town,
                PostalCode = merchant.Address.Postcode
            },
            Name = merchant.Name,
            Contact = new Contact { ContactName = merchant.Contact.ContactName, EmailAddress = merchant.Contact.EmailAddress },
            SettlementSchedule = settlementSchedule,
            CreatedDateTime = merchant.CreateDate,
        };
        Result<Guid> createMerchantResult = await this.TransactionProcessorClient.CreateMerchant(this.TokenResponse.AccessToken, this.EstateId, createMerchantRequest, cancellationToken);
        if (createMerchantResult.Value is not Success<Guid> { Value: var merchantId })
            return createMerchantResult.WithoutPayload();

        // Now add devices
        AddMerchantDeviceRequest addMerchantDeviceRequest = new AddMerchantDeviceRequest { DeviceIdentifier = merchant.Device.DeviceIdentifier };
        var addDeviceResult = await this.TransactionProcessorClient.AddDeviceToMerchant(this.TokenResponse.AccessToken, this.EstateId, merchantId, addMerchantDeviceRequest, cancellationToken);
        if (!addDeviceResult.IsSuccess())
            return addDeviceResult;

        // Now security user
        CreateMerchantUserRequest createMerchantUserRequest = new CreateMerchantUserRequest {
            EmailAddress = merchant.User.EmailAddress,
            FamilyName = merchant.User.FamilyName,
            GivenName = merchant.User.GivenName,
            MiddleName = merchant.User.MiddleName,
            Password = ResolveUserPassword(merchant.User.Password)
        };
        var createMerchantUserResult = await this.TransactionProcessorClient.CreateMerchantUser(this.TokenResponse.AccessToken, this.EstateId, merchantId, createMerchantUserRequest, cancellationToken);
        if (!createMerchantUserResult.IsSuccess())
            return createMerchantUserResult;


        var getEstateResult = await this.TransactionProcessorClient.GetEstate(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getEstateResult.Value is not Success<EstateResponse> { Value: var estate })
            return getEstateResult.WithoutPayload();
        foreach (var @operator in estate.Operators) {
            TransactionProcessor.DataTransferObjects.Requests.Merchant.AssignOperatorRequest assignOperatorRequest = new() { OperatorId = @operator.OperatorId, MerchantNumber = null, TerminalNumber = null };

            Result assignOperatorToMerchantResult = await this.TransactionProcessorClient.AssignOperatorToMerchant(this.TokenResponse.AccessToken, this.EstateId, merchantId, assignOperatorRequest, cancellationToken);
            if (!assignOperatorToMerchantResult.IsSuccess())
                return assignOperatorToMerchantResult;
        }

        var getContractsResult = await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getContractsResult.Value is not Success<List<ContractResponse>> { Value: var contracts })
            return getContractsResult.WithoutPayload();

        // Now contracts
        foreach (ContractResponse contractResponse in contracts) {
            AddMerchantContractRequest addMerchantContractRequest = new() { ContractId = contractResponse.ContractId };
            var addContractToMerchantResult = await this.TransactionProcessorClient.AddContractToMerchant(this.TokenResponse.AccessToken, this.EstateId, merchantId, addMerchantContractRequest, cancellationToken);
            if (!addContractToMerchantResult.IsSuccess())
                return addContractToMerchantResult;
        }

        return Results.Ok();
    }

    private async Task<Result> UpdateMerchant(Merchant merchant,
                                              MerchantResponse existingMerchant,
                                              CancellationToken cancellationToken) {
        var ensureDeviceResult = await EnsureMerchantDeviceAsync(merchant, existingMerchant, cancellationToken);
        if (!ensureDeviceResult.IsSuccess())
        {
            return ensureDeviceResult;
        }

        var ensureUserResult = await EnsureMerchantUserAsync(merchant, existingMerchant, cancellationToken);
        if (!ensureUserResult.IsSuccess())
        {
            return ensureUserResult;
        }

        var ensureOperatorsResult = await EnsureMerchantOperatorsAsync(existingMerchant, cancellationToken);
        if (!ensureOperatorsResult.IsSuccess())
        {
            return ensureOperatorsResult;
        }

        var ensureContractsResult = await EnsureMerchantContractsAsync(existingMerchant, cancellationToken);
        if (!ensureContractsResult.IsSuccess())
        {
            return ensureContractsResult;
        }

        return Results.Ok();
    }

    private async Task<Result> EnsureMerchantDeviceAsync(Merchant merchant, MerchantResponse existingMerchant, CancellationToken cancellationToken)
    {
        if (existingMerchant.Devices.ContainsValue(merchant.Device.DeviceIdentifier))
        {
            return Results.Ok();
        }

        AddMerchantDeviceRequest addMerchantDeviceRequest = new() { DeviceIdentifier = merchant.Device.DeviceIdentifier };
        var addDeviceToMerchantResult = await this.TransactionProcessorClient.AddDeviceToMerchant(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, addMerchantDeviceRequest, cancellationToken);
        return addDeviceToMerchantResult;
    }

    private async Task<Result> EnsureMerchantUserAsync(Merchant merchant, MerchantResponse existingMerchant, CancellationToken cancellationToken) {
        var managementTokenResult = await this.SecurityServiceClient.GetToken(this.IdentityServerConfiguration.managementclientid, this.IdentityServerConfiguration.managementclientsecret, cancellationToken);

        if (managementTokenResult.Value is not Success<TokenResponse> { Value: { } managementToken })
            return Results.Fail("Error getting management token");

        var userResult = await this.SecurityServiceClient.GetUsers(managementToken.AccessToken, merchant.User.EmailAddress, cancellationToken);
        if (userResult.Value is not Success<List<UserResponse>> { Value: var users })
        {
            return userResult.WithoutPayload();
        }

        if (users is { Count: > 0 })
        {
            return Results.Ok();
        }

        CreateMerchantUserRequest createMerchantUserRequest = new() {
            EmailAddress = merchant.User.EmailAddress,
            FamilyName = merchant.User.FamilyName,
            GivenName = merchant.User.GivenName,
            MiddleName = merchant.User.MiddleName,
            Password = merchant.User.Password
        };
        var createMerchantUserResult = await this.TransactionProcessorClient.CreateMerchantUser(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, createMerchantUserRequest, cancellationToken);
        return createMerchantUserResult;
    }

    private async Task<Result> EnsureMerchantOperatorsAsync(MerchantResponse existingMerchant, CancellationToken cancellationToken)
    {
        var getEstateResult = await this.TransactionProcessorClient.GetEstate(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getEstateResult.Value is not Success<EstateResponse> { Value: var estate })
        {
            return getEstateResult.WithoutPayload();
        }

        existingMerchant.Operators ??= new List<MerchantOperatorResponse>();
        foreach (var @operator in estate.Operators)
        {
            if (existingMerchant.Operators.Any(o => o.OperatorId == @operator.OperatorId))
            {
                continue;
            }

            DataTransferObjects.Requests.Merchant.AssignOperatorRequest assignOperatorRequest = new() { OperatorId = @operator.OperatorId, MerchantNumber = null, TerminalNumber = null };
            var assignOperatorToMerchantResult = await this.TransactionProcessorClient.AssignOperatorToMerchant(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, assignOperatorRequest, cancellationToken);
            if (!assignOperatorToMerchantResult.IsSuccess())
            {
                return assignOperatorToMerchantResult;
            }
        }

        return Results.Ok();
    }

    private async Task<Result> EnsureMerchantContractsAsync(MerchantResponse existingMerchant, CancellationToken cancellationToken)
    {
        var getContractsResult = await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getContractsResult.Value is not Success<List<ContractResponse>> { Value: var contracts })
        {
            return getContractsResult.WithoutPayload();
        }

        var merchantContractsResult = await this.TransactionProcessorClient.GetMerchantContracts(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, cancellationToken);
        if (!merchantContractsResult.IsSuccess() && merchantContractsResult.GetErrors()[0].ErrorType != ErrorType.NotFound)
        {
            return merchantContractsResult.WithoutPayload();
        }

        var merchantContracts = (merchantContractsResult.Value as Success<List<ContractResponse>>)?.Value ?? [];

        foreach (ContractResponse contractResponse in contracts)
        {
            if (merchantContracts.Any(c => c.ContractId == contractResponse.ContractId))
            {
                continue;
            }

            AddMerchantContractRequest addMerchantContractRequest = new() { ContractId = contractResponse.ContractId };
            var addContractToMerchantResult = await this.TransactionProcessorClient.AddContractToMerchant(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, addMerchantContractRequest, cancellationToken);
            if (!addContractToMerchantResult.IsSuccess())
            {
                return addContractToMerchantResult;
            }
        }

        return Results.Ok();
    }

    private async Task<Result> CreateMerchants(CancellationToken cancellationToken) {

        var getMerchantsResult = await this.GetMerchants(cancellationToken);
        if (!getMerchantsResult.IsSuccess() && getMerchantsResult.GetErrors()[0].ErrorType != ErrorType.NotFound)
            return getMerchantsResult.WithoutPayload();

        var merchants = (getMerchantsResult.Value as Success<List<MerchantResponse>>)?.Value ?? [];
        foreach (Merchant merchant in this.EstateConfig.Merchants) {
            MerchantResponse existingMerchant = merchants.SingleOrDefault(m => m.MerchantName == merchant.Name);
            if (existingMerchant == null) {
                var createMerchantResult = await this.CreateMerchant(merchant, cancellationToken);
                if (!createMerchantResult.IsSuccess())
                    return createMerchantResult;
            }
            else {
                var updateMerchantResult = await this.UpdateMerchant(merchant, existingMerchant, cancellationToken);
                if (!updateMerchantResult.IsSuccess())
                    return updateMerchantResult;
            }
        }

        return Results.Ok();
    }

    private async Task<Result> CreateFloats(CancellationToken cancellationToken) {

        var getOperatorsResult = await this.TransactionProcessorClient.GetOperators(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getOperatorsResult.Value is not Success<List<OperatorResponse>> { Value: var operators })
            return getOperatorsResult.WithoutPayload();

        foreach (OperatorResponse operatorResponse in operators)
        {
            // Create the required floats
            CreateFloatRequest request = new CreateFloatRequest { OperatorId = operatorResponse.OperatorId, CreateDateTime = DateTime.Now };

            Result<Guid> createFloatResult = await this.TransactionProcessorClient.CreateFloat(this.TokenResponse.AccessToken, this.EstateId, request, cancellationToken);
            if (!createFloatResult.IsSuccess())
                return createFloatResult.WithoutPayload();
        }

        return Results.Ok();
    }
}

public static class Retry {
    #region Fields

    /// <summary>
    /// The default retry for
    /// </summary>
    private static readonly TimeSpan DefaultRetryFor = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The default retry interval
    /// </summary>
    private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(5);

    #endregion

    #region Methods

    /// <summary>
    /// Fors the specified action.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="retryFor">The retry for.</param>
    /// <param name="retryInterval">The retry interval.</param>
    /// <returns></returns>
    public static async Task For(Func<Task> action,
                                 TimeSpan? retryFor = null,
                                 TimeSpan? retryInterval = null) {
        DateTime startTime = DateTime.Now;
        Exception lastException = null;

        if (retryFor == null) {
            retryFor = Retry.DefaultRetryFor;
        }

        while (DateTime.Now.Subtract(startTime).TotalMilliseconds < retryFor.Value.TotalMilliseconds) {
            try {
                await action().ConfigureAwait(false);
                lastException = null;
                break;
            }
            catch (Exception e) {
                lastException = e;

                // wait before retrying
                Thread.Sleep(retryInterval ?? Retry.DefaultRetryInterval);
            }
        }

        if (lastException != null) {
            throw lastException;
        }
    }

    #endregion
}

