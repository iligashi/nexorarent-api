using System.Text.Json;
using NexoraRent.Core.Notifications;

namespace NexoraRent.UnitTests;

/// <summary>Expected strings come from running <c>sendNotification</c>'s code in Node.</summary>
public class WhatsAppMessageTests
{
    private static readonly ReservationNotificationData Booking = new()
    {
        ReservationNo = "DRC-7",
        CustomerName = "Arta Krasniqi",
        Brand = "BMW",
        Model = "X5",
        PickupDate = new DateTime(2031, 7, 2, 10, 0, 0),
        DropoffDate = new DateTime(2031, 12, 15, 10, 0, 0),
        PickupLocation = "Airport",
        DropoffLocation = null,
        TotalPrice = 1234.5m,
        Status = "confirmed",
    };

    [Fact]
    public void Reservation_values_fill_the_template_and_unknown_placeholders_stay()
    {
        var data = WhatsAppMessage.TemplateData(new Dictionary<string, string?> { ["status"] = "old", ["note"] = null }, Booking);

        var text = WhatsAppMessage.Render(
            "{{customer_name}} #{{reservation_no}} {{car_name}} {{pickup_date}}-{{dropoff_date}} {{pickup_location}}/{{dropoff_location}} {{total_price}} {{status}} [{{note}}] {{other}}",
            data);

        Assert.Equal("Arta Krasniqi #DRC-7 BMW X5 7/2/2031-12/15/2031 Airport/ €1234.50 confirmed [] {{other}}", text);
    }

    [Fact]
    public void The_guest_name_is_used_when_the_customer_name_is_empty()
    {
        var data = WhatsAppMessage.TemplateData(null, Booking with { CustomerName = "", GuestName = "Guest" });

        Assert.Equal("Guest", data["customer_name"]);
    }

    [Fact]
    public void Without_a_reservation_only_the_extra_values_are_used()
    {
        Assert.Empty(WhatsAppMessage.TemplateData(null, null));
    }

    [Theory]
    [InlineData("+383 (44) 123-456", "Hi!", "https://wa.me/38344123456?text=Hi!")]
    [InlineData("0044 1", "a b&c=d", "https://wa.me/00441?text=a%20b%26c%3Dd")]
    [InlineData("+1", "it's (fine) *ok* ~ € ✓\n", "https://wa.me/1?text=it's%20(fine)%20*ok*%20~%20%E2%82%AC%20%E2%9C%93%0A")]
    public void The_link_cleans_the_number_and_encodes_like_encodeURIComponent(string recipient, string message, string expected) =>
        Assert.Equal(expected, WhatsAppMessage.Link(recipient, message));

    [Fact]
    public void A_send_needs_text_for_type_and_recipient()
    {
        Assert.Equal(NotificationRequestReader.SendRequiredMessage, NotificationRequestReader.ReadSend(Parse("""{"type":"x"}""")).Error);
        Assert.StartsWith("recipient must be text", NotificationRequestReader.ReadSend(Parse("""{"type":"x","recipient":44}""")).Error, StringComparison.Ordinal);
        Assert.Equal(
            new NotificationRequest("pickup_reminder", "+1", null),
            NotificationRequestReader.ReadSend(Parse("""{"type":"pickup_reminder","recipient":"+1","reservation_id":""}""")).Request);
    }

    [Theory]
    [InlineData("{}", NotificationRequestReader.NoFieldsMessage)]
    [InlineData("""{"template_body":null}""", "template_body must be text")]
    [InlineData("""{"template_body":"   "}""", "template_body must be text")]
    [InlineData("""{"is_active":1}""", "is_active must be true or false")]
    public void Template_changes_are_checked(string body, string message) =>
        Assert.StartsWith(message, NotificationRequestReader.ReadTemplateChanges(Parse(body)).Error, StringComparison.Ordinal);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
