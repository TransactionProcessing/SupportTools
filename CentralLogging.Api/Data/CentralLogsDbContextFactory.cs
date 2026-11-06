using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CentralLogging.Api.Data;

public sealed class CentralLogsDbContextFactory : IDesignTimeDbContextFactory<CentralLogsDbContext>
{
    public CentralLogsDbContext CreateDbContext(String[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(typeof(CentralLogsDbContextFactory).Assembly.Location)!)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        String connectionString = configuration.GetConnectionString("CentralLogs")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:CentralLogs for EF Core tooling.");

        DbContextOptions<CentralLogsDbContext> options = new DbContextOptionsBuilder<CentralLogsDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new CentralLogsDbContext(options);
    }
}
