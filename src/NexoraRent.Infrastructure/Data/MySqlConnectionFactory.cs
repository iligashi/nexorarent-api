using Microsoft.Extensions.Options;
using MySqlConnector;

namespace NexoraRent.Infrastructure.Data;

public sealed class MySqlConnectionFactory(IOptions<DatabaseOptions> options)
{
    private readonly string _connectionString = options.Value.BuildConnectionString();

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }
}
