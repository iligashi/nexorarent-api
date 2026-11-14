using System.Threading.RateLimiting;

namespace NexoraRent.Api.Http;

internal static class RateLimiting
{
    /// <summary>Same as the Express reservationLimiter: 20 requests per IP every 15 minutes.</summary>
    public const string ReservationsPolicy = "reservations";

    /// <summary>
    /// Same global budget as Express: 1000 requests per client IP every 15 minutes.
    /// Process-local for now; a shared store is needed once there is more than one instance.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var reservationsLimit = configuration.GetValue("RateLimits:ReservationsPerWindow", 20);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many requests, please try again later." }, cancellationToken);

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1000,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(ReservationsPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = reservationsLimit,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
