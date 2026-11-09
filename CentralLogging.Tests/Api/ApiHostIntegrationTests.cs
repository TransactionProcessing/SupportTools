using System.Net;
using System.Text.Json;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection("CentralLoggingApi")]
public sealed class ApiHostIntegrationTests(SqlServerApiFixture fixture)
{
    [SqlServerFact]
    public async Task Readiness_reports_ready_after_the_database_migrations_are_applied()
    {
        await fixture.ResetDatabaseAsync();
        using HttpResponseMessage response = await fixture.Client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().ShouldBe("ready");
    }
}
