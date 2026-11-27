using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// <c>/admin/notifications</c>. The tests set the templates they need and only count log rows for
/// the recipient numbers they use.
/// </summary>
public class AdminNotificationApiTests(ApiFixture api)
{
    private const string BookingId = "cccccccc-0000-4000-8000-000000000001";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private HttpClient Client => api.CreateClient();

    private static string Owner => TestData.AccessToken(TestData.OwnerId);

    private static string Staff => TestData.AccessToken(TestData.StaffId);

    [Fact]
    public async Task Staff_can_read_but_only_managers_and_owners_can_change_templates_and_send()
    {
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/notifications/templates", Staff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/notifications/log", Staff)).StatusCode);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Put, $"/api/v1/admin/notifications/templates/{Guid.NewGuid()}"),
            (HttpMethod.Post, "/api/v1/admin/notifications/send"),
        })
        {
            var response = await SendAsync(method, path, Staff, """{"type":"pickup_reminder","recipient":"+1","is_active":false}""");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Manager access required", await ErrorAsync(response));
        }
    }

    [Fact]
    public async Task A_message_is_filled_from_the_booking_logged_and_counted()
    {
        await SetTemplateAsync("booking_confirmation", "Hi {{customer_name}}! #{{reservation_no}} {{car_name}} on {{pickup_date}} at {{pickup_location}}: {{total_price}}");
        const string recipient = "+383 44 555-001";
        var before = await StatsAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/notifications/send", Owner,
            $$"""{"type":"booking_confirmation","recipient":"{{recipient}}","reservation_id":"{{BookingId}}"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("Hi Arta Krasniqi! #DRC-1 Volkswagen Golf 8 on 12/10/2026 at : €175.00", body.GetProperty("message").GetString());
        Assert.StartsWith("https://wa.me/38344555001?text=Hi%20Arta%20Krasniqi!%20%23DRC-1", body.GetProperty("whatsapp_url").GetString(), StringComparison.Ordinal);

        await using var db = await api.OpenDatabaseAsync();
        var logged = await db.QuerySingleAsync<LogRow>(
            "SELECT id AS Id, reservation_id AS ReservationId, status AS Status, sent_at IS NOT NULL AS HasSentAt FROM notification_log WHERE recipient = @recipient",
            new { recipient });
        Assert.Equal(body.GetProperty("log_id").GetString(), logged.Id);
        Assert.Equal(BookingId, logged.ReservationId);
        Assert.Equal("sent", logged.Status);
        Assert.True(logged.HasSentAt);

        var after = await StatsAsync();
        Assert.Equal(before.GetProperty("total").GetInt32() + 1, after.GetProperty("total").GetInt32());
        Assert.Equal(before.GetProperty("sent").GetInt32() + 1, after.GetProperty("sent").GetInt32());

        var entry = (await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/notifications/log?status=sent&limit=100", Staff)))
            .GetProperty("notifications").EnumerateArray().Single(n => n.GetProperty("recipient").GetString() == recipient);
        Assert.Equal("DRC-1", entry.GetProperty("reservation_no").GetString());
        Assert.Equal("", entry.GetProperty("customer_name").GetString());
    }

    [Fact]
    public async Task Without_an_active_template_nothing_is_logged()
    {
        await SetTemplateAsync("loyalty_points", "Points!", active: false);
        const string recipient = "+383 44 555-002";

        var body = await ReadAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/notifications/send", Owner,
            $$"""{"type":"loyalty_points","recipient":"{{recipient}}"}"""));

        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("No active template found", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountLogAsync(recipient));
    }

    [Theory]
    [InlineData("""{"type":"pickup_reminder"}""", HttpStatusCode.BadRequest, "type and recipient required")]
    [InlineData("""{"type":"pickup_reminder","recipient":"+383 44 555-003","reservation_id":"no-such-booking"}""", HttpStatusCode.NotFound, "Reservation not found")]
    public async Task Bad_sends_are_rejected_and_not_logged(string body, HttpStatusCode status, string message)
    {
        await SetTemplateAsync("pickup_reminder", "Reminder");

        var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/notifications/send", Owner, body);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(message, await ErrorAsync(response));
        Assert.Equal(0, await CountLogAsync("+383 44 555-003"));
    }

    [Fact]
    public async Task A_template_is_changed_and_a_missing_one_is_404()
    {
        var id = await SetTemplateAsync("return_reminder", "Return soon");

        var changed = (await ReadAsync(await SendAsync(HttpMethod.Put, $"/api/v1/admin/notifications/templates/{id}", Owner,
            """{"template_body":"Return tomorrow","is_active":false}"""))).GetProperty("template");
        Assert.Equal("Return tomorrow", changed.GetProperty("template_body").GetString());
        Assert.False(changed.GetProperty("is_active").GetBoolean());

        var missing = await SendAsync(HttpMethod.Put, $"/api/v1/admin/notifications/templates/{Guid.NewGuid()}", Owner, """{"is_active":true}""");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Template not found", await ErrorAsync(missing));
    }

    [Fact]
    public async Task The_log_pages_and_clamps_its_size()
    {
        var body = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/notifications/log?page=0&limit=5000", Staff));

        Assert.True(body.GetProperty("notifications").GetArrayLength() <= 100);
        Assert.True(body.GetProperty("total").GetInt32() >= 0);
    }

    /// <summary>Creates or replaces the WhatsApp template for a type and returns its id.</summary>
    private async Task<string> SetTemplateAsync(string type, string body, bool active = true)
    {
        await using var db = await api.OpenDatabaseAsync();
        await db.ExecuteAsync(
            """
            INSERT INTO notification_templates (id, type, channel, template_name, template_body, is_active)
            VALUES (UUID(), @type, 'whatsapp', @type, @body, @active)
            ON DUPLICATE KEY UPDATE template_body = VALUES(template_body), is_active = VALUES(is_active)
            """,
            new { type, body, active });
        return await db.QuerySingleAsync<string>(
            "SELECT id FROM notification_templates WHERE type = @type AND channel = 'whatsapp'", new { type });
    }

    private async Task<int> CountLogAsync(string recipient)
    {
        await using var db = await api.OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM notification_log WHERE recipient = @recipient", new { recipient });
    }

    private async Task<JsonElement> StatsAsync() =>
        await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/notifications/stats", Staff));

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

    private sealed record LogRow
    {
        public required string Id { get; init; }
        public string? ReservationId { get; init; }
        public string? Status { get; init; }
        public bool HasSentAt { get; init; }
    }
}
