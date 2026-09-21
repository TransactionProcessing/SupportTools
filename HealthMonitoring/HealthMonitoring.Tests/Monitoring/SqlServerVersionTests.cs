using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class SqlServerVersionTests
{
    [Theory]
    [InlineData("Microsoft SQL Server 2022 (RTM-CU12) - 16.0.4125.3 (X64)", "SQL Server 2022")]
    [InlineData("Microsoft SQL Server 2019 (RTM-CU29) (KB5037331) - 15.0.4390.2 (X64)", "SQL Server 2019")]
    [InlineData("Microsoft SQL Server 2017 (RTM-CU31) - 14.0.3456.2 (X64)", "SQL Server 2017")]
    [InlineData("16.0.1000.6", "SQL Server 2022")]
    [InlineData("15.0.2000.5", "SQL Server 2019")]
    public void Formats_representative_sql_server_versions(string rawVersion, string expected)
    {
        Assert.Equal(expected, SqlServerVersionFormatter.Format(rawVersion));
    }

    [Fact]
    public void Returns_null_for_an_unrecognized_sql_server_version()
    {
        Assert.Null(SqlServerVersionFormatter.Format("Microsoft SQL Server Developer Edition"));
    }

    [Fact]
    public async Task Registration_version_resolution_preserves_fallback_when_query_fails()
    {
        var service = new MonitoredService { MonitorType = MonitorType.SqlServer };
        var probe = new FailingVersionProbe();

        var resolver = new SqlServerRegistrationVersionResolver(probe, NullLogger<SqlServerRegistrationVersionResolver>.Instance);
        var version = await resolver.ResolveAsync(service, "reported-by-caller", CancellationToken.None);

        Assert.Equal("reported-by-caller", version);
    }

    [Fact]
    public async Task Registration_version_resolution_returns_human_readable_sql_server_version()
    {
        var service = new MonitoredService { MonitorType = MonitorType.SqlServer };
        var probe = new VersionProbe("16.0.1000.6");
        var resolver = new SqlServerRegistrationVersionResolver(probe, NullLogger<SqlServerRegistrationVersionResolver>.Instance);

        var version = await resolver.ResolveAsync(service, "reported-by-caller", CancellationToken.None);

        Assert.Equal("SQL Server 2022", version);
    }

    private sealed class FailingVersionProbe : ISqlServerProbe
    {
        public Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken) =>
            Task.FromException<string?>(new InvalidOperationException("version query failed"));
    }

    private sealed class VersionProbe(string version) : ISqlServerProbe
    {
        public Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(version);
    }
}
