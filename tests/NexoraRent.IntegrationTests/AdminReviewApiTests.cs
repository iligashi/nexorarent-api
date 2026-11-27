using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.IntegrationTests;

/// <summary>
/// <c>/admin/reviews</c> and the admin audit log. Each test makes its own reviews (without a
/// reservation, so car ratings in other tests do not change) and only asserts on those.
/// </summary>
public class AdminReviewApiTests(ApiFixture api)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private HttpClient Client => api.CreateClient();

    [Fact]
    public async Task The_admin_area_needs_a_token_and_an_admin_role()
    {
        var anonymous = await SendAsync(HttpMethod.Get, "/api/v1/admin/reviews", token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Access token required", await ErrorAsync(anonymous));

        var customer = await SendAsync(HttpMethod.Get, "/api/v1/admin/reviews", TestData.AccessToken(TestData.CustomerId));
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.Equal("Admin access required", await ErrorAsync(customer));
    }

    [Fact]
    public async Task Staff_can_list_but_only_managers_and_owners_can_change_reviews()
    {
        var id = await CreateReviewAsync(approved: false);
        var staff = TestData.AccessToken(TestData.StaffId);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/reviews", staff)).StatusCode);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Put, $"/api/v1/admin/reviews/{id}/approve"),
            (HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reject"),
            (HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply"),
            (HttpMethod.Delete, $"/api/v1/admin/reviews/{id}"),
        })
        {
            var response = await SendAsync(method, path, staff, """{"admin_reply":"Hi"}""");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Manager access required", await ErrorAsync(response));
        }

        Assert.False((await ReviewAsync(id)).IsApproved);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_includes_the_author()
    {
        var pending = await CreateReviewAsync(approved: false);
        var approved = await CreateReviewAsync(approved: true);

        var pendingIds = await ListIdsAsync("status=pending&limit=100");
        var approvedIds = await ListIdsAsync("status=approved&limit=100");
        var allIds = await ListIdsAsync("status=anything-else&limit=100");

        Assert.Contains(pending, pendingIds);
        Assert.DoesNotContain(approved, pendingIds);
        Assert.Contains(approved, approvedIds);
        Assert.DoesNotContain(pending, approvedIds);
        Assert.Contains(pending, allIds);
        Assert.Contains(approved, allIds);

        var body = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/reviews?limit=100", Owner));
        var review = body.GetProperty("reviews").EnumerateArray().Single(r => r.GetProperty("id").GetString() == pending);
        Assert.Equal("Arta", review.GetProperty("first_name").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("reservation_no").ValueKind);
        Assert.False(review.GetProperty("is_approved").GetBoolean());
    }

    [Fact]
    public async Task Pagination_reports_totals_and_clamps_the_page_size()
    {
        await CreateReviewAsync(approved: false);

        var body = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/reviews?page=0&limit=5000", Owner));
        var pagination = body.GetProperty("pagination");

        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(100, pagination.GetProperty("limit").GetInt32());
        var total = pagination.GetProperty("total").GetInt32();
        Assert.True(total >= 1);
        Assert.Equal((int)Math.Ceiling(total / 100.0), pagination.GetProperty("pages").GetInt32());
        Assert.True(body.GetProperty("reviews").GetArrayLength() <= 100);
    }

    [Fact]
    public async Task Approve_and_reject_return_the_review()
    {
        var id = await CreateReviewAsync(approved: false);

        var approved = await ReadAsync(await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/approve", Owner));
        Assert.True(approved.GetProperty("review").GetProperty("is_approved").GetBoolean());

        var rejected = await ReadAsync(await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reject", Owner));
        Assert.False(rejected.GetProperty("review").GetProperty("is_approved").GetBoolean());
        Assert.False((await ReviewAsync(id)).IsApproved);
    }

    [Fact]
    public async Task Reply_is_trimmed_and_dated()
    {
        var id = await CreateReviewAsync(approved: true);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply", Owner, """{"admin_reply":"  Thank you!  "}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = (await ReadAsync(response)).GetProperty("review");
        Assert.Equal("Thank you!", review.GetProperty("admin_reply").GetString());
        Assert.NotEqual(JsonValueKind.Null, review.GetProperty("replied_at").ValueKind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"admin_reply":"   "}""")]
    [InlineData("""{"admin_reply":42}""")]
    public async Task Reply_needs_text(string body)
    {
        var id = await CreateReviewAsync(approved: true);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply", Owner, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("admin_reply is required", await ErrorAsync(response));
    }

    [Fact]
    public async Task Delete_removes_the_review_once()
    {
        var id = await CreateReviewAsync(approved: false);

        var deleted = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/reviews/{id}", Owner);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("Review deleted", (await ReadAsync(deleted)).GetProperty("message").GetString());

        var again = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/reviews/{id}", Owner);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("Review not found", await ErrorAsync(again));
    }

    [Theory]
    [InlineData("PUT", "approve")]
    [InlineData("PUT", "reject")]
    [InlineData("PUT", "reply")]
    public async Task Changes_to_a_missing_review_are_404(string method, string action)
    {
        var response = await SendAsync(new HttpMethod(method), $"/api/v1/admin/reviews/{Guid.NewGuid()}/{action}", Owner, """{"admin_reply":"Hi"}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Review not found", await ErrorAsync(response));
    }

    [Fact]
    public async Task Every_successful_change_is_in_the_audit_log()
    {
        var id = await CreateReviewAsync(approved: false);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply", Owner, """{"admin_reply":"Noted","extra":1}""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requestId = response.Headers.GetValues("X-Request-Id").Single();

        var row = Assert.Single(await AuditRowsAsync(id));
        Assert.Equal(requestId, row.RequestId);
        Assert.Equal(TestData.OwnerId, row.UserId);
        Assert.Equal("PUT", row.Method);
        Assert.Equal($"/api/v1/admin/reviews/{id}/reply", row.Path);
        Assert.Equal(200, row.StatusCode);
        Assert.Equal("put /api/v1/admin/reviews/:id/reply", row.Action);
        var summary = JsonDocument.Parse(row.ChangeSummary!).RootElement;
        Assert.Equal(["admin_reply", "extra"], summary.GetProperty("changed_fields").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task Reads_and_failed_changes_are_not_audited()
    {
        var id = await CreateReviewAsync(approved: false);

        await SendAsync(HttpMethod.Get, "/api/v1/admin/reviews", Owner);
        await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/reply", Owner, "{}");
        await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/approve", TestData.AccessToken(TestData.StaffId));

        Assert.Empty(await AuditRowsAsync(id));
    }

    [Fact]
    public async Task A_change_without_a_body_is_audited_without_a_summary()
    {
        var id = await CreateReviewAsync(approved: false);

        await SendAsync(HttpMethod.Put, $"/api/v1/admin/reviews/{id}/approve", Owner);

        var row = Assert.Single(await AuditRowsAsync(id));
        Assert.Equal("put /api/v1/admin/reviews/:id/approve", row.Action);
        Assert.Null(row.ChangeSummary);
    }

    [Fact]
    public async Task An_oversized_chunked_body_is_413_and_not_audited()
    {
        var id = await CreateReviewAsync(approved: false);
        var json = $$"""{"admin_reply":"{{new string('a', 2 * 1024 * 1024)}}"}""";

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/admin/reviews/{id}/reply", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Owner);
        // A stream that cannot seek has no length, so the body goes chunked with no Content-Length.
        request.Content = new StreamContent(new UnseekableStream(Encoding.UTF8.GetBytes(json)));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await Client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await AuditRowsAsync(id));
    }

    private static string Owner => TestData.AccessToken(TestData.OwnerId);

    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private async Task<string> CreateReviewAsync(bool approved)
    {
        var id = Guid.NewGuid().ToString();
        await using var db = await api.OpenDatabaseAsync();
        await db.ExecuteAsync(
            "INSERT INTO reviews (id, user_id, reservation_id, rating, comment, is_approved) VALUES (@id, @user, NULL, 4, 'Admin test', @approved)",
            new { id, user = TestData.CustomerId, approved });
        return id;
    }

    private async Task<ReviewRow> ReviewAsync(string id)
    {
        await using var db = await api.OpenDatabaseAsync();
        return await db.QuerySingleAsync<ReviewRow>("SELECT is_approved AS IsApproved FROM reviews WHERE id = @id", new { id });
    }

    private async Task<List<AuditRow>> AuditRowsAsync(string targetId)
    {
        await using var db = await api.OpenDatabaseAsync();
        return [.. await db.QueryAsync<AuditRow>(
            """
            SELECT request_id AS RequestId, user_id AS UserId, method AS Method, path AS Path,
                   status_code AS StatusCode, action AS Action, CAST(change_summary AS CHAR) AS ChangeSummary
            FROM admin_audit_log WHERE target_id = @targetId ORDER BY id
            """,
            new { targetId })];
    }

    private async Task<List<string>> ListIdsAsync(string query)
    {
        var body = await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/reviews?{query}", Owner));
        return [.. body.GetProperty("reviews").EnumerateArray().Select(r => r.GetProperty("id").GetString()!)];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, string? json = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await Client.SendAsync(request, Cancel);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).GetProperty("error").GetString();

    private sealed record ReviewRow
    {
        public bool IsApproved { get; init; }
    }

    private sealed record AuditRow
    {
        public required string RequestId { get; init; }
        public string? UserId { get; init; }
        public required string Method { get; init; }
        public required string Path { get; init; }
        public int StatusCode { get; init; }
        public required string Action { get; init; }
        public string? ChangeSummary { get; init; }
    }
}
