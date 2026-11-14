using System.Text;
using Dapper;
using NexoraRent.Core.Cars;
using NexoraRent.Core.Reservations;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Cars;

/// <summary>
/// Read side of the cars API. The SQL mirrors <c>server/src/routes/cars.routes.js</c>
/// in the Express API so both services return the same data from the same tables.
/// </summary>
public sealed class CarRepository(MySqlConnectionFactory connections) : ICarRepository
{
    // A plain array, so Dapper expands "IN @statuses" into IN ('pending', ...).
    // An IReadOnlyList<string> would go through JsonStringListHandler and be sent as one JSON string.
    private static readonly string[] OccupyingStatuses = [.. ReservationStatuses.Occupying];

    private const string PrimaryImage =
        "(SELECT url FROM car_images WHERE car_id = c.id ORDER BY is_primary DESC, sort_order ASC LIMIT 1)";

    // Ratings come from approved reviews on any reservation of the car.
    private const string RatingJoins = """
        LEFT JOIN reservations res ON res.car_id = c.id
        LEFT JOIN reviews rv ON rv.reservation_id = res.id AND rv.is_approved = true
        """;

    public async Task<CarPage> ListAsync(CarListQuery query, CancellationToken cancellationToken)
    {
        var where = new StringBuilder("WHERE c.is_available = true");
        var parameters = new DynamicParameters();

        if (query.Category is not null)
        {
            where.Append(" AND c.category = @Category");
            parameters.Add("Category", query.Category);
        }

        if (query.Fuel is not null)
        {
            where.Append(" AND c.fuel = @Fuel");
            parameters.Add("Fuel", query.Fuel);
        }

        if (query.Transmission is not null)
        {
            where.Append(" AND c.transmission = @Transmission");
            parameters.Add("Transmission", query.Transmission);
        }

        if (query.PriceMin is not null)
        {
            where.Append(" AND c.price_per_day >= @PriceMin");
            parameters.Add("PriceMin", query.PriceMin);
        }

        if (query.PriceMax is not null)
        {
            where.Append(" AND c.price_per_day <= @PriceMax");
            parameters.Add("PriceMax", query.PriceMax);
        }

        if (query.Search is not null)
        {
            where.Append(" AND CONCAT(c.brand, ' ', c.model) LIKE @Search");
            parameters.Add("Search", $"%{EscapeLike(query.Search)}%");
        }

        var orderBy = query.Sort switch
        {
            CarSort.PriceAsc => "c.price_per_day ASC",
            CarSort.PriceDesc => "c.price_per_day DESC",
            CarSort.Newest => "c.created_at DESC",
            _ => "c.sort_order ASC, c.created_at DESC",
        };

        parameters.Add("Limit", query.Limit);
        parameters.Add("Offset", query.Offset);

        var sql = $"""
            SELECT c.*, {PrimaryImage} AS image,
                   COALESCE(AVG(rv.rating), 0) AS avg_rating,
                   COUNT(rv.id) AS review_count
            FROM cars c
            {RatingJoins}
            {where}
            GROUP BY c.id
            ORDER BY {orderBy}
            LIMIT @Limit OFFSET @Offset;

            SELECT COUNT(*) FROM cars c {where};
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var cars = (await results.ReadAsync<Car>()).AsList();
        var total = await results.ReadSingleAsync<int>();
        return new CarPage(cars, total);
    }

    public async Task<IReadOnlyList<Car>> ListFeaturedAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT c.*, {PrimaryImage} AS image
            FROM cars c
            WHERE c.is_available = true AND c.is_featured = true
            ORDER BY c.sort_order ASC
            LIMIT 6
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var cars = await connection.QueryAsync<Car>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return cars.AsList();
    }

    public async Task<Car?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var carSql = $"""
            SELECT c.*,
                   COALESCE(AVG(rv.rating), 0) AS avg_rating,
                   COUNT(rv.id) AS review_count
            FROM cars c
            {RatingJoins}
            WHERE c.slug = @slug
            GROUP BY c.id
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var car = await connection.QuerySingleOrDefaultAsync<Car>(
            new CommandDefinition(carSql, new { slug }, cancellationToken: cancellationToken));
        if (car is null)
        {
            return null;
        }

        const string detailsSql = """
            SELECT id, url, alt_text, is_primary, sort_order
            FROM car_images WHERE car_id = @id ORDER BY sort_order;

            SELECT rv.*, u.first_name, u.last_name
            FROM reviews rv JOIN users u ON u.id = rv.user_id
            WHERE rv.reservation_id IN (SELECT id FROM reservations WHERE car_id = @id)
              AND rv.is_approved = true
            ORDER BY rv.created_at DESC LIMIT 10;
            """;

        await using var details = await connection.QueryMultipleAsync(
            new CommandDefinition(detailsSql, new { id = car.Id }, cancellationToken: cancellationToken));
        var images = (await details.ReadAsync<CarImage>()).AsList();
        var reviews = (await details.ReadAsync<CarReview>()).AsList();

        return car with { Images = images, Reviews = reviews };
    }

    public async Task<bool?> IsAvailableAsync(string carId, BookingPeriod period, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT is_available FROM cars WHERE id = @carId;

            SELECT EXISTS (
                SELECT 1 FROM reservations
                WHERE car_id = @carId
                  AND status IN @statuses
                  AND pickup_date < @dropoff AND dropoff_date > @pickup
            );
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new { carId, statuses = OccupyingStatuses, pickup = period.Pickup, dropoff = period.Dropoff },
            cancellationToken: cancellationToken));

        var isAvailable = await results.ReadSingleOrDefaultAsync<bool?>();
        if (isAvailable is null)
        {
            return null;
        }

        var hasOverlap = await results.ReadSingleAsync<bool>();
        return isAvailable.Value && !hasOverlap;
    }

    public async Task<IReadOnlyList<BlockedPeriod>> ListBlockedPeriodsAsync(string carId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT pickup_date, dropoff_date FROM reservations
            WHERE car_id = @carId AND status IN @statuses
            ORDER BY pickup_date
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<BlockedPeriod>(new CommandDefinition(
            sql,
            new { carId, statuses = OccupyingStatuses },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>Treats % and _ in the search text as literal characters.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
