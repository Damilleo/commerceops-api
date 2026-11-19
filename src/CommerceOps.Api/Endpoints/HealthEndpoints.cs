using CommerceOps.Application.Health;

namespace CommerceOps.Api.Endpoints;

internal static class HealthEndpoints
{
    public static void MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => TypedResults.Ok(ServiceHealth.GetLiveness()))
            .WithName("GetHealth")
            .WithTags("Health")
            .WithSummary("Check whether the API is running");
    }
}
