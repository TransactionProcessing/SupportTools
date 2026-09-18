using Microsoft.Extensions.Configuration;

namespace HealthMonitoring.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Base_configuration_contains_the_development_sql_server_connection()
    {
        var repositoryRoot = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.Parent!.FullName;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(repositoryRoot, "HealthMonitoring"))
            .AddJsonFile("appsettings.json")
            .Build();

        Assert.Equal("Server=localhost;Database=HealthMonitoring;Trusted_Connection=True;TrustServerCertificate=True;", configuration.GetConnectionString("HealthMonitoring"));
    }
}
