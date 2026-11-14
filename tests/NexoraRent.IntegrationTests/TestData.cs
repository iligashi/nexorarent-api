using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NexoraRent.IntegrationTests;

internal static class TestData
{
    public const string CustomerId = "11111111-1111-4111-8111-111111111111";
    public const string GolfId = "aaaaaaaa-0000-4000-8000-000000000001";
    public const string BmwId = "aaaaaaaa-0000-4000-8000-000000000002";

    /// <summary>Creates a token the way Express does: <c>jwt.sign({ userId }, JWT_SECRET, { expiresIn: '15m' })</c>.</summary>
    public static string AccessToken(string userId, TimeSpan? lifetime = null, string secret = ApiFixture.JwtSecret)
    {
        var now = DateTime.UtcNow;
        var expires = now + (lifetime ?? TimeSpan.FromMinutes(15));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("userId", userId)]),
            IssuedAt = expires < now ? expires.AddMinutes(-15) : now,
            NotBefore = expires < now ? expires.AddMinutes(-15) : now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
