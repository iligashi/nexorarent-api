using System.Text.Json;

namespace NexoraRent.ParityTests;

/// <summary>
/// Sends the same requests to the Express API and the .NET API, running on the
/// same database, and fails on any difference in status code or JSON body.
/// <para>
/// Needs PARITY_EXPRESS_URL and PARITY_DOTNET_URL (for example
/// http://localhost:4000 and http://localhost:5080). Without them the tests are
/// skipped. CI starts both APIs on a seeded MySQL and sets the variables.
/// </para>
/// </summary>
public class ParityTests
{
    private static Uri? ExpressUrl => ParityEnvironment.ExpressUrl;
    private static Uri? DotnetUrl => ParityEnvironment.DotnetUrl;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Requests whose list order is fully defined, so items must come back in the same order.
    /// </summary>
    public static TheoryData<string> OrderedRequests() =>
    [
        "/api/v1/cars?sort=price_asc&limit=100",
        "/api/v1/cars?sort=price_desc&category=suv",
        "/api/v1/cars/featured",
        "/api/v1/blog",
        "/api/v1/blog?limit=1&page=2",
        "/api/v1/blog/parity-first-post",
        "/api/v1/blog/does-not-exist",
        "/api/v1/cars/does-not-exist",
        "/api/v1/cars/00000000-0000-4000-8000-000000000000/availability?pickup_date=2030-06-01T10:00:00&dropoff_date=2030-06-05T10:00:00",
        "/api/v1/does-not-exist",
    ];

    /// <summary>
    /// Requests where rows can tie on the sort column (same name, same created_at).
    /// Express has no tie-breaker, so only the set of items is compared.
    /// </summary>
    public static TheoryData<string> UnorderedRequests() =>
    [
        "/api/v1/cars",
        "/api/v1/cars?limit=100",
        "/api/v1/cars?fuel=diesel",
        "/api/v1/cars?transmission=automatic&price_min=30&price_max=80",
        "/api/v1/cars?search=bmw",
        "/api/v1/locations",
        "/api/v1/extras",
    ];

    [Theory]
    [MemberData(nameof(OrderedRequests))]
    public Task Ordered_responses_match(string path) => AssertSameAsync(path, ignoreArrayOrder: false);

    [Theory]
    [MemberData(nameof(UnorderedRequests))]
    public Task Responses_match_ignoring_tie_order(string path) => AssertSameAsync(path, ignoreArrayOrder: true);

    [Fact]
    public async Task Car_detail_matches_for_every_car()
    {
        foreach (var slug in await CarValuesAsync("slug"))
        {
            await AssertSameAsync($"/api/v1/cars/{slug}", ignoreArrayOrder: false);
        }
    }

    [Fact]
    public async Task Availability_matches_for_every_car()
    {
        foreach (var id in await CarValuesAsync("id"))
        {
            await AssertSameAsync($"/api/v1/cars/{id}/availability?pickup_date=2030-06-01T10:00:00&dropoff_date=2030-06-05T10:00:00", ignoreArrayOrder: false);
            await AssertSameAsync($"/api/v1/cars/{id}/availability", ignoreArrayOrder: false);
            await AssertSameAsync($"/api/v1/cars/{id}/availability?pickup_date=2030-06-05T10:00:00&dropoff_date=2030-06-01T10:00:00", ignoreArrayOrder: false);
        }
    }

    private static async Task<IReadOnlyList<string>> CarValuesAsync(string property)
    {
        SkipWhenNotConfigured();
        var (_, body) = await GetAsync(ExpressUrl!, "/api/v1/cars?limit=100");
        var values = body.GetProperty("cars").EnumerateArray().Select(c => c.GetProperty(property).GetString()!).ToList();
        Assert.NotEmpty(values);
        return values;
    }

    private static async Task AssertSameAsync(string path, bool ignoreArrayOrder)
    {
        SkipWhenNotConfigured();

        var express = await GetAsync(ExpressUrl!, path);
        var dotnet = await GetAsync(DotnetUrl!, path);

        Assert.True(express.Status == dotnet.Status, $"{path}: status {express.Status} from Express, {dotnet.Status} from .NET");

        var differences = JsonParity.Compare(express.Body, dotnet.Body, ignoreArrayOrder);
        Assert.True(differences.Count == 0, $"{path}:{Environment.NewLine}{string.Join(Environment.NewLine, differences.Take(20))}");
    }

    private static async Task<(int Status, JsonElement Body)> GetAsync(Uri baseUrl, string path)
    {
        using var response = await Http.GetAsync(new Uri(baseUrl, path), TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        return ((int)response.StatusCode, document.RootElement.Clone());
    }

    private static void SkipWhenNotConfigured() => ParityEnvironment.SkipUnlessApisConfigured();
}
