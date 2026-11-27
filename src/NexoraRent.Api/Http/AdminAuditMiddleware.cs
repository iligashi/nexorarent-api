using System.Buffers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using NexoraRent.Api.Auth;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Api.Http;

/// <summary>
/// Port of <c>auditAdminAction</c> in <c>server/src/middleware/request-context.js</c>. Every
/// successful admin write (POST, PUT, PATCH, DELETE with a status below 400) gets a row in
/// <c>admin_audit_log</c> <b>before</b> the response is sent. If that row cannot be written the
/// client gets a 500 instead of the success, so no admin change goes unrecorded.
/// <para>
/// The response is held in memory until the row is written. Only writes are held, and admin
/// write responses are small JSON bodies.
/// </para>
/// </summary>
internal sealed partial class AdminAuditMiddleware(
    RequestDelegate next, MySqlConnectionFactory connections, ILogger<AdminAuditMiddleware> logger)
{
    /// <summary>Set by an endpoint to record a richer summary than the changed field names.</summary>
    public const string ChangeSummaryItem = "nexora.audit.change_summary";

    private const int MaxActionLength = 120;

    private static readonly HashSet<string> Writes = new(StringComparer.Ordinal) { "POST", "PUT", "PATCH", "DELETE" };

    public static bool Applies(HttpContext context) =>
        Writes.Contains(context.Request.Method)
        && (context.Request.Path.StartsWithSegments("/api/v1/admin") || context.Request.Path.StartsWithSegments("/api/v1/upload"));

    public async Task InvokeAsync(HttpContext context)
    {
        var changedFields = await ReadChangedFieldsAsync(context.Request);

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);

            if (context.Response.StatusCode < StatusCodes.Status400BadRequest && !await TryRecordAsync(context, changedFields))
            {
                buffer.SetLength(0);
                context.Response.Body = original;
                context.Response.Headers.ContentLength = null;
                await ErrorHandling.Error(StatusCodes.Status500InternalServerError, "Admin action could not be durably recorded").ExecuteAsync(context);
                return;
            }

            context.Response.Body = original;
            context.Response.ContentLength = buffer.Length;
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private async Task<bool> TryRecordAsync(HttpContext context, IReadOnlyList<string> changedFields)
    {
        var path = context.Request.PathBase.Add(context.Request.Path).Value ?? string.Empty;
        var action = $"{context.Request.Method.ToLowerInvariant()} {UuidPattern().Replace(path, ":id")}";
        var summary = context.Items[ChangeSummaryItem]
            ?? (changedFields.Count > 0 ? new { changed_fields = changedFields } : null);

        try
        {
            await using var connection = await connections.OpenAsync(CancellationToken.None);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO admin_audit_log (request_id, user_id, method, path, status_code, action, target_id, change_summary)
                VALUES (@requestId, @userId, @method, @path, @statusCode, @action, @targetId, @changeSummary)
                """,
                new
                {
                    requestId = context.TraceIdentifier,
                    userId = context.CurrentUser()?.Id,
                    method = context.Request.Method,
                    path,
                    statusCode = context.Response.StatusCode,
                    action = action.Length > MaxActionLength ? action[..MaxActionLength] : action,
                    targetId = UuidPattern().Match(path) is { Success: true } match ? match.Value : null,
                    changeSummary = summary is null ? null : JsonSerializer.Serialize(summary),
                },
                // Record the action even if the client hangs up: the change itself has been made.
                cancellationToken: CancellationToken.None));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Admin audit log failed: {Method} {Path} by {UserId}", context.Request.Method, path, context.CurrentUser()?.Id);
            return false;
        }
    }

    /// <summary>
    /// The top-level keys of a JSON object body, like <c>Object.keys(req.body)</c> in Express.
    /// The body is rewound so the endpoint can read it again.
    /// <para>
    /// At most <see cref="JsonBody.MaxBytes"/> + 1 bytes are read, also for a chunked body with no
    /// Content-Length: a bigger body is not parsed here, and the endpoint answers it with 413.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadChangedFieldsAsync(HttpRequest request)
    {
        if (!request.HasJsonContentType() || request.ContentLength > JsonBody.MaxBytes)
        {
            return [];
        }

        request.EnableBuffering();
        var body = ArrayPool<byte>.Shared.Rent(JsonBody.MaxBytes + 1);
        try
        {
            var length = 0;
            int read;
            while (length <= JsonBody.MaxBytes
                && (read = await request.Body.ReadAsync(body.AsMemory(length, JsonBody.MaxBytes + 1 - length), request.HttpContext.RequestAborted)) > 0)
            {
                length += read;
            }

            if (length is 0 or > JsonBody.MaxBytes)
            {
                return [];
            }

            using var document = JsonDocument.Parse(body.AsMemory(0, length));
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? [.. document.RootElement.EnumerateObject().Select(property => property.Name)]
                : [];
        }
        catch (JsonException)
        {
            // The endpoint answers 400 for a bad body, which is never recorded.
            return [];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(body);
            request.Body.Position = 0;
        }
    }

    // The ids Express replaces with ":id": UUIDs of versions 1-8 with the RFC 4122 variant.
    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex UuidPattern();
}
