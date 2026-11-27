using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// <c>/admin/expenses</c>. Expenses made here are on the Skoda and dated in 2030, so lists filtered
/// to that car and year only see this class's rows. Other tests book the Skoda, so the summary
/// test only checks values within one response.
/// </summary>
public class AdminExpenseApiTests(ApiFixture api)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private HttpClient Client => api.CreateClient();

    private static string Owner => TestData.AccessToken(TestData.OwnerId);

    private static string Staff => TestData.AccessToken(TestData.StaffId);

    [Fact]
    public async Task Staff_can_read_but_only_managers_and_owners_can_change_expenses()
    {
        var id = await CreateAsync("repair", 10m, "2030-01-05");

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/expenses", Staff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/expenses/summary", Staff)).StatusCode);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/api/v1/admin/expenses"),
            (HttpMethod.Put, $"/api/v1/admin/expenses/{id}"),
            (HttpMethod.Delete, $"/api/v1/admin/expenses/{id}"),
        })
        {
            var response = await SendAsync(method, path, Staff, """{"amount":99}""");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Manager access required", await ErrorAsync(response));
        }
    }

    [Fact]
    public async Task An_expense_is_recorded_by_its_author_changed_and_deleted()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/v1/admin/expenses", Owner,
            $$"""{"car_id":"{{TestData.SkodaId}}","category":"tire","amount":"320.40","expense_date":"2030-03-01","vendor":"Tires Ltd"}""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var expense = (await ReadAsync(created)).GetProperty("expense");
        var id = expense.GetProperty("id").GetString()!;
        Assert.Equal(320.40m, expense.GetProperty("amount").GetDecimal());
        Assert.Equal(TestData.OwnerId, expense.GetProperty("created_by").GetString());
        Assert.Equal(JsonValueKind.Null, expense.GetProperty("description").ValueKind);

        var changed = (await ReadAsync(await SendAsync(HttpMethod.Put, $"/api/v1/admin/expenses/{id}", Owner, """{"category":"repair","vendor":null}""")))
            .GetProperty("expense");
        Assert.Equal("repair", changed.GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("vendor").ValueKind);

        Assert.Equal("Expense deleted", (await ReadAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/admin/expenses/{id}", Owner))).GetProperty("message").GetString());
        var again = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/expenses/{id}", Owner);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("Expense not found", await ErrorAsync(again));
    }

    [Fact]
    public async Task An_unknown_car_is_a_400_and_a_missing_expense_is_404_before_the_body_is_read()
    {
        var unknownCar = await SendAsync(HttpMethod.Post, "/api/v1/admin/expenses", Owner,
            """{"car_id":"no-such-car","category":"fuel","amount":5,"expense_date":"2030-01-01"}""");
        Assert.Equal(HttpStatusCode.BadRequest, unknownCar.StatusCode);
        Assert.Equal("Car not found", await ErrorAsync(unknownCar));

        var missing = await SendAsync(HttpMethod.Put, $"/api/v1/admin/expenses/{Guid.NewGuid()}", Owner, "{}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Expense not found", await ErrorAsync(missing));
    }

    [Fact]
    public async Task The_list_filters_pages_and_shows_the_car()
    {
        var older = await CreateAsync("fuel", 40m, "2030-05-01");
        var newer = await CreateAsync("fuel", 50m, "2030-05-20");
        await CreateAsync("wash", 8m, "2030-05-10");

        var body = await ReadAsync(await SendAsync(HttpMethod.Get,
            $"/api/v1/admin/expenses?car_id={TestData.SkodaId}&category=fuel&date_from=2030-05-01&date_to=2030-05-31&limit=1&page=2", Staff));

        var expense = Assert.Single(body.GetProperty("expenses").EnumerateArray());
        Assert.Equal(older, expense.GetProperty("id").GetString());
        Assert.Equal("Skoda", expense.GetProperty("brand").GetString());
        var pagination = body.GetProperty("pagination");
        Assert.Equal(2, pagination.GetProperty("total").GetInt32());
        Assert.Equal(2, pagination.GetProperty("pages").GetInt32());

        var forCar = await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/expenses/car/{TestData.SkodaId}?limit=100", Staff));
        Assert.Contains(newer, forCar.GetProperty("expenses").EnumerateArray().Select(e => e.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task The_summary_sets_expenses_against_revenue()
    {
        await CreateAsync("insurance", 1000m, "2030-02-01");

        var body = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/expenses/summary", Staff));

        var skoda = body.GetProperty("profitability").EnumerateArray().Single(c => c.GetProperty("car_id").GetString() == TestData.SkodaId);
        var revenue = skoda.GetProperty("revenue").GetDecimal();
        var expenses = skoda.GetProperty("expenses").GetDecimal();
        Assert.True(expenses >= 1000m);
        Assert.Equal(revenue - expenses, skoda.GetProperty("profit").GetDecimal());
        Assert.Contains("insurance", body.GetProperty("by_category").EnumerateArray().Select(c => c.GetProperty("category").GetString()));
    }

    private async Task<string> CreateAsync(string category, decimal amount, string date)
    {
        var id = Guid.NewGuid().ToString();
        await using var db = await api.OpenDatabaseAsync();
        await db.ExecuteAsync(
            "INSERT INTO car_expenses (id, car_id, category, amount, expense_date) VALUES (@id, @car, @category, @amount, @date)",
            new { id, car = TestData.SkodaId, category, amount, date });
        return id;
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
