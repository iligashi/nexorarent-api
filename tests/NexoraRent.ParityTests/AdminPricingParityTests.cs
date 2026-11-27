using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Compares <c>/admin/pricing</c> on Express and .NET. Rules made here are named
/// <c>Parity pricing …</c> and removed afterwards; the seeded rules are only read.
/// </summary>
[Collection(ParityEnvironment.SharedDatabase)]
public class AdminPricingParityTests
{
    private const string NamePrefix = "Parity pricing";

    /// <summary>Each API creates its own row, so the id and creation time differ.</summary>
    private static readonly HashSet<string> NewRowKeys = new(StringComparer.Ordinal) { "id", "created_at" };

    [Fact]
    public async Task Access_rules_match()
    {
        var customer = await AdminParity.TokenForRoleAsync("customer");
        var staff = await AdminParity.TokenForRoleAsync("staff");

        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/pricing/rules", null, token: null);
        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/pricing/rules", null, customer);
        await AdminParity.AssertSameAsync(HttpMethod.Post, "/api/v1/admin/pricing/rules", "{}", staff);
        await AdminParity.AssertSameAsync(HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{Guid.NewGuid()}", null, staff);
    }

    [Fact]
    public async Task Reads_match()
    {
        var staff = await AdminParity.TokenForRoleAsync("staff");
        var car = await AnyCarAsync();

        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/pricing/rules", null, staff, ignoreArrayOrder: true);
        foreach (var query in new[] { "", "?date=2031-06-01", "?date=2026-11-27" })
        {
            await AdminParity.AssertSameAsync(HttpMethod.Get, $"/api/v1/admin/pricing/demand{query}", null, staff);
        }

        foreach (var query in new[]
        {
            $"?car_id={car}&pickup_date=2031-06-01&dropoff_date=2031-06-05",
            $"?car_id={car}&pickup_date=2031-06-01&dropoff_date=2031-06-20",
            $"?car_id={car}&pickup_date=2031-06-01&dropoff_date=2031-06-01",
            $"?car_id={car}&pickup_date=2031-06-01",
            "?pickup_date=2031-06-01&dropoff_date=2031-06-05",
        })
        {
            await AdminParity.AssertSameAsync(HttpMethod.Get, $"/api/v1/admin/pricing/calculate{query}", null, staff);
        }
    }

    [Fact]
    public async Task Rules_are_created_changed_and_deleted_alike()
    {
        var owner = await AdminParity.TokenForRoleAsync("owner");
        var car = await AnyCarAsync();
        try
        {
            foreach (var body in new[]
            {
                $$"""{"name":"{{NamePrefix}} demand","type":"demand","multiplier":1.3,"conditions":{"min_demand_percent":50},"priority":7}""",
                $$"""{"name":"{{NamePrefix}} car","type":"duration","car_id":"{{car}}","category":"","multiplier":"0.9","conditions":{"min_days":7},"is_active":false,"start_date":"2031-01-01","end_date":"2031-12-31"}""",
                """{"name":"x","type":"demand","multiplier":0,"conditions":{}}""",
                """{"name":"x","type":"demand","multiplier":1.1}""",
                "{}",
            })
            {
                await AdminParity.AssertSameAsync(HttpMethod.Post, "/api/v1/admin/pricing/rules", body, owner, ignored: NewRowKeys);
            }

            // Change one rule on each side the same way and compare.
            var (expressRule, dotnetRule) = await RulePairAsync($"{NamePrefix} demand");
            foreach (var body in new[] { """{"multiplier":1.45,"priority":3}""", """{"is_active":false,"conditions":{"min_demand_percent":90}}""", """{"start_date":"2031-02-01","category":null}""" })
            {
                var express = await AdminParity.SendAsync(ParityEnvironment.ExpressUrl!, HttpMethod.Put, $"/api/v1/admin/pricing/rules/{expressRule}", body, owner);
                var dotnet = await AdminParity.SendAsync(ParityEnvironment.DotnetUrl!, HttpMethod.Put, $"/api/v1/admin/pricing/rules/{dotnetRule}", body, owner);
                Assert.Equal(express.Status, dotnet.Status);
                var differences = JsonParity.Compare(express.Body, dotnet.Body, ignoredKeys: NewRowKeys);
                Assert.True(differences.Count == 0, $"PUT {body}: {string.Join("; ", differences)}");
            }

            await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/pricing/rules/{expressRule}", "{}", owner);

            // Deleting answers the same whether or not the rule exists.
            await AdminParity.AssertSameAsync(HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{Guid.NewGuid()}", null, owner);
            var deletedOnExpress = await AdminParity.SendAsync(ParityEnvironment.ExpressUrl!, HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{expressRule}", null, owner);
            var deletedOnDotnet = await AdminParity.SendAsync(ParityEnvironment.DotnetUrl!, HttpMethod.Delete, $"/api/v1/admin/pricing/rules/{dotnetRule}", null, owner);
            Assert.Equal(deletedOnExpress.Status, deletedOnDotnet.Status);
            Assert.Empty(JsonParity.Compare(deletedOnExpress.Body, deletedOnDotnet.Body));
            Assert.Equal(0, await CountRulesAsync($"{NamePrefix} demand"));
        }
        finally
        {
            await DeleteParityRulesAsync();
        }
    }

    private static async Task<string> AnyCarAsync()
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        return await db.QueryFirstAsync<string>("SELECT id FROM cars ORDER BY id LIMIT 1");
    }

    /// <summary>
    /// The two rules with this name, one created through each API. They were created from the same
    /// body and are identical, so either can be changed through either API.
    /// </summary>
    private static async Task<(string First, string Second)> RulePairAsync(string name)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var ids = (await db.QueryAsync<string>("SELECT id FROM pricing_rules WHERE name = @name ORDER BY id", new { name })).ToList();
        Assert.Equal(2, ids.Count);
        return (ids[0], ids[1]);
    }

    private static async Task<int> CountRulesAsync(string name)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pricing_rules WHERE name = @name", new { name });
    }

    private static async Task DeleteParityRulesAsync()
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync("DELETE FROM pricing_rules WHERE name LIKE @prefix", new { prefix = NamePrefix + "%" });
    }
}
