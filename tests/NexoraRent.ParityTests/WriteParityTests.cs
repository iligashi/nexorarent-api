using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Sends the same write requests to Express and .NET and compares the responses
/// and the rows they leave behind. Between the two calls the database is put back
/// the way it was, so both APIs see the same state.
/// <para>
/// Express allows 20 requests to /reservations per IP every 15 minutes, and this
/// class sends 18. Restart Express before running it again within that window.
/// </para>
/// </summary>
[Collection(ParityEnvironment.SharedDatabase)]
public class WriteParityTests
{
    private const string CustomerId = "dddddddd-0000-4000-8000-000000000001";
    private const string OtherCustomerId = "dddddddd-0000-4000-8000-000000000002";
    private const string RetiredCarId = "dddddddd-0000-4000-8000-0000000000a1";
    private const string ClosedLocationId = "dddddddd-0000-4000-8000-0000000000b1";
    private const string RetiredExtraId = "dddddddd-0000-4000-8000-0000000000c1";
    private const string ConfirmedId = "dddddddd-0000-4000-8000-0000000000e1";
    private const string PendingId = "dddddddd-0000-4000-8000-0000000000e2";
    private const string CompletedId = "dddddddd-0000-4000-8000-0000000000e3";
    private const string OtherCustomersId = "dddddddd-0000-4000-8000-0000000000e4";

    /// <summary>Values each server generates itself.</summary>
    private static readonly HashSet<string> Generated = new(StringComparer.Ordinal) { "id", "reservation_no", "created_at", "updated_at" };

    private static readonly HashSet<string> UpdatedAt = new(StringComparer.Ordinal) { "updated_at" };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bookings_are_created_alike()
    {
        var data = await LookupAsync();
        var extras = new object[] { new { extra_id = data.ExtraIds[0], quantity = 2 }, new { extra_id = data.ExtraIds[1] } };

        // A guest booking with extras, and a signed-in booking in a seasonal price period.
        await AssertCreateAlikeAsync(Booking(data.FirstCarId, data, "2032-03-01T10:00:00", "2032-03-04T09:30:00", guest: true, extras), token: null);
        await AssertCreateAlikeAsync(Booking(data.SecondCarId, data, "2032-07-10T10:00:00", "2032-07-13T10:00:00", guest: false), Token(CustomerId));
    }

    [Fact]
    public async Task Booking_errors_match()
    {
        var data = await LookupAsync();
        var valid = Booking(data.FirstCarId, data, "2032-04-01T10:00:00", "2032-04-02T10:00:00", guest: true);

        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", "{}");
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", "[]");
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", Json(new Dictionary<string, object?>
        {
            ["car_id"] = 5,
            ["pickup_location"] = "not-a-uuid",
            ["dropoff_location"] = null,
            ["pickup_date"] = "2032-02-30T10:00:00",
            ["dropoff_date"] = "2032-03-01T10:00:00",
            ["guest_name"] = "  ",
            ["guest_email"] = "nope",
            ["guest_phone"] = "123",
            ["extras"] = new object[] { new { extra_id = "x", quantity = 1.5 }, "a" },
        }));
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", Json(With(valid, new()
        {
            ["pickup_date"] = "2032-04-03T10:00:00",
            ["extras"] = new object[] { new { extra_id = data.ExtraIds[0] }, new { extra_id = data.ExtraIds[0], quantity = 0 } },
        })));
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", Json(With(valid, new() { ["car_id"] = "aaaaaaaa-0000-4000-8000-000000000999" })));
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", Json(With(valid, new() { ["car_id"] = RetiredCarId })));
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", Json(With(valid, new() { ["dropoff_location"] = ClosedLocationId })));
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", Json(With(valid, new() { ["extras"] = new object[] { new { extra_id = RetiredExtraId } } })));
        // Overlaps the customer's confirmed booking (2033-01-10 to 2033-01-12) on the first car.
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reservations", Json(With(valid, new()
        {
            ["pickup_date"] = "2033-01-11T10:00:00",
            ["dropoff_date"] = "2033-01-13T10:00:00",
        })));
    }

    [Fact]
    public async Task Reading_reservations_matches()
    {
        await AssertSameAsync(HttpMethod.Get, "/api/v1/reservations/mine", null, Token(CustomerId), ignoreArrayOrder: true);
        await AssertSameAsync(HttpMethod.Get, $"/api/v1/reservations/{ConfirmedId}", null, Token(CustomerId), ignoreArrayOrder: true);
        await AssertSameAsync(HttpMethod.Get, $"/api/v1/reservations/{OtherCustomersId}", null, Token(CustomerId));
        await AssertSameAsync(HttpMethod.Get, "/api/v1/reservations/mine", null);
    }

    [Fact]
    public async Task Cancelling_matches()
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var reset = () => db.ExecuteAsync("UPDATE reservations SET status = 'pending' WHERE id = @PendingId", new { PendingId });

        await AssertSameAsync(HttpMethod.Put, $"/api/v1/reservations/{PendingId}/cancel", null, Token(CustomerId), afterExpress: reset, ignored: UpdatedAt);
        await reset();
        await AssertSameAsync(HttpMethod.Put, $"/api/v1/reservations/{OtherCustomersId}/cancel", null, Token(CustomerId));
    }

    [Fact]
    public async Task Both_apis_share_the_booking_lock()
    {
        var data = await LookupAsync();
        var body = Json(Booking(data.FirstCarId, data, "2032-09-01T10:00:00", "2032-09-03T10:00:00", guest: true));

        var results = await Task.WhenAll(
            SendAsync(ParityEnvironment.ExpressUrl!, HttpMethod.Post, "/api/v1/reservations", body, null),
            SendAsync(ParityEnvironment.DotnetUrl!, HttpMethod.Post, "/api/v1/reservations", body, null));

        foreach (var created in results.Where(r => r.Status == 201))
        {
            await DeleteReservationAsync(created.Body.GetProperty("reservation").GetProperty("id").GetString()!);
        }

        Assert.Equal([201, 409], results.Select(r => r.Status).Order());
    }

    [Fact]
    public async Task Reviews_match()
    {
        ParityEnvironment.SkipUnlessWritesConfigured();
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var token = Token(CustomerId);
        var review = Json(new { reservation_id = CompletedId, rating = 4, comment = "Smooth pickup" });
        var deleteReview = () => db.ExecuteAsync("DELETE FROM reviews WHERE reservation_id = @CompletedId", new { CompletedId });

        await deleteReview();
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reviews", review, token, afterExpress: deleteReview, ignored: Generated);
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reviews", review, token); // both: Already reviewed
        await deleteReview();

        await AssertSameAsync(HttpMethod.Post, "/api/v1/reviews", Json(new { reservation_id = OtherCustomersId, rating = 4 }), token);
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reviews", Json(new { reservation_id = "x", rating = 6, comment = 3 }), token);
        await AssertSameAsync(HttpMethod.Post, "/api/v1/reviews", "{}");
    }

    /// <summary>Creates the same booking on both APIs and compares the responses and the stored extras.</summary>
    private static async Task AssertCreateAlikeAsync(Dictionary<string, object> booking, string? token)
    {
        var body = Json(booking);

        var express = await SendAsync(ParityEnvironment.ExpressUrl!, HttpMethod.Post, "/api/v1/reservations", body, token);
        Assert.True(express.Status == 201, $"Express: {express.Status} {express.Body}");
        var expressId = express.Body.GetProperty("reservation").GetProperty("id").GetString()!;
        var expressExtras = await StoredExtrasAsync(expressId);
        await DeleteReservationAsync(expressId);

        var dotnet = await SendAsync(ParityEnvironment.DotnetUrl!, HttpMethod.Post, "/api/v1/reservations", body, token);
        Assert.True(dotnet.Status == 201, $".NET: {dotnet.Status} {dotnet.Body}");
        var dotnetId = dotnet.Body.GetProperty("reservation").GetProperty("id").GetString()!;
        var dotnetExtras = await StoredExtrasAsync(dotnetId);
        await DeleteReservationAsync(dotnetId);

        AssertNoDifferences("POST /api/v1/reservations", JsonParity.Compare(express.Body, dotnet.Body, ignoredKeys: Generated));
        Assert.Equal(expressExtras, dotnetExtras);
        Assert.Matches(@"^DRC-\d{8}-\d{6}$", dotnet.Body.GetProperty("reservation").GetProperty("reservation_no").GetString());
    }

    private static async Task AssertSameAsync(
        HttpMethod method, string path, string? body, string? token = null,
        Func<Task<int>>? afterExpress = null, IReadOnlySet<string>? ignored = null, bool ignoreArrayOrder = false)
    {
        ParityEnvironment.SkipUnlessWritesConfigured();

        var express = await SendAsync(ParityEnvironment.ExpressUrl!, method, path, body, token);
        if (afterExpress is not null)
        {
            await afterExpress();
        }

        var dotnet = await SendAsync(ParityEnvironment.DotnetUrl!, method, path, body, token);

        var label = $"{method} {path} {body}";
        Assert.True(express.Status == dotnet.Status, $"{label}: status {express.Status} from Express, {dotnet.Status} from .NET{Environment.NewLine}{express.Body}{Environment.NewLine}{dotnet.Body}");
        AssertNoDifferences(label, JsonParity.Compare(express.Body, dotnet.Body, ignoreArrayOrder, ignored));
    }

    private static void AssertNoDifferences(string label, IReadOnlyList<string> differences) =>
        Assert.True(differences.Count == 0, $"{label}:{Environment.NewLine}{string.Join(Environment.NewLine, differences.Take(20))}");

    private static async Task<(int Status, JsonElement Body)> SendAsync(Uri baseUrl, HttpMethod method, string path, string? body, string? token)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await Http.SendAsync(request, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        using var document = JsonDocument.Parse(text);
        return ((int)response.StatusCode, document.RootElement.Clone());
    }

    private static async Task<List<string>> StoredExtrasAsync(string reservationId)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var rows = await db.QueryAsync<(string ExtraId, int Quantity, decimal Price)>(
            "SELECT extra_id, quantity, price FROM reservation_extras WHERE reservation_id = @reservationId ORDER BY extra_id", new { reservationId });
        return [.. rows.Select(r => $"{r.ExtraId} x{r.Quantity} = {r.Price}")];
    }

    private static async Task DeleteReservationAsync(string id)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync("DELETE FROM reservation_extras WHERE reservation_id = @id; DELETE FROM reservations WHERE id = @id;", new { id });
    }

    private static async Task<Lookup> LookupAsync()
    {
        ParityEnvironment.SkipUnlessWritesConfigured();
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var cars = (await db.QueryAsync<string>("SELECT id FROM cars WHERE is_available = true ORDER BY slug LIMIT 2")).ToList();
        var locations = (await db.QueryAsync<string>("SELECT id FROM locations WHERE is_active = true ORDER BY name, id LIMIT 2")).ToList();
        var extras = (await db.QueryAsync<string>("SELECT id FROM extras WHERE is_active = true ORDER BY name, id LIMIT 2")).ToList();
        return new Lookup(cars[0], cars[1], locations[0], locations[^1], extras);
    }

    private static Dictionary<string, object> Booking(string carId, Lookup data, string pickup, string dropoff, bool guest, object[]? extras = null)
    {
        var booking = new Dictionary<string, object>
        {
            ["car_id"] = carId,
            ["pickup_location"] = data.PickupLocationId,
            ["dropoff_location"] = data.DropoffLocationId,
            ["pickup_date"] = pickup,
            ["dropoff_date"] = dropoff,
            ["notes"] = "Arriving late",
        };

        if (guest)
        {
            booking["guest_name"] = "  Parity Guest ";
            booking["guest_email"] = " guest@example.test ";
            booking["guest_phone"] = "+383 44 555 666";
        }

        if (extras is not null)
        {
            booking["extras"] = extras;
        }

        return booking;
    }

    private static Dictionary<string, object> With(Dictionary<string, object> booking, Dictionary<string, object> changes) =>
        new(booking.Concat(changes).GroupBy(p => p.Key).Select(g => g.Last()));

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string Token(string userId)
    {
        ParityEnvironment.SkipUnlessWritesConfigured();
        return ParityEnvironment.AccessToken(userId);
    }

    private sealed record Lookup(string FirstCarId, string SecondCarId, string PickupLocationId, string DropoffLocationId, List<string> ExtraIds);
}
