using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Compares the auth routes of Express and .NET, and checks that a session moves freely between
/// them: a password hashed by one is accepted by the other, and a refresh token issued by one is
/// rotated by the other.
/// <para>
/// Express allows 20 login and register requests per IP every 15 minutes, and this class sends 12
/// of them to Express. Restart Express before running it again within that window.
/// </para>
/// </summary>
[Collection(ParityEnvironment.SharedDatabase)]
public class AuthParityTests
{
    private const string Password = "parity-password-123";

    // The browser origin both APIs trust: CLIENT_URL in CI, and a localhost origin in development.
    private const string Origin = "http://localhost:3000";

    /// <summary>Values each server generates itself.</summary>
    private static readonly HashSet<string> Generated = new(StringComparer.Ordinal) { "id", "accessToken" };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registering_matches()
    {
        var email = NewEmail();
        var body = Register(email, phone: " +383 44 000 111 ");

        var express = await SendAsync(Api.Express, HttpMethod.Post, "/api/v1/auth/register", body);
        await DeleteUserAsync(email);
        var dotnet = await SendAsync(Api.Dotnet, HttpMethod.Post, "/api/v1/auth/register", body);

        AssertSame("POST /auth/register", express, dotnet, Generated);
        Assert.Equal(201, dotnet.Status);
        Assert.NotNull(dotnet.RefreshToken);

        // The user .NET created now exists for both: a second sign-up is a conflict on each.
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/register", Register(email.ToUpperInvariant()));
        await DeleteUserAsync(email);
    }

    [Fact]
    public async Task Validation_errors_match()
    {
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/register", "{}");
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/register", Json(new { email = "nope", password = "short", first_name = " ", last_name = 5, phone = (string?)null }));
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/login", "[]");
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/login", Json(new { email = "a@b", password = "" }));
    }

    [Fact]
    public async Task Passwords_work_across_both_apis()
    {
        var fromExpress = NewEmail();
        var fromDotnet = NewEmail();
        Assert.Equal(201, (await SendAsync(Api.Express, HttpMethod.Post, "/api/v1/auth/register", Register(fromExpress))).Status);
        Assert.Equal(201, (await SendAsync(Api.Dotnet, HttpMethod.Post, "/api/v1/auth/register", Register(fromDotnet))).Status);

        // Each API signs in the user whose password the other one hashed.
        var dotnetLogin = await SendAsync(Api.Dotnet, HttpMethod.Post, "/api/v1/auth/login", Json(new { email = fromExpress, password = Password }));
        var expressLogin = await SendAsync(Api.Express, HttpMethod.Post, "/api/v1/auth/login", Json(new { email = fromDotnet, password = Password }));
        Assert.True(dotnetLogin.Status == 200, $".NET login of an Express user: {dotnetLogin.Status} {dotnetLogin.Body}");
        Assert.True(expressLogin.Status == 200, $"Express login of a .NET user: {expressLogin.Status} {expressLogin.Body}");

        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/login", Json(new { email = fromExpress, password = "wrong-password" }));
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/login", Json(new { email = "nobody-" + fromExpress, password = Password }));

        await DeleteUserAsync(fromExpress);
        await DeleteUserAsync(fromDotnet);
    }

    [Fact]
    public async Task Refresh_tokens_rotate_across_both_apis()
    {
        var email = NewEmail();
        var registered = await SendAsync(Api.Express, HttpMethod.Post, "/api/v1/auth/register", Register(email));
        var fromExpress = registered.RefreshToken!;

        // Express issued it, .NET rotates it ...
        var rotated = await SendAsync(Api.Dotnet, HttpMethod.Post, "/api/v1/auth/refresh", null, cookie: fromExpress, origin: Origin);
        Assert.True(rotated.Status == 200, $".NET refresh: {rotated.Status} {rotated.Body}");

        // ... after which neither API accepts the old one,
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/refresh", null, cookie: fromExpress, origin: Origin);

        // and Express rotates the one .NET issued, with the same response.
        var expressRefresh = await SendAsync(Api.Express, HttpMethod.Post, "/api/v1/auth/refresh", null, cookie: rotated.RefreshToken, origin: Origin);
        Assert.True(expressRefresh.Status == 200, $"Express refresh: {expressRefresh.Status} {expressRefresh.Body}");
        AssertSame("refresh", expressRefresh, rotated, new HashSet<string>(StringComparer.Ordinal) { "accessToken" });

        // An access token from .NET works on Express too.
        var accessToken = rotated.Body.GetProperty("accessToken").GetString()!;
        Assert.Equal(200, (await SendAsync(Api.Express, HttpMethod.Get, "/api/v1/auth/me", null, token: accessToken)).Status);

        await DeleteUserAsync(email);
    }

    [Fact]
    public async Task Refresh_and_logout_errors_match()
    {
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/refresh", null, origin: Origin);
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/refresh", null, cookie: "not-a-jwt", origin: Origin);
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/refresh", null, cookie: "not-a-jwt");
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/refresh", null, cookie: "not-a-jwt", origin: "https://evil.example");
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/logout", null);
        await AssertSameAsync(HttpMethod.Post, "/api/v1/auth/logout", null, origin: Origin);
    }

    [Fact]
    public async Task Profile_and_loyalty_match()
    {
        var email = NewEmail();
        var registered = await SendAsync(Api.Dotnet, HttpMethod.Post, "/api/v1/auth/register", Register(email));
        var token = registered.Body.GetProperty("accessToken").GetString()!;

        await AssertSameAsync(HttpMethod.Get, "/api/v1/auth/me/loyalty", null, token: token);
        await AssertSameAsync(HttpMethod.Put, "/api/v1/auth/me", "{}", token: token);
        await AssertSameAsync(HttpMethod.Put, "/api/v1/auth/me", Json(new { phone = (string?)null }), token: token);
        await AssertSameAsync(HttpMethod.Put, "/api/v1/auth/me", Json(new { first_name = " Same ", phone = "044" }), token: token);
        await AssertSameAsync(HttpMethod.Put, "/api/v1/auth/me", Json(new { last_name = "Only" }));

        await DeleteUserAsync(email);
    }

    private enum Api
    {
        Express,
        Dotnet,
    }

    private sealed record Reply(int Status, JsonElement Body, string? RefreshToken);

    private static async Task AssertSameAsync(
        HttpMethod method, string path, string? body, string? token = null, string? cookie = null, string? origin = null)
    {
        var express = await SendAsync(Api.Express, method, path, body, token, cookie, origin);
        var dotnet = await SendAsync(Api.Dotnet, method, path, body, token, cookie, origin);
        AssertSame($"{method} {path} {body} cookie={cookie is not null} origin={origin}", express, dotnet);
    }

    private static void AssertSame(string label, Reply express, Reply dotnet, IReadOnlySet<string>? ignored = null)
    {
        Assert.True(express.Status == dotnet.Status, $"{label}: status {express.Status} from Express, {dotnet.Status} from .NET{Environment.NewLine}{express.Body}{Environment.NewLine}{dotnet.Body}");
        var differences = JsonParity.Compare(express.Body, dotnet.Body, ignoredKeys: ignored);
        Assert.True(differences.Count == 0, $"{label}:{Environment.NewLine}{string.Join(Environment.NewLine, differences.Take(20))}");
        // A new session sets a cookie on both sides; a failed one sets none on either.
        Assert.Equal(express.RefreshToken is null, dotnet.RefreshToken is null);
    }

    private static async Task<Reply> SendAsync(
        Api api, HttpMethod method, string path, string? body, string? token = null, string? cookie = null, string? origin = null)
    {
        ParityEnvironment.SkipUnlessWritesConfigured();
        var baseUrl = api == Api.Express ? ParityEnvironment.ExpressUrl! : ParityEnvironment.DotnetUrl!;

        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"refreshToken={cookie}");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        using var response = await Http.SendAsync(request, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        // A 204 has no body; compare it as null so both sides still line up.
        var json = JsonDocument.Parse(text.Length == 0 ? "null" : text).RootElement.Clone();
        return new Reply((int)response.StatusCode, json, NewRefreshToken(response));
    }

    /// <summary>The token of a <c>refreshToken</c> cookie being set, or null when there is none or it is being cleared.</summary>
    private static string? NewRefreshToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        var value = values
            .Where(v => v.StartsWith("refreshToken=", StringComparison.Ordinal))
            .Select(v => v["refreshToken=".Length..v.IndexOf(';', StringComparison.Ordinal)])
            .FirstOrDefault();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static async Task DeleteUserAsync(string email)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        // Sessions go with the user (ON DELETE CASCADE).
        await db.ExecuteAsync("DELETE FROM users WHERE email = @email", new { email = email.ToLowerInvariant() });
    }

    private static string NewEmail() => $"parity-auth-{Guid.NewGuid():N}@example.test";

    private static string Register(string email, string? phone = null) => Json(new Dictionary<string, object?>
    {
        ["email"] = email,
        ["password"] = Password,
        ["first_name"] = " Parity ",
        ["last_name"] = "Auth",
        ["phone"] = phone,
    }.Where(p => p.Value is not null).ToDictionary());

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
