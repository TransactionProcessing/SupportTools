using Microsoft.Extensions.Configuration;

namespace HealthMonitoring.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Development_configuration_contains_the_sql_server_connection()
    {
        var repositoryRoot = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.Parent!.FullName;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(repositoryRoot, "HealthMonitoring"))
            .AddJsonFile("appsettings.Development.json")
            .Build();

        var connectionString = configuration.GetConnectionString("HealthMonitoring");
        Assert.Contains("Server=localhost;Database=HealthMonitoring;User Id=sa;", connectionString, StringComparison.Ordinal);
        Assert.Contains("TrustServerCertificate=True;Encrypt=False;", connectionString, StringComparison.Ordinal);
    }
}
