using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Compares <c>/admin/loyalty</c> on Express and .NET. The parity account (fixtures.sql) is put
/// back after each adjustment; rewards made here are named <c>Parity reward …</c> and removed.
/// </summary>
[Collection(ParityEnvironment.SharedDatabase)]
public class AdminLoyaltyParityTests
{
    private const string AccountId = "dddddddd-0000-4000-8000-000000000101";
    private const string RewardPrefix = "Parity reward";

    /// <summary>Each API writes its own row or timestamp.</summary>
    private static readonly HashSet<string> WrittenKeys = new(StringComparer.Ordinal) { "id", "updated_at" };

    [Fact]
    public async Task Access_rules_match()
    {
        var customer = await AdminParity.TokenForRoleAsync("customer");
        var staff = await AdminParity.TokenForRoleAsync("staff");

        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/loyalty/accounts", null, token: null);
        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/loyalty/accounts", null, customer);
        await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{AccountId}/adjust", """{"points":5}""", staff);
        await AdminParity.AssertSameAsync(HttpMethod.Post, "/api/v1/admin/loyalty/rewards", "{}", staff);
        await AdminParity.AssertSameAsync(HttpMethod.Delete, $"/api/v1/admin/loyalty/rewards/{Guid.NewGuid()}", null, staff);
    }

    [Fact]
    public async Task Reads_match()
    {
        var staff = await AdminParity.TokenForRoleAsync("staff");

        foreach (var path in new[]
        {
            "/api/v1/admin/loyalty/accounts",
            $"/api/v1/admin/loyalty/accounts/{AccountId}",
            $"/api/v1/admin/loyalty/accounts/{Guid.NewGuid()}",
            "/api/v1/admin/loyalty/rewards",
            "/api/v1/admin/loyalty/config",
        })
        {
            await AdminParity.AssertSameAsync(HttpMethod.Get, path, null, staff, ignoreArrayOrder: path.EndsWith("/rewards", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Points_are_adjusted_alike()
    {
        var owner = await AdminParity.TokenForRoleAsync("owner");
        var before = await AccountStateAsync();
        try
        {
            foreach (var body in new[]
            {
                """{"points":40,"description":"Parity bonus"}""",
                """{"points":-500}""",
                """{"points":"25"}""",
                """{"points":2000,"description":"Parity platinum"}""",
                """{"points":0}""",
                "{}",
            })
            {
                await AdminParity.AssertSameAsync(
                    HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{AccountId}/adjust", body, owner,
                    afterExpress: () => RestoreAccountAsync(before), ignored: WrittenKeys);
                await RestoreAccountAsync(before);
            }

            await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/accounts/{Guid.NewGuid()}/adjust", """{"points":5}""", owner);
        }
        finally
        {
            await RestoreAccountAsync(before);
        }
    }

    [Fact]
    public async Task Rewards_are_created_changed_and_deleted_alike()
    {
        var owner = await AdminParity.TokenForRoleAsync("owner");
        try
        {
            foreach (var body in new[]
            {
                $$"""{"name":"{{RewardPrefix}} discount","type":"discount_percent","value":10,"points_cost":300,"description":"Ten off"}""",
                $$"""{"name":"{{RewardPrefix}} upgrade","type":"upgrade","value":"1","points_cost":"450","min_tier":"silver","description":""}""",
                """{"name":"x","type":"upgrade","value":0,"points_cost":1}""",
                """{"name":"x","type":"upgrade","value":1}""",
            })
            {
                await AdminParity.AssertSameAsync(HttpMethod.Post, "/api/v1/admin/loyalty/rewards", body, owner, ignored: WrittenKeys);
            }

            var (expressReward, dotnetReward) = await RewardPairAsync($"{RewardPrefix} discount");
            foreach (var body in new[] { """{"value":12.5,"points_cost":320}""", """{"is_active":false,"min_tier":"gold","description":""}""", """{"description":null,"name":"Parity reward discount"}""" })
            {
                var express = await AdminParity.SendAsync(ParityEnvironment.ExpressUrl!, HttpMethod.Put, $"/api/v1/admin/loyalty/rewards/{expressReward}", body, owner);
                var dotnet = await AdminParity.SendAsync(ParityEnvironment.DotnetUrl!, HttpMethod.Put, $"/api/v1/admin/loyalty/rewards/{dotnetReward}", body, owner);
                Assert.Equal(express.Status, dotnet.Status);
                var differences = JsonParity.Compare(express.Body, dotnet.Body, ignoredKeys: WrittenKeys);
                Assert.True(differences.Count == 0, $"PUT {body}: {string.Join("; ", differences)}");
            }

            await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/loyalty/rewards/{expressReward}", "{}", owner);
            await AdminParity.AssertSameAsync(HttpMethod.Delete, $"/api/v1/admin/loyalty/rewards/{Guid.NewGuid()}", null, owner);
        }
        finally
        {
            await using var db = await ParityEnvironment.OpenDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM loyalty_rewards WHERE name LIKE @prefix", new { prefix = RewardPrefix + "%" });
        }
    }

    private static async Task<AccountState> AccountStateAsync()
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        return await db.QuerySingleAsync<AccountState>(
            "SELECT points_balance AS Balance, lifetime_points AS Lifetime, tier AS Tier FROM loyalty_accounts WHERE id = @AccountId",
            new { AccountId });
    }

    /// <summary>Puts the points back and removes the adjustments the test made (the fixture rows have older dates).</summary>
    private static async Task RestoreAccountAsync(AccountState state)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync(
            "UPDATE loyalty_accounts SET points_balance = @Balance, lifetime_points = @Lifetime, tier = @Tier WHERE id = @AccountId",
            new { state.Balance, state.Lifetime, state.Tier, AccountId });
        await db.ExecuteAsync(
            "DELETE FROM loyalty_transactions WHERE account_id = @AccountId AND id NOT LIKE 'dddddddd-%'", new { AccountId });
    }

    private static async Task<(string First, string Second)> RewardPairAsync(string name)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var ids = (await db.QueryAsync<string>("SELECT id FROM loyalty_rewards WHERE name = @name ORDER BY id", new { name })).ToList();
        Assert.Equal(2, ids.Count);
        return (ids[0], ids[1]);
    }

    private sealed record AccountState
    {
        public int? Balance { get; init; }
        public int? Lifetime { get; init; }
        public string? Tier { get; init; }
    }
}
