using NexoraRent.Core.Reservations;

namespace NexoraRent.UnitTests;

public class BookingPeriodTests
{
    [Fact]
    public void Parses_valid_period()
    {
        Assert.True(BookingPeriod.TryParse("2026-12-01T10:00:00", "2026-12-04T10:00:00", out var period));
        Assert.Equal(new DateTime(2026, 12, 1, 10, 0, 0), period!.Pickup);
        Assert.Equal(new DateTime(2026, 12, 4, 10, 0, 0), period.Dropoff);
    }

    [Theory]
    [InlineData(null, "2026-12-04T10:00:00")]
    [InlineData("2026-12-01", "2026-12-04T10:00:00")]
    [InlineData("2026-02-30T10:00:00", "2026-03-04T10:00:00")]
    [InlineData("2026-12-01T25:00:00", "2026-12-04T10:00:00")]
    [InlineData("2026-12-01T10:00:00Z", "2026-12-04T10:00:00")]
    [InlineData("2026-12-04T10:00:00", "2026-12-01T10:00:00")]
    [InlineData("2026-12-01T10:00:00", "2026-12-01T10:00:00")]
    public void Rejects_invalid_or_reversed_periods(string? pickup, string? dropoff)
    {
        Assert.False(BookingPeriod.TryParse(pickup, dropoff, out var period));
        Assert.Null(period);
    }

    [Theory]
    [InlineData("2026-12-02T00:00:00", "2026-12-03T00:00:00", true)] // inside
    [InlineData("2026-11-30T00:00:00", "2026-12-02T00:00:00", true)] // overlaps start
    [InlineData("2026-12-03T00:00:00", "2026-12-06T00:00:00", true)] // overlaps end
    [InlineData("2026-11-28T00:00:00", "2026-12-01T10:00:00", false)] // ends exactly at pickup
    [InlineData("2026-12-04T10:00:00", "2026-12-06T00:00:00", false)] // starts exactly at dropoff
    public void Detects_overlaps_with_back_to_back_bookings_allowed(string otherPickup, string otherDropoff, bool expected)
    {
        BookingPeriod.TryParse("2026-12-01T10:00:00", "2026-12-04T10:00:00", out var period);

        var overlaps = period!.Overlaps(DateTime.Parse(otherPickup, System.Globalization.CultureInfo.InvariantCulture),
            DateTime.Parse(otherDropoff, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, overlaps);
    }
}
