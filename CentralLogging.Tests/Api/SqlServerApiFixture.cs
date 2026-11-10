using CentralLogging.Api.Data;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace CentralLogging.Tests.Api;

public sealed class SqlServerApiFixture : IAsyncLifetime
{
    public static String ConnectionStringEnvironmentVariable => "CENTRAL_LOGGING_TEST_CONNECTION";
    public static String ApiKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private const String SqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";
    private static readonly String SqlServerPassword = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";

    private WebApplicationFactory<Program>? Factory;
    private MsSqlContainer? Container;

    public HttpClient Client { get; private set; } = null!;
    public String ConnectionString { get; private set; } = String.Empty;

    public async Task InitializeAsync()
    {
        String? connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (String.IsNullOrWhiteSpace(connectionString))
        {
            await this.ConfigureWslDockerHostAsync();
            this.Container = new MsSqlBuilder(SqlServerImage)
                .WithPassword(SqlServerPassword)
                .WithDatabase("CentralLogsIntegration")
                .Build();
            await this.Container.StartAsync();
            connectionString = this.Container.GetConnectionString();
        }

        SqlConnectionStringBuilder connectionStringBuilder = new(connectionString)
        {
            Encrypt = false,
            TrustServerCertificate = true
        };
        this.ConnectionString = connectionStringBuilder.ConnectionString;

        this.Factory = new CentralLoggingApiFactory(this.ConnectionString);
        this.Client = this.CreateClientWithStartupEnvironment(this.ConnectionString);

        IConfiguration configuration = this.Factory.Services.GetRequiredService<IConfiguration>();
        if (configuration["LogIngestion:ApiKey"] != ApiKey)
            throw new InvalidOperationException("The integration test API key was not applied to the test host configuration.");
    }

    private async Task ConfigureWslDockerHostAsync()
    {
        if (!ShouldDiscoverWslHost(TestcontainersSettings.DockerHostOverride))
            return;

        String? output = await ReadWslHostAddressesAsync();
        String? wslHost = FindWslHostAddress(output);
        if (!String.IsNullOrWhiteSpace(wslHost))
            TestcontainersSettings.DockerHostOverride = wslHost;
    }

    private static Boolean ShouldDiscoverWslHost(String? configuredHost)
    {
        if (!OperatingSystem.IsWindows() || String.IsNullOrWhiteSpace(configuredHost))
            return OperatingSystem.IsWindows();

        if (String.Equals(configuredHost, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(configuredHost, out IPAddress? address) && IPAddress.IsLoopback(address);
    }

    private static async Task<String?> ReadWslHostAddressesAsync()
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-e");
            process.StartInfo.ArgumentList.Add("hostname");
            process.StartInfo.ArgumentList.Add("-I");

            if (!process.Start()) return null;
            String output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or TimeoutException)
        {
            // Without WSL, Testcontainers' default host detection remains in effect.
            return null;
        }
    }

    private static String? FindWslHostAddress(String? output) => output?
        .Split((Char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(address => IPAddress.TryParse(address, out IPAddress? parsed) ? parsed : null)
        .FirstOrDefault(address => address is { AddressFamily: System.Net.Sockets.AddressFamily.InterNetwork } && !IPAddress.IsLoopback(address))
        ?.ToString();

    private HttpClient CreateClientWithStartupEnvironment(String connectionString)
    {
        const String connectionSetting = "ConnectionStrings__CentralLogs";
        String keyEnvironmentVariable = String.Concat("LogIngestion", "__", "Api", "Key");
        String? previousConnectionString = Environment.GetEnvironmentVariable(connectionSetting);
        String? previousApiKey = Environment.GetEnvironmentVariable(keyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(connectionSetting, connectionString);
            Environment.SetEnvironmentVariable(keyEnvironmentVariable, ApiKey);
            return this.Factory!.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionSetting, previousConnectionString);
            Environment.SetEnvironmentVariable(keyEnvironmentVariable, previousApiKey);
        }
    }

    public async Task ResetDatabaseAsync()
    {
        await using AsyncServiceScope scope = this.Factory!.Services.CreateAsyncScope();
        CentralLogsDbContext database = scope.ServiceProvider.GetRequiredService<CentralLogsDbContext>();
        await database.ServiceStructuredFields.ExecuteDeleteAsync();
        await database.LogEvents.ExecuteDeleteAsync();
    }

    public async Task<T> ReadDatabaseAsync<T>(Func<CentralLogsDbContext, Task<T>> query)
    {
        await using AsyncServiceScope scope = this.Factory!.Services.CreateAsyncScope();
        CentralLogsDbContext database = scope.ServiceProvider.GetRequiredService<CentralLogsDbContext>();
        return await query(database);
    }

    public async Task DisposeAsync()
    {
        this.Client?.Dispose();
        this.Factory?.Dispose();
        if (this.Container is not null)
            await this.Container.DisposeAsync();
    }

    private sealed class CentralLoggingApiFactory(String connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<String, String?>
                {
                    ["ConnectionStrings:CentralLogs"] = connectionString,
                    ["LogIngestion:ApiKey"] = ApiKey
                }));
        }
    }
}

[CollectionDefinition("CentralLoggingApi")]
public sealed class CentralLoggingApiCollection : ICollectionFixture<SqlServerApiFixture>
{
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
}
