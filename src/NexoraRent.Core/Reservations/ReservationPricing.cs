using System.Globalization;
using System.Security.Cryptography;

namespace NexoraRent.Core.Reservations;

/// <summary>
/// Price rules from <c>createReservation</c> in the Express API. Express works in
/// integer cents; <see cref="decimal"/> gives the same results without rounding drift.
/// </summary>
public static class ReservationPricing
{
    /// <summary>Started days count as full days, with a minimum of one.</summary>
    public static int RentalDays(BookingPeriod period)
    {
        var ticks = (period.Dropoff - period.Pickup).Ticks;
        return (int)Math.Max(1, (ticks + TimeSpan.TicksPerDay - 1) / TimeSpan.TicksPerDay);
    }

    /// <summary>The daily rate with a seasonal multiplier, rounded half up to cents.</summary>
    public static decimal ChargedDailyRate(decimal dailyRate, decimal? seasonalMultiplier) =>
        seasonalMultiplier is { } multiplier
            ? Math.Round(dailyRate * multiplier, 2, MidpointRounding.AwayFromZero)
            : dailyRate;

    public static decimal ExtraPrice(decimal pricePerDay, int days, int quantity) => pricePerDay * days * quantity;

    /// <summary>A booking number like <c>DRC-20261114-482913</c> (UTC date and six random digits).</summary>
    public static string NewReservationNumber(DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"DRC-{now.UtcDateTime:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 1000000)}");
}
