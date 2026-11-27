using System.Text.Json;
using Dapper;
using MySqlConnector;
using NexoraRent.Core;
using NexoraRent.Core.Pricing;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Pricing;

/// <summary>Mirrors <c>server/src/routes/admin.pricing.routes.js</c> in the Express API.</summary>
public sealed class PricingRepository(MySqlConnectionFactory connections) : IPricingRepository
{
    private const string RuleColumns =
        "pr.id, pr.name, pr.type, pr.car_id, pr.category, pr.multiplier, pr.conditions, pr.priority, pr.is_active, pr.start_date, pr.end_date, pr.created_at";

    // Express counts both with these two statements, in /demand and in /calculate.
    private const string CountAvailableCars = "SELECT COUNT(*) FROM cars WHERE is_available = true";
    private const string CountBookedCars = """
        SELECT COUNT(DISTINCT car_id) FROM reservations
        WHERE status IN ('confirmed','active') AND pickup_date <= @date AND dropoff_date >= @date
        """;

    public async Task<IReadOnlyList<PricingRuleWithCar>> ListRulesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<RuleRow>(new CommandDefinition(
            $"""
            SELECT {RuleColumns}, c.brand, c.model
            FROM pricing_rules pr
            LEFT JOIN cars c ON c.id = pr.car_id
            ORDER BY pr.priority DESC, pr.created_at DESC
            """,
            cancellationToken: cancellationToken));
        return [.. rows.Select(row => row.ToRuleWithCar())];
    }

    public async Task<PricingRuleWrite> CreateRuleAsync(NewPricingRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var id = Guid.NewGuid().ToString();

        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO pricing_rules (id, name, type, car_id, category, multiplier, conditions, priority, is_active, start_date, end_date)
                VALUES (@id, @Name, @Type, @CarId, @Category, @Multiplier, @ConditionsJson, @Priority, @IsActive, @StartDate, @EndDate)
                """,
                new
                {
                    id,
                    rule.Name,
                    rule.Type,
                    rule.CarId,
                    rule.Category,
                    rule.Multiplier,
                    rule.ConditionsJson,
                    rule.Priority,
                    rule.IsActive,
                    StartDate = ToDate(rule.StartDate),
                    EndDate = ToDate(rule.EndDate),
                },
                cancellationToken: cancellationToken));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            return new PricingRuleWrite(PricingRuleWriteStatus.CarNotFound);
        }

        return new PricingRuleWrite(PricingRuleWriteStatus.Saved, await GetRuleAsync(connection, id, cancellationToken));
    }

    public async Task<PricingRuleWrite> UpdateRuleAsync(string id, PricingRuleChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Each column comes from a fixed list; only the values are parameters.
        var sets = new List<string>();
        var parameters = new DynamicParameters(new { id });
        void Set<T>(FieldChange<T> field, string column, Func<T, object?>? convert = null)
        {
            if (field.IsSet)
            {
                sets.Add($"{column} = @{column}");
                parameters.Add(column, convert is null ? field.Value : convert(field.Value));
            }
        }

        Set(changes.Name, "name");
        Set(changes.Type, "type");
        Set(changes.CarId, "car_id");
        Set(changes.Category, "category");
        Set(changes.Multiplier, "multiplier");
        Set(changes.ConditionsJson, "conditions");
        Set(changes.Priority, "priority");
        Set(changes.IsActive, "is_active");
        Set(changes.StartDate, "start_date", date => ToDate(date));
        Set(changes.EndDate, "end_date", date => ToDate(date));

        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE pricing_rules SET {string.Join(", ", sets)} WHERE id = @id", parameters, cancellationToken: cancellationToken));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            return new PricingRuleWrite(PricingRuleWriteStatus.CarNotFound);
        }

        // Read back instead of trusting the affected-row count, which is 0 when nothing changed.
        var rule = await GetRuleAsync(connection, id, cancellationToken);
        return rule is null
            ? new PricingRuleWrite(PricingRuleWriteStatus.NotFound)
            : new PricingRuleWrite(PricingRuleWriteStatus.Saved, rule);
    }

    public async Task DeleteRuleAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM pricing_rules WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task<DemandCounts> CountDemandAsync(string day, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await CountDemandAsync(connection, day, cancellationToken);
    }

    public async Task<DynamicPriceInputs?> LoadPriceInputsAsync(
        string carId, string pickupDate, string dropoffDate, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var car = await connection.QuerySingleOrDefaultAsync<CarPrice>(new CommandDefinition(
            "SELECT price_per_day AS PricePerDay, category AS Category FROM cars WHERE id = @carId", new { carId }, cancellationToken: cancellationToken));
        if (car is null)
        {
            return null;
        }

        var seasonal = await connection.QueryFirstOrDefaultAsync<decimal?>(new CommandDefinition(
            "SELECT multiplier FROM seasonal_pricing WHERE car_id = @carId AND start_date <= @pickupDate AND end_date >= @dropoffDate LIMIT 1",
            new { carId, pickupDate, dropoffDate },
            cancellationToken: cancellationToken));

        var rules = await connection.QueryAsync<RuleRow>(new CommandDefinition(
            $"""
            SELECT {RuleColumns} FROM pricing_rules pr
            WHERE pr.is_active = true
              AND (pr.car_id IS NULL OR pr.car_id = @carId)
              AND (pr.category IS NULL OR pr.category = @category)
              AND (pr.start_date IS NULL OR pr.start_date <= @pickupDate)
              AND (pr.end_date IS NULL OR pr.end_date >= @dropoffDate)
            ORDER BY pr.priority DESC
            """,
            new { carId, category = car.Category, pickupDate, dropoffDate },
            cancellationToken: cancellationToken));

        // Demand is measured on the pickup day.
        var demand = await CountDemandAsync(connection, pickupDate, cancellationToken);

        return new DynamicPriceInputs(car.PricePerDay, seasonal, [.. rules.Select(row => row.ToRule())], demand);
    }

    private static async Task<DemandCounts> CountDemandAsync(MySqlConnection connection, string date, CancellationToken cancellationToken)
    {
        var available = await connection.ExecuteScalarAsync<int>(new CommandDefinition(CountAvailableCars, cancellationToken: cancellationToken));
        var booked = await connection.ExecuteScalarAsync<int>(new CommandDefinition(CountBookedCars, new { date }, cancellationToken: cancellationToken));
        return new DemandCounts(available, booked);
    }

    private static async Task<PricingRule?> GetRuleAsync(MySqlConnection connection, string id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<RuleRow>(new CommandDefinition(
            $"SELECT {RuleColumns} FROM pricing_rules pr WHERE pr.id = @id", new { id }, cancellationToken: cancellationToken));
        return row?.ToRule();
    }

    private static DateTime? ToDate(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static readonly JsonElement NullJson = JsonDocument.Parse("null").RootElement.Clone();

    private sealed record CarPrice(decimal PricePerDay, string? Category);

    /// <summary>A <c>pricing_rules</c> row as Dapper reads it; the JSON column arrives as text.</summary>
    private sealed record RuleRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Type { get; init; }
        public string? CarId { get; init; }
        public string? Category { get; init; }
        public decimal Multiplier { get; init; }
        public string? Conditions { get; init; }
        public int? Priority { get; init; }
        public bool? IsActive { get; init; }
        public DateTime? StartDate { get; init; }
        public DateTime? EndDate { get; init; }
        public DateTime? CreatedAt { get; init; }
        public string? Brand { get; init; }
        public string? Model { get; init; }

        public PricingRule ToRule() => new()
        {
            Id = Id, Name = Name, Type = Type, CarId = CarId, Category = Category, Multiplier = Multiplier,
            Conditions = ParseJson(Conditions), Priority = Priority, IsActive = IsActive,
            StartDate = StartDate, EndDate = EndDate, CreatedAt = CreatedAt,
        };

        public PricingRuleWithCar ToRuleWithCar() => new()
        {
            Id = Id, Name = Name, Type = Type, CarId = CarId, Category = Category, Multiplier = Multiplier,
            Conditions = ParseJson(Conditions), Priority = Priority, IsActive = IsActive,
            StartDate = StartDate, EndDate = EndDate, CreatedAt = CreatedAt, Brand = Brand, Model = Model,
        };

        // MySQL keeps the column valid JSON. If a row still is not, return it as text, not a 500.
        private static JsonElement ParseJson(string? json)
        {
            if (json is null)
            {
                return NullJson;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return JsonSerializer.SerializeToElement(json);
            }
        }
    }
}
