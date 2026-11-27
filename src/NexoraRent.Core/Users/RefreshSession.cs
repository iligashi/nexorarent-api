namespace NexoraRent.Core.Users;

/// <summary>
/// A row of <c>refresh_token_sessions</c>. Only the SHA-256 hash of the refresh token is
/// stored, so a database leak does not hand out working tokens.
/// </summary>
public sealed record RefreshSession(string Id, string UserId, string TokenHash, DateTime ExpiresAt);

/// <summary>Mirrors <c>server/src/services/refresh-tokens.js</c> in the Express API.</summary>
public interface IRefreshSessionRepository
{
    Task CreateAsync(RefreshSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the session that matches both <paramref name="sessionId"/> and <paramref name="tokenHash"/>
    /// and stores <paramref name="replacement"/> in its place, in one transaction. Returns the user, or null when
    /// the session is unknown, expired or already revoked (each refresh token works once).
    /// </summary>
    Task<User?> RotateAsync(string sessionId, string tokenHash, string userId, RefreshSession replacement, CancellationToken cancellationToken);

    Task RevokeAsync(string tokenHash, CancellationToken cancellationToken);

    Task RevokeAllAsync(string userId, CancellationToken cancellationToken);
}
