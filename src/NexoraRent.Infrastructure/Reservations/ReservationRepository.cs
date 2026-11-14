using Dapper;
using MySqlConnector;
using NexoraRent.Core.Reservations;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Reservations;

/// <summary>
/// Reservations for customers. The SQL and the locking mirror
/// <c>server/src/routes/reservations.routes.js</c> and <c>createReservation</c>
/// in <c>server/src/services/reservations.js</c>, so Express and this API can
/// take bookings at the same time without double-booking a car.
/// </summary>
public sealed class ReservationRepository(MySqlConnectionFactory connections, TimeProvider time) : IReservationRepository
{
    private const string Columns = """
        r.id, r.reservation_no, r.user_id, r.car_id, r.pickup_location, r.dropoff_location,
        r.pickup_date, r.dropoff_date, r.status, r.total_days, r.daily_rate, r.extras_total,
        r.discount, r.total_price, r.guest_name, r.guest_email, r.guest_phone, r.notes,
        r.admin_notes, r.created_at, r.updated_at, r.loyalty_points_earned, r.loyalty_points_redeemed
        """;

    private const string WithCar = """
        c.brand, c.model, c.slug,
        pl.name AS pickup_location_name, dl.name AS dropoff_location_name
        FROM reservations r
        JOIN cars c ON c.id = r.car_id
        LEFT JOIN locations pl ON pl.id = r.pickup_location
        LEFT JOIN locations dl ON dl.id = r.dropoff_location
        """;

    // Same name as acquireCarReservationLock in Express: both APIs queue on one lock per car.
    private const int LockTimeoutSeconds = 30;

    public async Task<Reservation> CreateAsync(NewReservation reservation, string? userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var lockName = $"nexorarent:reservation:{reservation.CarId}";

        var acquired = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT GET_LOCK(@lockName, @timeout)", new { lockName, timeout = LockTimeoutSeconds }, cancellationToken: cancellationToken));
        if (acquired != 1)
        {
            throw new ReservationException(ReservationFailure.Busy, "Reservation system is busy; try again shortly");
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var created = await InsertAsync(connection, transaction, reservation, userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        finally
        {
            // Release even when the request was cancelled, or the car stays locked for this connection's lifetime.
            await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT RELEASE_LOCK(@lockName)", new { lockName }, cancellationToken: CancellationToken.None));
        }
    }

    private async Task<Reservation> InsertAsync(
        MySqlConnection connection, MySqlTransaction transaction, NewReservation request, string? userId, CancellationToken cancellationToken)
    {
        var carId = request.CarId;
        var pickup = request.Period.Pickup;
        var dropoff = request.Period.Dropoff;

        var car = await connection.QuerySingleOrDefaultAsync<CarRow>(Command(
            "SELECT price_per_day, is_available FROM cars WHERE id = @carId FOR UPDATE", new { carId }));
        if (car is null)
        {
            throw new ReservationException(ReservationFailure.NotFound, "Car not found");
        }

        if (!car.IsAvailable)
        {
            throw new ReservationException(ReservationFailure.Conflict, "Car is not available");
        }

        var overlap = await connection.QueryFirstOrDefaultAsync<string>(Command("""
            SELECT id FROM reservations
            WHERE car_id = @carId AND status IN ('pending', 'confirmed', 'active')
              AND pickup_date < @dropoff AND dropoff_date > @pickup
            LIMIT 1 FOR UPDATE
            """, new { carId, pickup, dropoff }));
        if (overlap is not null)
        {
            throw new ReservationException(ReservationFailure.Conflict, "Car is not available for selected dates");
        }

        var activeLocations = (await connection.QueryAsync<string>(Command(
            "SELECT id FROM locations WHERE is_active = true AND (id = @pickupLocation OR id = @dropoffLocation)",
            new { pickupLocation = request.PickupLocation, dropoffLocation = request.DropoffLocation })))
            .ToHashSet(StringComparer.Ordinal);
        if (!activeLocations.Contains(request.PickupLocation) || !activeLocations.Contains(request.DropoffLocation))
        {
            throw new ReservationException(ReservationFailure.Invalid, "Pickup and dropoff locations must be active");
        }

        var days = ReservationPricing.RentalDays(request.Period);
        var multiplier = await connection.QueryFirstOrDefaultAsync<decimal?>(Command("""
            SELECT multiplier FROM seasonal_pricing
            WHERE car_id = @carId AND start_date <= DATE(@pickup) AND end_date >= DATE(@dropoff)
            LIMIT 1
            """, new { carId, pickup, dropoff }));
        var dailyRate = ReservationPricing.ChargedDailyRate(car.PricePerDay, multiplier);

        var extras = await PriceExtrasAsync(connection, request.Extras, days, Command);
        var extrasTotal = extras.Sum(e => e.Price);

        var id = Guid.NewGuid().ToString();
        await connection.ExecuteAsync(Command("""
            INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_location, dropoff_location,
              pickup_date, dropoff_date, status, total_days, daily_rate, extras_total, total_price,
              guest_name, guest_email, guest_phone, notes, admin_notes)
            VALUES (@id, @reservationNo, @userId, @carId, @pickupLocation, @dropoffLocation,
              @pickup, @dropoff, 'pending', @days, @dailyRate, @extrasTotal, @totalPrice,
              @guestName, @guestEmail, @guestPhone, @notes, NULL)
            """, new
        {
            id,
            reservationNo = ReservationPricing.NewReservationNumber(time.GetUtcNow()),
            userId,
            carId,
            pickupLocation = request.PickupLocation,
            dropoffLocation = request.DropoffLocation,
            pickup,
            dropoff,
            days,
            dailyRate,
            extrasTotal,
            totalPrice = (dailyRate * days) + extrasTotal,
            guestName = request.GuestName,
            guestEmail = request.GuestEmail,
            guestPhone = request.GuestPhone,
            notes = request.Notes,
        }));

        foreach (var extra in extras)
        {
            await connection.ExecuteAsync(Command(
                "INSERT INTO reservation_extras (id, reservation_id, extra_id, quantity, price) VALUES (@lineId, @id, @extraId, @quantity, @price)",
                new { lineId = Guid.NewGuid().ToString(), id, extraId = extra.ExtraId, quantity = extra.Quantity, price = extra.Price }));
        }

        return await connection.QuerySingleAsync<Reservation>(Command(
            $"SELECT {Columns} FROM reservations r WHERE r.id = @id", new { id }));

        CommandDefinition Command(string sql, object parameters) =>
            new(sql, parameters, transaction, cancellationToken: cancellationToken);
    }

    private static async Task<List<(string ExtraId, int Quantity, decimal Price)>> PriceExtrasAsync(
        MySqlConnection connection, IReadOnlyList<ExtraSelection> selections, int days, Func<string, object, CommandDefinition> command)
    {
        if (selections.Count == 0)
        {
            return [];
        }

        var ids = selections.Select(s => s.ExtraId).ToArray();
        var prices = (await connection.QueryAsync<ExtraPriceRow>(command(
            "SELECT id, price_per_day FROM extras WHERE is_active = true AND id IN @ids", new { ids })))
            .ToDictionary(row => row.Id, row => row.PricePerDay, StringComparer.Ordinal);

        if (prices.Count != selections.Count)
        {
            throw new ReservationException(ReservationFailure.Invalid, "One or more selected extras are unavailable");
        }

        return [.. selections.Select(s => (s.ExtraId, s.Quantity, ReservationPricing.ExtraPrice(prices[s.ExtraId], days, s.Quantity)))];
    }

    public async Task<IReadOnlyList<ReservationListItem>> ListForUserAsync(string userId, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT {Columns},
              (SELECT url FROM car_images WHERE car_id = c.id ORDER BY is_primary DESC, sort_order ASC LIMIT 1) AS car_image,
              {WithCar}
            WHERE r.user_id = @userId
            ORDER BY r.created_at DESC, r.id
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReservationListItem>(new CommandDefinition(sql, new { userId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<ReservationDetail?> GetForUserAsync(string id, string userId, string role, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT {Columns}, {WithCar}
            WHERE r.id = @id AND (r.user_id = @userId OR @role IN ('staff', 'manager', 'owner'));

            SELECT re.id, re.reservation_id, re.extra_id, re.quantity, re.price, e.name, e.icon
            FROM reservation_extras re
            JOIN extras e ON e.id = re.extra_id
            WHERE re.reservation_id = @id
            ORDER BY e.name, re.id;
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql, new { id, userId, role }, cancellationToken: cancellationToken));

        var reservation = await results.ReadSingleOrDefaultAsync<ReservationDetail>();
        if (reservation is null)
        {
            return null;
        }

        var extras = (await results.ReadAsync<ReservationExtraLine>()).AsList();
        return reservation with { Extras = extras };
    }

    public async Task<Reservation?> CancelAsync(string id, string userId, CancellationToken cancellationToken)
    {
        // Same two steps as Express: a reservation that is already cancelled is returned again.
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE reservations SET status = 'cancelled'
            WHERE id = @id AND user_id = @userId AND status IN ('pending', 'confirmed')
            """,
            new { id, userId }, cancellationToken: cancellationToken));

        var reservation = await connection.QuerySingleOrDefaultAsync<Reservation>(new CommandDefinition(
            $"SELECT {Columns} FROM reservations r WHERE r.id = @id AND r.user_id = @userId",
            new { id, userId }, cancellationToken: cancellationToken));
        return reservation is { Status: "cancelled" } ? reservation : null;
    }

    private sealed class CarRow
    {
        public decimal PricePerDay { get; init; }
        public bool IsAvailable { get; init; }
    }

    private sealed class ExtraPriceRow
    {
        public required string Id { get; init; }
        public decimal PricePerDay { get; init; }
    }
}
