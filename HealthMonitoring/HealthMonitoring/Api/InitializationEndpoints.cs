using HealthMonitoring.Monitoring;

namespace HealthMonitoring.Api;

public static class InitializationEndpoints
{
    public static IEndpointRouteBuilder MapInitializationEndpoints(this IEndpointRouteBuilder endpoints, string dashboardEnvironment)
    {
        endpoints.MapGet("/api/initialization/status", async (IInitializationService initialization, CancellationToken cancellationToken) =>
            Results.Ok(await initialization.GetStatusAsync(cancellationToken)));

        endpoints.MapPost("/api/initialization", async (InitializationRequest request, IInitializationService initialization, CancellationToken cancellationToken) =>
        {
            try
            {
                await initialization.InitializeAsync(request, dashboardEnvironment, cancellationToken);
                return Results.Ok(await initialization.GetStatusAsync(cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["initialization"] = [exception.Message] });
            }
        });

        return endpoints;
    }
}
