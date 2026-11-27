using System.Globalization;
using System.Text.Json;

namespace NexoraRent.Core.Pricing;

/// <summary>The body of <c>GET /admin/pricing/calculate</c>.</summary>
public sealed record DynamicPrice(
    double DailyRate,
    double SeasonalMultiplier,
    double DynamicMultiplier,
    double FinalDailyRate,
    long TotalDays,
    double TotalPrice,
    long DemandPercent,
    IReadOnlyList<AppliedPricingRule> AppliedRules);

public sealed record AppliedPricingRule(string Name, string Type, double Multiplier);

/// <summary>
/// Port of <c>calculateDynamicPrice</c> in <c>server/src/routes/admin.pricing.routes.js</c>.
/// <para>
/// It is an admin preview only: bookings are priced by <c>ReservationPricing</c>, which applies the
/// seasonal multiplier but not these rules. The arithmetic is done in <see cref="double"/> on
/// purpose, exactly like the JavaScript it replaces, so both APIs show the same preview. The
/// pricing engine in the SaaS blueprint replaces it with decimal money and stored snapshots.
/// </para>
/// </summary>
public static class DynamicPricing
{
    private const double MillisecondsPerDay = 1000d * 60 * 60 * 24;

    public static DynamicPrice Calculate(DynamicPriceInputs inputs, DateTimeOffset pickup, DateTimeOffset dropoff, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var dailyRate = (double)inputs.PricePerDay;
        var totalDays = (long)Math.Max(1, Math.Ceiling((dropoff - pickup).TotalMilliseconds / MillisecondsPerDay));
        var seasonalMultiplier = inputs.SeasonalMultiplier is { } seasonal ? (double)seasonal : 1.0;
        var demandPercent = JsRound(inputs.Demand.BookedCars / (double)Math.Max(1, inputs.Demand.AvailableCars) * 100);
        var daysBeforePickup = Math.Ceiling((pickup - now).TotalMilliseconds / MillisecondsPerDay);

        var dynamicMultiplier = 1.0;
        var appliedRules = new List<AppliedPricingRule>();

        // Rules come ordered by priority, highest first; the first one that applies wins.
        foreach (var rule in inputs.ActiveRules)
        {
            if (Applies(rule, demandPercent, daysBeforePickup, totalDays))
            {
                dynamicMultiplier = (double)rule.Multiplier;
                appliedRules.Add(new AppliedPricingRule(rule.Name, rule.Type, dynamicMultiplier));
                break;
            }
        }

        var finalRate = dailyRate * seasonalMultiplier * dynamicMultiplier;
        return new DynamicPrice(
            dailyRate,
            seasonalMultiplier,
            dynamicMultiplier,
            JsRound(finalRate * 100) / 100d,
            totalDays,
            JsRound(finalRate * totalDays * 100) / 100d,
            (long)demandPercent,
            appliedRules);
    }

    /// <summary>
    /// <c>Date.parse</c> for the formats the admin page sends: a date (<c>2026-12-01</c>, midnight
    /// UTC) or an ISO date-time. A date-time without an offset is read as UTC, which is what Node
    /// does on a server running with <c>TZ=UTC</c>.
    /// </summary>
    public static bool TryParseDate(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);

    private static bool Applies(PricingRule rule, double demandPercent, double daysBeforePickup, long totalDays)
    {
        if (rule.Conditions.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // `cond.x && value >= cond.x`: a missing, zero or empty condition never applies.
        bool Check(string key, Func<double, bool> test) =>
            rule.Conditions.TryGetProperty(key, out var condition) && IsTruthy(condition) && test(ToNumber(condition));

        return rule.Type switch
        {
            PricingRuleTypes.Demand => Check("min_demand_percent", min => demandPercent >= min),
            PricingRuleTypes.LastMinute => Check("days_before_pickup_max", max => daysBeforePickup <= max),
            PricingRuleTypes.AdvanceBooking => Check("days_before_pickup_min", min => daysBeforePickup >= min),
            PricingRuleTypes.Duration => Check("min_days", min => totalDays >= min),
            _ => false,
        };
    }

    /// <summary>JavaScript truthiness of a JSON value.</summary>
    private static bool IsTruthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble() is var number && number != 0 && !double.IsNaN(number),
        JsonValueKind.String => value.GetString()!.Length > 0,
        JsonValueKind.True or JsonValueKind.Object or JsonValueKind.Array => true,
        _ => false,
    };

    /// <summary>The number JavaScript's <c>&gt;=</c> compares with; NaN makes every comparison false.</summary>
    private static double ToNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => 1,
        JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN,
        _ => double.NaN,
    };

    /// <summary><c>Math.round</c>: halves round up, towards positive infinity.</summary>
    private static double JsRound(double value) => Math.Floor(value + 0.5);
}
