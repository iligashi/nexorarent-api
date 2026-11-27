using NexoraRent.Api.Http;

namespace NexoraRent.Api.Auth;

/// <summary>
/// The <c>refreshToken</c> cookie with the options of <c>refreshCookieOptions</c> in Express:
/// HttpOnly, Secure in production, SameSite from COOKIE_SAME_SITE, and only sent to
/// <c>/api/v1/auth</c>, so no other route ever sees it.
/// </summary>
internal sealed class RefreshCookie(IConfiguration configuration, IHostEnvironment environment, TimeProvider time)
{
    public const string Name = "refreshToken";
    private const string Path = "/api/v1/auth";

    private readonly SameSiteMode _sameSite = LegacyEnvironment.CookieSameSite(configuration);
    private readonly bool _secure = environment.IsProduction();

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public void Write(HttpResponse response, string token) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secure,
            SameSite = _sameSite,
            Path = Path,
            MaxAge = AuthTokens.RefreshLifetime,
            Expires = time.GetUtcNow() + AuthTokens.RefreshLifetime,
        });

    /// <summary>Expires the cookie. The path, SameSite and Secure flags must match or browsers keep it.</summary>
    public void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secure,
            SameSite = _sameSite,
            Path = Path,
        });
}
