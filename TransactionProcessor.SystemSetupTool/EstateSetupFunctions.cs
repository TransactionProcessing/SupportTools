using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Exceptions;
using Shared.Results;
using SimpleResults;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects;
using TransactionProcessor.DataTransferObjects.Requests.Contract;
using TransactionProcessor.DataTransferObjects.Requests.Estate;
using TransactionProcessor.DataTransferObjects.Requests.Merchant;
using TransactionProcessor.DataTransferObjects.Requests.Operator;
using TransactionProcessor.DataTransferObjects.Responses.Contract;
using TransactionProcessor.DataTransferObjects.Responses.Estate;
using TransactionProcessor.DataTransferObjects.Responses.Merchant;
using TransactionProcessor.SystemSetupTool.estateconfig;
using Address = TransactionProcessor.DataTransferObjects.Requests.Merchant.Address;
using AssignOperatorRequest = TransactionProcessor.DataTransferObjects.Requests.Estate.AssignOperatorRequest;
using Contact = TransactionProcessor.DataTransferObjects.Requests.Merchant.Contact;
using Contract = TransactionProcessor.SystemSetupTool.estateconfig.Contract;
using ProductType = TransactionProcessor.SystemSetupTool.estateconfig.ProductType;

namespace TransactionProcessor.SystemSetupTool;

public class EstateSetupFunctions {
    private readonly ISecurityServiceClient SecurityServiceClient;

    private readonly ITransactionProcessorClient TransactionProcessorClient;

    private readonly Estate EstateConfig;

    private TokenResponse TokenResponse;

    private Guid EstateId;

    public EstateSetupFunctions(ISecurityServiceClient securityServiceClient,
                                ITransactionProcessorClient transactionProcessorClient,
                                Estate estateConfig) {
        this.SecurityServiceClient = securityServiceClient;
        this.TransactionProcessorClient = transactionProcessorClient;
        this.EstateConfig = estateConfig;
    }

    public async Task<Result> SetupEstate(CancellationToken cancellationToken) {
        var result = await this.SecurityServiceClient.GetToken("serviceClient", "d192cbc46d834d0da90e8a9d50ded543", CancellationToken.None);
        this.TokenResponse = result.Data;

        Result<Guid> createEstateResult = await this.CreateEstate(cancellationToken);
        if (createEstateResult.IsFailed)
            return ResultHelpers.CreateFailure(createEstateResult);
        this.EstateId = createEstateResult.Data;
        
        var createUserResult = await this.CreateEstateUser(cancellationToken);
        if (createUserResult.IsFailed)
            return ResultHelpers.CreateFailure(createUserResult);

        var createOperatorsResult = await this.CreateOperators(cancellationToken);
        if (createOperatorsResult.IsFailed)
            return ResultHelpers.CreateFailure(createOperatorsResult);

        var createContractsResult = await this.CreateContracts(cancellationToken);
        if (createContractsResult.IsFailed)
            return ResultHelpers.CreateFailure(createContractsResult);

        var createMerchantsResult = await this.CreateMerchants(cancellationToken);
        if (createMerchantsResult.IsFailed)
            return ResultHelpers.CreateFailure(createMerchantsResult);

        var createFloatsResult = await this.CreateFloats(cancellationToken);
        if (createFloatsResult.IsFailed)
            return ResultHelpers.CreateFailure(createFloatsResult);

        // Will only get here is everything was OK
        return Result.Success();
    }

    private async Task<Result<EstateResponse>> GetEstate(Guid estateId,
                                                         CancellationToken cancellationToken) {
        Result<EstateResponse> estateResponse = await this.TransactionProcessorClient.GetEstate(this.TokenResponse.AccessToken, estateId, cancellationToken);
        return estateResponse;
    }

    private async Task<Result<Guid>> CreateEstate(CancellationToken cancellationToken) {
        var estateResponse = await this.GetEstate(Guid.Parse(this.EstateConfig.Id), cancellationToken);
        if (estateResponse.IsFailed && estateResponse.Status != ResultStatus.NotFound)
            return ResultHelpers.CreateFailure(estateResponse);

        if (estateResponse.Status == ResultStatus.NotFound) {
            // need to create the actual estate
            // Create the estate
            CreateEstateRequest createEstateRequest = new CreateEstateRequest { EstateId = Guid.Parse(this.EstateConfig.Id), EstateName = this.EstateConfig.Name };

            var createResult = await this.TransactionProcessorClient.CreateEstate(this.TokenResponse.AccessToken, createEstateRequest, cancellationToken);
            if (createResult.IsFailed)
                return ResultHelpers.CreateFailure(createResult);

            return Result.Success(createEstateRequest.EstateId);
        }

        // Found the estate so just return the Guid
        return Result.Success(estateResponse.Data.EstateId);
    }

    private async Task<Result> CreateEstateUser(CancellationToken cancellationToken) {
        var estateResponse = await this.GetEstate(this.EstateId, cancellationToken);
        if (estateResponse.IsFailed)
            return ResultHelpers.CreateFailure(estateResponse);
        var estate = estateResponse.Data;

        SecurityUserResponse existingUser = estate.SecurityUsers.SingleOrDefault(u => u.EmailAddress == this.EstateConfig.User.EmailAddress);

        if (existingUser == null) {
            // Need to create the user
            // Create Estate user
            CreateEstateUserRequest createEstateUserRequest = new CreateEstateUserRequest {
                EmailAddress = this.EstateConfig.User.EmailAddress,
                FamilyName = this.EstateConfig.User.FamilyName,
                GivenName = this.EstateConfig.User.GivenName,
                MiddleName = this.EstateConfig.User.MiddleName,
                Password = this.EstateConfig.User.Password
            };
            return await this.TransactionProcessorClient.CreateEstateUser(this.TokenResponse.AccessToken, this.EstateId, createEstateUserRequest, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<Result> CreateOperators(CancellationToken cancellationToken) {
        var estateResponse = await this.GetEstate(this.EstateId, cancellationToken);
        if (estateResponse.IsFailed)
            return ResultHelpers.CreateFailure(estateResponse);

        var estate = estateResponse.Data;

        // Now do the operators
        foreach (Operator @operator in this.EstateConfig.Operators) {
            EstateOperatorResponse existingOperator = estate.Operators.SingleOrDefault(o => o.Name == @operator.Name);

            if (existingOperator != null) {
                continue;
            }

            CreateOperatorRequest createOperatorRequest = new CreateOperatorRequest {
                OperatorId = Guid.NewGuid(), Name = @operator.Name, RequireCustomMerchantNumber = @operator.RequireCustomMerchantNumber, RequireCustomTerminalNumber = @operator.RequireCustomMerchantNumber,
            };
            Result createResult = await this.TransactionProcessorClient.CreateOperator(this.TokenResponse.AccessToken, this.EstateId, createOperatorRequest, cancellationToken);
            if (createResult.IsFailed)
                return ResultHelpers.CreateFailure(createResult);

            // Now assign this to the estate
            var assignResult = await this.TransactionProcessorClient.AssignOperatorToEstate(this.TokenResponse.AccessToken, this.EstateId, new AssignOperatorRequest { OperatorId = createOperatorRequest.OperatorId }, cancellationToken);
            if (assignResult.IsFailed)
                return ResultHelpers.CreateFailure(assignResult);
        }

        return Result.Success();
    }

    private async Task<Result<Guid>> GetContractId(String contractDescription,
                                                   CancellationToken cancellationToken) {
        Guid contractId = Guid.Empty;
        await Retry.For(async () => {
            var existingContractsResult = await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
            if (existingContractsResult.IsFailed)
                throw new Exception("GetContracts failed");

            var c = existingContractsResult.Data.SingleOrDefault(c => c.Description == contractDescription);
            if (c == null) {
                throw new NotFoundException($"Contract with description {contractDescription} not found");
            }

            contractId = c.ContractId;
        });
        if (contractId == Guid.Empty)
            return Result.NotFound($"Contract with description {contractDescription} not found");

        return Result.Success(contractId);
    }

    private async Task<Result<Guid>> GetContractProductId(Guid contractId,
                                                          String productName,
                                                          CancellationToken cancellationToken) {


        Guid contractProductId = Guid.Empty;
        await Retry.For(async () => {

            var getContractResult = await this.TransactionProcessorClient.GetContract(this.TokenResponse.AccessToken, this.EstateId, contractId, cancellationToken);
            if (getContractResult.IsFailed)
                throw new Exception("GetContract failed");

            var cp = getContractResult.Data.Products.SingleOrDefault(p => p.Name == productName);
            if (cp == null) {
                throw new NotFoundException($"Contract Product with name {productName} not found on Contract Id {contractId}");
            }

            contractProductId = cp.ProductId;
        });
        if (contractProductId == Guid.Empty)
            return Result.NotFound($"Contract Product with name {productName} not found on Contract Id {contractId}");

        return Result.Success(contractProductId);
    }

    private async Task<Result> UpdateExistingContract(Contract contract,
                                                      ContractResponse existingContract,
                                                      CancellationToken cancellationToken) {
        var getContractResult = await this.TransactionProcessorClient.GetContract(this.TokenResponse.AccessToken, this.EstateId, existingContract.ContractId, cancellationToken);
        if (getContractResult.IsFailed)
            return ResultHelpers.CreateFailure(getContractResult);

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

                if (addContractProductResult.IsFailed)
                    return ResultHelpers.CreateFailure(addContractProductResult);

                Result<Guid> contractProductIdResult = await GetContractProductId(existingContract.ContractId, contractProduct.ProductName, cancellationToken);
                if (contractProductIdResult.IsFailed)
                    return ResultHelpers.CreateFailure(contractProductIdResult);

                foreach (var transactionFee in contractProduct.TransactionFees) {
                    Result addTransactionFeeForProductToContractResult = await this.TransactionProcessorClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken, this.EstateId, existingContract.ContractId, contractProductIdResult.Data, new AddTransactionFeeForProductToContractRequest { Value = transactionFee.Value, Description = transactionFee.Description, CalculationType = (CalculationType)transactionFee.CalculationType, FeeType = (FeeType)transactionFee.FeeType }, cancellationToken);

                    if (addTransactionFeeForProductToContractResult.IsFailed)
                        return ResultHelpers.CreateFailure(addTransactionFeeForProductToContractResult);
                }
            }
            else {
                var fullProduct = getContractResult.Data.Products.Single(p => p.Name == contractProduct.ProductName);

                foreach (var transactionFee in contractProduct.TransactionFees) {
                    var feeExists = fullProduct.TransactionFees.Any(p => p.CalculationType == (CalculationType)transactionFee.CalculationType && p.FeeType == (FeeType)transactionFee.FeeType && p.Description == transactionFee.Description && p.Value == transactionFee.Value);
                    if (feeExists == false) {
                        Result addTransactionFeeForProductToContractResult = await this.TransactionProcessorClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken, this.EstateId, existingContract.ContractId, product.ProductId, new AddTransactionFeeForProductToContractRequest { Value = transactionFee.Value, Description = transactionFee.Description, CalculationType = (CalculationType)transactionFee.CalculationType, FeeType = (FeeType)transactionFee.FeeType }, cancellationToken);

                        if (addTransactionFeeForProductToContractResult.IsFailed)
                            return ResultHelpers.CreateFailure(addTransactionFeeForProductToContractResult);
                    }
                }
            }
        }

        return Result.Success();
    }

    private async Task<Result> CreateNewContract(EstateResponse estate,
                                                 Contract contract,
                                                 CancellationToken cancellationToken) {
        var @operator = estate.Operators.SingleOrDefault(o => o.Name == contract.OperatorName);

        CreateContractRequest createContractRequest = new CreateContractRequest { Description = contract.Description, OperatorId = @operator.OperatorId };
        Result createResult = await this.TransactionProcessorClient.CreateContract(this.TokenResponse.AccessToken, this.EstateId, createContractRequest, cancellationToken);
        if (createResult.IsFailed)
            return ResultHelpers.CreateFailure(createResult);

        // Contract has been created, we now need to get it from the RM to get the contract Id
        Result<Guid> getContractIdResult = await GetContractId(contract.Description, cancellationToken);
        if (getContractIdResult.IsFailed)
            return ResultHelpers.CreateFailure(getContractIdResult);

        foreach (Product contractProduct in contract.Products) {
            AddProductToContractRequest addProductToContractRequest = new AddProductToContractRequest { DisplayText = contractProduct.DisplayText, ProductName = contractProduct.ProductName, Value = contractProduct.Value };

            Result addProductToContractResult = await this.TransactionProcessorClient.AddProductToContract(this.TokenResponse.AccessToken, this.EstateId, getContractIdResult.Data, addProductToContractRequest, cancellationToken);

            if (addProductToContractResult.IsFailed)
                return ResultHelpers.CreateFailure(addProductToContractResult);

            Result<Guid> contractProductIdResult = await GetContractProductId(getContractIdResult.Data, contractProduct.ProductName, cancellationToken);
            if (contractProductIdResult.IsFailed)
                return ResultHelpers.CreateFailure(contractProductIdResult);

            foreach (TransactionFee contractProductTransactionFee in contractProduct.TransactionFees) {
                AddTransactionFeeForProductToContractRequest addTransactionFeeForProductToContractRequest = new AddTransactionFeeForProductToContractRequest { Description = contractProductTransactionFee.Description, Value = contractProductTransactionFee.Value, CalculationType = (CalculationType)contractProductTransactionFee.CalculationType, FeeType = (FeeType)contractProductTransactionFee.FeeType };

                Result addTransactionFeeForProductToContractResult = await this.TransactionProcessorClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken, this.EstateId, getContractIdResult.Data, contractProductIdResult.Data, addTransactionFeeForProductToContractRequest, cancellationToken);

                if (addTransactionFeeForProductToContractResult.IsFailed)
                    return ResultHelpers.CreateFailure(addTransactionFeeForProductToContractResult);
            }
        }

        return Result.Success();
    }

    private async Task<Result> CreateContracts(CancellationToken cancellationToken) {
        var existingContractsResult= await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (existingContractsResult.IsFailed)
            return ResultHelpers.CreateFailure(existingContractsResult);
        var estateResponse = await this.GetEstate(Guid.Parse(this.EstateConfig.Id), cancellationToken);
        if (estateResponse.IsFailed)
            return ResultHelpers.CreateFailure(estateResponse);

        foreach (Contract contract in this.EstateConfig.Contracts) {
            // Is the contact created 
            ContractResponse existingContract = existingContractsResult.Data.SingleOrDefault(c => c.Description == contract.Description);
            if (existingContract == null) {
                // New contract
                Result createResult = await CreateNewContract(estateResponse.Data, contract, cancellationToken);
                if (createResult.IsFailed)
                    return ResultHelpers.CreateFailure(createResult);
            }
            else {
                // Update contract
                var updateResult = await UpdateExistingContract(contract, existingContract, cancellationToken);
                if (updateResult.IsFailed)
                    return ResultHelpers.CreateFailure(updateResult);
            }
        }

        return Result.Success();
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
            },
            Name = merchant.Name,
            Contact = new Contact { ContactName = merchant.Contact.ContactName, EmailAddress = merchant.Contact.EmailAddress },
            SettlementSchedule = settlementSchedule,
            CreatedDateTime = merchant.CreateDate,
            MerchantId = merchant.MerchantId,
        };
        Result createMerchantResult = await this.TransactionProcessorClient.CreateMerchant(this.TokenResponse.AccessToken, this.EstateId, createMerchantRequest, cancellationToken);
        if (createMerchantResult.IsFailed)
            return ResultHelpers.CreateFailure(createMerchantResult);

        // Now add devices
        AddMerchantDeviceRequest addMerchantDeviceRequest = new AddMerchantDeviceRequest { DeviceIdentifier = merchant.Device.DeviceIdentifier };
        var addDeviceResult = await this.TransactionProcessorClient.AddDeviceToMerchant(this.TokenResponse.AccessToken, this.EstateId, createMerchantRequest.MerchantId.Value, addMerchantDeviceRequest, cancellationToken);
        if (addDeviceResult.IsFailed)
            return ResultHelpers.CreateFailure(addDeviceResult);

        // Now security user
        CreateMerchantUserRequest createMerchantUserRequest = new CreateMerchantUserRequest {
            EmailAddress = merchant.User.EmailAddress,
            FamilyName = merchant.User.FamilyName,
            GivenName = merchant.User.GivenName,
            MiddleName = merchant.User.MiddleName,
            Password = merchant.User.Password
        };
        var createMerchantUserResult = await this.TransactionProcessorClient.CreateMerchantUser(this.TokenResponse.AccessToken, this.EstateId, createMerchantRequest.MerchantId.Value, createMerchantUserRequest, cancellationToken);
        if (createMerchantUserResult.IsFailed)
            return ResultHelpers.CreateFailure(createMerchantUserResult);


        var getEstateResult = await this.TransactionProcessorClient.GetEstate(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getEstateResult.IsFailed)
            return ResultHelpers.CreateFailure(getEstateResult);
        foreach (var @operator in getEstateResult.Data.Operators) {
            TransactionProcessor.DataTransferObjects.Requests.Merchant.AssignOperatorRequest assignOperatorRequest = new() { OperatorId = @operator.OperatorId, MerchantNumber = null, TerminalNumber = null };

            Result assignOperatorToMerchantResult = await this.TransactionProcessorClient.AssignOperatorToMerchant(this.TokenResponse.AccessToken, this.EstateId, createMerchantRequest.MerchantId.Value, assignOperatorRequest, cancellationToken);
            if (assignOperatorToMerchantResult.IsFailed)
                return ResultHelpers.CreateFailure(assignOperatorToMerchantResult);
        }

        var getContractsResult = await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getContractsResult.IsFailed)
            return ResultHelpers.CreateFailure(getContractsResult);

        // Now contracts
        foreach (ContractResponse contractResponse in getContractsResult.Data) {
            AddMerchantContractRequest addMerchantContractRequest = new() { ContractId = contractResponse.ContractId };
            var addContractToMerchantResult = await this.TransactionProcessorClient.AddContractToMerchant(this.TokenResponse.AccessToken, this.EstateId, createMerchantRequest.MerchantId.Value, addMerchantContractRequest, cancellationToken);
            if (addContractToMerchantResult.IsFailed)
                return ResultHelpers.CreateFailure(addContractToMerchantResult);
        }

        return Result.Success();
    }

    private async Task<Result> UpdateMerchant(Merchant merchant,
                                              MerchantResponse existingMerchant,
                                              CancellationToken cancellationToken) {
        // check the merchants device
        if (existingMerchant.Devices.ContainsValue(merchant.Device.DeviceIdentifier) == false) {
            AddMerchantDeviceRequest addMerchantDeviceRequest = new AddMerchantDeviceRequest { DeviceIdentifier = merchant.Device.DeviceIdentifier };
            var addDeviceToMerchantResult = await this.TransactionProcessorClient.AddDeviceToMerchant(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, addMerchantDeviceRequest, cancellationToken);
            if (addDeviceToMerchantResult.IsFailed)
                return ResultHelpers.CreateFailure(addDeviceToMerchantResult);
        }

        // Check the users
        var userResult = await this.SecurityServiceClient.GetUsers(merchant.User.EmailAddress, cancellationToken);
        if (userResult.IsFailed)
            return ResultHelpers.CreateFailure(userResult);
        if (userResult.Data == null) {
            CreateMerchantUserRequest createMerchantUserRequest = new CreateMerchantUserRequest {
                EmailAddress = merchant.User.EmailAddress,
                FamilyName = merchant.User.FamilyName,
                GivenName = merchant.User.GivenName,
                MiddleName = merchant.User.MiddleName,
                Password = merchant.User.Password
            };
            var createMerchantUserResult = await this.TransactionProcessorClient.CreateMerchantUser(this.TokenResponse.AccessToken, this.EstateId, merchant.MerchantId, createMerchantUserRequest, cancellationToken);
            if (createMerchantUserResult.IsFailed)
                return ResultHelpers.CreateFailure(createMerchantUserResult);
        }

        var getEstateResult = await this.TransactionProcessorClient.GetEstate(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getEstateResult.IsFailed)
            return ResultHelpers.CreateFailure(getEstateResult);

        foreach (var @operator in getEstateResult.Data.Operators) {
            if (existingMerchant.Operators == null) {
                existingMerchant.Operators = new List<MerchantOperatorResponse>();
            }

            var merchantOperator = existingMerchant.Operators.SingleOrDefault(o => o.OperatorId == @operator.OperatorId);
            if (merchantOperator != null)
                continue;

            DataTransferObjects.Requests.Merchant.AssignOperatorRequest assignOperatorRequest = new() { OperatorId = @operator.OperatorId, MerchantNumber = null, TerminalNumber = null };

            var assignOperatorToMerchantResult = await this.TransactionProcessorClient.AssignOperatorToMerchant(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, assignOperatorRequest, cancellationToken);
            if (assignOperatorToMerchantResult.IsFailed)
                return ResultHelpers.CreateFailure(assignOperatorToMerchantResult);
        }

        var getContractsResult = await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getContractsResult.IsFailed)
            return ResultHelpers.CreateFailure(getContractsResult);

        var merchantContractsResult = await this.TransactionProcessorClient.GetMerchantContracts(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, cancellationToken);
        if (merchantContractsResult.IsFailed && merchantContractsResult.Status != ResultStatus.NotFound)
            return ResultHelpers.CreateFailure(merchantContractsResult);
        List<ContractResponse> merchantContracts = merchantContractsResult.Data;
        if (merchantContractsResult.Status == ResultStatus.NotFound) {
            merchantContracts = new List<ContractResponse>();
        }

        // Now contracts
            foreach (ContractResponse contractResponse in getContractsResult.Data) {
            if (merchantContracts.SingleOrDefault(c => c.ContractId == contractResponse.ContractId) != null)
                continue;

            AddMerchantContractRequest addMerchantContractRequest = new() { ContractId = contractResponse.ContractId };
            var addContractToMerchantResult = await this.TransactionProcessorClient.AddContractToMerchant(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, addMerchantContractRequest, cancellationToken);

            if (addContractToMerchantResult.IsFailed)
                return ResultHelpers.CreateFailure(addContractToMerchantResult);
        }

        return Result.Success();
    }

    private async Task<Result> CreateMerchants(CancellationToken cancellationToken) {

        var getMerchantsResult = await this.GetMerchants(cancellationToken);
        if (getMerchantsResult.IsFailed && getMerchantsResult.Status != ResultStatus.NotFound)
            return ResultHelpers.CreateFailure(getMerchantsResult);

        var merchants = getMerchantsResult.Data == null ? new List<MerchantResponse>() : getMerchantsResult.Data;
        foreach (Merchant merchant in this.EstateConfig.Merchants) {
            MerchantResponse existingMerchant = merchants.SingleOrDefault(m => m.MerchantName == merchant.Name);
            if (existingMerchant == null) {
                var createMerchantResult = await this.CreateMerchant(merchant, cancellationToken);
                if (createMerchantResult.IsFailed)
                    return ResultHelpers.CreateFailure(createMerchantResult);
            }
            else {
                var updateMerchantResult = await this.UpdateMerchant(merchant, existingMerchant, cancellationToken);
                if (updateMerchantResult.IsFailed)
                    return ResultHelpers.CreateFailure(updateMerchantResult);
            }
        }

        return Result.Success();
    }

    private async Task<Result> CreateFloats(CancellationToken cancellationToken) {

        var getContractsResult = await this.TransactionProcessorClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
        if (getContractsResult.IsFailed)
            return ResultHelpers.CreateFailure(getContractsResult);

        foreach (ContractResponse contractResponse in getContractsResult.Data) {
            foreach (ContractProduct contractProduct in contractResponse.Products) {
                
                // Create the required floats
                CreateFloatForContractProductRequest request = new CreateFloatForContractProductRequest { ContractId = contractResponse.ContractId, ProductId = contractProduct.ProductId, CreateDateTime = DateTime.Now };
                
                // TODO: Need a way to verify the float exists
                Result createFloatResult = await this.TransactionProcessorClient.CreateFloatForContractProduct(this.TokenResponse.AccessToken, this.EstateId, request, cancellationToken);
                if (createFloatResult.IsFailed)
                    return ResultHelpers.CreateFailure(createFloatResult);
            }
        }

        return Result.Success();
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

