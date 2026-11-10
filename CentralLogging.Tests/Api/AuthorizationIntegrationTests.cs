using System.Net;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection("CentralLoggingApi")]
public sealed class AuthorizationIntegrationTests(SqlServerApiFixture fixture)
{
    [SqlServerFact]
    public async Task Protected_api_endpoints_accept_the_configured_key()
    {
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(HttpMethod.Get, "/api/logs/services");
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [SqlServerFact]
    public async Task Protected_api_endpoints_reject_missing_and_incorrect_keys()
    {
        await fixture.ResetDatabaseAsync();

        String[] protectedRoutes =
        [
            "/api/logs/services",
            "/api/logs/page?fromUtc=2026-11-09T00%3A00%3A00Z&toUtc=2026-11-10T00%3A00%3A00Z",
            "/api/logs/export?fromUtc=2026-11-09T00%3A00%3A00Z&toUtc=2026-11-10T00%3A00%3A00Z",
            "/api/logs/aggregates?fromUtc=2026-11-09T00%3A00%3A00Z&toUtc=2026-11-10T00%3A00%3A00Z"
        ];

        foreach (String route in protectedRoutes)
        {
            using HttpResponseMessage missingKey = await fixture.Client.GetAsync(route);
            missingKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            using HttpRequestMessage wrongKeyRequest = new(HttpMethod.Get, route);
            wrongKeyRequest.Headers.Add("X-Log-Api-Key", "incorrect-key");
            using HttpResponseMessage wrongKey = await fixture.Client.SendAsync(wrongKeyRequest);
            wrongKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [SqlServerFact]
    public async Task Readiness_and_static_search_ui_remain_available_without_an_api_key()
    {
        using HttpResponseMessage readiness = await fixture.Client.GetAsync("/health/ready");
        readiness.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage ui = await fixture.Client.GetAsync("/");
        ui.StatusCode.ShouldBe(HttpStatusCode.OK);
        ui.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        (await ui.Content.ReadAsStringAsync()).ShouldContain("Central Log Search");
    }
}
