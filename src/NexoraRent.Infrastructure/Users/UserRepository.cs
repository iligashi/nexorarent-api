using Dapper;
using MySqlConnector;
using NexoraRent.Core.Users;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Users;

/// <summary>Mirrors the user queries in <c>server/src/routes/auth.routes.js</c>.</summary>
public sealed class UserRepository(MySqlConnectionFactory connections) : IUserRepository
{
    /// <summary>The public columns of a user. Never includes <c>password_hash</c>.</summary>
    internal const string SelectById = "SELECT id, email, first_name, last_name, phone, role FROM users WHERE id = @id";

    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<User>(
            new CommandDefinition(SelectById, new { id }, cancellationToken: cancellationToken));
    }

    public async Task<UserCredentials?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = "SELECT id, email, password_hash, first_name, last_name, phone, role FROM users WHERE email = @email";

        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CredentialsRow>(
            new CommandDefinition(sql, new { email }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new UserCredentials(
                new User { Id = row.Id, Email = row.Email, FirstName = row.FirstName, LastName = row.LastName, Phone = row.Phone, Role = row.Role },
                row.PasswordHash);
    }

    public async Task<User?> CreateAsync(NewUser user, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var existing = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT id FROM users WHERE email = @Email", new { user.Email }, cancellationToken: cancellationToken));
        if (existing is not null)
        {
            return null;
        }

        var id = Guid.NewGuid().ToString();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO users (id, email, password_hash, first_name, last_name, phone)
                VALUES (@id, @Email, @PasswordHash, @FirstName, @LastName, @Phone)
                """,
                new { id, user.Email, user.PasswordHash, user.FirstName, user.LastName, user.Phone },
                cancellationToken: cancellationToken));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // Two sign-ups with the same email at once: the unique key decides, and the loser gets 409.
            return null;
        }

        return await connection.QuerySingleAsync<User>(
            new CommandDefinition(SelectById, new { id }, cancellationToken: cancellationToken));
    }

    public async Task<User?> UpdateProfileAsync(string id, ProfileChanges changes, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE users
            SET first_name = COALESCE(@FirstName, first_name),
                last_name = COALESCE(@LastName, last_name),
                phone = COALESCE(@Phone, phone)
            WHERE id = @id
            """,
            new { id, changes.FirstName, changes.LastName, changes.Phone },
            cancellationToken: cancellationToken));

        return await connection.QuerySingleOrDefaultAsync<User>(
            new CommandDefinition(SelectById, new { id }, cancellationToken: cancellationToken));
    }

    public async Task<LoyaltySummary?> GetLoyaltyAsync(string userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LoyaltySummary>(new CommandDefinition(
            "SELECT points_balance, tier FROM loyalty_accounts WHERE user_id = @userId",
            new { userId }, cancellationToken: cancellationToken));
    }

    private sealed record CredentialsRow
    {
        public required string Id { get; init; }
        public required string Email { get; init; }
        public required string PasswordHash { get; init; }
        public required string FirstName { get; init; }
        public required string LastName { get; init; }
        public string? Phone { get; init; }
        public required string Role { get; init; }
    }
}
