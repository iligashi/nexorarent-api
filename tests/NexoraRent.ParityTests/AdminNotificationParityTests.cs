using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Compares <c>/admin/notifications</c> on Express and .NET. Template changes are put back, and the
/// log rows a send writes are removed between the two requests.
/// </summary>
[Collection(ParityEnvironment.SharedDatabase)]
public class AdminNotificationParityTests
{
    private const string BookingId = "dddddddd-0000-4000-8000-0000000000e3";
    private const string Recipient = "+383 (44) 000-009";

    /// <summary>Each API writes its own log row.</summary>
    private static readonly HashSet<string> WrittenKeys = new(StringComparer.Ordinal) { "log_id" };

    [Fact]
    public async Task Access_rules_match()
    {
        var customer = await AdminParity.TokenForRoleAsync("customer");
        var staff = await AdminParity.TokenForRoleAsync("staff");

        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/notifications/templates", null, token: null);
        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/notifications/stats", null, customer);
        await AdminParity.AssertSameAsync(HttpMethod.Post, "/api/v1/admin/notifications/send", "{}", staff);
        await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/notifications/templates/{Guid.NewGuid()}", "{}", staff);
    }

    [Fact]
    public async Task Reads_match()
    {
        var staff = await AdminParity.TokenForRoleAsync("staff");

        foreach (var path in new[]
        {
            "/api/v1/admin/notifications/templates",
            "/api/v1/admin/notifications/stats",
            "/api/v1/admin/notifications/log",
            "/api/v1/admin/notifications/log?status=failed",
            "/api/v1/admin/notifications/log?status=sent&limit=1",
            "/api/v1/admin/notifications/log?page=2&limit=1",
            "/api/v1/admin/notifications/log?status=no-such-status",
            "/api/v1/admin/notifications/log?status=",
        })
        {
            await AdminParity.AssertSameAsync(HttpMethod.Get, path, null, staff);
        }
    }

    [Fact]
    public async Task Templates_are_changed_alike()
    {
        var owner = await AdminParity.TokenForRoleAsync("owner");
        var template = await TemplateAsync("booking_confirmation");
        try
        {
            foreach (var body in new[]
            {
                """{"template_body":"Parity {{customer_name}} / {{car_name}}"}""",
                """{"is_active":false}""",
                """{"template_body":"Parity again","is_active":true}""",
                "{}",
            })
            {
                await AdminParity.AssertSameAsync(
                    HttpMethod.Put, $"/api/v1/admin/notifications/templates/{template.Id}", body, owner,
                    afterExpress: () => RestoreTemplateAsync(template));
                await RestoreTemplateAsync(template);
            }
        }
        finally
        {
            await RestoreTemplateAsync(template);
        }
    }

    [Fact]
    public async Task Messages_are_prepared_alike()
    {
        var owner = await AdminParity.TokenForRoleAsync("owner");
        var reminder = await TemplateAsync("pickup_reminder");
        try
        {
            foreach (var body in new[]
            {
                $$"""{"type":"booking_confirmation","recipient":"{{Recipient}}","reservation_id":"{{BookingId}}"}""",
                $$"""{"type":"pickup_reminder","recipient":"{{Recipient}}"}""",
                $$"""{"type":"review_request","recipient":"{{Recipient}}","reservation_id":""}""",
                $$"""{"type":"loyalty_points","recipient":"{{Recipient}}"}""",
                """{"type":"booking_confirmation"}""",
                "{}",
            })
            {
                await AdminParity.AssertSameAsync(
                    HttpMethod.Post, "/api/v1/admin/notifications/send", body, owner,
                    afterExpress: DeleteSentRowsAsync, ignored: WrittenKeys);
                await DeleteSentRowsAsync();
            }

            // A template with the characters encodeURIComponent leaves alone, and placeholders
            // that have no value.
            await SetTemplateBodyAsync(reminder.Id, "Hi (it's) {{customer_name}}! *Now* ~ {{unknown}} € ✓ {{status}}");
            await AdminParity.AssertSameAsync(
                HttpMethod.Post, "/api/v1/admin/notifications/send",
                $$"""{"type":"pickup_reminder","recipient":"{{Recipient}}","reservation_id":"{{BookingId}}"}""", owner,
                afterExpress: DeleteSentRowsAsync, ignored: WrittenKeys);
        }
        finally
        {
            await RestoreTemplateAsync(reminder);
            await DeleteSentRowsAsync();
        }
    }

    private static async Task<TemplateRow> TemplateAsync(string type)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        return await db.QuerySingleAsync<TemplateRow>(
            "SELECT id AS Id, template_body AS Body, is_active AS IsActive FROM notification_templates WHERE type = @type AND channel = 'whatsapp'",
            new { type });
    }

    private static async Task RestoreTemplateAsync(TemplateRow template)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync(
            "UPDATE notification_templates SET template_body = @Body, is_active = @IsActive WHERE id = @Id", template);
    }

    private static async Task SetTemplateBodyAsync(string id, string body)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE notification_templates SET template_body = @body WHERE id = @id", new { id, body });
    }

    private static async Task DeleteSentRowsAsync()
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync("DELETE FROM notification_log WHERE recipient = @Recipient", new { Recipient });
    }

    private sealed record TemplateRow
    {
        public required string Id { get; init; }
        public required string Body { get; init; }
        public bool IsActive { get; init; }
    }
}
