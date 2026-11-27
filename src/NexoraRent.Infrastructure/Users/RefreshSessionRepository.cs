using Dapper;
using NexoraRent.Core.Users;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Users;

/// <summary>
/// Mirrors <c>server/src/services/refresh-tokens.js</c>. Times are compared with
/// <c>UTC_TIMESTAMP()</c> because <c>expires_at</c> is stored in UTC by both APIs,
/// whatever time zone the MySQL server runs in.
/// </summary>
public sealed class RefreshSessionRepository(MySqlConnectionFactory connections) : IRefreshSessionRepository
{
    private const string Insert =
        "INSERT INTO refresh_token_sessions (id, user_id, token_hash, expires_at) VALUES (@Id, @UserId, @TokenHash, @ExpiresAt)";

    public async Task CreateAsync(RefreshSession session, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Insert, session, cancellationToken: cancellationToken));
    }

    public async Task<User?> RotateAsync(string sessionId, string tokenHash, string userId, RefreshSession replacement, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        // Disposing without a commit rolls back, so every early return below leaves the session untouched.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The row lock taken here makes two refreshes with the same token race safely: one wins, one gets 0 rows.
        var revoked = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE refresh_token_sessions
            SET revoked_at = UTC_TIMESTAMP()
            WHERE id = @sessionId AND token_hash = @tokenHash AND revoked_at IS NULL AND expires_at > UTC_TIMESTAMP()
            """,
            new { sessionId, tokenHash }, transaction, cancellationToken: cancellationToken));
        if (revoked != 1)
        {
            return null;
        }

        var user = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            UserRepository.SelectById, new { id = userId }, transaction, cancellationToken: cancellationToken));
        if (user is null)
        {
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(Insert, replacement, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_token_sessions SET replaced_by_session_id = @nextId WHERE id = @sessionId",
            new { nextId = replacement.Id, sessionId }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    public async Task RevokeAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_token_sessions SET revoked_at = UTC_TIMESTAMP() WHERE token_hash = @tokenHash AND revoked_at IS NULL",
            new { tokenHash }, cancellationToken: cancellationToken));
    }

    public async Task RevokeAllAsync(string userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_token_sessions SET revoked_at = UTC_TIMESTAMP() WHERE user_id = @userId AND revoked_at IS NULL",
            new { userId }, cancellationToken: cancellationToken));
    }
}
