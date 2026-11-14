using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// Booking, reading and cancelling reservations against a real MySQL database.
/// Each test books its own car and dates, so the tests stay independent on the shared database.
/// </summary>
public class ReservationsApiTests(ApiFixture api)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Guest_booking_prices_days_and_extras()
    {
        var (status, body) = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-03-01T10:00:00", "2031-03-04T10:00:00", guest: true,
            extras: [new { extra_id = TestData.GpsId, quantity = 2 }]));

        Assert.Equal(HttpStatusCode.Created, status);
        var reservation = body.GetProperty("reservation");
        Assert.Equal("pending", reservation.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reservation.GetProperty("user_id").ValueKind);
        Assert.Equal(3, reservation.GetProperty("total_days").GetInt32());
        Assert.Equal(45m, reservation.GetProperty("daily_rate").GetDecimal());
        Assert.Equal(30m, reservation.GetProperty("extras_total").GetDecimal()); // 5.00 x 3 days x 2
        Assert.Equal(165m, reservation.GetProperty("total_price").GetDecimal());
        Assert.Equal("Ann Guest", reservation.GetProperty("guest_name").GetString());
        Assert.StartsWith("2031-03-01T10:00:00", reservation.GetProperty("pickup_date").GetString(), StringComparison.Ordinal);
        Assert.Matches(@"^DRC-\d{8}-\d{6}$", reservation.GetProperty("reservation_no").GetString());

        // The owner can read any booking, including its extras.
        var (_, detail) = await GetAsync($"/api/v1/reservations/{reservation.GetProperty("id").GetString()}", TestData.OwnerId);
        var extra = Assert.Single(detail.GetProperty("reservation").GetProperty("extras").EnumerateArray());
        Assert.Equal("GPS Navigation", extra.GetProperty("name").GetString());
        Assert.Equal(30m, extra.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Seasonal_multiplier_raises_the_daily_rate()
    {
        var (status, body) = await PostAsync("/api/v1/reservations", Booking(TestData.BmwId, "2031-07-10T10:00:00", "2031-07-12T10:00:00", guest: true));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(114.60m, body.GetProperty("reservation").GetProperty("daily_rate").GetDecimal()); // 95.50 x 1.20
        Assert.Equal(229.20m, body.GetProperty("reservation").GetProperty("total_price").GetDecimal());
    }

    [Fact]
    public async Task Signed_in_booking_belongs_to_the_user_and_needs_no_contact_details()
    {
        var (status, body) = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-04-01T10:00:00", "2031-04-02T10:00:00", guest: false), TestData.CustomerId);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(TestData.CustomerId, body.GetProperty("reservation").GetProperty("user_id").GetString());
    }

    [Fact]
    public async Task Invalid_token_books_as_a_guest_like_express()
    {
        var (status, body) = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-04-05T10:00:00", "2031-04-06T10:00:00", guest: false), token: "not-a-token");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Validation failed", body.GetProperty("error").GetString());
        Assert.Equal(["guest_name", "guest_email", "guest_phone"],
            body.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()));
    }

    [Theory]
    [InlineData("aaaaaaaa-0000-4000-8000-000000000999", null, null, HttpStatusCode.NotFound, "Car not found")]
    [InlineData(TestData.FiatId, null, null, HttpStatusCode.Conflict, "Car is not available")]
    [InlineData(TestData.SkodaId, TestData.ClosedLocationId, null, HttpStatusCode.BadRequest, "Pickup and dropoff locations must be active")]
    [InlineData(TestData.SkodaId, null, TestData.RetiredExtraId, HttpStatusCode.BadRequest, "One or more selected extras are unavailable")]
    public async Task Booking_rules_return_the_express_errors(string carId, string? pickupLocation, string? extraId, HttpStatusCode expectedStatus, string expectedError)
    {
        var booking = Booking(carId, "2031-05-01T10:00:00", "2031-05-02T10:00:00", guest: true,
            extras: extraId is null ? null : [new { extra_id = extraId }], pickupLocation: pickupLocation);

        var (status, body) = await PostAsync("/api/v1/reservations", booking);

        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedError, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Overlapping_booking_is_rejected()
    {
        var first = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-06-01T10:00:00", "2031-06-05T10:00:00", guest: true));
        var overlapping = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-06-04T10:00:00", "2031-06-06T10:00:00", guest: true));
        var adjacent = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-06-05T10:00:00", "2031-06-06T10:00:00", guest: true));

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.Conflict, overlapping.Status);
        Assert.Equal("Car is not available for selected dates", overlapping.Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Created, adjacent.Status);
    }

    [Fact]
    public async Task Concurrent_bookings_for_the_same_dates_create_exactly_one()
    {
        var attempts = Enumerable.Range(0, 8).Select(_ =>
            PostAsync("/api/v1/reservations", Booking(TestData.BmwId, "2031-09-01T10:00:00", "2031-09-03T10:00:00", guest: true)));

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r.Status == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
    }

    [Theory]
    [InlineData("[]", "JSON request body must be an object")]
    [InlineData("{\"car_id\":", "Request body is not valid JSON")]
    public async Task Rejects_bodies_that_are_not_json_objects(string json, string expectedError)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await api.CreateClient().PostAsync(new Uri("/api/v1/reservations", UriKind.Relative), content, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Mine_lists_only_the_users_bookings_with_car_details()
    {
        var (status, body) = await GetAsync("/api/v1/reservations/mine", TestData.OtherCustomerId);

        Assert.Equal(HttpStatusCode.OK, status);
        var reservation = Assert.Single(body.GetProperty("reservations").EnumerateArray());
        Assert.Equal(TestData.OtherCustomersBookingId, reservation.GetProperty("id").GetString());
        Assert.Equal("Skoda", reservation.GetProperty("brand").GetString());
        Assert.Equal(JsonValueKind.Null, reservation.GetProperty("car_image").ValueKind);
        Assert.Equal(JsonValueKind.Null, reservation.GetProperty("pickup_location_name").ValueKind);
    }

    [Fact]
    public async Task Mine_requires_a_bearer_token_with_express_casing()
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"bearer {TestData.AccessToken(TestData.CustomerId)}");

        using var response = await client.GetAsync(new Uri("/api/v1/reservations/mine", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Access token required", (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Customers_cannot_read_other_customers_bookings()
    {
        var (status, body) = await GetAsync($"/api/v1/reservations/{TestData.OtherCustomersBookingId}", TestData.CustomerId);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Reservation not found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Cancel_frees_the_dates_and_returns_the_booking_again_when_repeated()
    {
        var (_, created) = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-08-01T10:00:00", "2031-08-03T10:00:00", guest: false), TestData.CustomerId);
        var id = created.GetProperty("reservation").GetProperty("id").GetString();

        var first = await PutAsync($"/api/v1/reservations/{id}/cancel", TestData.CustomerId);
        var again = await PutAsync($"/api/v1/reservations/{id}/cancel", TestData.CustomerId);
        var rebooked = await PostAsync("/api/v1/reservations", Booking(TestData.SkodaId, "2031-08-01T10:00:00", "2031-08-03T10:00:00", guest: true));

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal("cancelled", first.Body.GetProperty("reservation").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(HttpStatusCode.Created, rebooked.Status);
    }

    [Theory]
    [InlineData(TestData.OtherCustomersBookingId, TestData.CustomerId)] // another customer's booking
    [InlineData(TestData.CompletedUnreviewedId, TestData.CustomerId)] // already completed
    public async Task Cancel_refuses_other_users_and_finished_bookings(string reservationId, string userId)
    {
        var (status, body) = await PutAsync($"/api/v1/reservations/{reservationId}/cancel", userId);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Reservation not found or cannot be cancelled", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Review_is_accepted_once_for_a_completed_booking()
    {
        var review = new { reservation_id = TestData.CompletedUnreviewedId, rating = 5, comment = "Great car" };

        var first = await PostAsync("/api/v1/reviews", review, TestData.CustomerId);
        var second = await PostAsync("/api/v1/reviews", review, TestData.CustomerId);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        var created = first.Body.GetProperty("review");
        Assert.Equal(5, created.GetProperty("rating").GetInt32());
        Assert.False(created.GetProperty("is_approved").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, second.Status);
        Assert.Equal("Already reviewed", second.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Review_needs_a_completed_booking_of_the_user()
    {
        var (status, body) = await PostAsync("/api/v1/reviews", new { reservation_id = TestData.OtherCustomersBookingId, rating = 4 }, TestData.CustomerId);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Can only review completed reservations", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Review_requires_sign_in_before_validation()
    {
        var (status, body) = await PostAsync("/api/v1/reviews", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Access token required", body.GetProperty("error").GetString());
    }

    private static Dictionary<string, object> Booking(string carId, string pickup, string dropoff, bool guest, object[]? extras = null, string? pickupLocation = null)
    {
        var body = new Dictionary<string, object>
        {
            ["car_id"] = carId,
            ["pickup_location"] = pickupLocation ?? TestData.DrenasId,
            ["dropoff_location"] = TestData.AirportId,
            ["pickup_date"] = pickup,
            ["dropoff_date"] = dropoff,
        };

        if (guest)
        {
            body["guest_name"] = " Ann Guest ";
            body["guest_email"] = "ann@example.test";
            body["guest_phone"] = "+38344111222";
        }

        if (extras is not null)
        {
            body["extras"] = extras;
        }

        return body;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object body, string? userId = null, string? token = null) =>
        SendAsync(HttpMethod.Post, path, userId, token, JsonContent.Create(body));

    private Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string path, string userId) =>
        SendAsync(HttpMethod.Put, path, userId, null, null);

    private Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path, string userId) =>
        SendAsync(HttpMethod.Get, path, userId, null, null);

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string path, string? userId, string? token, HttpContent? content)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        token ??= userId is null ? null : TestData.AccessToken(userId);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await api.CreateClient().SendAsync(request, Cancel);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        return (response.StatusCode, body);
    }
}
