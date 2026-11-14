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
    public const string SkodaId = "aaaaaaaa-0000-4000-8000-000000000003";
    public const string FiatId = "aaaaaaaa-0000-4000-8000-000000000004";
    public const string OwnerId = "22222222-2222-4222-8222-222222222222";
    public const string OtherCustomerId = "33333333-3333-4333-8333-333333333333";
    public const string DrenasId = "eeeeeeee-0000-4000-8000-000000000001";
    public const string AirportId = "eeeeeeee-0000-4000-8000-000000000002";
    public const string ClosedLocationId = "eeeeeeee-0000-4000-8000-000000000003";
    public const string GpsId = "ffffffff-0000-4000-8000-000000000001";
    public const string ChildSeatId = "ffffffff-0000-4000-8000-000000000002";
    public const string RetiredExtraId = "ffffffff-0000-4000-8000-000000000003";
    public const string CompletedUnreviewedId = "cccccccc-0000-4000-8000-000000000004";
    public const string OtherCustomersBookingId = "cccccccc-0000-4000-8000-000000000005";

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
