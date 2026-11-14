using System.Text.RegularExpressions;

namespace NexoraRent.Api.Http;

internal static partial class CorsSetup
{
    public const string PolicyName = "client";

    /// <summary>
    /// Allows the origins listed in CLIENT_URL, with credentials so the refresh
    /// cookie is sent. In development any localhost port is allowed too.
    /// </summary>
    public static IServiceCollection AddClientCors(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var allowed = LegacyEnvironment.ClientOrigins(configuration).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowLocalhost = !environment.IsProduction();

        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .SetIsOriginAllowed(origin => allowed.Contains(origin) || (allowLocalhost && LocalhostOrigin().IsMatch(origin)))
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(RequestIdMiddleware.HeaderName)));

        return services;
    }

    [GeneratedRegex(@"^http://(localhost|127\.0\.0\.1)(:\d+)?$")]
    private static partial Regex LocalhostOrigin();
}
