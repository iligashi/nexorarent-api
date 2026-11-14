using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexoraRent.IntegrationTests;

/// <summary>Tokens signed the way Express signs them must work here.</summary>
public class AuthApiTests(ApiFixture api)
{
    private HttpClient Client => api.CreateClient();

    [Fact]
    public async Task Me_returns_the_user_for_an_express_style_token()
    {
        var response = await GetMeAsync(TestData.AccessToken(TestData.CustomerId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("user");
        Assert.Equal(TestData.CustomerId, user.GetProperty("id").GetString());
        Assert.Equal("customer@example.test", user.GetProperty("email").GetString());
        Assert.Equal("customer", user.GetProperty("role").GetString());
        Assert.False(user.TryGetProperty("password_hash", out _));
    }

    [Fact]
    public async Task Me_without_token_is_rejected()
    {
        await AssertUnauthorizedAsync(await GetMeAsync(null), "Access token required");
    }

    [Fact]
    public async Task Me_with_expired_token_is_rejected()
    {
        await AssertUnauthorizedAsync(await GetMeAsync(TestData.AccessToken(TestData.CustomerId, TimeSpan.FromMinutes(-1))), "Token expired");
    }

    [Fact]
    public async Task Me_with_wrong_signature_is_rejected()
    {
        var token = TestData.AccessToken(TestData.CustomerId, secret: "a-different-secret-that-is-also-long-enough-000");
        await AssertUnauthorizedAsync(await GetMeAsync(token), "Invalid token");
    }

    [Fact]
    public async Task Me_for_deleted_user_is_rejected()
    {
        await AssertUnauthorizedAsync(await GetMeAsync(TestData.AccessToken("99999999-9999-4999-8999-999999999999")), "User not found");
    }

    private async Task<HttpResponseMessage> GetMeAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/me", UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response, string expectedError)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(expectedError, body.GetProperty("error").GetString());
    }
}
