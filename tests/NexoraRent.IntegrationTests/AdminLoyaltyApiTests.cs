using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// <c>/admin/loyalty</c>. Each test makes its own account (for a user it creates) or reward, so
/// tests do not see each other's points.
/// </summary>
public class AdminLoyaltyApiTests(ApiFixture api)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private HttpClient Client => api.CreateClient();

    private static string Owner => TestData.AccessToken(TestData.OwnerId);

    private static string Staff => TestData.AccessToken(TestData.StaffId);

    [Fact]
    public async Task Staff_can_read_but_only_managers_and_owners_can_change_points_and_rewards()
    {
        var account = await CreateAccountAsync(balance: 10, lifetime: 10);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/v1/admin/loyalty/accounts", TestData.AccessToken(TestData.CustomerId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/v1/admin/loyalty/accounts/{account}", Staff)).StatusCode);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{account}/adjust"),
            (HttpMethod.Post, "/api/v1/admin/loyalty/rewards"),
            (HttpMethod.Put, $"/api/v1/admin/loyalty/rewards/{Guid.NewGuid()}"),
            (HttpMethod.Delete, $"/api/v1/admin/loyalty/rewards/{Guid.NewGuid()}"),
        })
        {
            var response = await SendAsync(method, path, Staff, """{"points":5}""");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Manager access required", await ErrorAsync(response));
        }

        Assert.Equal(10, (await PointsAsync(account)).Balance);
    }

    [Fact]
    public async Task An_account_shows_its_member_and_latest_transactions()
    {
        var account = await CreateAccountAsync(balance: 50, lifetime: 50);
        await SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{account}/adjust", Owner, """{"points":7,"description":"First"}""");

        var body = await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/loyalty/accounts/{account}", Staff));

        Assert.Equal("Loyal", body.GetProperty("account").GetProperty("first_name").GetString());
        var transaction = Assert.Single(body.GetProperty("transactions").EnumerateArray());
        Assert.Equal("adjust", transaction.GetProperty("type").GetString());
        Assert.Equal(7, transaction.GetProperty("points").GetInt32());
        Assert.Equal(JsonValueKind.Null, transaction.GetProperty("reservation_no").ValueKind);

        var listed = (await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/loyalty/accounts", Staff)))
            .GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("id").GetString() == account);
        Assert.Equal(57, listed.GetProperty("lifetime_points").GetInt32());

        var missing = await SendAsync(HttpMethod.Get, $"/api/v1/admin/loyalty/accounts/{Guid.NewGuid()}", Staff);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Account not found", await ErrorAsync(missing));
    }

    [Fact]
    public async Task Adding_points_raises_the_tier_and_taking_them_away_stops_at_zero()
    {
        var account = await CreateAccountAsync(balance: 150, lifetime: 150);

        var raised = (await ReadAsync(await SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{account}/adjust", Owner, """{"points":400}""")))
            .GetProperty("account");
        Assert.Equal(550, raised.GetProperty("points_balance").GetInt32());
        Assert.Equal(550, raised.GetProperty("lifetime_points").GetInt32());
        Assert.Equal("gold", raised.GetProperty("tier").GetString());

        var lowered = (await ReadAsync(await SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{account}/adjust", Owner, """{"points":-1000}""")))
            .GetProperty("account");
        Assert.Equal(0, lowered.GetProperty("points_balance").GetInt32());
        Assert.Equal(550, lowered.GetProperty("lifetime_points").GetInt32());
        Assert.Equal("gold", lowered.GetProperty("tier").GetString());

        await using var db = await api.OpenDatabaseAsync();
        var logged = await db.QueryAsync<int>("SELECT points FROM loyalty_transactions WHERE account_id = @account ORDER BY points", new { account });
        Assert.Equal([-1000, 400], logged);
    }

    [Fact]
    public async Task Adjustments_at_the_same_time_are_all_counted()
    {
        var account = await CreateAccountAsync(balance: 0, lifetime: 0);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{account}/adjust", Owner, """{"points":10}""")));

        var points = await PointsAsync(account);
        Assert.Equal(100, points.Balance);
        Assert.Equal(100, points.Lifetime);
    }

    [Theory]
    [InlineData("{}", HttpStatusCode.BadRequest, "points required")]
    [InlineData("""{"points":1.5}""", HttpStatusCode.BadRequest, "points must be a whole number")]
    public async Task Bad_adjustments_are_a_400(string body, HttpStatusCode status, string message)
    {
        var account = await CreateAccountAsync(balance: 5, lifetime: 5);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{account}/adjust", Owner, body);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(message, await ErrorAsync(response));
        Assert.Equal(5, (await PointsAsync(account)).Balance);
    }

    [Fact]
    public async Task Adjusting_a_missing_account_is_404()
    {
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{Guid.NewGuid()}/adjust", Owner, """{"points":5}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Account not found", await ErrorAsync(response));
    }

    [Fact]
    public async Task A_reward_is_created_changed_and_deleted()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/v1/admin/loyalty/rewards", Owner,
            """{"name":"Test reward","type":"discount_fixed","value":15,"points_cost":250}""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reward = (await ReadAsync(created)).GetProperty("reward");
        var id = reward.GetProperty("id").GetString()!;
        Assert.Equal("bronze", reward.GetProperty("min_tier").GetString());
        Assert.True(reward.GetProperty("is_active").GetBoolean());

        var changed = (await ReadAsync(await SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/rewards/{id}", Owner, """{"is_active":false,"value":"17.5"}""")))
            .GetProperty("reward");
        Assert.False(changed.GetProperty("is_active").GetBoolean());
        Assert.Equal(17.5m, changed.GetProperty("value").GetDecimal());

        Assert.Contains(id, (await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/loyalty/rewards", Staff)))
            .GetProperty("rewards").EnumerateArray().Select(r => r.GetProperty("id").GetString()));

        Assert.Equal("Reward deleted", (await ReadAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/admin/loyalty/rewards/{id}", Owner))).GetProperty("message").GetString());

        var gone = await SendAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/rewards/{id}", Owner, """{"points_cost":1}""");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("Reward not found", await ErrorAsync(gone));
    }

    [Fact]
    public async Task The_config_lists_the_tiers()
    {
        var config = (await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/loyalty/config", Staff))).GetProperty("config");

        Assert.Equal(1, config.GetProperty("points_per_euro").GetInt32());
        Assert.Equal(1000, config.GetProperty("tier_thresholds").GetProperty("platinum").GetInt32());
    }

    private async Task<string> CreateAccountAsync(int balance, int lifetime)
    {
        var userId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();
        await using var db = await api.OpenDatabaseAsync();
        await db.ExecuteAsync(
            "INSERT INTO users (id, email, password_hash, first_name, last_name, role) VALUES (@userId, @email, 'not-a-hash', 'Loyal', 'Member', 'customer')",
            new { userId, email = $"loyal-{userId}@example.test" });
        await db.ExecuteAsync(
            "INSERT INTO loyalty_accounts (id, user_id, points_balance, lifetime_points) VALUES (@accountId, @userId, @balance, @lifetime)",
            new { accountId, userId, balance, lifetime });
        return accountId;
    }

    private async Task<PointsRow> PointsAsync(string account)
    {
        await using var db = await api.OpenDatabaseAsync();
        return await db.QuerySingleAsync<PointsRow>(
            "SELECT points_balance AS Balance, lifetime_points AS Lifetime FROM loyalty_accounts WHERE id = @account", new { account });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, string? json = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await Client.SendAsync(request, Cancel);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).GetProperty("error").GetString();

    private sealed record PointsRow
    {
        public int? Balance { get; init; }
        public int? Lifetime { get; init; }
    }
}
