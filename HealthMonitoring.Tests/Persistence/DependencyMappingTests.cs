using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMonitoring.Tests.Persistence;

public sealed class DependencyMappingTests
{
    [Fact]
    public void Dependency_links_are_mapped_with_a_unique_parent_and_name_index()
    {
        var options = new DbContextOptionsBuilder<HealthMonitoringDbContext>()
            .UseSqlServer("Server=.;Database=ignored;")
            .Options;
        using var context = new HealthMonitoringDbContext(options);

        var entity = context.Model.FindEntityType(typeof(ServiceDependencyLink));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ServiceDependencyLink.MonitoredServiceId), nameof(ServiceDependencyLink.DependencyName)]));
    }
}
