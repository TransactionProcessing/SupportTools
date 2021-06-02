using System;

namespace TransactionProcessor.SystemSetupTool
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Requests;
    using SecurityService.DataTransferObjects.Responses;

    class Program
    {
        private static EstateClient EstateClient;

        private static SecurityServiceClient SecurityServiceClient;

        private static TokenResponse TokenResponse;

        static async Task Main(string[] args)
        {
            Func<String, String> estateResolver = s => { return "http://192.168.1.133:5000"; };
            Func<String, String> securityResolver = s => { return "http://192.168.1.133:5001"; };
            HttpClient client = new HttpClient();

            Program.EstateClient = new EstateClient(estateResolver, client);
            Program.SecurityServiceClient = new SecurityServiceClient(securityResolver, client);

            // Read the json string
            String jsonData = null;
            using(StreamReader sr = new StreamReader("setupconfig.json"))
            {
                jsonData = await sr.ReadToEndAsync();
            }

            Program.TokenResponse = await Program.SecurityServiceClient.GetToken("serviceClient", "d192cbc46d834d0da90e8a9d50ded543", CancellationToken.None);
            Root estateConfiguration = JsonSerializer.Deserialize<Root>(jsonData);

            foreach (var estate in estateConfiguration.Estates)
            {
                await CreateEstate(estate, CancellationToken.None);
            }
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
                                                                            }
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

    public class Address
    {
        [JsonPropertyName("address_line_1")]
        public string AddressLine1 { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonPropertyName("region")]
        public string Region { get; set; }

        [JsonPropertyName("town")]
        public string Town { get; set; }
    }

    public class Contact
    {
        [JsonPropertyName("contact_name")]
        public string ContactName { get; set; }

        [JsonPropertyName("email_address")]
        public string EmailAddress { get; set; }
    }

    public class User
    {
        [JsonPropertyName("email_address")]
        public string EmailAddress { get; set; }

        [JsonPropertyName("password")]
        public string Password { get; set; }

        [JsonPropertyName("given_name")]
        public string GivenName { get; set; }

        [JsonPropertyName("middle_name")]
        public string MiddleName { get; set; }

        [JsonPropertyName("family_name")]
        public string FamilyName { get; set; }
    }

    public class Device
    {
        [JsonPropertyName("device_identifier")]
        public string DeviceIdentifier { get; set; }
    }

    public class Merchant
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("address")]
        public Address Address { get; set; }

        [JsonPropertyName("contact")]
        public Contact Contact { get; set; }

        [JsonPropertyName("user")]
        public User User { get; set; }

        [JsonPropertyName("device")]
        public Device Device { get; set; }
    }

    public class Operator
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("require_custom_merchant_number")]
        public bool RequireCustomMerchantNumber { get; set; }

        [JsonPropertyName("require_custom_terminal_number")]
        public bool RequireCustomTerminalNumber { get; set; }
    }

    public class TransactionFee
    {
        [JsonPropertyName("calculation_type")]
        public int CalculationType { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("value")]
        public decimal Value { get; set; }

        [JsonPropertyName("fee_type")]
        public int FeeType { get; set; }
    }

    public class Product
    {
        [JsonPropertyName("display_text")]
        public string DisplayText { get; set; }

        [JsonPropertyName("product_name")]
        public string ProductName { get; set; }

        [JsonPropertyName("value")]
        public decimal? Value { get; set; }

        [JsonPropertyName("transaction_fees")]
        public List<TransactionFee> TransactionFees { get; set; }
    }

    public class Contract
    {
        [JsonPropertyName("operator_name")]
        public string OperatorName { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("products")]
        public List<Product> Products { get; set; }
    }

    public class Estate
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

    [JsonPropertyName("user")]
    public User User { get; set; }

    [JsonPropertyName("merchants")]
        public List<Merchant> Merchants { get; set; }

        [JsonPropertyName("operators")]
        public List<Operator> Operators { get; set; }

        [JsonPropertyName("contracts")]
        public List<Contract> Contracts { get; set; }
    }

    public class Root
    {
        [JsonPropertyName("estates")]
        public List<Estate> Estates { get; set; }
    }


}
