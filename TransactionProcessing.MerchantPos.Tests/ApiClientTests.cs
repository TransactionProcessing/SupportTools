using System.Net;
using System.Text;
using MerchantPos.EF.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using SecurityService.Client;
using SecurityService.DataTransferObjects;
using Shouldly;
using TransactionProcessing.MerchantPos.Persistence;
using TransactionProcessing.MerchantPos.Runtime;
using TransactionProcessor.Client;
using TransactionProcessorACL.DataTransferObjects;
using TransactionProcessorACL.DataTransferObjects.Responses;
using Xunit;

namespace TransactionProcessing.MerchantPos.Tests;

public sealed class ApiClientTests
{
    [Fact]
    public async Task SendReconciliation_posts_the_amount_and_count_for_each_operator_and_the_grand_totals()
    {
        using var handler = new RecordingHandler("{}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var config = new MerchantConfig { DeviceIdentifier = "terminal-1", ApplicationVersion = "1.2.3" };
        var first = new OperatorTotal { OperatorId = Guid.NewGuid(), ContractId = Guid.NewGuid(), Total = 19.75m, TotalCount = 2 };
        var second = new OperatorTotal { OperatorId = Guid.NewGuid(), ContractId = Guid.NewGuid(), Total = 4.50m, TotalCount = 1 };

        var result = await client.SendReconciliation(config, CreateToken(), [first, second], CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Message);
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldBe("/api/reconciliationtransactions");
        request.Authorization.ShouldBe("Bearer merchant-token");
        var body = JsonConvert.DeserializeObject<ReconciliationRequestMessage>(request.Body);
        body.ShouldNotBeNull();
        body.DeviceIdentifier.ShouldBe("terminal-1");
        body.ApplicationVersion.ShouldBe("1.2.3");
        body.TransactionCount.ShouldBe(3);
        body.TransactionValue.ShouldBe(24.25m);
        body.OperatorTotals.Select(total => (total.OperatorId, total.ContractId, total.TransactionCount, total.TransactionValue))
            .ShouldBe([(first.OperatorId, first.ContractId, 2, 19.75m), (second.OperatorId, second.ContractId, 1, 4.50m)], ignoreOrder: true);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(2, 0)]
    public async Task SendReconciliation_rejects_a_row_with_exactly_one_zero_before_posting(int count, int value)
    {
        using var handler = new RecordingHandler("{}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var valid = new OperatorTotal { OperatorId = Guid.NewGuid(), ContractId = Guid.NewGuid(), TotalCount = 1, Total = 5m };
        var invalid = new OperatorTotal { OperatorId = Guid.NewGuid(), ContractId = Guid.NewGuid(), TotalCount = count, Total = value };

        var result = await client.SendReconciliation(new MerchantConfig(), CreateToken(), [valid, invalid], CancellationToken.None);

        result.IsFailed.ShouldBeTrue();
        result.Message.ShouldContain(invalid.OperatorId.ToString());
        result.Message.ShouldContain(invalid.ContractId.ToString());
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, -1, 15, 5)]
    [InlineData(1, 2, 15, -15)]
    public async Task SendReconciliation_rejects_an_aggregate_with_a_zero_mismatch(
        int firstCount, int secondCount, int firstValue, int secondValue)
    {
        using var handler = new RecordingHandler("{}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var first = new OperatorTotal { OperatorId = Guid.NewGuid(), ContractId = Guid.NewGuid(), TotalCount = firstCount, Total = firstValue };
        var second = new OperatorTotal { OperatorId = Guid.NewGuid(), ContractId = Guid.NewGuid(), TotalCount = secondCount, Total = secondValue };

        var result = await client.SendReconciliation(new MerchantConfig(), CreateToken(), [first, second], CancellationToken.None);

        result.IsFailed.ShouldBeTrue();
        result.Message.ShouldContain("aggregate");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendReconciliation_posts_a_row_when_both_count_and_value_are_zero()
    {
        using var handler = new RecordingHandler("{}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var total = new OperatorTotal { OperatorId = Guid.NewGuid(), ContractId = Guid.NewGuid() };

        var result = await client.SendReconciliation(new MerchantConfig(), CreateToken(), [total], CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Message);
        var request = handler.Requests.ShouldHaveSingleItem();
        var body = JsonConvert.DeserializeObject<ReconciliationRequestMessage>(request.Body);
        body.ShouldNotBeNull();
        var row = body.OperatorTotals.ShouldHaveSingleItem();
        row.TransactionCount.ShouldBe(0);
        row.TransactionValue.ShouldBe(0m);
    }

    [Theory]
    [InlineData("Safaricom", ProductType.MobileTopup, ProductSubType.MobileTopup)]
    [InlineData("Voucher", ProductType.Voucher, ProductSubType.Voucher)]
    [InlineData("PataPawa PostPay", ProductType.BillPayment, ProductSubType.BillPaymentPostPay)]
    [InlineData("PataPawa PrePay", ProductType.BillPayment, ProductSubType.BillPaymentPrePay)]
    [InlineData("Unknown operator", ProductType.NotSet, ProductSubType.NotSet)]
    public async Task GetProductList_maps_contract_products_to_simulator_products(
        string operatorName, ProductType productType, ProductSubType productSubType)
    {
        var contract = new ContractResponseX
        {
            ContractId = Guid.NewGuid(),
            OperatorId = Guid.NewGuid(),
            OperatorName = operatorName,
            Products =
            [
                new ContractProductX { ProductId = Guid.NewGuid(), Name = "Fixed price", Value = 12.50m },
                new ContractProductX { ProductId = Guid.NewGuid(), Name = "Variable price" }
            ]
        };
        using var handler = new RecordingHandler(JsonConvert.SerializeObject(new[] { contract }));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetProductList(new MerchantConfig { ApplicationVersion = "1.2.3" },
            CreateToken(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Message);
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.PathAndQuery.ShouldBe("/api/merchants/contracts?applicationVersion=1.2.3");
        var products = result.Data;
        products.Count.ShouldBe(2);
        products.ShouldAllBe(product => product.ContractId == contract.ContractId && product.OperatorId == contract.OperatorId
            && product.ProductType == productType && product.ProductSubType == productSubType);
        products.Select(product => (product.ProductId, product.Name, product.Value))
            .ShouldBe([(contract.Products[0].ProductId, "Fixed price", 12.50m), (contract.Products[1].ProductId, "Variable price", 0m)], ignoreOrder: true);
    }

    [Theory]
    [InlineData(ProductType.MobileTopup, "CustomerAccountNumber", "0000", true)]
    [InlineData(ProductType.Voucher, "RecipientMobile", "0000", true)]
    [InlineData(ProductType.Voucher, "RecipientMobile", "1001", false)]
    public async Task SendSale_posts_product_specific_metadata_and_uses_the_response_code(
        ProductType productType, string customerMetadataKey, string responseCode, bool expectedAuthorised)
    {
        using var handler = new RecordingHandler(JsonConvert.SerializeObject(new SaleTransactionResponseMessage { ResponseCode = responseCode }));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var config = new MerchantConfig { DeviceIdentifier = "terminal-1", ApplicationVersion = "1.2.3" };
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            ContractId = Guid.NewGuid(),
            OperatorId = Guid.NewGuid(),
            ProductType = productType
        };

        var result = await client.SendSale(config, CreateToken(), product, 15m, 42, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Data.Authorised.ShouldBe(expectedAuthorised);
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldBe("/api/saletransactions");
        var body = JsonConvert.DeserializeObject<SaleTransactionRequestMessage>(request.Body);
        body.ShouldNotBeNull();
        body.TransactionNumber.ShouldBe("0042");
        body.ProductId.ShouldBe(product.ProductId);
        body.ContractId.ShouldBe(product.ContractId);
        body.OperatorId.ShouldBe(product.OperatorId);
        body.AdditionalRequestMetadata["Amount"].ShouldBe("15");
        body.AdditionalRequestMetadata[customerMetadataKey].ShouldNotBeNullOrWhiteSpace();
    }

    private static TokenResponse CreateToken()
    {
        var issued = DateTimeOffset.UtcNow;
        return TokenResponse.Create("merchant-token", "refresh-token", 3600, issued, issued.AddHours(1));
    }

    private static ApiClient CreateClient(HttpClient httpClient)
    {
        var store = new MerchantPosSettingsStore(new ConfigurationBuilder().Build(), new(), NullLogger<MerchantPosSettingsStore>.Instance);
        store.Current.ApiConfiguration.TransactionProcessorACL = "https://pos.example";
        return new ApiClient(Mock.Of<ISecurityServiceClient>(), Mock.Of<ITransactionProcessorClient>(), httpClient, store,
            JsonConvert.SerializeObject, (json, type) => JsonConvert.DeserializeObject(json, type)!);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
