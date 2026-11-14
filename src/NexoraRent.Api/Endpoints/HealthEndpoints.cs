using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Api.Endpoints;

internal static class HealthEndpoints
{
    /// <summary><c>GET /api/health</c>, same response shape as the Express health check.</summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (MySqlConnectionFactory database, IHostEnvironment environment, CancellationToken cancellationToken) =>
        {
            var databaseOk = await database.CanConnectAsync(cancellationToken);

            return Results.Json(
                new
                {
                    status = databaseOk ? "ok" : "degraded",
                    database = databaseOk ? "ok" : "unavailable",
                    rate_limit_store = "process-local",
                    image_storage = environment.IsProduction() ? "object-storage" : "local",
                    service = "nexorarent-api (.NET)",
                    timestamp = DateTime.UtcNow,
                },
                statusCode: databaseOk ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        })
        .WithTags("Health")
        .WithName("Health");

        return app;
    }
}
