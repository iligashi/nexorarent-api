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

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            // A dictionary keeps the camelCase "requestId" key the client expects;
            // the snake_case naming policy only renames object properties.
            await context.Response.WriteAsJsonAsync(new Dictionary<string, string>
            {
                ["error"] = message,
                ["requestId"] = context.TraceIdentifier,
            });
        }));
    }

    public static IResult Error(int statusCode, string message) =>
        Results.Json(new { error = message }, statusCode: statusCode);
}
