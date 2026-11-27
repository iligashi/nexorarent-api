using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Testcontainers.MySql;

[assembly: AssemblyFixture(typeof(NexoraRent.IntegrationTests.ApiFixture))]

namespace NexoraRent.IntegrationTests;

/// <summary>
/// Starts one MySQL container for the test run, loads the schema and seed data,
/// and hosts the API against it.
/// <para>
/// Without Docker the tests are skipped locally. CI sets REQUIRE_DOCKER=true so
/// a missing Docker daemon fails the run instead of silently skipping.
/// </para>
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string JwtSecret = "integration-test-secret-that-is-long-enough-0123456789";
    public const string JwtRefreshSecret = "integration-refresh-secret-that-is-long-enough-987654";
    public const string ClientOrigin = "http://localhost:3000";

    private MySqlContainer? _mysql;
    private WebApplicationFactory<Program>? _factory;

    public string? SkipReason { get; private set; }

    public HttpClient CreateClient()
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }

        return _factory!.CreateClient();
    }

    /// <summary>A client that keeps no cookies, so a test can send and read the refresh cookie itself.</summary>
    public HttpClient CreateClientWithoutCookies()
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }

        return _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    /// <summary>A connection to the test database, to check the rows a request left behind.</summary>
    public async Task<MySqlConnection> OpenDatabaseAsync()
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }

        // Like the API: CHAR(36) ids stay strings.
        var settings = new MySqlConnectionStringBuilder(_mysql!.GetConnectionString()) { GuidFormat = MySqlGuidFormat.None };
        var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _mysql = new MySqlBuilder("mysql:8.4").Build();
            await _mysql.StartAsync();
        }
        catch (Exception exception) when (!RequireDocker() && IsDockerUnavailable(exception))
        {
            SkipReason = "Docker is not available, so the MySQL integration tests were skipped.";
            return;
        }

        await RunScriptAsync("001_schema.sql");
        await RunScriptAsync("002_seed.sql");

        var connectionString = _mysql.GetConnectionString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("JWT_SECRET", JwtSecret);
            builder.UseSetting("JWT_REFRESH_SECRET", JwtRefreshSecret);
            builder.UseSetting("CLIENT_URL", ClientOrigin);
            // All test clients share one rate-limit partition, so lift the 20-per-15-minutes booking limit.
            builder.UseSetting("RateLimits:ReservationsPerWindow", "100000");
            builder.UseSetting("RateLimits:AuthPerWindow", "100000");
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_mysql is not null)
        {
            await _mysql.DisposeAsync();
        }
    }

    private async Task RunScriptAsync(string fileName)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", fileName));
        await using var connection = new MySqlConnection(_mysql!.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static bool RequireDocker() =>
        string.Equals(Environment.GetEnvironmentVariable("REQUIRE_DOCKER"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DotNet.Testcontainers.Builders.DockerUnavailableException
        || exception.GetType().Name.Contains("Docker", StringComparison.Ordinal)
        || exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}
