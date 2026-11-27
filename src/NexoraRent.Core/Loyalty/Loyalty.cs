namespace NexoraRent.Core.Loyalty;

/// <summary>A row of <c>loyalty_accounts</c> (<c>SELECT *</c> in <c>admin.loyalty.routes.js</c>).</summary>
public record LoyaltyAccount
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public int? PointsBalance { get; init; }
    public int? LifetimePoints { get; init; }
    public string? Tier { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>An account with its member (<c>la.*, u.first_name, u.last_name, u.email</c>).</summary>
public sealed record LoyaltyAccountWithUser : LoyaltyAccount
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
}

/// <summary>A row of <c>loyalty_transactions</c> with its reservation number.</summary>
public sealed record LoyaltyTransaction
{
    public required string Id { get; init; }
    public required string AccountId { get; init; }
    public string? ReservationId { get; init; }
    public required string Type { get; init; }
    public int Points { get; init; }
    public string? Description { get; init; }
    public DateTime? CreatedAt { get; init; }
    public string? ReservationNo { get; init; }
}

/// <summary>A row of <c>loyalty_rewards</c>.</summary>
public sealed record LoyaltyReward
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Type { get; init; }
    public decimal Value { get; init; }
    public int PointsCost { get; init; }
    public bool? IsActive { get; init; }
    public string? MinTier { get; init; }
}

public sealed record LoyaltyAccountDetail(LoyaltyAccountWithUser Account, IReadOnlyList<LoyaltyTransaction> Transactions);

/// <summary>A manual change to an account's points, logged as an <c>adjust</c> transaction.</summary>
public sealed record PointsAdjustment(int Points, string Description);

/// <summary>An account's points after a change.</summary>
public sealed record PointsTotals(int Balance, int? Lifetime, string Tier);

public enum PointsAdjustmentStatus
{
    Saved,
    NotFound,

    /// <summary>The new balance or lifetime total would not fit the INT columns.</summary>
    TooLarge,
}

public sealed record PointsAdjustmentResult(PointsAdjustmentStatus Status, LoyaltyAccount? Account = null);

/// <summary>The arithmetic of <c>PUT /accounts/:id/adjust</c>.</summary>
public static class LoyaltyPoints
{
    /// <summary>
    /// <c>Math.max(0, balance + points)</c>; lifetime points only grow, and set the tier. NULL counts
    /// as 0, as in JS, but a NULL lifetime stays NULL when nothing is added. Null when a total would
    /// not fit an INT column.
    /// </summary>
    public static PointsTotals? Apply(int? balance, int? lifetime, int points)
    {
        var newBalance = Math.Max(0L, (long)(balance ?? 0) + points);
        long? newLifetime = points > 0 ? (long)(lifetime ?? 0) + points : lifetime;
        if (newBalance > int.MaxValue || newLifetime > int.MaxValue)
        {
            return null;
        }

        return new PointsTotals((int)newBalance, (int?)newLifetime, LoyaltyTiers.ForLifetimePoints(newLifetime ?? 0));
    }
}

public sealed record NewLoyaltyReward(string Name, string? Description, string Type, decimal Value, int PointsCost, string MinTier);

/// <summary>The fields sent to <c>PUT /admin/loyalty/rewards/:id</c>; each is set only when its key was sent.</summary>
public sealed record LoyaltyRewardChanges
{
    public FieldChange<string> Name { get; init; }
    public FieldChange<string?> Description { get; init; }
    public FieldChange<string> Type { get; init; }
    public FieldChange<decimal> Value { get; init; }
    public FieldChange<int> PointsCost { get; init; }
    public FieldChange<bool> IsActive { get; init; }
    public FieldChange<string> MinTier { get; init; }

    public bool IsEmpty =>
        !Name.IsSet && !Description.IsSet && !Type.IsSet && !Value.IsSet && !PointsCost.IsSet && !IsActive.IsSet && !MinTier.IsSet;
}

/// <summary>Tiers and how many lifetime points each needs, as in Express's <c>TIER_THRESHOLDS</c>.</summary>
public static class LoyaltyTiers
{
    public const string Bronze = "bronze";
    public const string Silver = "silver";
    public const string Gold = "gold";
    public const string Platinum = "platinum";

    public const int SilverPoints = 200;
    public const int GoldPoints = 500;
    public const int PlatinumPoints = 1000;

    public static readonly IReadOnlyList<string> All = [Bronze, Silver, Gold, Platinum];

    /// <summary><c>getTier(lifetimePoints)</c>.</summary>
    public static string ForLifetimePoints(long lifetimePoints) => lifetimePoints switch
    {
        >= PlatinumPoints => Platinum,
        >= GoldPoints => Gold,
        >= SilverPoints => Silver,
        _ => Bronze,
    };
}

/// <summary>The reward types of the <c>loyalty_rewards.type</c> enum.</summary>
public static class LoyaltyRewardTypes
{
    public static readonly IReadOnlyList<string> All = ["discount_percent", "discount_fixed", "free_day", "upgrade", "free_extra"];
}

/// <summary>Mirrors <c>server/src/routes/admin.loyalty.routes.js</c>.</summary>
public interface ILoyaltyRepository
{
    Task<IReadOnlyList<LoyaltyAccountWithUser>> ListAccountsAsync(CancellationToken cancellationToken);

    /// <summary>The account and its latest 50 transactions; null when there is no such account.</summary>
    Task<LoyaltyAccountDetail?> GetAccountAsync(string id, CancellationToken cancellationToken);

    /// <summary>Applies and logs the change in one transaction.</summary>
    Task<PointsAdjustmentResult> AdjustPointsAsync(string id, PointsAdjustment adjustment, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoyaltyReward>> ListRewardsAsync(CancellationToken cancellationToken);

    Task<LoyaltyReward> CreateRewardAsync(NewLoyaltyReward reward, CancellationToken cancellationToken);

    /// <summary>Null when there is no such reward.</summary>
    Task<LoyaltyReward?> UpdateRewardAsync(string id, LoyaltyRewardChanges changes, CancellationToken cancellationToken);

    Task DeleteRewardAsync(string id, CancellationToken cancellationToken);
}
