using ClientProxyBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MerchantPos.EF.Persistence;
using SecurityService.Client;
using Shared.Serialisation;
using TransactionProcessing.MerchantPos.Persistence;
using TransactionProcessing.MerchantPos.Runtime;
using TransactionProcessor.Client;
using Xunit;

namespace TransactionProcessing.MerchantPos.Tests;

public sealed class MerchantRuntimeFactoryTests
{
    [Fact]
    public void Create_resolves_a_merchant_runtime_from_the_application_service_graph()
    {
        using var provider = BuildServiceProvider();
        var factory = provider.GetRequiredService<IMerchantRuntimeFactory>();

        var runtime = factory.Create(new MerchantConfig
        {
            MerchantId = Guid.NewGuid(),
            MerchantName = "Test Merchant",
            Enabled = true,
            Username = "user",
            Password = "password",
            DeviceIdentifier = "device",
            ApplicationVersion = "1.0.0"
        });

        Assert.NotNull(runtime);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MerchantDb"] = "Data Source=:memory:",
                ["ConnectionStrings:SettingsDb"] = "Data Source=:memory:",
                ["WorkerSettings:ClientId"] = "client-id",
                ["WorkerSettings:ClientSecret"] = "client-secret",
                ["WorkerSettings:ServiceClientId"] = "service-client-id",
                ["WorkerSettings:ServiceClientSecret"] = "service-client-secret",
                ["ApiConfiguration:SecurityService"] = "https://security.example",
                ["ApiConfiguration:TransactionProcessorACL"] = "https://processor-acl.example",
                ["ApiConfiguration:TransactionProcessorApi"] = "https://processor-api.example",
                ["ApiConfiguration:TestHost"] = "https://test-host.example"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(SystemTextJsonSerializer.GetDefaultJsonSerializerOptions());
        services.AddSingleton<IStringSerialiser, SystemTextJsonSerializer>();
        services.AddSingleton<Func<object, string>>(_ => value => StringSerialiser.Serialise(value));
        services.AddSingleton<Func<string, Type, object>>(_ => (json, type) => StringSerialiser.DeserializeObject<object>(json, type));
        services.AddHttpContextAccessor();
        services.AddSingleton<Func<string, string>>(sp =>
        {
            var apiConfiguration = sp.GetRequiredService<IConfiguration>().GetSection("ApiConfiguration");

            return configSetting =>
            {
                if (string.IsNullOrWhiteSpace(configSetting))
                {
                    return string.Empty;
                }

                var child = apiConfiguration.GetChildren()
                    .FirstOrDefault(c => string.Equals(c.Key, configSetting, StringComparison.OrdinalIgnoreCase));

                return child?.Value ?? string.Empty;
            };
        });
        services.AddSingleton<MerchantPosSettingsStore>();
        services.AddSingleton<MerchantMetrics>();
        services.AddSingleton<IMerchantRuntimeFactory, MerchantRuntimeFactory>();
        services.AddDbContext<MerchantDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddScoped<IEfRepository, EfRepository>();
        services.AddScoped<MerchantRuntime>();
        services.RegisterHttpClient<ISecurityServiceClient, SecurityServiceClient>();
        services.RegisterHttpClient<ITransactionProcessorClient, TransactionProcessorClient>();
        services.RegisterHttpClient<IApiClient, ApiClient>();

        return services.BuildServiceProvider();
    }
}
