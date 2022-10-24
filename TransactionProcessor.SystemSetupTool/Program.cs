using System;

namespace TransactionProcessor.SystemSetupTool
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using estateconfig;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using identityserverconfig;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Requests;
    using SecurityService.DataTransferObjects.Responses;
    using EventStore.Client;
    using Microsoft.Extensions.Configuration;
    using Shared.General;

    class Program
    {
        private static EstateClient EstateClient;

        private static SecurityServiceClient SecurityServiceClient;

        private static EventStoreProjectionManagementClient ProjectionClient;

        private static EventStorePersistentSubscriptionsClient PersistentSubscriptionsClient;

        private static TokenResponse TokenResponse;
        
        static async Task Main(string[] args)
        {


            
            IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            IConfigurationRoot configurationRoot = builder.Build();
            ConfigurationReader.Initialise(configurationRoot);

            Func<String, String> estateResolver = s => { return ConfigurationReader.GetValue("EstateManagementUri"); };
            Func<String, String> securityResolver = s => { return ConfigurationReader.GetValue("SecurityServiceUri"); };
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

            Program.EstateClient = new EstateClient(estateResolver, client);
            Program.SecurityServiceClient = new SecurityServiceClient(securityResolver, client);
            EventStoreClientSettings settings = EventStoreClientSettings.Create(ConfigurationReader.GetValue("EventStoreAddress"));
            Program.ProjectionClient = new EventStoreProjectionManagementClient(settings);
            Program.PersistentSubscriptionsClient = new EventStorePersistentSubscriptionsClient(settings);

            //await Program.SetupIdentityServerFromConfig();

            //Setup latest projections
            //await DeployProjections();

            //Setup subcriptions
            //await SetupSubscriptions();

            await Program.SetupEstatesFromConfig();            
        }

        private static async Task SetupSubscriptions()
        {
            String estateJsonData = null;
            using (StreamReader sr = new StreamReader("setupconfig.json"))
            {
                estateJsonData = await sr.ReadToEndAsync();
            }

            EstateConfig estateConfiguration = JsonSerializer.Deserialize<EstateConfig>(estateJsonData);

            
            foreach (var estate in estateConfiguration.Estates)
            {
                
                // Setup the subscrtipions
                await PersistentSubscriptionsClient.CreateAsync(estate.Name.Replace(" ", ""), "Reporting", CreatePersistentSettings());
                await PersistentSubscriptionsClient.CreateAsync($"FileProcessorSubscriptionStream_{estate.Name.Replace(" ", "")}", "File Processor", CreatePersistentSettings(2));
                await Program.PersistentSubscriptionsClient.CreateAsync($"EstateManagementSubscriptionStream_{estate.Name.Replace(" ", "")}", "Estate Management", CreatePersistentSettings());
                await PersistentSubscriptionsClient.CreateAsync($"TransactionProcessorSubscriptionStream_{estate.Name.Replace(" ", "")}", "Transaction Processor", CreatePersistentSettings(1));
            }

            await PersistentSubscriptionsClient.CreateAsync($"$et-EstateCreatedEvent", "Transaction Processor - Ordered", CreatePersistentSettings(1));
            await PersistentSubscriptionsClient.CreateAsync($"$ce-MerchantBalanceArchive", "Transaction Processor - Ordered", CreatePersistentSettings());
        }

        private static PersistentSubscriptionSettings CreatePersistentSettings(Int32 retryCount = 0) {
            return new PersistentSubscriptionSettings(resolveLinkTos: true, maxRetryCount: retryCount);
        }

        private static async Task DeployProjections()
        {
            var currentProjections = await ProjectionClient.ListAllAsync().ToListAsync();

            var projectionsToDeploy = Directory.GetFiles("projections/continuous");

            foreach (var projection in projectionsToDeploy)
            {
                FileInfo f = new FileInfo(projection);
                String name = f.Name.Substring(0, f.Name.Length - (f.Name.Length - f.Name.LastIndexOf(".")));
                var body = File.ReadAllText(f.FullName);

                var x = body.IndexOf("//endtestsetup");
                x = x + "//endtestsetup".Length;

                body = body.Substring(x);

                // Is this already deployed (in the master list)
                if ( currentProjections.Any(p => p.Name == name) == false)
                {
                    // Projection does not exist so create
                    await ProjectionClient.CreateContinuousAsync(name, body, true);
                }
                else
                {
                    // Already exists so we need to update but do not reset
                    await ProjectionClient.DisableAsync(name);
                    await ProjectionClient.UpdateAsync(name, body, true);
                    await ProjectionClient.EnableAsync(name);
                }
            }
        }

        private static async Task SetupIdentityServerFromConfig()
        {
            // Read the identity server config json string
            String identityServerJsonData = null;
            using(StreamReader sr = new StreamReader("identityserverconfig.json"))
            {
                identityServerJsonData = await sr.ReadToEndAsync();
            }

            IdentityServerConfiguration identityServerConfiguration = JsonSerializer.Deserialize<IdentityServerConfiguration>(identityServerJsonData);

            foreach (String role in identityServerConfiguration.roles)
            {
                await Program.CreateRole(role, CancellationToken.None);
            }
            foreach (ApiResource apiResource in identityServerConfiguration.apiresources)
            {
                await Program.CreateApiResource(apiResource, CancellationToken.None);
            }

            foreach (IdentityResource identityResource in identityServerConfiguration.identityresources)
            {
                await Program.CreateIdentityResource(identityResource, CancellationToken.None);
            }

            foreach (Client client in identityServerConfiguration.clients)
            {
                await Program.CreateClient(client, CancellationToken.None);
            }

            foreach (ApiScope apiscope in identityServerConfiguration.apiscopes)
            {
                await Program.CreateApiScope(apiscope, CancellationToken.None);
            }
        }

        private static async Task CreateRole(String role,
                                             CancellationToken cancellationToken)
        {
            CreateRoleRequest createRoleRequest = new CreateRoleRequest
                                                  {
                                                      RoleName = role
                                                  };

            await Program.SecurityServiceClient.CreateRole(createRoleRequest, cancellationToken);
        }

        private static async Task CreateApiScope(ApiScope apiscope,
                                                 CancellationToken cancellationToken)
        {
            CreateApiScopeRequest createApiScopeRequest = new CreateApiScopeRequest
                                                          {
                                                              Description = apiscope.description,
                                                              DisplayName = apiscope.display_name,
                                                              Name = apiscope.name
                                                          };

            await Program.SecurityServiceClient.CreateApiScope(createApiScopeRequest, cancellationToken);
        }

        private static async Task CreateIdentityResource(IdentityResource identityResource,
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

            await Program.SecurityServiceClient.CreateIdentityResource(createIdentityResourceRequest, cancellationToken);
        }

        private static async Task SetupEstatesFromConfig()
        {
            // Read the estate config json string
            String estateJsonData = null;
            using(StreamReader sr = new StreamReader("setupconfig.json"))
            {
                estateJsonData = await sr.ReadToEndAsync();
            }

            Program.TokenResponse = await Program.SecurityServiceClient.GetToken("serviceClient", "d192cbc46d834d0da90e8a9d50ded543", CancellationToken.None);
            EstateConfig estateConfiguration = JsonSerializer.Deserialize<EstateConfig>(estateJsonData);

            foreach (var estate in estateConfiguration.Estates)
            {
                    await Program.CreateEstate(estate, CancellationToken.None);
            }
        }

        static async Task CreateClient(Client client, CancellationToken cancellationToken)
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
            await Program.SecurityServiceClient.CreateClient(createClientRequest, cancellationToken);
        }

        static async Task CreateApiResource(ApiResource apiResource,
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

            await Program.SecurityServiceClient.CreateApiResource(createApiResourceRequest, cancellationToken);
        }

        static async Task CreateEstate(Estate estateToCreate, CancellationToken cancellationToken)
        {
            List<(CreateOperatorRequest, CreateOperatorResponse)> operatorResponses = new List<(CreateOperatorRequest, CreateOperatorResponse)>();

            // Create the estate
            CreateEstateRequest createEstateRequest = new CreateEstateRequest
                                                      {
                                                          EstateId = String.IsNullOrEmpty(estateToCreate.Id) ? Guid.NewGuid() : Guid.Parse(estateToCreate.Id),
                                                          EstateName = estateToCreate.Name
                                                      };

            var estateResponse = await Program.EstateClient.CreateEstate(Program.TokenResponse.AccessToken, createEstateRequest, cancellationToken);
            
            // Create Estate user
            CreateEstateUserRequest createEstateUserRequest = new CreateEstateUserRequest
            {
                                                                      EmailAddress = estateToCreate.User.EmailAddress,
                                                                      FamilyName = estateToCreate.User.FamilyName,
                                                                      GivenName = estateToCreate.User.GivenName,
                                                                      MiddleName = estateToCreate.User.MiddleName,
                                                                      Password = estateToCreate.User.Password
                                                                  };
            await Program.EstateClient.CreateEstateUser(Program.TokenResponse.AccessToken,
                                                          estateResponse.EstateId,
                                                          createEstateUserRequest,
                                                          cancellationToken);

            // Now do the operators
            foreach (Operator @operator in estateToCreate.Operators)
            {
                CreateOperatorRequest createOperatorRequest = new CreateOperatorRequest
                                                              {
                                                                  Name = @operator.Name,
                                                                  RequireCustomMerchantNumber = @operator.RequireCustomMerchantNumber,
                                                                  RequireCustomTerminalNumber = @operator.RequireCustomMerchantNumber,
                                                              };
                operatorResponses.Add((createOperatorRequest, await Program.EstateClient.CreateOperator(Program.TokenResponse.AccessToken, estateResponse.EstateId, createOperatorRequest, cancellationToken)));
            }

            // Now the contracts
            foreach (Contract contract in estateToCreate.Contracts)
            {
                var operatorTuple = operatorResponses.Single(o => o.Item1.Name == contract.OperatorName);

                CreateContractRequest createContractRequest = new CreateContractRequest
                                                              {
                                                                  Description = contract.Description,
                                                                  OperatorId = operatorTuple.Item2.OperatorId
                                                              };
                var createContractResponse = await Program.EstateClient.CreateContract(Program.TokenResponse.AccessToken, estateResponse.EstateId, createContractRequest, cancellationToken);

                foreach (Product contractProduct in contract.Products)
                {
                    AddProductToContractRequest addProductToContractRequest = new AddProductToContractRequest
                                                                              {
                                                                                  DisplayText = contractProduct.DisplayText,
                                                                                  ProductName = contractProduct.ProductName,
                                                                                  Value = contractProduct.Value
                                                                              };

                    var createContractProductResponse = await Program.EstateClient.AddProductToContract(Program.TokenResponse.AccessToken,
                                                              estateResponse.EstateId,
                                                              createContractResponse.ContractId,
                                                              addProductToContractRequest,
                                                              cancellationToken);

                    foreach (TransactionFee contractProductTransactionFee in contractProduct.TransactionFees)
                    {
                        AddTransactionFeeForProductToContractRequest addTransactionFeeForProductToContractRequest = new AddTransactionFeeForProductToContractRequest
                            {
                                Description = contractProductTransactionFee.Description,
                                Value = contractProductTransactionFee.Value,
                                CalculationType = (CalculationType)contractProductTransactionFee.CalculationType,
                                FeeType = (FeeType)contractProductTransactionFee.FeeType
                            };

                        await Program.EstateClient.AddTransactionFeeForProductToContract(Program.TokenResponse.AccessToken,
                                                                                   estateResponse.EstateId,
                                                                                   createContractResponse.ContractId,
                                                                                   createContractProductResponse.ProductId,
                                                                                   addTransactionFeeForProductToContractRequest,
                                                                                   cancellationToken);
                    }
                }
            }

            // Now create the merchants
            foreach (Merchant merchant in estateToCreate.Merchants)
            {
                SettlementSchedule settlementSchedule = Enum.Parse<SettlementSchedule>(merchant.SettlementSchedule);

                CreateMerchantRequest createMerchantRequest = new CreateMerchantRequest
                                                              {
                                                                  Address = new EstateManagement.DataTransferObjects.Requests.Address
                                                                            {
                                                                                AddressLine1 = merchant.Address.AddressLine1,
                                                                                Country = merchant.Address.Country,
                                                                                Region = merchant.Address.Region,
                                                                                Town = merchant.Address.Town,
                                                                            },
                                                                  Name = merchant.Name,
                                                                  Contact = new EstateManagement.DataTransferObjects.Requests.Contact
                                                                            {
                                                                                ContactName = merchant.Contact.ContactName,
                                                                                EmailAddress = merchant.Contact.EmailAddress
                                                                            },
                                                                  SettlementSchedule = settlementSchedule,
                                                                  CreatedDateTime = merchant.CreateDate,
                                                                  MerchantId = merchant.MerchantId,
                                                              };
                var merchantResponse = await Program.EstateClient.CreateMerchant(Program.TokenResponse.AccessToken, estateResponse.EstateId, createMerchantRequest, cancellationToken);

                // Now add devices
                AddMerchantDeviceRequest addMerchantDeviceRequest = new AddMerchantDeviceRequest
                                                                    {
                                                                        DeviceIdentifier = merchant.Device.DeviceIdentifier
                                                                    };
                await Program.EstateClient.AddDeviceToMerchant(Program.TokenResponse.AccessToken,
                                                         estateResponse.EstateId,
                                                         merchantResponse.MerchantId,
                                                         addMerchantDeviceRequest,
                                                         cancellationToken);

                // Now security user
                CreateMerchantUserRequest createMerchantUserRequest = new CreateMerchantUserRequest
                                                                      {
                                                                          EmailAddress = merchant.User.EmailAddress,
                                                                          FamilyName = merchant.User.FamilyName,
                                                                          GivenName = merchant.User.GivenName,
                                                                          MiddleName = merchant.User.MiddleName,
                                                                          Password = merchant.User.Password
                                                                      };
                await Program.EstateClient.CreateMerchantUser(Program.TokenResponse.AccessToken,
                                                        estateResponse.EstateId,
                                                        merchantResponse.MerchantId,
                                                        createMerchantUserRequest,
                                                        cancellationToken);

                foreach (var @operator in operatorResponses)
                {
                    AssignOperatorRequest assignOperatorRequest = new AssignOperatorRequest
                                                                  {
                                                                      MerchantNumber = null,
                                                                      OperatorId = @operator.Item2.OperatorId,
                                                                      TerminalNumber = null
                                                                  };
                    await Program.EstateClient.AssignOperatorToMerchant(Program.TokenResponse.AccessToken,
                                                                  estateResponse.EstateId,
                                                                  merchantResponse.MerchantId,
                                                                  assignOperatorRequest,
                                                                  cancellationToken);
                }
            }
        }
    }
}
