namespace NexoraRent.Core.Reservations;

/// <summary>
/// A row of the <c>reservations</c> table, as the Express API returns it
/// (<c>SELECT * FROM reservations</c>). Property names become snake_case JSON.
/// </summary>
public record Reservation
{
    public required string Id { get; init; }
    public required string ReservationNo { get; init; }
    public string? UserId { get; init; }
    public required string CarId { get; init; }
    public string? PickupLocation { get; init; }
    public string? DropoffLocation { get; init; }
    public DateTime PickupDate { get; init; }
    public DateTime DropoffDate { get; init; }
    public required string Status { get; init; }
    public int TotalDays { get; init; }
    public decimal DailyRate { get; init; }
    public decimal? ExtrasTotal { get; init; }
    public decimal? Discount { get; init; }
    public decimal TotalPrice { get; init; }
    public string? GuestName { get; init; }
    public string? GuestEmail { get; init; }
    public string? GuestPhone { get; init; }
    public string? Notes { get; init; }
    public string? AdminNotes { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? LoyaltyPointsEarned { get; init; }
    public int? LoyaltyPointsRedeemed { get; init; }
}

/// <summary>A reservation joined with its car and location names.</summary>
public record ReservationWithCar : Reservation
{
    public required string Brand { get; init; }
    public required string Model { get; init; }
    public required string Slug { get; init; }
    public string? PickupLocationName { get; init; }
    public string? DropoffLocationName { get; init; }
}

/// <summary>An item of <c>GET /reservations/mine</c>.</summary>
public sealed record ReservationListItem : ReservationWithCar
{
    public string? CarImage { get; init; }
}

/// <summary>The body of <c>GET /reservations/{id}</c>.</summary>
public sealed record ReservationDetail : ReservationWithCar
{
    public IReadOnlyList<ReservationExtraLine> Extras { get; init; } = [];
}

/// <summary>A booked extra: a <c>reservation_extras</c> row with the extra's name and icon.</summary>
public sealed record ReservationExtraLine
{
    public required string Id { get; init; }
    public required string ReservationId { get; init; }
    public required string ExtraId { get; init; }
    public int? Quantity { get; init; }
    public decimal Price { get; init; }
    public required string Name { get; init; }
    public string? Icon { get; init; }
}

/// <summary>A validated booking request.</summary>
public sealed record NewReservation
{
    public required string CarId { get; init; }
    public required string PickupLocation { get; init; }
    public required string DropoffLocation { get; init; }
    public required BookingPeriod Period { get; init; }
    public string? GuestName { get; init; }
    public string? GuestEmail { get; init; }
    public string? GuestPhone { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<ExtraSelection> Extras { get; init; } = [];
}

public sealed record ExtraSelection(string ExtraId, int Quantity);

public enum ReservationFailure
{
    NotFound,
    Conflict,
    Invalid,
    Busy,
}

/// <summary>A booking rule was broken. The message is the one Express returns.</summary>
public sealed class ReservationException : Exception
{
    public ReservationException()
    {
    }

    public ReservationException(string message)
        : base(message)
    {
    }

    public ReservationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ReservationException(ReservationFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public ReservationFailure Failure { get; }
}

public interface IReservationRepository
{
    /// <summary>
    /// Books a car, holding the same per-car advisory lock as Express
    /// (<c>nexorarent:reservation:{carId}</c>) so the two APIs never double-book.
    /// </summary>
    /// <exception cref="ReservationException">A booking rule is broken.</exception>
    Task<Reservation> CreateAsync(NewReservation reservation, string? userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationListItem>> ListForUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The reservation if it belongs to the user, or any reservation for admin roles.</summary>
    Task<ReservationDetail?> GetForUserAsync(string id, string userId, string role, CancellationToken cancellationToken);

    /// <summary>Cancels a pending or confirmed reservation of the user and returns it, or null.</summary>
    Task<Reservation?> CancelAsync(string id, string userId, CancellationToken cancellationToken);
}
