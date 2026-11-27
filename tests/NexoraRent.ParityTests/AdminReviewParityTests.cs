using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Compares <c>/admin/reviews</c> on Express and .NET, and the audit rows each writes for the
/// same change. Reviews are made here without a reservation and removed afterwards.
/// </summary>
[Collection(ParityEnvironment.SharedDatabase)]
public class AdminReviewParityTests
{
    /// <summary><c>replied_at</c> is set by each request with NOW(), so it can differ by a second.</summary>
    private static readonly HashSet<string> RepliedAt = new(StringComparer.Ordinal) { "replied_at" };

    [Fact]
    public async Task Access_rules_match()
    {
        var customer = await UserIdAsync("customer");

        await AssertSameAsync(HttpMethod.Get, "/api/v1/admin/reviews", null, token: null);
        await AssertSameAsync(HttpMethod.Get, "/api/v1/admin/reviews", null, ParityEnvironment.AccessToken(customer));
        await AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{Guid.NewGuid()}/approve", null, ParityEnvironment.AccessToken(customer));
    }

    [Fact]
    public async Task Listing_matches()
    {
        var owner = await OwnerTokenAsync();
        var ids = new[] { await CreateReviewAsync(approved: true), await CreateReviewAsync(approved: false) };
        try
        {
            foreach (var query in new[] { "", "?status=pending", "?status=approved&limit=2", "?status=other&page=2&limit=1" })
            {
                await AssertSameAsync(HttpMethod.Get, $"/api/v1/admin/reviews{query}", null, owner, ignoreArrayOrder: true);
            }
        }
        finally
        {
            await DeleteReviewsAsync(ids);
        }
    }

    [Fact]
    public async Task Changes_match_and_are_audited_alike()
    {
        var owner = await OwnerTokenAsync();
        var id = await CreateReviewAsync(approved: false);
        var reset = () => ResetReviewAsync(id);
        try
        {
            await AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/approve", null, owner, afterExpress: reset);
            await AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reject", null, owner, afterExpress: reset);
            await AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply", """{"admin_reply":"  Thanks!  "}""", owner, afterExpress: reset, ignored: RepliedAt);
            await AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply", """{"admin_reply":"   "}""", owner);
            await AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply", "{}", owner);
            await AssertSameAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{Guid.NewGuid()}/approve", null, owner);
            await AssertSameAsync(HttpMethod.Delete, $"/api/v1/admin/reviews/{Guid.NewGuid()}", null, owner);

            // Each successful change above wrote one audit row on each API; compare them pairwise.
            AdminParity.AssertAuditPairsMatch(await AdminParity.AuditRowsAsync(id), expectedPairs: 3);
        }
        finally
        {
            await DeleteReviewsAsync([id]);
        }
    }

    [Fact]
    public async Task Deleting_matches()
    {
        var owner = await OwnerTokenAsync();
        var id = await CreateReviewAsync(approved: true);
        try
        {
            await AssertSameAsync(
                HttpMethod.Delete, $"/api/v1/admin/reviews/{id}", null, owner,
                afterExpress: async () => await RestoreReviewAsync(id));
        }
        finally
        {
            await DeleteReviewsAsync([id]);
        }
    }

    private static Task<string> OwnerTokenAsync() => AdminParity.TokenForRoleAsync("owner");

    private static Task<string> UserIdAsync(string role) => AdminParity.UserIdAsync(role);

    private static Task AssertSameAsync(
        HttpMethod method, string path, string? body, string? token,
        Func<Task>? afterExpress = null, IReadOnlySet<string>? ignored = null, bool ignoreArrayOrder = false) =>
        AdminParity.AssertSameAsync(method, path, body, token, afterExpress, ignored, ignoreArrayOrder);

    private static async Task<string> CreateReviewAsync(bool approved)
    {
        var id = Guid.NewGuid().ToString();
        var author = await UserIdAsync("customer");
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync(
            "INSERT INTO reviews (id, user_id, reservation_id, rating, comment, is_approved, created_at) VALUES (@id, @author, NULL, 4, 'Parity admin review', @approved, '2031-01-01 10:00:00')",
            new { id, author, approved });
        return id;
    }

    private static async Task ResetReviewAsync(string id)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE reviews SET is_approved = false, admin_reply = NULL, replied_at = NULL WHERE id = @id", new { id });
    }

    private static async Task RestoreReviewAsync(string id)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        var author = await UserIdAsync("customer");
        await db.ExecuteAsync(
            "INSERT IGNORE INTO reviews (id, user_id, reservation_id, rating, comment, is_approved, created_at) VALUES (@id, @author, NULL, 4, 'Parity admin review', true, '2031-01-01 10:00:00')",
            new { id, author });
    }

    private static async Task DeleteReviewsAsync(IEnumerable<string> ids)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        foreach (var id in ids)
        {
            await db.ExecuteAsync("DELETE FROM reviews WHERE id = @id; DELETE FROM admin_audit_log WHERE target_id = @id;", new { id });
        }
    }
}
