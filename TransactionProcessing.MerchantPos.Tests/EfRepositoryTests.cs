using MerchantPos.EF.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace TransactionProcessing.MerchantPos.Tests;

public sealed class EfRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task UpdateTotals_starts_the_transaction_count_at_one_for_a_new_total()
    {
        var merchantId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var contractId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            await new EfRepository(db).UpdateTotals(merchantId, operatorId, contractId, 12.50m);
        }

        await using var readDb = CreateDbContext();
        var totals = await new EfRepository(readDb).GetTotals(merchantId);
        var total = totals.ShouldHaveSingleItem();
        total.OperatorId.ShouldBe(operatorId);
        total.ContractId.ShouldBe(contractId);
        total.Total.ShouldBe(12.50m);
        total.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateTotals_increments_the_transaction_count_for_an_existing_total()
    {
        var merchantId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var contractId = Guid.NewGuid();

        await using (var firstSaleDb = CreateDbContext())
        {
            await new EfRepository(firstSaleDb).UpdateTotals(merchantId, operatorId, contractId, 12.50m);
        }

        await using (var secondSaleDb = CreateDbContext())
        {
            await new EfRepository(secondSaleDb).UpdateTotals(merchantId, operatorId, contractId, 7.25m);
        }

        await using var readDb = CreateDbContext();
        var totals = await new EfRepository(readDb).GetTotals(merchantId);
        var total = totals.ShouldHaveSingleItem();
        total.Total.ShouldBe(19.75m);
        total.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ClearTotals_removes_only_the_selected_merchants_totals()
    {
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var contractId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            var repository = new EfRepository(db);
            await repository.UpdateTotals(merchantId, operatorId, contractId, 12.50m);
            await repository.UpdateTotals(merchantId, operatorId, Guid.NewGuid(), 7.25m);
            await repository.UpdateTotals(otherMerchantId, operatorId, contractId, 4.50m);
        }

        await using (var db = CreateDbContext())
        {
            await new EfRepository(db).ClearTotals(merchantId);
        }

        await using var readDb = CreateDbContext();
        var readRepository = new EfRepository(readDb);
        (await readRepository.GetTotals(merchantId)).ShouldBeEmpty();
        (await readRepository.GetTotals(otherMerchantId)).ShouldHaveSingleItem().Total.ShouldBe(4.50m);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9997, 9998)]
    [InlineData(9998, 1)]
    public async Task IncrementTransactionNumber_persists_the_next_number_and_wraps_at_the_limit(int current, int expected)
    {
        var merchantId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            var repository = new EfRepository(db);
            var merchant = await repository.CreateMerchantRecord(merchantId, "Test Merchant");
            merchant.TransactionNumber = current;
            await db.SaveChangesAsync();
            await repository.IncrementTransactionNumber(merchantId, "Test Merchant");
        }

        await using var readDb = CreateDbContext();
        var savedMerchant = await new EfRepository(readDb).GetMerchant(merchantId);
        savedMerchant.ShouldNotBeNull();
        savedMerchant.TransactionNumber.ShouldBe(expected);
    }

    private MerchantDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MerchantDbContext>()
            .UseSqlite(connection)
            .Options;

        return new MerchantDbContext(options);
    }
}
