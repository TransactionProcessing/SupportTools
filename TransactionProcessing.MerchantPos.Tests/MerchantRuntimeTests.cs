using MerchantPos.EF.Models;
using MerchantPos.EF.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using SecurityService.Client;
using SecurityService.DataTransferObjects;
using Shouldly;
using TransactionProcessing.MerchantPos.Runtime;
using TransactionProcessing.UnionResults;
using Xunit;

namespace TransactionProcessing.MerchantPos.Tests;

public sealed class MerchantRuntimeTests
{
    private const string ServiceClientId = "service-client";
    private const string PosClientId = "pos-client";
    private static readonly string ServiceClientSecret = Guid.NewGuid().ToString("N");
    private static readonly string PosClientSecret = Guid.NewGuid().ToString("N");

    public enum SaleOutcome { Authorised, Declined, ApiFailure }

    [Theory]
    [InlineData(SaleOutcome.Authorised, 10, 10, 90, false)]
    [InlineData(SaleOutcome.Authorised, 20, 20, 130, true)]
    [InlineData(SaleOutcome.Declined, 10, 110, 100, false)]
    [InlineData(SaleOutcome.ApiFailure, 10, 10, 100, false)]
    public async Task RunAsync_records_the_sale_outcome_and_deposits_only_below_the_threshold(
        SaleOutcome outcome, int productValue, int expectedSaleAmount, int expectedBalance, bool expectDeposit)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MerchantDbContext>().UseSqlite(connection).Options;
        await using var db = new MerchantDbContext(options);
        await db.Database.EnsureCreatedAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cancellation.Token;
        var config = new MerchantConfig
        {
            MerchantId = Guid.NewGuid(),
            MerchantName = "Test Merchant",
            Enabled = true,
            Username = "merchant-user",
            Password = "merchant-password",
            OpeningTime = TimeOnly.MinValue,
            ClosingTime = TimeOnly.MaxValue,
            SaleIntervalSeconds = 0,
            DepositThreshold = 90m,
            DepositAmount = 50m,
            FailureInjectionProbability = outcome == SaleOutcome.Declined ? 1 : 0
        };
        var product = new Product
        {
            OperatorId = Guid.NewGuid(),
            ContractId = Guid.NewGuid(),
            Value = productValue
        };
        var issued = DateTimeOffset.UtcNow;
        var serviceToken = TokenResponse.Create("service-token", "refresh-token", 3600, issued, issued.AddHours(1));
        var userToken = TokenResponse.Create("merchant-token", "refresh-token", 3600, issued, issued.AddHours(1));
        var security = new Mock<ISecurityServiceClient>(MockBehavior.Strict);
        security.Setup(client => client.GetToken(ServiceClientId, ServiceClientSecret, ct))
            .ReturnsAsync(Results.Ok(serviceToken));
        security.Setup(client => client.GetToken(config.Username, config.Password, PosClientId, PosClientSecret, ct))
            .ReturnsAsync(Results.Ok(userToken));

        var api = new Mock<IApiClient>(MockBehavior.Strict);
        api.Setup(client => client.GetProductList(config, userToken, ct))
            .ReturnsAsync(Results.Ok(new List<Product> { product }));
        api.Setup(client => client.GetBalance(config, serviceToken, ct)).ReturnsAsync(Results.Ok(100m));
        api.Setup(client => client.GetMerchant(config, userToken, ct))
            .ReturnsAsync(Results.Ok(new MerchantResponse { MerchantId = config.MerchantId }));
        api.Setup(client => client.SendLogon(config, userToken, 0, ct)).ReturnsAsync(Results.Ok());
        var authorised = outcome == SaleOutcome.Authorised;
        var saleResult = outcome == SaleOutcome.ApiFailure
            ? new Result<SaleResponse>(new Error(ErrorType.Failure, "Sale request failed"))
            : Results.Ok(new SaleResponse(authorised));
        api.Setup(client => client.SendSale(config, userToken, product, expectedSaleAmount, 1, ct))
            .Returns(() =>
            {
                if (!expectDeposit)
                    cancellation.Cancel();

                return Task.FromResult(saleResult);
            });
        api.Setup(client => client.SendDeposit(config, serviceToken, 50m, ct))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(Results.Ok());
            });

        var metrics = new MerchantMetrics();
        var runtime = new MerchantRuntime(api.Object, security.Object, new EfRepository(db), metrics);

        await runtime.RunAsync((ServiceClientId, ServiceClientSecret), (PosClientId, PosClientSecret), config, ct);

        api.Verify(client => client.SendLogon(config, userToken, 0, ct), Times.Once);
        api.Verify(client => client.SendSale(config, userToken, product, expectedSaleAmount, 1, ct), Times.Once);
        api.Verify(client => client.SendDeposit(config, serviceToken, 50m, ct), expectDeposit ? Times.Once() : Times.Never());
        await using var readDb = new MerchantDbContext(options);
        var repository = new EfRepository(readDb);
        var merchant = await repository.GetMerchant(config.MerchantId);
        merchant.ShouldNotBeNull();
        merchant.TransactionNumber.ShouldBe(2);
        merchant.LastLogonDateTime.ShouldNotBe(DateTime.MinValue);
        merchant.Balance.ShouldBe(expectedBalance);
        var totals = await repository.GetTotals(config.MerchantId);
        if (authorised)
        {
            var total = totals.ShouldHaveSingleItem();
            total.OperatorId.ShouldBe(product.OperatorId);
            total.ContractId.ShouldBe(product.ContractId);
            total.Total.ShouldBe(product.Value);
        }
        else
        {
            totals.ShouldBeEmpty();
        }

        var snapshot = metrics.Get(config.MerchantId);
        snapshot.SalesCount.ShouldBe(authorised ? 1 : 0);
        snapshot.FailedSales.ShouldBe(authorised ? 0 : 1);
        if (outcome != SaleOutcome.ApiFailure)
            snapshot.Balance.ShouldBe(expectedBalance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_completes_end_of_day_only_when_reconciliation_succeeds(bool accepted)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MerchantDbContext>().UseSqlite(connection).Options;
        await using var db = new MerchantDbContext(options);
        await db.Database.EnsureCreatedAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cancellation.Token;
        var config = new MerchantConfig { MerchantId = Guid.NewGuid(), ClosingTime = TimeOnly.MinValue, OpeningTime = TimeOnly.MinValue };
        db.OperatorTotals.Add(new OperatorTotal
        {
            MerchantId = config.MerchantId,
            OperatorId = Guid.NewGuid(),
            ContractId = Guid.NewGuid(),
            Total = 15m,
            TotalCount = 1
        });
        await db.SaveChangesAsync();

        var issued = DateTimeOffset.UtcNow;
        var token = TokenResponse.Create("token", "refresh-token", 3600, issued, issued.AddHours(1));
        var security = new Mock<ISecurityServiceClient>(MockBehavior.Strict);
        security.Setup(client => client.GetToken(ServiceClientId, ServiceClientSecret, ct)).ReturnsAsync(Results.Ok(token));
        security.Setup(client => client.GetToken(config.Username, config.Password, PosClientId, PosClientSecret, ct))
            .ReturnsAsync(Results.Ok(token));

        var api = new Mock<IApiClient>(MockBehavior.Strict);
        api.Setup(client => client.GetProductList(config, token, ct)).ReturnsAsync(Results.Ok(new List<Product>()));
        api.Setup(client => client.GetBalance(config, token, ct)).ReturnsAsync(Results.Ok(100m));
        api.Setup(client => client.GetMerchant(config, token, ct)).ReturnsAsync(Results.Ok(new MerchantResponse()));
        Result result = accepted ? Results.Ok() : Results.Fail("Reconciliation rejected");
        api.Setup(client => client.SendReconciliation(config, token, It.IsAny<List<OperatorTotal>>(), ct))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(result);
            });

        var metrics = new MerchantMetrics();
        var runtime = new MerchantRuntime(api.Object, security.Object, new EfRepository(db), metrics);

        await runtime.RunAsync((ServiceClientId, ServiceClientSecret), (PosClientId, PosClientSecret), config, ct);

        api.Verify(client => client.SendReconciliation(config, token, It.IsAny<List<OperatorTotal>>(), ct), Times.Once);
        await using var readDb = new MerchantDbContext(options);
        var repository = new EfRepository(readDb);
        var merchant = await repository.GetMerchant(config.MerchantId);
        merchant.ShouldNotBeNull();
        var totals = await repository.GetTotals(config.MerchantId);
        if (accepted)
        {
            totals.ShouldBeEmpty();
            merchant.LastEndOfDayDateTime.ShouldNotBe(DateTime.MinValue);
            metrics.Get(config.MerchantId).LastEndOfDay.ShouldNotBeNull();
        }
        else
        {
            totals.ShouldHaveSingleItem().Total.ShouldBe(15m);
            merchant.LastEndOfDayDateTime.ShouldBe(DateTime.MinValue);
            metrics.Get(config.MerchantId).LastEndOfDay.ShouldBeNull();
        }
    }
}
