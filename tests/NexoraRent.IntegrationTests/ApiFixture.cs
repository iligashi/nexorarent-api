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
