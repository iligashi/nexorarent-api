using System.Text.Json;

namespace NexoraRent.Api.Http;

/// <summary>
/// Reads a request body the way Express does with <c>express.json({ limit: '1mb' })</c>
/// and the <c>requireJsonObject</c> middleware: a missing or non-JSON body is an
/// empty object, and only a JSON object is accepted.
/// </summary>
internal static class JsonBody
{
    public const int MaxBytes = 1024 * 1024;

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public static async Task<(JsonElement Body, IResult? Error)> ReadObjectAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return (EmptyObject, null);
        }

        if (request.ContentLength > MaxBytes)
        {
            return (default, TooLarge());
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return (default, TooLarge());
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            return (EmptyObject, null);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return (default, InvalidJson());
        }

        return root.ValueKind switch
        {
            JsonValueKind.Object => (root, null),
            JsonValueKind.Array => (default, ErrorHandling.Error(StatusCodes.Status400BadRequest, "JSON request body must be an object")),
            // express.json is strict: a top-level string, number or null is a syntax error there.
            _ => (default, InvalidJson()),
        };
    }

    private static IResult TooLarge() => ErrorHandling.Error(StatusCodes.Status413PayloadTooLarge, "request entity too large");

    private static IResult InvalidJson() => ErrorHandling.Error(StatusCodes.Status400BadRequest, "Request body is not valid JSON");
}
