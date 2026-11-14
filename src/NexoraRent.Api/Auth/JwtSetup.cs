using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NexoraRent.Api.Http;
using NexoraRent.Core.Users;

namespace NexoraRent.Api.Auth;

/// <summary>
/// Validates the access tokens issued by the Express API (HS256, signed with
/// JWT_SECRET, carrying a <c>userId</c> claim), so a user signed in through
/// Express can call routes that have already moved to this API.
/// </summary>
internal static class JwtSetup
{
    public const string UserIdClaim = "userId";
    private const string UserItemKey = "nexora.user";
    private const int MinimumSecretLength = 32;

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var secret = LegacyEnvironment.JwtSecret(configuration);
        if (string.IsNullOrEmpty(secret) || secret.Length < MinimumSecretLength)
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException($"JWT_SECRET must be set and at least {MinimumSecretLength} characters long.");
            }

            // Local development without a configured secret: tokens from Express will not validate.
            secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ReadTokenLikeExpress,
                    OnTokenValidated = LoadUserAsync,
                    OnChallenge = WriteChallengeAsync,
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>The signed-in user, loaded once per request after the token is validated.</summary>
    public static User? CurrentUser(this HttpContext context) => context.Items[UserItemKey] as User;

    /// <summary>
    /// Express only accepts <c>Bearer &lt;token&gt;</c> with that exact casing and takes the
    /// second space-separated part as the token. Anything else counts as no token.
    /// </summary>
    private static Task ReadTokenLikeExpress(MessageReceivedContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            context.NoResult();
            return Task.CompletedTask;
        }

        context.Token = header.Split(' ')[1];
        return Task.CompletedTask;
    }

    private static async Task LoadUserAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirst(UserIdClaim)?.Value;
        var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = userId is null ? null : await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);

        if (user is null)
        {
            context.Fail(new UserNotFoundException());
            return;
        }

        context.HttpContext.Items[UserItemKey] = user;
    }

    /// <summary>Returns the same 401 messages as the Express <c>authenticate</c> middleware.</summary>
    private static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var message = context.AuthenticateFailure switch
        {
            null => "Access token required",
            SecurityTokenExpiredException => "Token expired",
            UserNotFoundException => "User not found",
            _ => "Invalid token",
        };

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }

    private sealed class UserNotFoundException : Exception
    {
        public UserNotFoundException()
            : base("User not found")
        {
        }
    }
}
