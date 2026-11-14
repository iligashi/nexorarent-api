using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// Checks the cars endpoints against a real MySQL database and the JSON shape
/// the Next.js client reads (snake_case keys, numbers, booleans).
/// </summary>
public class CarsApiTests(ApiFixture api)
{
    private HttpClient Client => api.CreateClient();

    [Fact]
    public async Task List_returns_only_available_cars_with_paging_info()
    {
        var body = await GetJsonAsync("/api/v1/cars");

        var slugs = body.GetProperty("cars").EnumerateArray().Select(c => c.GetProperty("slug").GetString()).ToList();
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.DoesNotContain("fiat-panda", slugs);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(12, body.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task List_uses_the_client_json_contract()
    {
        var body = await GetJsonAsync("/api/v1/cars?search=golf");
        var golf = Assert.Single(body.GetProperty("cars").EnumerateArray());

        Assert.Equal(35m, golf.GetProperty("price_per_day").GetDecimal());
        Assert.True(golf.GetProperty("is_available").GetBoolean());
        Assert.Equal("https://img.example.test/golf-front.jpg", golf.GetProperty("image").GetString());
        Assert.Equal(["Bluetooth", "Air conditioning"], golf.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(4, golf.GetProperty("avg_rating").GetDouble());
        Assert.Equal(1, golf.GetProperty("review_count").GetInt32());
        Assert.EndsWith("Z", golf.GetProperty("created_at").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_filters_and_sorts()
    {
        var body = await GetJsonAsync("/api/v1/cars?fuel=diesel&sort=price_desc");

        var slugs = body.GetProperty("cars").EnumerateArray().Select(c => c.GetProperty("slug").GetString());
        Assert.Equal(["skoda-octavia", "volkswagen-golf-8"], slugs);
    }

    [Fact]
    public async Task List_treats_like_wildcards_in_search_as_text()
    {
        var body = await GetJsonAsync("/api/v1/cars?search=%25");

        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Featured_returns_featured_cars_in_admin_order()
    {
        var body = await GetJsonAsync("/api/v1/cars/featured");

        var cars = body.GetProperty("cars").EnumerateArray().ToList();
        Assert.Equal(["volkswagen-golf-8", "bmw-x5"], cars.Select(c => c.GetProperty("slug").GetString()));
        Assert.False(cars[0].TryGetProperty("avg_rating", out _));
    }

    [Fact]
    public async Task Detail_includes_images_and_only_approved_reviews()
    {
        var body = await GetJsonAsync("/api/v1/cars/volkswagen-golf-8");
        var car = body.GetProperty("car");

        var images = car.GetProperty("images").EnumerateArray().ToList();
        Assert.Equal(2, images.Count);
        Assert.True(images[0].GetProperty("is_primary").GetBoolean());

        var review = Assert.Single(car.GetProperty("reviews").EnumerateArray());
        Assert.Equal("Arta", review.GetProperty("first_name").GetString());
        Assert.Equal(4, review.GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task Detail_returns_404_for_unknown_slug()
    {
        var response = await Client.GetAsync(new Uri("/api/v1/cars/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Car not found", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("2026-12-12T10:00:00", "2026-12-13T10:00:00", false)] // inside a confirmed booking
    [InlineData("2026-12-15T10:00:00", "2026-12-17T10:00:00", true)] // starts when it ends
    [InlineData("2026-12-20T10:00:00", "2026-12-22T10:00:00", true)] // only a cancelled booking there
    public async Task Availability_checks_overlapping_reservations(string pickup, string dropoff, bool expected)
    {
        var body = await GetJsonAsync($"/api/v1/cars/{TestData.GolfId}/availability?pickup_date={pickup}&dropoff_date={dropoff}");

        Assert.Equal(expected, body.GetProperty("is_available").GetBoolean());
        var blocked = Assert.Single(body.GetProperty("blocked_dates").EnumerateArray());
        Assert.StartsWith("2026-12-10T10:00:00", blocked.GetProperty("pickup_date").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "pickup_date and dropoff_date required")]
    [InlineData("?pickup_date=2026-12-05T10:00:00&dropoff_date=2026-12-01T10:00:00", "Pickup and dropoff dates must be valid and dropoff must be after pickup")]
    public async Task Availability_rejects_bad_dates(string queryString, string expectedError)
    {
        var response = await Client.GetAsync(new Uri($"/api/v1/cars/{TestData.GolfId}/availability{queryString}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Availability_returns_404_for_unknown_car()
    {
        var response = await Client.GetAsync(
            new Uri("/api/v1/cars/00000000-0000-4000-8000-000000000000/availability?pickup_date=2026-12-01T10:00:00&dropoff_date=2026-12-02T10:00:00", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_routes_return_json_404_with_request_id()
    {
        var response = await Client.GetAsync(new Uri("/api/v1/nope", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Request-Id"));
        Assert.Equal("Route not found", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Health_reports_database_ok()
    {
        var body = await GetJsonAsync("/api/health");

        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("ok", body.GetProperty("database").GetString());
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await Client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
