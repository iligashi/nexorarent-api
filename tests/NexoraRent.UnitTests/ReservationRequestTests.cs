using System.Text.Json;
using NexoraRent.Core.Reservations;
using NexoraRent.Core.Reviews;

namespace NexoraRent.UnitTests;

/// <summary>
/// The expected issues are the exact output of the zod schemas in the Express API
/// (server/src/services/reservations.js and routes/reviews.routes.js) for the same input.
/// </summary>
public class ReservationRequestTests
{
    private const string Id = "aaaaaaaa-0000-4000-8000-000000000001";
    private const string Valid = $$"""
        "car_id":"{{Id}}","pickup_location":"{{Id}}","dropoff_location":"{{Id}}",
        "pickup_date":"2030-01-01T10:00:00","dropoff_date":"2030-01-03T10:00:00"
        """;
    private const string Reversed = $$"""
        "car_id":"{{Id}}","pickup_location":"{{Id}}","dropoff_location":"{{Id}}",
        "pickup_date":"2030-01-03T10:00:00","dropoff_date":"2030-01-01T10:00:00"
        """;

    [Fact]
    public void Reads_a_valid_guest_booking_with_trimmed_contact_details()
    {
        var (reservation, issues) = Read($$"""{{{Valid}},"guest_name":"  Ann ","guest_email":" a@b.co ","guest_phone":" 12345678 ","extras":[{"extra_id":"{{Id}}"}],"junk":1}""", isGuest: true);

        Assert.Empty(issues);
        Assert.Equal("Ann", reservation!.GuestName);
        Assert.Equal("a@b.co", reservation.GuestEmail);
        Assert.Equal("12345678", reservation.GuestPhone);
        Assert.Equal(new ExtraSelection(Id, 1), Assert.Single(reservation.Extras));
        Assert.Equal(new DateTime(2030, 1, 1, 10, 0, 0), reservation.Period.Pickup);
    }

    [Fact]
    public void Stores_empty_optional_text_as_null()
    {
        var (reservation, _) = Read($$"""{{{Valid}},"notes":"","guest_phone":"  "}""", isGuest: false);

        Assert.Null(reservation!.Notes);
        Assert.Null(reservation.GuestPhone);
    }

    [Theory]
    [InlineData("{}", false,
        "car_id|Invalid input: expected string, received undefined",
        "pickup_location|Invalid input: expected string, received undefined",
        "dropoff_location|Invalid input: expected string, received undefined",
        "pickup_date|Invalid input: expected string, received undefined",
        "dropoff_date|Invalid input: expected string, received undefined")]
    [InlineData("{" + Valid + "}", true,
        "guest_name|Invalid input: expected string, received undefined",
        "guest_email|Invalid input: expected string, received undefined",
        "guest_phone|Invalid input: expected string, received undefined")]
    [InlineData("""{"car_id":5,"pickup_location":"x","dropoff_location":null,"pickup_date":1,"dropoff_date":"2030-02-30T10:00:00"}""", false,
        "car_id|Invalid input: expected string, received number",
        "pickup_location|Invalid UUID",
        "dropoff_location|Invalid input: expected string, received null",
        "pickup_date|Invalid input: expected string, received number",
        "dropoff_date|Use a valid local date and time (YYYY-MM-DDTHH:mm:ss)")]
    [InlineData("{" + Reversed + ",\"extras\":[{\"extra_id\":\"" + Id + "\"},{\"extra_id\":\"" + Id + "\",\"quantity\":0}]}", false,
        "extras.1.quantity|Too small: expected number to be >=1",
        "dropoff_date|Dropoff date must be after pickup date",
        "extras|Each extra may only be selected once")]
    [InlineData("{" + Valid + ",\"guest_name\":\"  \",\"guest_email\":\"nope\",\"guest_phone\":\"123\"}", true,
        "guest_name|Too small: expected string to have >=1 characters",
        "guest_email|Invalid email address",
        "guest_phone|Too small: expected string to have >=8 characters")]
    [InlineData("{" + Valid + ",\"extras\":\"x\"}", false,
        "extras|Invalid input: expected array, received string")]
    [InlineData("{" + Valid + ",\"extras\":[{\"extra_id\":\"x\",\"quantity\":1.5},{\"quantity\":21},\"a\"]}", false,
        "extras.0.extra_id|Invalid UUID",
        "extras.0.quantity|Invalid input: expected int, received number",
        "extras.1.extra_id|Invalid input: expected string, received undefined",
        "extras.1.quantity|Too big: expected number to be <=20",
        "extras.2|Invalid input: expected object, received string")]
    [InlineData("{" + Valid + ",\"notes\":5,\"guest_phone\":5,\"guest_email\":\"\"}", false,
        "guest_email|Invalid email address",
        "guest_phone|Invalid input: expected string, received number",
        "notes|Invalid input: expected string, received number")]
    [InlineData("{" + Reversed + ",\"car_id\":5}", false,
        "car_id|Invalid input: expected string, received number")]
    [InlineData("{" + Reversed + ",\"car_id\":\"x\"}", false,
        "car_id|Invalid UUID",
        "dropoff_date|Dropoff date must be after pickup date")]
    [InlineData("{" + Reversed + ",\"car_id\":\"aaaaaaaa-0000-0000-0000-000000000001\\n\"}", false,
        "car_id|Invalid UUID",
        "dropoff_date|Dropoff date must be after pickup date")]
    [InlineData("{" + Reversed + ",\"pickup_date\":\"0050-01-01T10:00:00\",\"dropoff_date\":\"2030-01-01T24:00:00\"}", false,
        "pickup_date|Use a valid local date and time (YYYY-MM-DDTHH:mm:ss)",
        "dropoff_date|Use a valid local date and time (YYYY-MM-DDTHH:mm:ss)")]
    public void Reports_the_same_issues_as_zod(string json, bool isGuest, params string[] expected)
    {
        var (reservation, issues) = Read(json, isGuest);

        Assert.Null(reservation);
        Assert.Equal(expected, issues.Select(i => $"{i.Field}|{i.Message}"));
    }

    [Fact]
    public void An_over_long_extras_list_is_reported_once_and_skips_refinements()
    {
        var extras = string.Join(',', Enumerable.Repeat($$"""{"extra_id":"{{Id}}"}""", 21));

        var (_, issues) = Read($$"""{{{Reversed}},"extras":[{{extras}}]}""", isGuest: false);

        Assert.Equal(["extras|Too big: expected array to have <=20 items"], issues.Select(i => $"{i.Field}|{i.Message}"));
    }

    [Fact]
    public void Checks_email_and_length_of_the_same_field()
    {
        var (_, issues) = Read($$"""{{{Valid}},"guest_name":"Ann","guest_email":"{{new string('x', 300)}}","guest_phone":"12345678"}""", isGuest: true);

        Assert.Equal(
            ["guest_email|Invalid email address", "guest_email|Too big: expected string to have <=255 characters"],
            issues.Select(i => $"{i.Field}|{i.Message}"));
    }

    [Theory]
    [InlineData("{}", "reservation_id|Invalid input: expected string, received undefined", "rating|Invalid input: expected number, received undefined")]
    [InlineData("""{"reservation_id":"x","rating":6,"comment":3}""", "reservation_id|Invalid UUID", "rating|Too big: expected number to be <=5", "comment|Invalid input: expected string, received number")]
    [InlineData("{\"reservation_id\":\"" + Id + "\",\"rating\":2.5}", "rating|Invalid input: expected int, received number")]
    [InlineData("{\"reservation_id\":\"" + Id + "\",\"rating\":0}", "rating|Too small: expected number to be >=1")]
    [InlineData("{\"reservation_id\":\"" + Id + "\",\"rating\":\"4\"}", "rating|Invalid input: expected number, received string")]
    [InlineData("{\"reservation_id\":\"" + Id + "\",\"rating\":1e20}", "rating|Too big: expected int to be <=9007199254740991", "rating|Too big: expected number to be <=5")]
    public void Reviews_report_the_same_issues_as_zod(string json, params string[] expected)
    {
        var (review, issues) = ReviewRequest.Read(Parse(json));

        Assert.Null(review);
        Assert.Equal(expected, issues.Select(i => $"{i.Field}|{i.Message}"));
    }

    [Fact]
    public void Reads_a_valid_review()
    {
        var (review, issues) = ReviewRequest.Read(Parse($$"""{"reservation_id":"{{Id}}","rating":1.0,"comment":""}"""));

        Assert.Empty(issues);
        Assert.Equal(new NewReview(Id, 1, null), review);
    }

    private static (NewReservation? Reservation, IReadOnlyList<Core.Validation.ValidationIssue> Issues) Read(string json, bool isGuest) =>
        ReservationRequest.Read(Parse(json), isGuest);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
