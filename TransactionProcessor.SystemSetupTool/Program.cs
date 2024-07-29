using System;

namespace TransactionProcessor.SystemSetupTool
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Client;
    using DataTransferObjects;
    using estateconfig;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Requests.Contract;
    using EstateManagement.DataTransferObjects.Requests.Estate;
    using EstateManagement.DataTransferObjects.Requests.Merchant;
    using EstateManagement.DataTransferObjects.Requests.Operator;
    using EstateManagement.DataTransferObjects.Responses.Contract;
    using EstateManagement.DataTransferObjects.Responses.Estate;
    using EstateManagement.DataTransferObjects.Responses.Merchant;
    using EstateManagement.DataTransferObjects.Responses.Operator;
    using identityserverconfig;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Requests;
    using SecurityService.DataTransferObjects.Responses;
    using EventStore.Client;
    using Microsoft.Extensions.Configuration;
    using Newtonsoft.Json;
    using Shared.General;
    using AssignOperatorRequest = EstateManagement.DataTransferObjects.Requests.Estate.AssignOperatorRequest;
    using JsonSerializer = System.Text.Json.JsonSerializer;
    using SettlementSchedule = EstateManagement.DataTransferObjects.Responses.Merchant.SettlementSchedule;
    using Microsoft.AspNetCore.Http.HttpResults;
    using Microsoft.IdentityModel.Tokens;
    using ProductType = EstateManagement.DataTransferObjects.Responses.Contract.ProductType;

    class Program
    {
        private static EstateClient EstateClient;
        private static TransactionProcessorClient TransactionProcessorClient;
        private static HttpClient HttpClient;

        private static SecurityServiceClient SecurityServiceClient;

        private static EventStoreProjectionManagementClient ProjectionClient;

        private static EventStorePersistentSubscriptionsClient PersistentSubscriptionsClient;

        private static TokenResponse TokenResponse;
        
        static async Task Main(string[] args)
        {
            CancellationToken cancellationToken = new CancellationToken();

            IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            IConfigurationRoot configurationRoot = builder.Build();
            ConfigurationReader.Initialise(configurationRoot);

            Func<String, String> estateResolver = s => { return ConfigurationReader.GetValue("EstateManagementUri"); };
            Func<String, String> securityResolver = s => { return ConfigurationReader.GetValue("SecurityServiceUri"); };
            Func<String, String> transactionProcessorResolver = s => { return ConfigurationReader.GetValue("TransactionProcessorApi"); };
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
            HttpClient client = new HttpClient(handler);
            Program.HttpClient = new HttpClient(handler);

            Program.EstateClient = new EstateClient(estateResolver, client);
            Program.SecurityServiceClient = new SecurityServiceClient(securityResolver, client);
            Program.TransactionProcessorClient = new TransactionProcessorClient(transactionProcessorResolver, client);
            EventStoreClientSettings settings = EventStoreClientSettings.Create(ConfigurationReader.GetValue("EventStoreAddress"));
            Program.ProjectionClient = new EventStoreProjectionManagementClient(settings);
            Program.PersistentSubscriptionsClient = new EventStorePersistentSubscriptionsClient(settings);

            Boolean isEstateSetup = true;

            String configFileName = "setupconfig.json";

            if (isEstateSetup == false){

                IdentityServerConfiguration identityServerConfiguration = await Program.GetIdentityServerConfig(cancellationToken);
                IdentityServerFunctions identityServerFunctions = new IdentityServerFunctions(Program.SecurityServiceClient, identityServerConfiguration);
                await identityServerFunctions.CreateConfig(cancellationToken);

                EventStoreFunctions eventStoreFunctions = new EventStoreFunctions(Program.ProjectionClient, Program.PersistentSubscriptionsClient);
                await eventStoreFunctions.SetupEventStore(cancellationToken);
            }
            else{
                EstateConfig estateConfiguration = await GetEstatesConfig(configFileName, cancellationToken);

                foreach (Estate estate in estateConfiguration.Estates){
                    EstateSetupFunctions estateSetup = new EstateSetupFunctions(Program.SecurityServiceClient, Program.EstateClient, Program.TransactionProcessorClient, estate);
                    await estateSetup.SetupEstate(cancellationToken);
                }
            }
        }

        private static async Task<IdentityServerConfiguration> GetIdentityServerConfig(CancellationToken cancellationToken)
        {
            // Read the identity server config json string
            String identityServerJsonData = null;
            using(StreamReader sr = new StreamReader("identityserverconfig.json"))
            {
                identityServerJsonData = await sr.ReadToEndAsync(cancellationToken);
            }

            IdentityServerConfiguration identityServerConfiguration = JsonSerializer.Deserialize<IdentityServerConfiguration>(identityServerJsonData);

            return identityServerConfiguration;
        }
        
        private static async Task<EstateConfig> GetEstatesConfig(String configFileName, CancellationToken cancellationToken)
        {
            // Read the estate config json string
            String estateJsonData = null;
            using(StreamReader sr = new StreamReader(configFileName))
            {
                estateJsonData = await sr.ReadToEndAsync(cancellationToken);
            }
            
            EstateConfig estateConfiguration = JsonSerializer.Deserialize<EstateConfig>(estateJsonData);
            return estateConfiguration;
        }
        
    }

    public class IdentityServerFunctions{
        private readonly ISecurityServiceClient SecurityServiceClient;

        private readonly IdentityServerConfiguration identityServerConfiguration;

        public IdentityServerFunctions(ISecurityServiceClient securityServiceClient, IdentityServerConfiguration configuration){
            this.SecurityServiceClient = securityServiceClient;
            this.identityServerConfiguration = configuration;
        }

        public async Task CreateConfig(CancellationToken cancellationToken){

            var roles = await this.SecurityServiceClient.GetRoles(cancellationToken);
            if (roles == null)
                roles = new List<RoleDetails>();

            foreach (String role in identityServerConfiguration.roles)
            {
                if (roles.Any(r => r.RoleName == role))
                    continue;
                await this.CreateRole(role, CancellationToken.None);
            }

            var apiResources = await this.SecurityServiceClient.GetApiResources(cancellationToken);
            if (apiResources == null)
                apiResources = new List<ApiResourceDetails>();
            foreach (ApiResource apiResource in identityServerConfiguration.apiresources)
            {
                if (apiResources.Any(a => a.Name == apiResource.name))
                    continue;
                await this.CreateApiResource(apiResource, CancellationToken.None);
            }

            var identityResources = await this.SecurityServiceClient.GetIdentityResources(cancellationToken);
            if (identityResources == null)
                identityResources = new List<IdentityResourceDetails>();

            foreach (IdentityResource identityResource in identityServerConfiguration.identityresources)
            {
                if (identityResources.Any(i => i.Name == identityResource.name))
                    continue;
                await this.CreateIdentityResource(identityResource, CancellationToken.None);
            }

            var clients = await this.SecurityServiceClient.GetClients(cancellationToken);
            if(clients ==null)
                clients = new List<ClientDetails>();
            foreach (Client client in identityServerConfiguration.clients)
            {
                if (clients.Any(c => c.ClientId == client.client_id))
                    continue;
                await this.CreateClient(client, CancellationToken.None);
            }

            var apiScopes = await this.SecurityServiceClient.GetApiScopes(cancellationToken);
            if(apiScopes == null)
                apiScopes = new List<ApiScopeDetails>();
            foreach (ApiScope apiscope in identityServerConfiguration.apiscopes)
            {
                if (apiScopes.Any(a => a.Name== apiscope.name))
                    continue;
                await this.CreateApiScope(apiscope, CancellationToken.None);
            }
        }

        private async Task CreateRole(String role, CancellationToken cancellationToken){
            
            CreateRoleRequest createRoleRequest = new CreateRoleRequest
            {
                RoleName = role
            };

            await this.SecurityServiceClient.CreateRole(createRoleRequest, cancellationToken);
        }

        private async Task CreateApiScope(ApiScope apiscope,
                                                 CancellationToken cancellationToken)
        {
            CreateApiScopeRequest createApiScopeRequest = new CreateApiScopeRequest
            {
                Description = apiscope.description,
                DisplayName = apiscope.display_name,
                Name = apiscope.name
            };

            await this.SecurityServiceClient.CreateApiScope(createApiScopeRequest, cancellationToken);
        }

        private async Task CreateIdentityResource(IdentityResource identityResource,
                                                         CancellationToken cancellationToken)
        {
            CreateIdentityResourceRequest createIdentityResourceRequest = new CreateIdentityResourceRequest
            {
                Claims = identityResource.claims,
                Description = identityResource.description,
                DisplayName = identityResource.displayName,
                Emphasize = identityResource.emphasize,
                Name = identityResource.name,
                Required = identityResource.required,
                ShowInDiscoveryDocument = identityResource.showInDiscoveryDocument
            };

            await this.SecurityServiceClient.CreateIdentityResource(createIdentityResourceRequest, cancellationToken);
        }

        private async Task CreateClient(Client client, CancellationToken cancellationToken)
        {
            CreateClientRequest createClientRequest = new CreateClientRequest
            {
                AllowOfflineAccess = client.allow_offline_access.GetValueOrDefault(false),
                AllowedGrantTypes = client.allowed_grant_types,
                AllowedScopes = client.allowed_scopes,
                ClientDescription = client.client_description,
                ClientId = client.client_id,
                ClientName = client.client_name,
                ClientPostLogoutRedirectUris = client.client_post_logout_redirect_uris,
                ClientRedirectUris = client.client_redirect_uris,
                RequireConsent = client.require_consent.GetValueOrDefault(false),
                Secret = client.secret
            };
            await this.SecurityServiceClient.CreateClient(createClientRequest, cancellationToken);
        }

        private async Task CreateApiResource(ApiResource apiResource,
                                            CancellationToken cancellationToken)
        {
            CreateApiResourceRequest createApiResourceRequest = new CreateApiResourceRequest
            {
                Secret = apiResource.secret,
                Description = apiResource.description,
                DisplayName = apiResource.display_name,
                Name = apiResource.name,
                Scopes = apiResource.scopes,
                UserClaims = apiResource.user_claims
            };

            await this.SecurityServiceClient.CreateApiResource(createApiResourceRequest, cancellationToken);
        }
    }

    public class EventStoreFunctions{
        private readonly EventStoreProjectionManagementClient ProjectionClient;

        private readonly EventStorePersistentSubscriptionsClient PersistentSubscriptionsClient;

        public EventStoreFunctions(EventStoreProjectionManagementClient projectionClient,EventStorePersistentSubscriptionsClient persistentSubscriptionsClient){
            this.ProjectionClient = projectionClient;
            this.PersistentSubscriptionsClient = persistentSubscriptionsClient;
        }

        private static PersistentSubscriptionSettings CreatePersistentSettings(Int32 retryCount = 0) => new PersistentSubscriptionSettings(resolveLinkTos: true, maxRetryCount: retryCount, startFrom:new StreamPosition(0));

        public async Task SetupEventStore(CancellationToken cancellationToken)
        {
            await this.DeployProjections(cancellationToken);
            await this.SetupSubscriptions(cancellationToken);
        }

        private async Task SetupSubscriptions(CancellationToken cancellationToken){
            List<(String streamName, String groupName, Int32 retryCount)> subscriptions = new List<(String streamName, String groupName, Int32 retryCount)>();
            subscriptions.Add(("$ce-TransactionAggregate", "Transaction Processor", 0));
            subscriptions.Add(("$ce-SettlementAggregate", "Transaction Processor", 0));
            subscriptions.Add(("$ce-VoucherAggregate", "Transaction Processor", 0));
            subscriptions.Add(("$ce-FloatAggregate", "Transaction Processor", 0));

            subscriptions.Add(("$ce-EstateAggregate", "Transaction Processor - Ordered", 1));
            subscriptions.Add(("$ce-SettlementAggregate", "Transaction Processor - Ordered", 1));
            subscriptions.Add(("$ce-VoucherAggregate", "Transaction Processor - Ordered", 1));
            
            subscriptions.Add(("$ce-TransactionAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-SettlementAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-VoucherAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-MerchantStatementAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-ContractAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-EstateAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-MerchantAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-CallbackMessageAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-ReconciliationAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-FileAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-FileImportLogAggregate", "Estate Management", 0));
            subscriptions.Add(("$ce-OperatorAggregate", "Estate Management", 0));

            subscriptions.Add(("$ce-TransactionAggregate", "Estate Management - Ordered", 0));
            subscriptions.Add(("$ce-MerchantStatementAggregate", "Estate Management - Ordered", 0));
            subscriptions.Add(("$ce-EstateAggregate", "Estate Management - Ordered", 0));
            subscriptions.Add(("$ce-FileAggregate", "File Processor", 0));
            subscriptions.Add(("$ce-FileImportLogAggregate", "File Processor", 0));
            subscriptions.Add(("$ce-EmailAggregate", "Messaging Service", 0));
            subscriptions.Add(("$ce-SMSAggregate", "Messaging Service", 0));

            foreach ((String streamName, String groupName, Int32 retryCount) subscription in subscriptions){
                Boolean exists = false;
                try{
                    var x = await PersistentSubscriptionsClient.GetInfoToStreamAsync(subscription.streamName, subscription.groupName, cancellationToken: cancellationToken, deadline:TimeSpan.FromSeconds(30));
                    exists = true;
                }
                catch(PersistentSubscriptionNotFoundException pex){
                    exists = false;
                }

                if (exists == false){
                    await PersistentSubscriptionsClient.CreateToStreamAsync(subscription.streamName, subscription.groupName, CreatePersistentSettings(subscription.retryCount), cancellationToken: cancellationToken, deadline: TimeSpan.FromSeconds(30));
                }
            }
            
            
            
            
        }
        private async Task DeployProjections(CancellationToken cancellationToken)
        {
            var currentProjections = await this.ProjectionClient.ListAllAsync(cancellationToken: cancellationToken).ToListAsync(cancellationToken);

            var projectionsToDeploy = Directory.GetFiles("projections/continuous");

            foreach (var projection in projectionsToDeploy)
            {
                if (projection.Contains("EstateManagementSubscriptionStreamBuilder") ||
                    projection.Contains("FileProcessorSubscriptionStreamBuilder") ||
                    projection.Contains("TransactionProcessorSubscriptionStreamBuilder"))
                {
                    continue;
                }

                FileInfo f = new FileInfo(projection);
                String name = f.Name.Substring(0, f.Name.Length - (f.Name.Length - f.Name.LastIndexOf(".")));
                var body = File.ReadAllText(f.FullName);

                var x = body.IndexOf("//endtestsetup");
                x = x + "//endtestsetup".Length;

                body = body.Substring(x);

                // Is this already deployed (in the master list)
                if (currentProjections.Any(p => p.Name == name) == false)
                {
                    // Projection does not exist so create
                    await ProjectionClient.CreateContinuousAsync(name, body, true, cancellationToken: cancellationToken);
                }
                else
                {
                    // Already exists so we need to update but do not reset
                    await ProjectionClient.DisableAsync(name, cancellationToken: cancellationToken);
                    await ProjectionClient.UpdateAsync(name, body, true, cancellationToken: cancellationToken);
                    await ProjectionClient.EnableAsync(name, cancellationToken: cancellationToken);
                }
            }
        }
    }

    public class EstateSetupFunctions{
        private readonly ISecurityServiceClient SecurityServiceClient;

        private readonly IEstateClient EstateClient;

        private readonly ITransactionProcessorClient TransactionProcessorClient;

        private readonly Estate EstateConfig;

        private TokenResponse TokenResponse;

        private Guid EstateId;

        public EstateSetupFunctions(ISecurityServiceClient securityServiceClient, IEstateClient estateClient,ITransactionProcessorClient transactionProcessorClient, Estate estateConfig){
            this.SecurityServiceClient = securityServiceClient;
            this.EstateClient = estateClient;
            this.TransactionProcessorClient = transactionProcessorClient;
            this.EstateConfig = estateConfig;
        }

        public async Task SetupEstate(CancellationToken cancellationToken){
            this.TokenResponse = await this.SecurityServiceClient.GetToken("serviceClient", "d192cbc46d834d0da90e8a9d50ded543", CancellationToken.None);

            this.EstateId = await this.CreateEstate(cancellationToken);
            await this.CreateEstateUser(cancellationToken);
            await this.CreateOperators(cancellationToken);
            await this.CreateContracts(cancellationToken);
            await this.CreateMerchants(cancellationToken);
            await this.CreateFloats(cancellationToken);
        }

        private async Task<EstateResponse> GetEstate(Guid estateId, CancellationToken cancellationToken){
            try{
                EstateResponse estateResponse = await this.EstateClient.GetEstate(this.TokenResponse.AccessToken,
                                                                                  Guid.Parse(this.EstateConfig.Id),
                                                                                  cancellationToken);
                return estateResponse;
            }
            catch(Exception k){
                if (k.InnerException != null && k.InnerException is KeyNotFoundException){
                    return null;
                }

                throw;
            }
        }

        private async Task<Guid> CreateEstate(CancellationToken cancellationToken){
            CreateEstateResponse createEstateResponse = null;
            EstateResponse estateResponse = await this.GetEstate(Guid.Parse(this.EstateConfig.Id),
                                                                 cancellationToken);
            if (estateResponse != null){
                createEstateResponse = new CreateEstateResponse{
                                                                   EstateId = estateResponse.EstateId
                                                               };
            }
            else{

                // Create the estate
                CreateEstateRequest createEstateRequest = new CreateEstateRequest{
                                                                                     EstateId = String.IsNullOrEmpty(this.EstateConfig.Id) ? Guid.NewGuid() : Guid.Parse(this.EstateConfig.Id),
                                                                                     EstateName = this.EstateConfig.Name
                                                                                 };

                createEstateResponse = await this.EstateClient.CreateEstate(this.TokenResponse.AccessToken, createEstateRequest, cancellationToken);
            }

            return createEstateResponse.EstateId;
        }

        private async Task CreateEstateUser(CancellationToken cancellationToken){
            EstateResponse estate = await this.GetEstate(this.EstateId, cancellationToken);

            SecurityUserResponse existingUser = estate.SecurityUsers.SingleOrDefault(u => u.EmailAddress == this.EstateConfig.User.EmailAddress);

            if (existingUser == null){
                // Need to create the user
                // Create Estate user
                CreateEstateUserRequest createEstateUserRequest = new CreateEstateUserRequest{
                                                                                                 EmailAddress = this.EstateConfig.User.EmailAddress,
                                                                                                 FamilyName = this.EstateConfig.User.FamilyName,
                                                                                                 GivenName = this.EstateConfig.User.GivenName,
                                                                                                 MiddleName = this.EstateConfig.User.MiddleName,
                                                                                                 Password = this.EstateConfig.User.Password
                                                                                             };
                await this.EstateClient.CreateEstateUser(this.TokenResponse.AccessToken,
                                                         this.EstateId,
                                                         createEstateUserRequest,
                                                         cancellationToken);
            }


        }

        private async Task CreateOperators(CancellationToken cancellationToken){
            EstateResponse estate = await this.GetEstate(this.EstateId, cancellationToken);
            
            // Now do the operators
            foreach (Operator @operator in this.EstateConfig.Operators){
                EstateOperatorResponse existingOperator = estate.Operators.SingleOrDefault(o => o.Name == @operator.Name);

                if (existingOperator != null){

                    continue;
                }

                CreateOperatorRequest createOperatorRequest = new CreateOperatorRequest
                                                              {
                                                                  OperatorId = Guid.NewGuid(),
                                                                  Name = @operator.Name,
                                                                  RequireCustomMerchantNumber = @operator.RequireCustomMerchantNumber,
                                                                  RequireCustomTerminalNumber = @operator.RequireCustomMerchantNumber,
                                                              };
                CreateOperatorResponse createOperatorResponse = await this.EstateClient.CreateOperator(this.TokenResponse.AccessToken,
                                                                                                       this.EstateId,
                                                                                                          createOperatorRequest,
                                                                                                          cancellationToken);
                // Now assign this to the estate
                await this.EstateClient.AssignOperatorToEstate(this.TokenResponse.AccessToken,
                                                                  this.EstateId,
                                                                  new AssignOperatorRequest
                                                                  {
                                                                      OperatorId = createOperatorResponse.OperatorId
                                                                  },
                                                                  cancellationToken);
            }
        }

        private async Task<ContractResponse> GetContract(Guid contractId, CancellationToken cancellationToken){
            try
            {
                ContractResponse fullContract = await this.EstateClient.GetContract(this.TokenResponse.AccessToken,
                                                                                    this.EstateId,
                                                                                    contractId, cancellationToken);
                return fullContract;
            }
            catch (Exception k)
            {
                if (k.InnerException != null && k.InnerException is KeyNotFoundException)
                {
                    return null;
                }

                throw;
            }
        }

        private async Task CreateContracts(CancellationToken cancellationToken){
            List<ContractResponse> existingContracts = await this.EstateClient.GetContracts(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
            EstateResponse esatate = await this.EstateClient.GetEstate(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);

            foreach (Contract contract in this.EstateConfig.Contracts)
            {
                // Is the contact created 
                ContractResponse existingContract = existingContracts.SingleOrDefault(c => c.Description == contract.Description);

                if (existingContract == null){

                    var @operator = esatate.Operators.SingleOrDefault(o => o.Name == contract.OperatorName);
                    
                    CreateContractRequest createContractRequest = new CreateContractRequest{
                                                                                               Description = contract.Description,
                                                                                               OperatorId = @operator.OperatorId
                                                                                           };
                    CreateContractResponse createContractResponse = await this.EstateClient.CreateContract(this.TokenResponse.AccessToken, this.EstateId, 
                                                                                                           createContractRequest, cancellationToken);
                    //createdContracts.Add(createContractResponse);

                    foreach (Product contractProduct in contract.Products){
                        AddProductToContractRequest addProductToContractRequest = new AddProductToContractRequest{
                                                                                                                     DisplayText = contractProduct.DisplayText,
                                                                                                                     ProductName = contractProduct.ProductName,
                                                                                                                     Value = contractProduct.Value
                                                                                                                 };

                        AddProductToContractResponse addProductToContractResponse = await this.EstateClient.AddProductToContract(this.TokenResponse.AccessToken,
                                                                                                                                    this.EstateId,
                                                                                                                                    createContractResponse.ContractId,
                                                                                                                                    addProductToContractRequest,
                                                                                                                                    cancellationToken);
                        //contractProductResponses.Add((addProductToContractRequest, addProductToContractResponse));


                        foreach (TransactionFee contractProductTransactionFee in contractProduct.TransactionFees){
                            AddTransactionFeeForProductToContractRequest addTransactionFeeForProductToContractRequest = new AddTransactionFeeForProductToContractRequest{
                                                                                                                                                                            Description = contractProductTransactionFee.Description,
                                                                                                                                                                            Value = contractProductTransactionFee.Value,
                                                                                                                                                                            CalculationType = (CalculationType)contractProductTransactionFee.CalculationType,
                                                                                                                                                                            FeeType = (FeeType)contractProductTransactionFee.FeeType
                                                                                                                                                                        };

                            await this.EstateClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken,
                                                                                             this.EstateId,
                                                                                             createContractResponse.ContractId,
                                                                                             addProductToContractResponse.ProductId,
                                                                                             addTransactionFeeForProductToContractRequest,
                                                                                             cancellationToken);
                        }
                    }
                }
                else{
                    var fullContract = await this.GetContract(existingContract.ContractId, cancellationToken);

                    // Now we need to check if all the products are created
                    foreach (Product contractProduct in contract.Products){
                        var product = existingContract.Products.SingleOrDefault(p => p.Name == contractProduct.ProductName);

                        if (product == null){
                            var addContractProductResponse = await this.EstateClient.AddProductToContract(this.TokenResponse.AccessToken,
                                                                   this.EstateId,
                                                                   existingContract.ContractId,
                                                                   new AddProductToContractRequest{
                                                                                                      ProductName = contractProduct.ProductName,
                                                                                                      DisplayText = contractProduct.DisplayText,
                                                                                                      Value = contractProduct.Value,
                                                                                                      ProductType = Enum.Parse<ProductType>(contractProduct.ProductType.ToString())
                                                                                                  }, cancellationToken);

                            foreach (var transactionFee in contractProduct.TransactionFees){
                                await this.EstateClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken,
                                                                                              this.EstateId, existingContract.ContractId,
                                                                                              addContractProductResponse.ProductId,
                                                                                              new AddTransactionFeeForProductToContractRequest{
                                                                                                                                                  Value = transactionFee.Value,
                                                                                                                                                  Description = transactionFee.Description,
                                                                                                                                                  CalculationType = (CalculationType)transactionFee.CalculationType,
                                                                                                                                                  FeeType = (FeeType)transactionFee.FeeType
                                                                                                                                              },
                                                                                              cancellationToken);
                            }
                        }
                        else{
                            var fullProduct = fullContract.Products.Single(p => p.Name == contractProduct.ProductName);

                            foreach (var transactionFee in contractProduct.TransactionFees){
                                var feeExists = fullProduct.TransactionFees.Any(p => p.CalculationType == (CalculationType)transactionFee.CalculationType &&
                                                                                     p.FeeType == (FeeType)transactionFee.FeeType &&
                                                                                     p.Description == transactionFee.Description &&
                                                                                     p.Value == transactionFee.Value);
                                if (feeExists == false){
                                    await this.EstateClient.AddTransactionFeeForProductToContract(this.TokenResponse.AccessToken,
                                                                                                  this.EstateId,
                                                                                                  existingContract.ContractId,
                                                                                                  product.ProductId,
                                                                                                  new AddTransactionFeeForProductToContractRequest{
                                                                                                                                                      Value = transactionFee.Value,
                                                                                                                                                      Description = transactionFee.Description,
                                                                                                                                                      CalculationType = (CalculationType)transactionFee.CalculationType,
                                                                                                                                                      FeeType = (FeeType)transactionFee.FeeType
                                                                                                                                                  },
                                                                                                  cancellationToken);
                                }
                            }
                        }
                    }
                }
            }

        }

        private async Task<List<MerchantResponse>> GetMerchants(CancellationToken cancellationToken){
            
            try
            {
                List<MerchantResponse> merchants = await this.EstateClient.GetMerchants(this.TokenResponse.AccessToken, this.EstateId, cancellationToken);
                return merchants;
            }
            catch (Exception k)
            {
                if (k.InnerException != null && k.InnerException is KeyNotFoundException)
                {
                    return new List<MerchantResponse>();
                }

                throw;
            }

        }

        private async Task CreateMerchants(CancellationToken cancellationToken){

            var merchants = await this.GetMerchants(cancellationToken);

            foreach (Merchant merchant in this.EstateConfig.Merchants){
                var existingMerchant = merchants.SingleOrDefault(m => m.MerchantName == merchant.Name);

                if (existingMerchant == null){

                    EstateManagement.DataTransferObjects.Responses.Merchant.SettlementSchedule settlementSchedule = Enum.Parse<EstateManagement.DataTransferObjects.Responses.Merchant.SettlementSchedule>(merchant.SettlementSchedule);

                    CreateMerchantRequest createMerchantRequest = new CreateMerchantRequest{
                                                                                               Address = new EstateManagement.DataTransferObjects.Requests.Merchant.Address{
                                                                                                                                                                               AddressLine1 = merchant.Address.AddressLine1,
                                                                                                                                                                               Country = merchant.Address.Country,
                                                                                                                                                                               Region = merchant.Address.Region,
                                                                                                                                                                               Town = merchant.Address.Town,
                                                                                                                                                                           },
                                                                                               Name = merchant.Name,
                                                                                               Contact = new EstateManagement.DataTransferObjects.Requests.Merchant.Contact{
                                                                                                                                                                               ContactName = merchant.Contact.ContactName,
                                                                                                                                                                               EmailAddress = merchant.Contact.EmailAddress
                                                                                                                                                                           },
                                                                                               SettlementSchedule = settlementSchedule,
                                                                                               CreatedDateTime = merchant.CreateDate,
                                                                                               MerchantId = merchant.MerchantId,
                                                                                           };
                    CreateMerchantResponse merchantResponse = await this.EstateClient.CreateMerchant(this.TokenResponse.AccessToken, 
                                                                                                    this.EstateId, createMerchantRequest, cancellationToken);

                    // Now add devices
                    AddMerchantDeviceRequest addMerchantDeviceRequest = new AddMerchantDeviceRequest{
                                                                                                        DeviceIdentifier = merchant.Device.DeviceIdentifier
                                                                                                    };
                    await this.EstateClient.AddDeviceToMerchant(this.TokenResponse.AccessToken,
                                                                   this.EstateId,
                                                                   merchantResponse.MerchantId,
                                                                   addMerchantDeviceRequest,
                                                                   cancellationToken);

                    // Now security user
                    CreateMerchantUserRequest createMerchantUserRequest = new CreateMerchantUserRequest{
                                                                                                           EmailAddress = merchant.User.EmailAddress,
                                                                                                           FamilyName = merchant.User.FamilyName,
                                                                                                           GivenName = merchant.User.GivenName,
                                                                                                           MiddleName = merchant.User.MiddleName,
                                                                                                           Password = merchant.User.Password
                                                                                                       };
                    await this.EstateClient.CreateMerchantUser(this.TokenResponse.AccessToken,
                                                                  this.EstateId,
                                                                  merchantResponse.MerchantId,
                                                                  createMerchantUserRequest,
                                                                  cancellationToken);

                    var estate = await this.EstateClient.GetEstate(this.TokenResponse.AccessToken,
                                                                      this.EstateId,
                                                                      cancellationToken);

                    foreach (var @operator in estate.Operators){
                        EstateManagement.DataTransferObjects.Requests.Merchant.AssignOperatorRequest assignOperatorRequest = new(){
                                                                                                                                      OperatorId = @operator.OperatorId,
                                                                                                                                      MerchantNumber = null,
                                                                                                                                      TerminalNumber = null
                                                                                                                                  };

                        await this.EstateClient.AssignOperatorToMerchant(this.TokenResponse.AccessToken,
                                                                            this.EstateId,
                                                                            merchantResponse.MerchantId,
                                                                            assignOperatorRequest,
                                                                            cancellationToken);
                    }

                    List<ContractResponse> contracts = await this.EstateClient.GetContracts(this.TokenResponse.AccessToken,
                                                                                            this.EstateId,
                                                                                            cancellationToken);

                    // Now contracts
                    foreach (ContractResponse contractResponse in contracts){
                        AddMerchantContractRequest addMerchantContractRequest = new(){
                                                                                         ContractId = contractResponse.ContractId
                                                                                     };
                        await this.EstateClient.AddContractToMerchant(this.TokenResponse.AccessToken,
                                                                         this.EstateId,
                                                                         merchantResponse.MerchantId,
                                                                         addMerchantContractRequest,
                                                                         cancellationToken);
                    }
                }
                else{
                    // check the merchants device
                    if (existingMerchant.Devices.ContainsValue(merchant.Device.DeviceIdentifier) == false){
                        AddMerchantDeviceRequest addMerchantDeviceRequest = new AddMerchantDeviceRequest
                                                                            {
                                                                                DeviceIdentifier = merchant.Device.DeviceIdentifier
                                                                            };
                        await this.EstateClient.AddDeviceToMerchant(this.TokenResponse.AccessToken,
                                                                    this.EstateId,
                                                                    existingMerchant.MerchantId,
                                                                    addMerchantDeviceRequest,
                                                                    cancellationToken);
                    }
                    
                    // Check the users
                    var user = await this.SecurityServiceClient.GetUsers(merchant.User.EmailAddress, cancellationToken);

                    if (user == null){
                        CreateMerchantUserRequest createMerchantUserRequest = new CreateMerchantUserRequest
                                                                              {
                                                                                  EmailAddress = merchant.User.EmailAddress,
                                                                                  FamilyName = merchant.User.FamilyName,
                                                                                  GivenName = merchant.User.GivenName,
                                                                                  MiddleName = merchant.User.MiddleName,
                                                                                  Password = merchant.User.Password
                                                                              };
                        await this.EstateClient.CreateMerchantUser(this.TokenResponse.AccessToken,
                                                                   this.EstateId,
                                                                   merchant.MerchantId,
                                                                   createMerchantUserRequest,
                                                                   cancellationToken);
                    }

                    var estate = await this.EstateClient.GetEstate(this.TokenResponse.AccessToken,
                                                                   this.EstateId,
                                                                   cancellationToken);

                    foreach (var @operator in estate.Operators) {
                        if (existingMerchant.Operators == null)
                            existingMerchant.Operators = new List<MerchantOperatorResponse>();

                        var merchantOperator = existingMerchant.Operators.SingleOrDefault(o => o.Name == @operator.OperatorId.ToString());
                        if(merchantOperator != null)
                            continue;
                        
                        EstateManagement.DataTransferObjects.Requests.Merchant.AssignOperatorRequest assignOperatorRequest = new()
                                                                                                                             {
                                                                                                                                 OperatorId = @operator.OperatorId,
                                                                                                                                 MerchantNumber = null,
                                                                                                                                 TerminalNumber = null
                                                                                                                             };

                        await this.EstateClient.AssignOperatorToMerchant(this.TokenResponse.AccessToken,
                                                                         this.EstateId,
                                                                         existingMerchant.MerchantId,
                                                                         assignOperatorRequest,
                                                                         cancellationToken);
                    }

                    List<ContractResponse> contracts = await this.EstateClient.GetContracts(this.TokenResponse.AccessToken,
                                                                                            this.EstateId,
                                                                                            cancellationToken);

                    var merchantContracts = await this.EstateClient.GetMerchantContracts(this.TokenResponse.AccessToken, this.EstateId, existingMerchant.MerchantId, cancellationToken);
                    // Now contracts
                    foreach (ContractResponse contractResponse in contracts){
                        if (merchantContracts.SingleOrDefault(c => c.ContractId == contractResponse.ContractId) != null)
                            continue;

                        AddMerchantContractRequest addMerchantContractRequest = new(){
                                                                                         ContractId = contractResponse.ContractId
                                                                                     };
                        await this.EstateClient.AddContractToMerchant(this.TokenResponse.AccessToken,
                                                                      this.EstateId,
                                                                      existingMerchant.MerchantId,
                                                                      addMerchantContractRequest,
                                                                      cancellationToken);
                    }

                }
            }
        }

        private async Task CreateFloats(CancellationToken cancellationToken){

            List<ContractResponse> contracts = await this.EstateClient.GetContracts(this.TokenResponse.AccessToken,
                                                                                    this.EstateId,
                                                                                    cancellationToken);

            foreach (ContractResponse contractResponse in contracts){
                foreach (ContractProduct contractProduct in contractResponse.Products){
                    try{

                        // Create the required floats
                        CreateFloatForContractProductRequest request = new CreateFloatForContractProductRequest{
                                                                                                                   ContractId = contractResponse.ContractId,
                                                                                                                   ProductId = contractProduct.ProductId,
                                                                                                                   CreateDateTime = DateTime.Now
                                                                                                               };
                        await this.TransactionProcessorClient.CreateFloatForContractProduct(this.TokenResponse.AccessToken,
                                                                                               this.EstateId,
                                                                                               request,
                                                                                               cancellationToken);
                    }
                    catch(Exception ex){
                        continue;
                    }
                }
            }

        }
    }
}
