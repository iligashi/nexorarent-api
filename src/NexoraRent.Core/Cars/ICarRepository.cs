using NexoraRent.Core.Reservations;

namespace NexoraRent.Core.Cars;

public interface ICarRepository
{
    Task<CarPage> ListAsync(CarListQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<Car>> ListFeaturedAsync(CancellationToken cancellationToken);

    /// <summary>Returns the car with its images and latest approved reviews, or null.</summary>
    Task<Car?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the car can be booked for the period. Returns null when the car does not exist.
    /// </summary>
    Task<bool?> IsAvailableAsync(string carId, BookingPeriod period, CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockedPeriod>> ListBlockedPeriodsAsync(string carId, CancellationToken cancellationToken);
}
