using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TransactionProcessing.MerchantPos.Persistence;
using Xunit;

namespace TransactionProcessing.MerchantPos.Tests;

public sealed class MerchantPosSettingsStoreTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("merchant-pos-settings-");
    private string SettingsPath => Path.Combine(directory.FullName, "settings.db");

    [Fact]
    public async Task InitialiseAsync_creates_the_settings_database_from_configuration()
    {
        var merchantId = Guid.NewGuid();
        var store = CreateStore(new Dictionary<string, string?>
        {
            ["WorkerSettings:MerchantScanIntervalSeconds"] = "0",
            ["WorkerSettings:Merchants:0:MerchantId"] = merchantId.ToString(),
            ["WorkerSettings:Merchants:0:MerchantName"] = "Test Merchant"
        });

        await store.InitialiseAsync();

        File.Exists(SettingsPath).ShouldBeTrue();
        store.Current.ConnectionStrings.SettingsDb.ShouldBe(SettingsPath);
        store.Current.WorkerSettings.MerchantScanIntervalSeconds.ShouldBe(5);
        var merchant = store.Current.WorkerSettings.Merchants.ShouldHaveSingleItem();
        merchant.MerchantId.ShouldBe(merchantId);
        merchant.MerchantName.ShouldBe("Test Merchant");
    }

    [Fact]
    public async Task InitialiseAsync_reloads_saved_overrides_and_fills_blank_values_from_configuration()
    {
        var defaults = new Dictionary<string, string?>
        {
            ["ApiConfiguration:TransactionProcessorACL"] = "https://default.example",
            ["ApiConfiguration:TestHost"] = "https://test-host.example"
        };
        var store = CreateStore(defaults);
        await store.InitialiseAsync();
        store.Current.ApiConfiguration.TransactionProcessorACL = "https://override.example";
        store.Current.ApiConfiguration.TestHost = string.Empty;
        store.Current.WorkerSettings.MerchantScanIntervalSeconds = 17;
        await store.SaveAsync(store.Current);

        var reopened = CreateStore(defaults);
        await reopened.InitialiseAsync();

        reopened.Current.ApiConfiguration.TransactionProcessorACL.ShouldBe("https://override.example");
        reopened.Current.ApiConfiguration.TestHost.ShouldBe("https://test-host.example");
        reopened.Current.WorkerSettings.MerchantScanIntervalSeconds.ShouldBe(17);
    }

    public void Dispose()
    {
        using var connection = new SqliteConnection($"Data Source={SettingsPath}");
        SqliteConnection.ClearPool(connection);
        directory.Delete(recursive: true);
    }

    private MerchantPosSettingsStore CreateStore(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:SettingsDb"] = $"Data Source={SettingsPath}";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new MerchantPosSettingsStore(configuration, new JsonSerializerOptions(), NullLogger<MerchantPosSettingsStore>.Instance);
    }
}
