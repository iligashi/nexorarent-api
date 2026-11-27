using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MySqlConnector;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// Register, login, refresh and logout against MySQL, with the cookie and token rules of
/// <c>server/src/routes/auth.routes.js</c>. Each test signs up its own user, so tests do not share sessions.
/// </summary>
public class AuthSessionApiTests(ApiFixture api)
{
    private const string Password = "long-enough-password";
    private const string LegacyUserPassword = "correct horse battery staple";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_creates_a_customer_and_starts_a_session()
    {
        var email = NewEmail();
        var response = await RegisterAsync(email, phone: "");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response);
        var user = body.GetProperty("user");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("customer", user.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("phone").ValueKind);
        Assert.False(user.TryGetProperty("password_hash", out _));
        Assert.Equal(["accessToken", "user"], body.EnumerateObject().Select(p => p.Name).Order());

        var cookie = RefreshCookieHeader(response);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=604800", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await GetMeAsync(body.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Access_tokens_carry_only_what_express_puts_in_them()
    {
        var body = await ReadAsync(await RegisterAsync(NewEmail()));
        var payload = DecodePayload(body.GetProperty("accessToken").GetString()!);

        Assert.Equal(["exp", "iat", "userId"], payload.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(15 * 60, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
    }

    [Fact]
    public async Task Register_lowercases_the_email_and_rejects_a_second_account()
    {
        var email = NewEmail();
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(email)).StatusCode);

        var again = await RegisterAsync($"  {email.ToUpperInvariant()} ");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("Email already registered", (await ReadAsync(again)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Register_validates_the_body()
    {
        var response = await PostJsonAsync("/api/v1/auth/register", """{"email":"nope","password":"short","first_name":"A","last_name":"B"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Validation failed", body.GetProperty("error").GetString());
        Assert.Equal(["email", "password"], body.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()));
    }

    [Fact]
    public async Task Login_accepts_a_password_hashed_by_express()
    {
        var response = await LoginAsync("LEGACY@example.test", LegacyUserPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("44444444-4444-4444-8444-444444444444", (await ReadAsync(response)).GetProperty("user").GetProperty("id").GetString());
        Assert.NotNull(RefreshCookieHeader(response));
    }

    [Theory]
    [InlineData("legacy@example.test", "wrong password")]
    [InlineData("nobody@example.test", LegacyUserPassword)]
    // Seeded with "x" as its hash: not a bcrypt hash, so nobody can sign in as this user.
    [InlineData("customer@example.test", "x")]
    public async Task Login_failures_all_look_the_same(string email, string password)
    {
        var response = await LoginAsync(email, password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password", (await ReadAsync(response)).GetProperty("error").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        var first = RefreshToken(await RegisterAsync(NewEmail()));

        var refreshed = await RefreshAsync(first);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = RefreshToken(refreshed);
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync((await ReadAsync(refreshed)).GetProperty("accessToken").GetString()!)).StatusCode);

        var reused = await RefreshAsync(first);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal("Refresh token has expired or been revoked", (await ReadAsync(reused)).GetProperty("error").GetString());
        Assert.Contains("expires=Thu, 01 Jan 1970", RefreshCookieHeader(reused), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(second)).StatusCode);
    }

    [Fact]
    public async Task Two_refreshes_with_the_same_token_at_once_give_one_new_session()
    {
        var token = RefreshToken(await RegisterAsync(NewEmail()));

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(token)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Stored_sessions_keep_only_a_hash_of_the_token()
    {
        var token = RefreshToken(await RegisterAsync(NewEmail()));
        var sessionId = DecodePayload(token).GetProperty("jti").GetString();

        await using var db = await api.OpenDatabaseAsync();
        await using var command = new MySqlCommand("SELECT token_hash FROM refresh_token_sessions WHERE id = @id", db);
        command.Parameters.AddWithValue("@id", sessionId);
        var stored = (string?)await command.ExecuteScalarAsync(Cancel);

        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))), stored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:3001")]
    public async Task Refresh_and_logout_need_a_trusted_origin(string? origin)
    {
        var token = RefreshToken(await RegisterAsync(NewEmail()));

        foreach (var path in new[] { "/api/v1/auth/refresh", "/api/v1/auth/logout" })
        {
            var response = await SendWithCookieAsync(path, token, origin);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Request origin is not allowed", (await ReadAsync(response)).GetProperty("error").GetString());
        }

        // Nothing was revoked by the rejected logout.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(token)).StatusCode);
    }

    [Theory]
    [InlineData(null, "Refresh token required")]
    [InlineData("not-a-jwt", "Invalid refresh token")]
    public async Task Refresh_without_a_usable_cookie_is_rejected(string? cookie, string expectedError)
    {
        var response = await RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(expectedError, (await ReadAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_access_token_is_not_accepted_as_a_refresh_token()
    {
        var body = await ReadAsync(await RegisterAsync(NewEmail()));

        var response = await RefreshAsync(body.GetProperty("accessToken").GetString());

        Assert.Equal("Invalid refresh token", (await ReadAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Logout_revokes_the_session()
    {
        var token = RefreshToken(await RegisterAsync(NewEmail()));

        var logout = await SendWithCookieAsync("/api/v1/auth/logout", token, ApiFixture.ClientOrigin);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", RefreshCookieHeader(logout), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Logout_all_revokes_every_session_of_the_user()
    {
        var email = NewEmail();
        var registered = await RegisterAsync(email);
        var accessToken = (await ReadAsync(registered)).GetProperty("accessToken").GetString()!;
        var sessions = new[] { RefreshToken(registered), RefreshToken(await LoginAsync(email, Password)) };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/logout-all", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await Client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var session in sessions)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session)).StatusCode);
        }
    }

    [Fact]
    public async Task Profile_updates_only_the_fields_sent()
    {
        var body = await ReadAsync(await RegisterAsync(NewEmail(), phone: "+38344111222"));
        var token = body.GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/api/v1/auth/me", UriKind.Relative))
        {
            Content = new StringContent("""{"first_name":"  Renamed  "}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await ReadAsync(response)).GetProperty("user");
        Assert.Equal("Renamed", user.GetProperty("first_name").GetString());
        Assert.Equal("Test", user.GetProperty("last_name").GetString());
        Assert.Equal("+38344111222", user.GetProperty("phone").GetString());
    }

    [Theory]
    [InlineData(TestData.OwnerId, 340, "gold")]
    [InlineData(TestData.CustomerId, 0, "bronze")]
    public async Task Loyalty_returns_the_account_or_the_default(string userId, int points, string tier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/me/loyalty", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestData.AccessToken(userId));
        var response = await Client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(points, body.GetProperty("points_balance").GetInt32());
        Assert.Equal(tier, body.GetProperty("tier").GetString());
    }

    private HttpClient Client => api.CreateClientWithoutCookies();

    private static string NewEmail() => $"auth-{Guid.NewGuid():N}@example.test";

    private Task<HttpResponseMessage> RegisterAsync(string email, string? phone = null) =>
        PostJsonAsync("/api/v1/auth/register", JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["email"] = email,
            ["password"] = Password,
            ["first_name"] = "Auth",
            ["last_name"] = "Test",
            ["phone"] = phone,
        }.Where(p => p.Value is not null).ToDictionary()));

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        PostJsonAsync("/api/v1/auth/login", JsonSerializer.Serialize(new { email, password }));

    private Task<HttpResponseMessage> RefreshAsync(string? token) =>
        SendWithCookieAsync("/api/v1/auth/refresh", token, ApiFixture.ClientOrigin);

    private async Task<HttpResponseMessage> PostJsonAsync(string path, string json) =>
        await Client.PostAsync(new Uri(path, UriKind.Relative), new StringContent(json, Encoding.UTF8, "application/json"), Cancel);

    private async Task<HttpResponseMessage> SendWithCookieAsync(string path, string? token, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Add("Cookie", $"refreshToken={token}");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await Client.SendAsync(request, Cancel);
    }

    private async Task<HttpResponseMessage> GetMeAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/me", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request, Cancel);
    }

    private static string RefreshCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Single(v => v.StartsWith("refreshToken=", StringComparison.Ordinal))
            : throw new Xunit.Sdk.XunitException("No Set-Cookie header");

    private static string RefreshToken(HttpResponseMessage response)
    {
        var header = RefreshCookieHeader(response);
        return header["refreshToken=".Length..header.IndexOf(';', StringComparison.Ordinal)];
    }

    private static JsonElement DecodePayload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(part)).RootElement.Clone();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
}
