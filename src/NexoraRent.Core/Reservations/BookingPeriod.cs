using System.Globalization;

namespace NexoraRent.Core.Reservations;

/// <summary>
/// A pickup/dropoff window expressed as wall-clock times in the format
/// <c>yyyy-MM-ddTHH:mm:ss</c>, the same format the Express API accepts.
/// </summary>
public sealed record BookingPeriod(DateTime Pickup, DateTime Dropoff)
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss";
    public const string InvalidMessage = "Pickup and dropoff dates must be valid and dropoff must be after pickup";

    public static bool TryParse(string? pickup, string? dropoff, out BookingPeriod? period)
    {
        period = null;
        if (!TryParseLocal(pickup, out var start) || !TryParseLocal(dropoff, out var end) || end <= start)
        {
            return false;
        }

        period = new BookingPeriod(start, end);
        return true;
    }

    /// <summary>Two periods overlap when each one starts before the other ends.</summary>
    public bool Overlaps(DateTime otherPickup, DateTime otherDropoff) => otherPickup < Dropoff && otherDropoff > Pickup;

    private static bool TryParseLocal(string? value, out DateTime result) =>
        DateTime.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
}

public sealed record BlockedPeriod(DateTime PickupDate, DateTime DropoffDate);

public static class ReservationStatuses
{
    /// <summary>Statuses that hold a car and block other bookings.</summary>
    public static readonly IReadOnlyList<string> Occupying = ["pending", "confirmed", "active"];
}
