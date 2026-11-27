using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Compares <c>/admin/expenses</c> on Express and .NET. Expenses made here have a description
/// starting with <c>Parity made</c> and are removed afterwards.
/// </summary>
[Collection(ParityEnvironment.SharedDatabase)]
public class AdminExpenseParityTests
{
    private const string MadePrefix = "Parity made";

    /// <summary>Each API creates its own row.</summary>
    private static readonly HashSet<string> NewRowKeys = new(StringComparer.Ordinal) { "id", "created_at" };

    [Fact]
    public async Task Access_rules_match()
    {
        var customer = await AdminParity.TokenForRoleAsync("customer");
        var staff = await AdminParity.TokenForRoleAsync("staff");

        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/expenses", null, token: null);
        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/expenses/summary", null, customer);
        await AdminParity.AssertSameAsync(HttpMethod.Post, "/api/v1/admin/expenses", "{}", staff);
        await AdminParity.AssertSameAsync(HttpMethod.Delete, $"/api/v1/admin/expenses/{Guid.NewGuid()}", null, staff);
    }

    [Fact]
    public async Task Reads_match()
    {
        var staff = await AdminParity.TokenForRoleAsync("staff");
        var car = await ParityCarAsync();

        foreach (var query in new[]
        {
            $"?car_id={car}",
            $"?car_id={car}&category=fuel",
            $"?car_id={car}&date_from=2026-01-01&date_to=2026-09-30",
            $"?car_id={car}&limit=1&page=2",
            $"?car_id={car}&category=",
            "?car_id=no-such-car",
        })
        {
            await AdminParity.AssertSameAsync(HttpMethod.Get, $"/api/v1/admin/expenses{query}", null, staff);
        }

        await AdminParity.AssertSameAsync(HttpMethod.Get, $"/api/v1/admin/expenses/car/{car}", null, staff);
        await AdminParity.AssertSameAsync(HttpMethod.Get, $"/api/v1/admin/expenses/car/{car}?limit=2&page=2", null, staff);

        // Express leaves the order of equal totals to MySQL; .NET breaks ties by name or id.
        await AdminParity.AssertSameAsync(HttpMethod.Get, "/api/v1/admin/expenses/summary", null, staff, ignoreArrayOrder: true);
    }

    [Fact]
    public async Task Expenses_are_created_changed_and_deleted_alike()
    {
        var owner = await AdminParity.TokenForRoleAsync("owner");
        var car = await ParityCarAsync();
        try
        {
            foreach (var body in new[]
            {
                $$"""{"car_id":"{{car}}","category":"wash","amount":"15.5","expense_date":"2026-11-01","description":"{{MadePrefix}} wash","vendor":""}""",
                $$"""{"car_id":"{{car}}","category":"tax","amount":-20,"expense_date":"2026-11-02","description":"{{MadePrefix}} refund"}""",
                $$"""{"car_id":"{{car}}","category":"wash","amount":0,"expense_date":"2026-11-01"}""",
                """{"category":"wash","amount":5,"expense_date":"2026-11-01"}""",
            })
            {
                await AdminParity.AssertSameAsync(HttpMethod.Post, "/api/v1/admin/expenses", body, owner, ignored: NewRowKeys);
            }

            var (expressExpense, dotnetExpense) = await ExpensePairAsync($"{MadePrefix} wash");
            foreach (var body in new[]
            {
                """{"amount":17.25,"vendor":"Parity Wash"}""",
                """{"category":"parking","expense_date":"2026-11-03","description":""}""",
                """{"vendor":null}""",
            })
            {
                var express = await AdminParity.SendAsync(ParityEnvironment.ExpressUrl!, HttpMethod.Put, $"/api/v1/admin/expenses/{expressExpense}", body, owner);
                var dotnet = await AdminParity.SendAsync(ParityEnvironment.DotnetUrl!, HttpMethod.Put, $"/api/v1/admin/expenses/{dotnetExpense}", body, owner);
                Assert.Equal(express.Status, dotnet.Status);
                var differences = JsonParity.Compare(express.Body, dotnet.Body, ignoredKeys: NewRowKeys);
                Assert.True(differences.Count == 0, $"PUT {body}: {string.Join("; ", differences)}");
            }

            await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/expenses/{expressExpense}", "{}", owner);
            await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/expenses/{Guid.NewGuid()}", """{"amount":1}""", owner);
            await AdminParity.AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/expenses/{Guid.NewGuid()}", "{}", owner);
            await AdminParity.AssertSameAsync(HttpMethod.Delete, $"/api/v1/admin/expenses/{Guid.NewGuid()}", null, owner);

            var deletedOnExpress = await AdminParity.SendAsync(ParityEnvironment.ExpressUrl!, HttpMethod.Delete, $"/api/v1/admin/expenses/{expressExpense}", null, owner);
            var deletedOnDotnet = await AdminParity.SendAsync(ParityEnvironment.DotnetUrl!, HttpMethod.Delete, $"/api/v1/admin/expenses/{dotnetExpense}", null, owner);
            Assert.Equal(deletedOnExpress.Status, deletedOnDotnet.Status);
            Assert.Empty(JsonParity.Compare(deletedOnExpress.Body, deletedOnDotnet.Body));
        }
        finally
        {
            await using var db = await ParityEnvironment.OpenDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM car_expenses WHERE description LIKE @prefix", new { prefix = MadePrefix + "%" });
        }
    }

    private static async Task<string> ParityCarAsync()
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        return await db.QuerySingleAsync<string>("SELECT car_id FROM car_expenses WHERE id = 'dddddddd-0000-4000-8000-000000000301'");
    }

    private static async Task<(string First, string Second)> ExpensePairAsync(string description)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var ids = (await db.QueryAsync<string>("SELECT id FROM car_expenses WHERE description = @description ORDER BY id", new { description })).ToList();
        Assert.Equal(2, ids.Count);
        return (ids[0], ids[1]);
    }
}
