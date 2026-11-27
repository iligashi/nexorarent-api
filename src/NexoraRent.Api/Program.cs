using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddScoped<SignIn>();
builder.Services.AddClientCors(builder.Configuration, builder.Environment);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddProblemDetails();

// Like `app.set('trust proxy', 1)` in Express: in production the API runs behind exactly one
// proxy (Railway), so the client IP is the last X-Forwarded-For entry, the one that proxy added.
// Without this every request has the proxy's IP and all clients share one rate-limit bucket.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsProduction())
{
    app.UseForwardedHeaders();
}

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
v1.MapCatalogEndpoints();
v1.MapBlogEndpoints();
v1.MapReservationEndpoints();
v1.MapReviewEndpoints();
v1.MapAuthEndpoints();

app.MapFallback(() => ErrorHandling.Error(StatusCodes.Status404NotFound, "Route not found"));

await app.RunAsync();

/// <summary>Entry point, exposed so integration tests can host the API.</summary>
public partial class Program;
