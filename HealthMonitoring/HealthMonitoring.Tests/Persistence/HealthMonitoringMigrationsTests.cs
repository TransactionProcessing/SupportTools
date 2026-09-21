using HealthMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMonitoring.Tests.Persistence;

public sealed class HealthMonitoringMigrationsTests
{
    [Fact]
    public void Health_monitoring_context_has_an_EF_migration()
    {
        var options = new DbContextOptionsBuilder<HealthMonitoringDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=HealthMonitoringTest;")
            .Options;

        using var context = new HealthMonitoringDbContext(options);

        Assert.NotEmpty(context.Database.GetMigrations());
    }
}
