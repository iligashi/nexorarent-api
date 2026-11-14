using Microsoft.AspNetCore.Diagnostics;

namespace NexoraRent.Api.Http;

internal static class ErrorHandling
{
    /// <summary>
    /// Turns unhandled exceptions into <c>{ error, requestId }</c> with status 500,
    /// the same shape the Next.js client already handles from Express.
    /// </summary>
    public static IApplicationBuilder UseApiExceptionHandler(this WebApplication app)
    {
        return app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NexoraRent.Api.Errors");
            logger.LogError(exception, "Request failed: {Method} {Path}", context.Request.Method, context.Request.Path);

            var message = app.Environment.IsProduction() || exception is null
                ? "Internal server error"
                : exception.Message;

            await Error(StatusCodes.Status500InternalServerError, message).ExecuteAsync(context);
        }));
    }

    /// <summary>
    /// An error response, <c>{ error, requestId }</c>. The request id matches the
    /// <c>X-Request-Id</c> header, so a reported error can be found in the logs.
    /// </summary>
    public static IResult Error(int statusCode, string message) => new ErrorResult(statusCode, message);

    private sealed class ErrorResult(int statusCode, string message) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            // A dictionary keeps the camelCase "requestId" key the client expects;
            // the snake_case naming policy only renames object properties.
            return httpContext.Response.WriteAsJsonAsync(new Dictionary<string, string>
            {
                ["error"] = message,
                ["requestId"] = httpContext.TraceIdentifier,
            });
        }
    }
}
