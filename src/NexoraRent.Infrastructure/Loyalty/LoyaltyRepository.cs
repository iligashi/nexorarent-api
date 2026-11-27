using Dapper;
using MySqlConnector;
using NexoraRent.Core;
using NexoraRent.Core.Loyalty;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Loyalty;

/// <summary>Mirrors <c>server/src/routes/admin.loyalty.routes.js</c> in the Express API.</summary>
public sealed class LoyaltyRepository(MySqlConnectionFactory connections) : ILoyaltyRepository
{
    private const string AccountColumns = "la.id, la.user_id, la.points_balance, la.lifetime_points, la.tier, la.created_at, la.updated_at";
    private const string AccountWithUserColumns = AccountColumns + ", u.first_name, u.last_name, u.email";
    private const string RewardColumns = "id, name, description, type, value, points_cost, is_active, min_tier";
    private const int TransactionLimit = 50;

    public async Task<IReadOnlyList<LoyaltyAccountWithUser>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LoyaltyAccountWithUser>(new CommandDefinition(
            $"""
            SELECT {AccountWithUserColumns}
            FROM loyalty_accounts la
            JOIN users u ON u.id = la.user_id
            ORDER BY la.lifetime_points DESC
            """,
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<LoyaltyAccountDetail?> GetAccountAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var account = await connection.QuerySingleOrDefaultAsync<LoyaltyAccountWithUser>(new CommandDefinition(
            $"SELECT {AccountWithUserColumns} FROM loyalty_accounts la JOIN users u ON u.id = la.user_id WHERE la.id = @id",
            new { id },
            cancellationToken: cancellationToken));
        if (account is null)
        {
            return null;
        }

        var transactions = await connection.QueryAsync<LoyaltyTransaction>(new CommandDefinition(
            $"""
            SELECT lt.id, lt.account_id, lt.reservation_id, lt.type, lt.points, lt.description, lt.created_at, r.reservation_no
            FROM loyalty_transactions lt
            LEFT JOIN reservations r ON r.id = lt.reservation_id
            WHERE lt.account_id = @id
            ORDER BY lt.created_at DESC
            LIMIT {TransactionLimit}
            """,
            new { id },
            cancellationToken: cancellationToken));

        return new LoyaltyAccountDetail(account, [.. transactions]);
    }

    public async Task<PointsAdjustmentResult> AdjustPointsAsync(string id, PointsAdjustment adjustment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Express reads, adds and writes back in separate statements, so two adjustments at once
        // could lose one. The row lock makes them run one after the other.
        var current = await connection.QuerySingleOrDefaultAsync<PointsRow>(new CommandDefinition(
            "SELECT points_balance AS Balance, lifetime_points AS Lifetime FROM loyalty_accounts WHERE id = @id FOR UPDATE",
            new { id },
            transaction,
            cancellationToken: cancellationToken));
        if (current is null)
        {
            return new PointsAdjustmentResult(PointsAdjustmentStatus.NotFound);
        }

        // Leaving without a commit rolls the transaction back and releases the lock.
        if (LoyaltyPoints.Apply(current.Balance, current.Lifetime, adjustment.Points) is not { } totals)
        {
            return new PointsAdjustmentResult(PointsAdjustmentStatus.TooLarge);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE loyalty_accounts SET points_balance = @Balance, lifetime_points = @Lifetime, tier = @Tier WHERE id = @id",
            new { totals.Balance, totals.Lifetime, totals.Tier, id },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO loyalty_transactions (id, account_id, type, points, description) VALUES (@transactionId, @id, 'adjust', @Points, @Description)",
            new { transactionId = Guid.NewGuid().ToString(), id, adjustment.Points, adjustment.Description },
            transaction,
            cancellationToken: cancellationToken));

        var account = await connection.QuerySingleAsync<LoyaltyAccount>(new CommandDefinition(
            $"SELECT {AccountColumns} FROM loyalty_accounts la WHERE la.id = @id",
            new { id },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new PointsAdjustmentResult(PointsAdjustmentStatus.Saved, account);
    }

    public async Task<IReadOnlyList<LoyaltyReward>> ListRewardsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LoyaltyReward>(new CommandDefinition(
            $"SELECT {RewardColumns} FROM loyalty_rewards ORDER BY points_cost", cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<LoyaltyReward> CreateRewardAsync(NewLoyaltyReward reward, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reward);
        var id = Guid.NewGuid().ToString();

        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO loyalty_rewards (id, name, description, type, value, points_cost, min_tier)
            VALUES (@id, @Name, @Description, @Type, @Value, @PointsCost, @MinTier)
            """,
            new { id, reward.Name, reward.Description, reward.Type, reward.Value, reward.PointsCost, reward.MinTier },
            cancellationToken: cancellationToken));

        return (await GetRewardAsync(connection, id, cancellationToken))!;
    }

    public async Task<LoyaltyReward?> UpdateRewardAsync(string id, LoyaltyRewardChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Each column comes from a fixed list; only the values are parameters.
        var sets = new List<string>();
        var parameters = new DynamicParameters(new { id });
        void Set<T>(FieldChange<T> field, string column)
        {
            if (field.IsSet)
            {
                sets.Add($"{column} = @{column}");
                parameters.Add(column, field.Value);
            }
        }

        Set(changes.Name, "name");
        Set(changes.Description, "description");
        Set(changes.Type, "type");
        Set(changes.Value, "value");
        Set(changes.PointsCost, "points_cost");
        Set(changes.IsActive, "is_active");
        Set(changes.MinTier, "min_tier");

        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE loyalty_rewards SET {string.Join(", ", sets)} WHERE id = @id", parameters, cancellationToken: cancellationToken));

        // Read back instead of trusting the affected-row count, which is 0 when nothing changed.
        return await GetRewardAsync(connection, id, cancellationToken);
    }

    public async Task DeleteRewardAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM loyalty_rewards WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    private static Task<LoyaltyReward?> GetRewardAsync(MySqlConnection connection, string id, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<LoyaltyReward>(new CommandDefinition(
            $"SELECT {RewardColumns} FROM loyalty_rewards WHERE id = @id", new { id }, cancellationToken: cancellationToken));

    private sealed record PointsRow
    {
        public int? Balance { get; init; }
        public int? Lifetime { get; init; }
    }
}
