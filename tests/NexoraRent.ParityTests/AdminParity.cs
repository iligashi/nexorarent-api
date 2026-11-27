using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;

namespace NexoraRent.ParityTests;

/// <summary>
/// Sends the same admin request to Express and to .NET and compares the answers, for the
/// <c>/admin/*</c> parity tests.
/// </summary>
internal static class AdminParity
{
    // No cookie jar: cookies ignore the port, so one from Express would be sent to .NET and back.
    private static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <param name="afterExpress">Runs between the two requests, to undo what Express changed.</param>
    /// <param name="ignored">Keys each server fills in itself, such as new ids and timestamps.</param>
    public static async Task AssertSameAsync(
        HttpMethod method, string path, string? body, string? token,
        Func<Task>? afterExpress = null, IReadOnlySet<string>? ignored = null, bool ignoreArrayOrder = false)
    {
        var express = await SendAsync(ParityEnvironment.ExpressUrl!, method, path, body, token);
        if (afterExpress is not null)
        {
            await afterExpress();
        }

        var dotnet = await SendAsync(ParityEnvironment.DotnetUrl!, method, path, body, token);

        var label = $"{method} {path} {body}";
        Assert.True(express.Status == dotnet.Status, $"{label}: status {express.Status} from Express, {dotnet.Status} from .NET{Environment.NewLine}{express.Body}{Environment.NewLine}{dotnet.Body}");
        var differences = JsonParity.Compare(express.Body, dotnet.Body, ignoreArrayOrder, ignored);
        Assert.True(differences.Count == 0, $"{label}:{Environment.NewLine}{string.Join(Environment.NewLine, differences.Take(20))}");
    }

    public static async Task<(int Status, JsonElement Body)> SendAsync(Uri baseUrl, HttpMethod method, string path, string? body, string? token)
    {
        ParityEnvironment.SkipUnlessWritesConfigured();
        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await Http.SendAsync(request, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        return ((int)response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    public static async Task<string> TokenForRoleAsync(string role) => ParityEnvironment.AccessToken(await UserIdAsync(role));

    public static async Task<string> UserIdAsync(string role)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        return await db.QueryFirstAsync<string>("SELECT id FROM users WHERE role = @role ORDER BY email", new { role });
    }

    public static async Task<List<AuditRow>> AuditRowsAsync(string targetId)
    {
        await using var db = await ParityEnvironment.OpenDatabaseAsync();
        return [.. await db.QueryAsync<AuditRow>(
            """
            SELECT request_id AS RequestId, user_id AS UserId, method AS Method, path AS Path, status_code AS StatusCode,
                   action AS Action, target_id AS TargetId, CAST(change_summary AS CHAR) AS ChangeSummary
            FROM admin_audit_log WHERE target_id = @targetId ORDER BY id
            """,
            new { targetId })];
    }

    /// <summary>
    /// Each successful change wrote one audit row on each API, Express first; the pairs must match
    /// except for the request id.
    /// </summary>
    public static void AssertAuditPairsMatch(IReadOnlyList<AuditRow> rows, int expectedPairs)
    {
        Assert.Equal(expectedPairs * 2, rows.Count);
        for (var i = 0; i < rows.Count; i += 2)
        {
            Assert.Equal(rows[i] with { RequestId = "" }, rows[i + 1] with { RequestId = "" });
        }
    }

    // Settable properties, not a positional record: Dapper then converts column types itself
    // (MySQL 8 and MariaDB disagree on some integer types).
    public sealed record AuditRow
    {
        public required string RequestId { get; init; }
        public string? UserId { get; init; }
        public required string Method { get; init; }
        public required string Path { get; init; }
        public int StatusCode { get; init; }
        public required string Action { get; init; }
        public string? TargetId { get; init; }
        public string? ChangeSummary { get; init; }
    }
}
