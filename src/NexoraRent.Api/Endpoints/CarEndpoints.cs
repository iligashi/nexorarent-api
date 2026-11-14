using NexoraRent.Api.Http;
using NexoraRent.Core.Cars;
using NexoraRent.Core.Reservations;

namespace NexoraRent.Api.Endpoints;

/// <summary>Public cars endpoints, compatible with <c>/api/v1/cars</c> in the Express API.</summary>
internal static class CarEndpoints
{
    public static RouteGroupBuilder MapCarEndpoints(this RouteGroupBuilder api)
    {
        var cars = api.MapGroup("/cars").WithTags("Cars");

        cars.MapGet("/", ListCars).WithName("ListCars");
        cars.MapGet("/featured", ListFeatured).WithName("ListFeaturedCars");
        cars.MapGet("/{slug}", GetBySlug).WithName("GetCarBySlug");
        cars.MapGet("/{id}/availability", GetAvailability).WithName("GetCarAvailability");

        return api;
    }

    private static async Task<IResult> ListCars(
        ICarRepository repository,
        CancellationToken cancellationToken,
        string? category = null,
        string? fuel = null,
        string? transmission = null,
        string? price_min = null,
        string? price_max = null,
        string? search = null,
        string? sort = null,
        string? page = null,
        string? limit = null)
    {
        var query = CarListQuery.FromRaw(category, fuel, transmission, price_min, price_max, search, sort, page, limit);
        var result = await repository.ListAsync(query, cancellationToken);
        return Results.Ok(new { cars = result.Cars, total = result.Total, page = query.Page, limit = query.Limit });
    }

    private static async Task<IResult> ListFeatured(ICarRepository repository, CancellationToken cancellationToken)
    {
        var cars = await repository.ListFeaturedAsync(cancellationToken);
        return Results.Ok(new { cars });
    }

    private static async Task<IResult> GetBySlug(string slug, ICarRepository repository, CancellationToken cancellationToken)
    {
        var car = await repository.GetBySlugAsync(slug, cancellationToken);
        return car is null
            ? ErrorHandling.Error(StatusCodes.Status404NotFound, "Car not found")
            : Results.Ok(new { car });
    }

    private static async Task<IResult> GetAvailability(
        string id,
        ICarRepository repository,
        CancellationToken cancellationToken,
        string? pickup_date = null,
        string? dropoff_date = null)
    {
        if (string.IsNullOrEmpty(pickup_date) || string.IsNullOrEmpty(dropoff_date))
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, "pickup_date and dropoff_date required");
        }

        if (!BookingPeriod.TryParse(pickup_date, dropoff_date, out var period) || period is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, BookingPeriod.InvalidMessage);
        }

        var isAvailable = await repository.IsAvailableAsync(id, period, cancellationToken);
        if (isAvailable is null)
        {
            return ErrorHandling.Error(StatusCodes.Status404NotFound, "Car not found");
        }

        var blocked = await repository.ListBlockedPeriodsAsync(id, cancellationToken);
        return Results.Ok(new { is_available = isAvailable.Value, blocked_dates = blocked });
    }
}
