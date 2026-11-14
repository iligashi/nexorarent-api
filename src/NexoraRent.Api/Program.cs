using System.Text.Json;
using NexoraRent.Api.Auth;
using NexoraRent.Api.Endpoints;
using NexoraRent.Api.Http;
using NexoraRent.Infrastructure;
using NexoraRent.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
builder.Services.PostConfigure<DatabaseOptions>(options => LegacyEnvironment.ApplyDatabase(builder.Configuration, options));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // The Next.js client expects snake_case keys (price_per_day, is_available).
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

builder.Services.AddInfrastructure();
builder.Services.AddJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddClientCors(builder.Configuration, builder.Environment);
builder.Services.AddApiRateLimiting();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseMiddleware<RequestIdMiddleware>();
app.UseApiExceptionHandler();
app.UseCors(CorsSetup.PolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();

var v1 = app.MapGroup("/api/v1");
v1.MapCarEndpoints();
v1.MapAuthEndpoints();

app.MapFallback(() => Results.Json(new { error = "Route not found" }, statusCode: StatusCodes.Status404NotFound));

await app.RunAsync();

/// <summary>Entry point, exposed so integration tests can host the API.</summary>
public partial class Program;
