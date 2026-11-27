using System.Text.Json;

namespace NexoraRent.Core.Pricing;

/// <summary>
/// A row of <c>pricing_rules</c>, as <c>SELECT *</c> returns it in
/// <c>server/src/routes/admin.pricing.routes.js</c>.
/// </summary>
public record PricingRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? CarId { get; init; }
    public string? Category { get; init; }
    public decimal Multiplier { get; init; }

    /// <summary>The JSON column as JSON, the way mysql2 returns it from MySQL 8.</summary>
    public JsonElement Conditions { get; init; }

    public int? Priority { get; init; }
    public bool? IsActive { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public DateTime? CreatedAt { get; init; }
}

/// <summary>A rule in the admin list, with the car it is limited to (<c>pr.*, c.brand, c.model</c>).</summary>
public sealed record PricingRuleWithCar : PricingRule
{
    public string? Brand { get; init; }
    public string? Model { get; init; }
}

/// <summary>The rule types of the <c>pricing_rules.type</c> enum.</summary>
public static class PricingRuleTypes
{
    public const string Demand = "demand";
    public const string AdvanceBooking = "advance_booking";
    public const string Duration = "duration";
    public const string LastMinute = "last_minute";
    public const string Event = "event";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Demand, AdvanceBooking, Duration, LastMinute, Event,
    };
}

/// <summary>A validated rule for <c>POST /admin/pricing/rules</c>.</summary>
public sealed record NewPricingRule(
    string Name,
    string Type,
    string? CarId,
    string? Category,
    decimal Multiplier,
    string ConditionsJson,
    int Priority,
    bool IsActive,
    DateOnly? StartDate,
    DateOnly? EndDate);

/// <summary>
/// The fields sent to <c>PUT /admin/pricing/rules/:id</c>. Each field is set only when the key was
/// in the body; <see cref="FieldChange{T}.Value"/> may be null for the nullable columns.
/// </summary>
public sealed record PricingRuleChanges
{
    public FieldChange<string> Name { get; init; }
    public FieldChange<string> Type { get; init; }
    public FieldChange<string?> CarId { get; init; }
    public FieldChange<string?> Category { get; init; }
    public FieldChange<decimal> Multiplier { get; init; }
    public FieldChange<string> ConditionsJson { get; init; }
    public FieldChange<int> Priority { get; init; }
    public FieldChange<bool> IsActive { get; init; }
    public FieldChange<DateOnly?> StartDate { get; init; }
    public FieldChange<DateOnly?> EndDate { get; init; }

    public bool IsEmpty =>
        !Name.IsSet && !Type.IsSet && !CarId.IsSet && !Category.IsSet && !Multiplier.IsSet
        && !ConditionsJson.IsSet && !Priority.IsSet && !IsActive.IsSet && !StartDate.IsSet && !EndDate.IsSet;
}

/// <summary>The outcome of creating or updating a rule.</summary>
public enum PricingRuleWriteStatus
{
    Saved,
    NotFound,
    CarNotFound,
}

public sealed record PricingRuleWrite(PricingRuleWriteStatus Status, PricingRule? Rule = null);

/// <summary>Car counts for one day: cars marked available, and cars booked (confirmed or active) that day.</summary>
public sealed record DemandCounts(int AvailableCars, int BookedCars);

/// <summary>What <see cref="DynamicPricing.Calculate"/> needs from the database.</summary>
public sealed record DynamicPriceInputs(
    decimal PricePerDay,
    decimal? SeasonalMultiplier,
    IReadOnlyList<PricingRule> ActiveRules,
    DemandCounts Demand);

/// <summary>Mirrors <c>server/src/routes/admin.pricing.routes.js</c>.</summary>
public interface IPricingRepository
{
    Task<IReadOnlyList<PricingRuleWithCar>> ListRulesAsync(CancellationToken cancellationToken);

    Task<PricingRuleWrite> CreateRuleAsync(NewPricingRule rule, CancellationToken cancellationToken);

    Task<PricingRuleWrite> UpdateRuleAsync(string id, PricingRuleChanges changes, CancellationToken cancellationToken);

    Task DeleteRuleAsync(string id, CancellationToken cancellationToken);

    /// <param name="day">Compared with the reservation dates as given, like Express does.</param>
    Task<DemandCounts> CountDemandAsync(string day, CancellationToken cancellationToken);

    /// <summary>Null when there is no car with that id.</summary>
    /// <param name="pickupDate">As given in the query string; MySQL compares it with the rule dates.</param>
    /// <param name="dropoffDate">As given in the query string.</param>
    Task<DynamicPriceInputs?> LoadPriceInputsAsync(string carId, string pickupDate, string dropoffDate, CancellationToken cancellationToken);
}
