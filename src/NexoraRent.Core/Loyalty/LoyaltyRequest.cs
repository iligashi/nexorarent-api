using System.Text.Json;

namespace NexoraRent.Core.Loyalty;

/// <summary>
/// Reads the bodies of the <c>/admin/loyalty</c> writes. The required-field messages are Express's;
/// values Express would let MySQL reject are a 400 here (see <see cref="BodyReader"/>).
/// </summary>
public static class LoyaltyRequest
{
    public const string PointsRequiredMessage = "points required";
    public const string RewardRequiredMessage = "name, type, value, points_cost required";
    public const string NoFieldsMessage = "No fields";
    public const string DefaultAdjustmentDescription = "Manual adjustment by admin";

    // Larger changes are almost certainly typos, and would overflow the INT columns long before.
    public const int MaxPointsChange = 1_000_000;

    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 255;
    private const int MaxRewardDescriptionLength = 65_535;
    private const decimal MaxRewardValue = 99_999_999.99m;

    public static (PointsAdjustment? Adjustment, string? Error) ReadAdjustment(JsonElement body)
    {
        var reader = new BodyReader(body);

        // `if (!points)`
        if (!reader.IsTruthy("points"))
        {
            return (null, PointsRequiredMessage);
        }

        var points = reader.WholeNumber("points", -MaxPointsChange, MaxPointsChange);
        if (reader.Error is null && points == 0)
        {
            return (null, PointsRequiredMessage);
        }

        // `description || 'Manual adjustment by admin'`
        var description = reader.OptionalText("description", MaxDescriptionLength) ?? DefaultAdjustmentDescription;

        return reader.Error is null ? (new PointsAdjustment(points, description), null) : (null, reader.Error);
    }

    public static (NewLoyaltyReward? Reward, string? Error) ReadNewReward(JsonElement body)
    {
        var reader = new BodyReader(body);

        // `if (!name || !type || !value || !points_cost)`
        if (!reader.IsTruthy("name") || !reader.IsTruthy("type") || !reader.IsTruthy("value") || !reader.IsTruthy("points_cost"))
        {
            return (null, RewardRequiredMessage);
        }

        var name = reader.RequiredText("name", MaxNameLength);
        var type = reader.OneOf("type", LoyaltyRewardTypes.All);
        var value = reader.PositiveDecimal("value", MaxRewardValue);
        var pointsCost = reader.WholeNumber("points_cost", 1);

        // `description || null`, `min_tier || 'bronze'`
        var description = reader.OptionalText("description", MaxRewardDescriptionLength);
        var minTier = reader.IsTruthy("min_tier") ? reader.OneOf("min_tier", LoyaltyTiers.All) : LoyaltyTiers.Bronze;

        return reader.Error is null
            ? (new NewLoyaltyReward(name!, description, type!, value, pointsCost, minTier!), null)
            : (null, reader.Error);
    }

    public static (LoyaltyRewardChanges? Changes, string? Error) ReadRewardChanges(JsonElement body)
    {
        var reader = new BodyReader(body);
        var changes = new LoyaltyRewardChanges();

        // `if (x !== undefined)`; the description is stored as sent, so "" stays "".
        if (reader.Has("name", out _)) changes = changes with { Name = reader.RequiredText("name", MaxNameLength)! };
        if (reader.Has("description", out _)) changes = changes with { Description = reader.NullableText("description", MaxRewardDescriptionLength) };
        if (reader.Has("type", out _)) changes = changes with { Type = reader.OneOf("type", LoyaltyRewardTypes.All)! };
        if (reader.Has("value", out _)) changes = changes with { Value = reader.PositiveDecimal("value", MaxRewardValue) };
        if (reader.Has("points_cost", out _)) changes = changes with { PointsCost = reader.WholeNumber("points_cost", 1) };
        if (reader.Has("is_active", out _)) changes = changes with { IsActive = reader.Flag("is_active") };
        if (reader.Has("min_tier", out _)) changes = changes with { MinTier = reader.OneOf("min_tier", LoyaltyTiers.All)! };

        if (reader.Error is not null)
        {
            return (null, reader.Error);
        }

        return changes.IsEmpty ? (null, NoFieldsMessage) : (changes, null);
    }
}
