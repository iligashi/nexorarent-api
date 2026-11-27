using System.Text.Json;
using NexoraRent.Core.Pricing;

namespace NexoraRent.UnitTests;

/// <summary>
/// Expected numbers come from running <c>calculateDynamicPrice</c>'s arithmetic in Node with the
/// same inputs, so the preview matches the Express one to the cent.
/// </summary>
public class DynamicPricingTests
{
    private static readonly DateTimeOffset Now = new(2031, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_highest_priority_rule_that_applies_wins_on_top_of_the_seasonal_price()
    {
        var rules = new[]
        {
            Rule("Surge", PricingRuleTypes.Demand, 1.25m, """{"min_demand_percent":80}"""),
            Rule("Weekly", PricingRuleTypes.Duration, 0.9m, """{"min_days":7}"""),
        };

        var price = Calculate(45.5m, 1.15m, rules, new DemandCounts(10, 9), "2031-06-01", "2031-06-08");

        Assert.Equal(1.15, price.SeasonalMultiplier);
        Assert.Equal(1.25, price.DynamicMultiplier);
        Assert.Equal(65.41, price.FinalDailyRate);
        Assert.Equal(7, price.TotalDays);
        Assert.Equal(457.84, price.TotalPrice);
        Assert.Equal(90, price.DemandPercent);
        Assert.Equal([new AppliedPricingRule("Surge", PricingRuleTypes.Demand, 1.25)], price.AppliedRules);
    }

    [Fact]
    public void Part_days_round_up_and_money_rounds_like_Math_round()
    {
        var rules = new[] { Rule("Long", PricingRuleTypes.Duration, 0.9m, """{"min_days":2}""") };

        var price = Calculate(33.33m, null, rules, new DemandCounts(0, 0), "2031-06-01T10:00:00Z", "2031-06-03T09:00:00Z");

        Assert.Equal(2, price.TotalDays);
        Assert.Equal(30, price.FinalDailyRate);
        Assert.Equal(59.99, price.TotalPrice);
        Assert.Equal(1.0, price.SeasonalMultiplier);
    }

    [Fact]
    public void A_dropoff_before_pickup_is_one_day()
    {
        var price = Calculate(20m, null, [], new DemandCounts(3, 0), "2031-06-05", "2031-06-01");

        Assert.Equal(1, price.TotalDays);
        Assert.Equal(20, price.TotalPrice);
        Assert.Empty(price.AppliedRules);
    }

    [Theory]
    [InlineData(PricingRuleTypes.LastMinute, """{"days_before_pickup_max":31}""", true)]
    [InlineData(PricingRuleTypes.LastMinute, """{"days_before_pickup_max":30}""", false)]
    [InlineData(PricingRuleTypes.AdvanceBooking, """{"days_before_pickup_min":31}""", true)]
    [InlineData(PricingRuleTypes.Demand, """{"min_demand_percent":"67"}""", true)]
    [InlineData(PricingRuleTypes.Demand, """{"min_demand_percent":0}""", false)]
    [InlineData(PricingRuleTypes.Demand, """{"min_demand_percent":"many"}""", false)]
    [InlineData(PricingRuleTypes.Demand, """{}""", false)]
    [InlineData(PricingRuleTypes.Duration, """{"min_days":4}""", true)]
    [InlineData(PricingRuleTypes.Event, """{"min_days":1}""", false)]
    [InlineData(PricingRuleTypes.Duration, "\"{\\\"min_days\\\":1}\"", false)]
    public void Each_rule_type_reads_its_own_condition(string type, string conditions, bool applies)
    {
        // Pickup is 30.5 days after Now, so 31 days before pickup; 2 of 3 cars booked is 67%.
        var price = Calculate(10m, null, [Rule("Rule", type, 2m, conditions)], new DemandCounts(3, 2), "2031-06-01", "2031-06-05");

        Assert.Equal(applies ? 2.0 : 1.0, price.DynamicMultiplier);
    }

    [Theory]
    [InlineData("2031-06-01", 2031, 6, 1, 0)]
    [InlineData("2031-06-01T10:30:00", 2031, 6, 1, 10)]
    [InlineData("2031-06-01T10:30:00+02:00", 2031, 6, 1, 8)]
    public void Dates_without_an_offset_are_UTC(string value, int year, int month, int day, int hour)
    {
        Assert.True(DynamicPricing.TryParseDate(value, out var parsed));
        Assert.Equal(new DateTimeOffset(year, month, day, hour, parsed.Minute, 0, TimeSpan.Zero), parsed);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("")]
    public void Unreadable_dates_are_rejected(string value) => Assert.False(DynamicPricing.TryParseDate(value, out _));

    private static DynamicPrice Calculate(
        decimal pricePerDay, decimal? seasonal, IReadOnlyList<PricingRule> rules, DemandCounts demand, string pickup, string dropoff)
    {
        Assert.True(DynamicPricing.TryParseDate(pickup, out var from));
        Assert.True(DynamicPricing.TryParseDate(dropoff, out var to));
        return DynamicPricing.Calculate(new DynamicPriceInputs(pricePerDay, seasonal, rules, demand), from, to, Now);
    }

    private static PricingRule Rule(string name, string type, decimal multiplier, string conditions) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Type = type,
        Multiplier = multiplier,
        Conditions = JsonDocument.Parse(conditions).RootElement.Clone(),
    };
}
