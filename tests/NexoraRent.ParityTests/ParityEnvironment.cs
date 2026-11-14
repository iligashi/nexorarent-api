using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;

namespace NexoraRent.ParityTests;

/// <summary>
/// Settings for the parity run, read from environment variables:
/// <list type="bullet">
/// <item>PARITY_EXPRESS_URL and PARITY_DOTNET_URL: the two APIs, on the same database.</item>
/// <item>PARITY_JWT_SECRET: their shared JWT_SECRET, to sign customer tokens (write tests only).</item>
/// <item>PARITY_DB: a MySqlConnector connection string to that database (write tests only).</item>
/// </list>
/// </summary>
internal static class ParityEnvironment
{
    /// <summary>
    /// One xUnit collection for every class that uses the parity database. Collections run in
    /// parallel, and the write tests change the database for a moment, so a read in another
    /// collection could see an unfinished booking on one API and not the other.
    /// </summary>
    public const string SharedDatabase = "Parity database";

    public static readonly Uri? ExpressUrl = Url("PARITY_EXPRESS_URL");
    public static readonly Uri? DotnetUrl = Url("PARITY_DOTNET_URL");
    public static readonly string? JwtSecret = Environment.GetEnvironmentVariable("PARITY_JWT_SECRET");
    public static readonly string? Database = Environment.GetEnvironmentVariable("PARITY_DB");

    public static void SkipUnlessApisConfigured()
    {
        if (ExpressUrl is null || DotnetUrl is null)
        {
            Assert.Skip("Set PARITY_EXPRESS_URL and PARITY_DOTNET_URL to compare the two APIs.");
        }
    }

    public static void SkipUnlessWritesConfigured()
    {
        SkipUnlessApisConfigured();
        if (string.IsNullOrEmpty(JwtSecret) || string.IsNullOrEmpty(Database))
        {
            Assert.Skip("Set PARITY_JWT_SECRET and PARITY_DB to compare write routes.");
        }
    }

    public static async Task<MySqlConnection> OpenDatabaseAsync()
    {
        // Callers may open the database before sending anything, so skip here too.
        SkipUnlessWritesConfigured();

        // Like the API: CHAR(36) ids stay strings, and the test SQL may use several statements.
        var settings = new MySqlConnectionStringBuilder(Database!) { GuidFormat = MySqlGuidFormat.None, AllowUserVariables = true };
        var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    /// <summary>An HS256 access token like Express issues: <c>jwt.sign({ userId }, JWT_SECRET, { expiresIn: '15m' })</c>.</summary>
    public static string AccessToken(string userId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);
        var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { userId, iat = now, exp = now + 900 }));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(JwtSecret!), Encoding.ASCII.GetBytes($"{header}.{payload}"));
        return $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
    }

    private static Uri? Url(string name) =>
        Uri.TryCreate(Environment.GetEnvironmentVariable(name), UriKind.Absolute, out var url) ? url : null;
}
