using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NexoraRent.Core.Users;

namespace NexoraRent.Api.Auth;

internal sealed record IssuedRefreshToken(RefreshSession Session, string Token);

internal sealed record RefreshClaims(string UserId, string SessionId);

/// <summary>
/// Issues and reads tokens exactly like Express, so either API accepts the other's tokens:
/// <list type="bullet">
/// <item>access: <c>jwt.sign({ userId }, JWT_SECRET, { expiresIn: '15m' })</c></item>
/// <item>refresh: <c>jwt.sign({ userId }, JWT_REFRESH_SECRET, { expiresIn: '7d', jwtid })</c></item>
/// </list>
/// Payloads carry only <c>userId</c>, <c>iat</c>, <c>exp</c> (and <c>jti</c>); no <c>nbf</c>, which
/// Express does not set and which would fail on a server whose clock is slightly behind.
/// </summary>
internal sealed class AuthTokens(JwtSecrets secrets, TimeProvider time)
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7);

    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly SigningCredentials _access = new(new SymmetricSecurityKey(secrets.Access), SecurityAlgorithms.HmacSha256);
    private readonly SigningCredentials _refresh = new(new SymmetricSecurityKey(secrets.Refresh), SecurityAlgorithms.HmacSha256);

    public string CreateAccessToken(string userId)
    {
        var now = time.GetUtcNow();
        return Sign(new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = (now + AccessLifetime).ToUnixTimeSeconds(),
        }, _access);
    }

    public IssuedRefreshToken CreateRefreshToken(string userId)
    {
        var now = time.GetUtcNow();
        var sessionId = Guid.NewGuid().ToString();
        var token = Sign(new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = (now + RefreshLifetime).ToUnixTimeSeconds(),
            ["jti"] = sessionId,
        }, _refresh);

        return new IssuedRefreshToken(new RefreshSession(sessionId, userId, Hash(token), (now + RefreshLifetime).UtcDateTime), token);
    }

    /// <summary>The user and session of a valid, unexpired refresh token; null for anything else.</summary>
    public async Task<RefreshClaims?> ReadRefreshTokenAsync(string token)
    {
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = _refresh.Key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        });

        if (!result.IsValid)
        {
            return null;
        }

        // Express rejects a token without a userId or jti (`!decoded.userId || !decoded.jti`).
        return result.Claims.TryGetValue("userId", out var userId) && userId is string { Length: > 0 } user
            && result.Claims.TryGetValue("jti", out var jti) && jti is string { Length: > 0 } session
                ? new RefreshClaims(user, session)
                : null;
    }

    /// <summary>The value stored in <c>refresh_token_sessions.token_hash</c>: lowercase hex SHA-256.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Sign(Dictionary<string, object> payload, SigningCredentials credentials) =>
        Handler.CreateToken(JsonSerializer.Serialize(payload), credentials);
}
