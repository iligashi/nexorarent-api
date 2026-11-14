using System.Text.RegularExpressions;
using NexoraRent.Core.Reservations;

namespace NexoraRent.UnitTests;

public class ReservationPricingTests
{
    [Theory]
    [InlineData("2030-01-01T10:00:00", "2030-01-01T10:30:00", 1)] // minimum one day
    [InlineData("2030-01-01T10:00:00", "2030-01-02T10:00:00", 1)]
    [InlineData("2030-01-01T10:00:00", "2030-01-02T10:00:01", 2)] // a started day counts
    [InlineData("2030-01-01T10:00:00", "2030-01-08T09:00:00", 7)]
    public void Counts_started_days(string pickup, string dropoff, int expected)
    {
        Assert.True(BookingPeriod.TryParse(pickup, dropoff, out var period));

        Assert.Equal(expected, ReservationPricing.RentalDays(period!));
    }

    [Theory]
    [InlineData("35.00", null, "35.00")]
    [InlineData("35.00", "1.15", "40.25")]
    [InlineData("33.33", "1.15", "38.33")] // 38.3295 rounds down
    [InlineData("33.35", "1.10", "36.69")] // 36.685 rounds half up, like Express' integer cents
    [InlineData("99.99", "0.50", "50.00")] // 49.995 rounds half up
    public void Applies_the_seasonal_multiplier_in_cents(string rate, string? multiplier, string expected)
    {
        var charged = ReservationPricing.ChargedDailyRate(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture),
            multiplier is null ? null : decimal.Parse(multiplier, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), charged);
    }

    [Fact]
    public void Prices_extras_per_day_and_quantity() => Assert.Equal(30m, ReservationPricing.ExtraPrice(5m, 3, 2));

    [Fact]
    public void Reservation_numbers_use_the_utc_date_and_six_digits()
    {
        var number = ReservationPricing.NewReservationNumber(new DateTimeOffset(2026, 11, 14, 23, 30, 0, TimeSpan.FromHours(-2)));

        Assert.Matches(new Regex(@"^DRC-20261115-[1-9]\d{5}$"), number);
        Assert.True(number.Length <= 20, "reservations.reservation_no is VARCHAR(20)");
    }

    [Theory]
    [InlineData("0050-01-01T10:00:00", false)] // Date.UTC reads years below 100 as 19xx
    [InlineData("0100-01-01T10:00:00", true)]
    [InlineData("2030-01-01T10:00:00\n", false)]
    [InlineData("2030-01-01T24:00:00", false)]
    public void Validates_local_date_times_like_express(string value, bool expected) =>
        Assert.Equal(expected, BookingPeriod.IsValidLocalDateTime(value));
}
