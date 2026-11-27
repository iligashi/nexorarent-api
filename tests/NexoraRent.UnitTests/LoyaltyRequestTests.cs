using System.Text.Json;
using NexoraRent.Core.Loyalty;

namespace NexoraRent.UnitTests;

public class LoyaltyRequestTests
{
    [Theory]
    [InlineData(0, "bronze")]
    [InlineData(199, "bronze")]
    [InlineData(200, "silver")]
    [InlineData(500, "gold")]
    [InlineData(999, "gold")]
    [InlineData(1000, "platinum")]
    public void Tiers_follow_lifetime_points(long points, string tier) => Assert.Equal(tier, LoyaltyTiers.ForLifetimePoints(points));

    [Theory]
    [InlineData(150, 150, 400, 550, 550, "gold")]
    [InlineData(100, 550, -1000, 0, 550, "gold")]
    [InlineData(null, null, 5, 5, 5, "bronze")]
    [InlineData(null, null, -5, 0, null, "bronze")]
    public void Points_follow_the_Express_arithmetic(int? balance, int? lifetime, int points, int newBalance, int? newLifetime, string tier) =>
        Assert.Equal(new PointsTotals(newBalance, newLifetime, tier), LoyaltyPoints.Apply(balance, lifetime, points));

    [Theory]
    [InlineData(int.MaxValue, 0, 1)]
    [InlineData(0, int.MaxValue, 1)]
    public void Totals_past_the_INT_columns_are_refused(int balance, int lifetime, int points) =>
        Assert.Null(LoyaltyPoints.Apply(balance, lifetime, points));

    [Fact]
    public void Taking_points_from_a_full_account_still_works() =>
        Assert.Equal(new PointsTotals(int.MaxValue - 1, int.MaxValue, "platinum"), LoyaltyPoints.Apply(int.MaxValue, int.MaxValue, -1));

    [Theory]
    [InlineData("""{"points":40,"description":"Bonus"}""", 40, "Bonus")]
    [InlineData("""{"points":"-25","description":""}""", -25, LoyaltyRequest.DefaultAdjustmentDescription)]
    [InlineData("""{"points":5}""", 5, LoyaltyRequest.DefaultAdjustmentDescription)]
    public void An_adjustment_reads_points_and_a_default_description(string body, int points, string description) =>
        Assert.Equal((new PointsAdjustment(points, description), (string?)null), LoyaltyRequest.ReadAdjustment(Parse(body)));

    [Theory]
    [InlineData("{}", LoyaltyRequest.PointsRequiredMessage)]
    [InlineData("""{"points":0}""", LoyaltyRequest.PointsRequiredMessage)]
    [InlineData("""{"points":"0"}""", LoyaltyRequest.PointsRequiredMessage)]
    [InlineData("""{"points":2.5}""", "points must be a whole number")]
    [InlineData("""{"points":"lots"}""", "points must be a whole number")]
    [InlineData("""{"points":5000000}""", "points must be from -1000000 to 1000000")]
    [InlineData("""{"points":5,"description":7}""", "description must be text")]
    public void Bad_adjustments_are_rejected(string body, string message)
    {
        var (adjustment, error) = LoyaltyRequest.ReadAdjustment(Parse(body));

        Assert.Null(adjustment);
        Assert.StartsWith(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_reward_gets_the_Express_defaults()
    {
        var (reward, error) = LoyaltyRequest.ReadNewReward(Parse("""{"name":"Free day","type":"free_day","value":"1","points_cost":"900","description":"","min_tier":""}"""));

        Assert.Null(error);
        Assert.Equal(new NewLoyaltyReward("Free day", null, "free_day", 1m, 900, LoyaltyTiers.Bronze), reward);
    }

    [Theory]
    [InlineData("{}", LoyaltyRequest.RewardRequiredMessage)]
    [InlineData("""{"name":"x","type":"upgrade","value":0,"points_cost":1}""", LoyaltyRequest.RewardRequiredMessage)]
    [InlineData("""{"name":"x","type":"cashback","value":1,"points_cost":1}""", "type must be one of")]
    [InlineData("""{"name":"x","type":"upgrade","value":-1,"points_cost":1}""", "value must be a number above 0")]
    [InlineData("""{"name":"x","type":"upgrade","value":1,"points_cost":-5}""", "points_cost must be from 1")]
    [InlineData("""{"name":"x","type":"upgrade","value":1,"points_cost":1,"min_tier":"diamond"}""", "min_tier must be one of")]
    public void Bad_rewards_are_rejected(string body, string message)
    {
        var (reward, error) = LoyaltyRequest.ReadNewReward(Parse(body));

        Assert.Null(reward);
        Assert.StartsWith(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Reward_changes_keep_an_empty_description_and_clear_a_null_one()
    {
        var (empty, _) = LoyaltyRequest.ReadRewardChanges(Parse("""{"description":"","is_active":false}"""));
        var (cleared, _) = LoyaltyRequest.ReadRewardChanges(Parse("""{"description":null}"""));

        Assert.Equal(new LoyaltyRewardChanges { Description = "", IsActive = false }, empty);
        Assert.True(cleared!.Description.IsSet);
        Assert.Null(cleared.Description.Value);
    }

    [Theory]
    [InlineData("{}", LoyaltyRequest.NoFieldsMessage)]
    [InlineData("""{"name":null}""", "name must be text")]
    [InlineData("""{"value":null}""", "value must be a number")]
    [InlineData("""{"is_active":"yes"}""", "is_active must be true or false")]
    public void Bad_reward_changes_are_rejected(string body, string message)
    {
        var (changes, error) = LoyaltyRequest.ReadRewardChanges(Parse(body));

        Assert.Null(changes);
        Assert.StartsWith(message, error, StringComparison.Ordinal);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
