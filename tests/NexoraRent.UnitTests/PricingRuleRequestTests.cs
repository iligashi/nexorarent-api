using System.Text.Json;
using NexoraRent.Core.Pricing;

namespace NexoraRent.UnitTests;

/// <summary>
/// The 400 messages Express sends are kept; values Express would let MySQL reject (a 500 there)
/// are a 400 here.
/// </summary>
public class PricingRuleRequestTests
{
    [Fact]
    public void A_new_rule_gets_the_Express_defaults()
    {
        var (rule, error) = PricingRuleRequest.ReadNew(Parse("""
            {"name":"Surge","type":"demand","multiplier":1.255,"conditions":{"min_demand_percent":80},"car_id":"","category":"","start_date":"","is_active":"no"}
            """));

        Assert.Null(error);
        Assert.Equal(
            new NewPricingRule("Surge", "demand", null, null, 1.26m, """{"min_demand_percent":80}""", 0, true, null, null),
            rule);
    }

    [Fact]
    public void A_new_rule_keeps_its_optional_values()
    {
        var (rule, error) = PricingRuleRequest.ReadNew(Parse("""
            {"name":"Car","type":"duration","multiplier":"0.9","conditions":{"min_days":7},"car_id":"c1","category":"suv","priority":4,"is_active":false,"start_date":"2031-01-01","end_date":"2031-12-31"}
            """));

        Assert.Null(error);
        Assert.Equal(
            new NewPricingRule("Car", "duration", "c1", "suv", 0.9m, """{"min_days":7}""", 4, false, new DateOnly(2031, 1, 1), new DateOnly(2031, 12, 31)),
            rule);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"name":"x","type":"demand","multiplier":0,"conditions":{}}""")]
    [InlineData("""{"name":"","type":"demand","multiplier":1,"conditions":{}}""")]
    [InlineData("""{"name":"x","type":"demand","multiplier":1}""")]
    public void Missing_or_falsy_required_fields_get_the_Express_message(string body)
    {
        var (rule, error) = PricingRuleRequest.ReadNew(Parse(body));

        Assert.Null(rule);
        Assert.Equal(PricingRuleRequest.RequiredMessage, error);
    }

    [Theory]
    [InlineData("""{"name":"x","type":"surge","multiplier":1,"conditions":{}}""", "type must be one of")]
    [InlineData("""{"name":"x","type":"demand","multiplier":120,"conditions":{}}""", "multiplier must be")]
    [InlineData("""{"name":"x","type":"demand","multiplier":-1,"conditions":{}}""", "multiplier must be")]
    [InlineData("""{"name":"x","type":"demand","multiplier":"lots","conditions":{}}""", "multiplier must be")]
    [InlineData("""{"name":"x","type":"demand","multiplier":1,"conditions":"{\"min_days\":1}"}""", "conditions must be an object")]
    [InlineData("""{"name":"x","type":"demand","multiplier":1,"conditions":{},"priority":1.5}""", "priority must be a whole number")]
    [InlineData("""{"name":"x","type":"demand","multiplier":1,"conditions":{},"start_date":"01/02/2031"}""", "start_date must be a date")]
    [InlineData("""{"name":7,"type":"demand","multiplier":1,"conditions":{}}""", "name must be text")]
    public void Values_MySQL_would_reject_are_a_400(string body, string message)
    {
        var (rule, error) = PricingRuleRequest.ReadNew(Parse(body));

        Assert.Null(rule);
        Assert.StartsWith(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_include_only_the_keys_that_were_sent_and_null_clears_a_nullable_field()
    {
        var (changes, error) = PricingRuleRequest.ReadChanges(Parse("""{"multiplier":1.5,"category":null,"end_date":"","is_active":false}"""));

        Assert.Null(error);
        Assert.Equal(new PricingRuleChanges
        {
            Multiplier = 1.5m,
            Category = (string?)null,
            EndDate = (DateOnly?)null,
            IsActive = false,
        }, changes);
        Assert.False(changes!.Name.IsSet);
        Assert.True(changes.Category.IsSet);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"unknown":1}""")]
    public void Changes_without_known_fields_get_the_Express_message(string body)
    {
        var (changes, error) = PricingRuleRequest.ReadChanges(Parse(body));

        Assert.Null(changes);
        Assert.Equal(PricingRuleRequest.NoFieldsMessage, error);
    }

    [Theory]
    [InlineData("""{"name":null}""", "name must be text")]
    [InlineData("""{"multiplier":null}""", "multiplier must be")]
    [InlineData("""{"is_active":1}""", "is_active must be true or false")]
    public void Changes_cannot_null_a_required_field(string body, string message)
    {
        var (changes, error) = PricingRuleRequest.ReadChanges(Parse(body));

        Assert.Null(changes);
        Assert.StartsWith(message, error, StringComparison.Ordinal);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
