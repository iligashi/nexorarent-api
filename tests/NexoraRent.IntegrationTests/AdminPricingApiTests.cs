using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// <c>/admin/pricing</c>. Rules made here are limited to the BMW and named <c>Test pricing …</c>,
/// so the global price preview of other cars does not change.
/// </summary>
public class AdminPricingApiTests(ApiFixture api)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private HttpClient Client => api.CreateClient();

    private static string Owner => TestData.AccessToken(TestData.OwnerId);

    private static string Staff => TestData.AccessToken(TestData.StaffId);

    [Fact]
    public async Task Staff_can_read_but_only_managers_and_owners_can_change_rules()
    {
        var customer = await SendAsync(HttpMethod.Get, "/api/v1/admin/pricing/rules", TestData.AccessToken(TestData.CustomerId));
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/pricing/rules", Staff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/pricing/demand", Staff)).StatusCode);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/api/v1/admin/pricing/rules"),
            (HttpMethod.Put, $"/api/v1/admin/pricing/rules/{Guid.NewGuid()}"),
            (HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{Guid.NewGuid()}"),
        })
        {
            var response = await SendAsync(method, path, Staff, RuleBody("Staff"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Manager access required", await ErrorAsync(response));
        }

        Assert.Equal(0, await CountRulesAsync("Test pricing Staff"));
    }

    [Fact]
    public async Task A_rule_is_created_listed_with_its_car_changed_and_deleted()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/v1/admin/pricing/rules", Owner, RuleBody("Lifecycle"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = (await ReadAsync(created)).GetProperty("rule");
        var id = rule.GetProperty("id").GetString()!;
        Assert.Equal(0.8m, rule.GetProperty("multiplier").GetDecimal());
        Assert.Equal(5, rule.GetProperty("conditions").GetProperty("min_days").GetInt32());
        Assert.True(rule.GetProperty("is_active").GetBoolean());

        var listed = (await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/pricing/rules", Staff)))
            .GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        Assert.Equal("BMW", listed.GetProperty("brand").GetString());

        var changed = await SendAsync(HttpMethod.Put, $"/api/v1/admin/pricing/rules/{id}", Owner, """{"multiplier":0.75,"end_date":null}""");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = (await ReadAsync(changed)).GetProperty("rule");
        Assert.Equal(0.75m, after.GetProperty("multiplier").GetDecimal());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("end_date").ValueKind);
        Assert.Equal($"Test pricing Lifecycle", after.GetProperty("name").GetString());

        var deleted = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{id}", Owner);
        Assert.Equal("Rule deleted", (await ReadAsync(deleted)).GetProperty("message").GetString());
        Assert.Equal(0, await CountRulesAsync("Test pricing Lifecycle"));

        // Like Express, deleting a missing rule still answers 200.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{id}", Owner)).StatusCode);
    }

    [Theory]
    [InlineData("{}", "name, type, multiplier, and conditions are required")]
    [InlineData("""{"name":"x","type":"surge","multiplier":1,"conditions":{}}""", "type must be one of")]
    [InlineData("""{"name":"x","type":"demand","multiplier":1,"conditions":{},"car_id":"no-such-car"}""", "Car not found")]
    public async Task Bad_rules_are_a_400(string body, string message)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/pricing/rules", Owner, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith(message, await ErrorAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_a_missing_rule_is_404_and_an_empty_change_is_400()
    {
        var missing = await SendAsync(HttpMethod.Put, $"/api/v1/admin/pricing/rules/{Guid.NewGuid()}", Owner, """{"priority":2}""");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Pricing rule not found", await ErrorAsync(missing));

        var empty = await SendAsync(HttpMethod.Put, $"/api/v1/admin/pricing/rules/{Guid.NewGuid()}", Owner, "{}");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("No fields to update", await ErrorAsync(empty));
    }

    [Fact]
    public async Task Demand_counts_available_cars_and_bookings_that_day()
    {
        // DRC-1 (confirmed, Golf) covers 2026-12-12; three seeded cars are available.
        var body = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/pricing/demand?date=2026-12-12", Staff));

        Assert.Equal("2026-12-12", body.GetProperty("date").GetString());
        Assert.Equal(3, body.GetProperty("total_cars").GetInt32());
        Assert.Equal(1, body.GetProperty("booked_cars").GetInt32());
        Assert.Equal(33, body.GetProperty("demand_percent").GetInt32());
    }

    [Fact]
    public async Task The_preview_applies_the_season_and_the_best_active_rule()
    {
        var active = await CreateRuleAsync(RuleBody("Preview", priority: 1000));
        var inactive = await CreateRuleAsync(RuleBody("Preview off", multiplier: 0.5m, priority: 2000, isActive: false));
        try
        {
            var body = await ReadAsync(await SendAsync(
                HttpMethod.Get, $"/api/v1/admin/pricing/calculate?car_id={TestData.BmwId}&pickup_date=2031-07-02&dropoff_date=2031-07-09", Staff));

            Assert.Equal(95.5, body.GetProperty("daily_rate").GetDouble());
            Assert.Equal(1.2, body.GetProperty("seasonal_multiplier").GetDouble());
            Assert.Equal(0.8, body.GetProperty("dynamic_multiplier").GetDouble());
            Assert.Equal(7, body.GetProperty("total_days").GetInt32());
            Assert.Equal(91.68, body.GetProperty("final_daily_rate").GetDouble(), 2);
            Assert.Equal(641.76, body.GetProperty("total_price").GetDouble(), 2);
            var applied = Assert.Single(body.GetProperty("applied_rules").EnumerateArray());
            Assert.Equal("Test pricing Preview", applied.GetProperty("name").GetString());
        }
        finally
        {
            await SendAsync(HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{active}", Owner);
            await SendAsync(HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{inactive}", Owner);
        }
    }

    [Theory]
    [InlineData("?pickup_date=2031-07-02&dropoff_date=2031-07-09", HttpStatusCode.BadRequest, "car_id, pickup_date, dropoff_date required")]
    [InlineData($"?car_id={TestData.BmwId}&pickup_date=soon&dropoff_date=2031-07-09", HttpStatusCode.BadRequest, "pickup_date and dropoff_date must be dates")]
    [InlineData("?car_id=no-such-car&pickup_date=2031-07-02&dropoff_date=2031-07-09", HttpStatusCode.NotFound, "Car not found")]
    public async Task The_preview_checks_its_query(string query, HttpStatusCode status, string message)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/admin/pricing/calculate{query}", Staff);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(message, await ErrorAsync(response));
    }

    [Fact]
    public async Task Rule_changes_are_audited()
    {
        var id = await CreateRuleAsync(RuleBody("Audit"));
        try
        {
            var response = await SendAsync(HttpMethod.Put, $"/api/v1/admin/pricing/rules/{id}", Owner, """{"priority":4}""");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await using var db = await api.OpenDatabaseAsync();
            var action = await db.QuerySingleAsync<string>(
                "SELECT action FROM admin_audit_log WHERE target_id = @id AND method = 'PUT'", new { id });
            Assert.Equal("put /api/v1/admin/pricing/rules/:id", action);
        }
        finally
        {
            await SendAsync(HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{id}", Owner);
        }
    }

    private static string RuleBody(string name, decimal multiplier = 0.8m, int priority = 0, bool isActive = true) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = $"Test pricing {name}",
            ["type"] = "duration",
            ["car_id"] = TestData.BmwId,
            ["multiplier"] = multiplier,
            ["conditions"] = new { min_days = 5 },
            ["priority"] = priority,
            ["is_active"] = isActive,
            ["start_date"] = "2031-01-01",
            ["end_date"] = "2031-12-31",
        });

    private async Task<string> CreateRuleAsync(string body)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/pricing/rules", Owner, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("rule").GetProperty("id").GetString()!;
    }

    private async Task<int> CountRulesAsync(string name)
    {
        await using var db = await api.OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pricing_rules WHERE name = @name", new { name });
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
}
