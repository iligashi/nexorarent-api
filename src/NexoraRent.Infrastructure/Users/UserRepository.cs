using Dapper;
using NexoraRent.Core.Users;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Users;

public sealed class UserRepository(MySqlConnectionFactory connections) : IUserRepository
{
    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT id, email, first_name, last_name, phone, role FROM users WHERE id = @id";

        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<User>(
            new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));
    }
}
