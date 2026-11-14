using System.Globalization;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Api.Http;

/// <summary>
/// Lets this API read the same flat environment variables as the Express API
/// (DB_HOST, JWT_SECRET, CLIENT_URL, ...) so both services can run from one
/// shared Railway configuration while routes are migrated.
/// </summary>
internal static class LegacyEnvironment
{
    public static void ApplyDatabase(IConfiguration configuration, DatabaseOptions options)
    {
        options.Host = configuration["DB_HOST"] ?? options.Host;
        options.Name = configuration["DB_NAME"] ?? options.Name;
        options.User = configuration["DB_USER"] ?? options.User;
        options.Password = configuration["DB_PASSWORD"] ?? options.Password;

        if (uint.TryParse(configuration["DB_PORT"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            options.Port = port;
        }
    }

    public static string? JwtSecret(IConfiguration configuration) =>
        configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"];

    public static IReadOnlyList<string> ClientOrigins(IConfiguration configuration) =>
        (configuration["CLIENT_URL"] ?? configuration["Cors:ClientUrl"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
