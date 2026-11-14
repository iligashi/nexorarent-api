namespace NexoraRent.Api.Http;

/// <summary>
/// Gives every request an id, returns it in <c>X-Request-Id</c> and adds it to
/// the log scope, like <c>requestContext</c> in the Express API.
/// </summary>
internal sealed class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString();
        context.TraceIdentifier = requestId;
        context.Response.Headers[HeaderName] = requestId;

        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
        {
            await next(context);
        }
    }
}
