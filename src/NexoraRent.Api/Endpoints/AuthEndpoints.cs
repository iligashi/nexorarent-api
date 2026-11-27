using System.Text.Json.Serialization;
using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core.Users;

namespace NexoraRent.Api.Endpoints;

/// <summary>
/// Ported from <c>server/src/routes/auth.routes.js</c>. Same URLs, status codes, messages and
/// JSON, and the same tokens, cookie and <c>refresh_token_sessions</c> rows, so a session started
/// on one API keeps working on the other while traffic moves over.
/// </summary>
internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimiting.AuthPolicy).WithName("Register");
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimiting.AuthPolicy).WithName("Login");
        auth.MapPost("/refresh", RefreshAsync).AddEndpointFilter<TrustedOriginFilter>().WithName("RefreshSession");
        auth.MapPost("/logout", LogoutAsync).AddEndpointFilter<TrustedOriginFilter>().WithName("Logout");
        auth.MapPost("/logout-all", LogoutAllAsync).RequireAuthorization().WithName("LogoutEverywhere");

        auth.MapGet("/me", (HttpContext context) => Results.Ok(new { user = context.CurrentUser() }))
            .RequireAuthorization()
            .WithName("GetCurrentUser");
        auth.MapGet("/me/loyalty", GetLoyaltyAsync).RequireAuthorization().WithName("GetLoyalty");
        auth.MapPut("/me", UpdateProfileAsync).RequireAuthorization().WithName("UpdateProfile");

        return api;
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, IUserRepository users, IPasswordHasher passwords, SignIn signIn, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (input, issues) = AuthRequest.ReadRegister(body);
        if (input is null)
        {
            return ErrorHandling.ValidationFailed(issues);
        }

        var user = await users.CreateAsync(
            new NewUser(input.Email, passwords.Hash(input.Password), input.FirstName, input.LastName, input.Phone),
            cancellationToken);
        if (user is null)
        {
            return ErrorHandling.Error(StatusCodes.Status409Conflict, "Email already registered");
        }

        return await signIn.StartAsync(context, user, StatusCodes.Status201Created, cancellationToken);
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context, IUserRepository users, IPasswordHasher passwords, SignIn signIn, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (input, issues) = AuthRequest.ReadLogin(body);
        if (input is null)
        {
            return ErrorHandling.ValidationFailed(issues);
        }

        var credentials = await users.GetCredentialsByEmailAsync(input.Email, cancellationToken);
        if (credentials is null)
        {
            passwords.VerifyNothing(input.Password);
            return InvalidCredentials();
        }

        if (!passwords.Verify(input.Password, credentials.PasswordHash))
        {
            return InvalidCredentials();
        }

        return await signIn.StartAsync(context, credentials.User, StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext context, AuthTokens tokens, RefreshCookie cookie, IRefreshSessionRepository sessions, CancellationToken cancellationToken)
    {
        var token = RefreshCookie.Read(context.Request);
        if (string.IsNullOrEmpty(token))
        {
            return ErrorHandling.Error(StatusCodes.Status401Unauthorized, "Refresh token required");
        }

        var claims = await tokens.ReadRefreshTokenAsync(token);
        if (claims is null)
        {
            cookie.Clear(context.Response);
            return ErrorHandling.Error(StatusCodes.Status401Unauthorized, "Invalid refresh token");
        }

        var next = tokens.CreateRefreshToken(claims.UserId);
        var user = await sessions.RotateAsync(claims.SessionId, AuthTokens.Hash(token), claims.UserId, next.Session, cancellationToken);
        if (user is null)
        {
            cookie.Clear(context.Response);
            return ErrorHandling.Error(StatusCodes.Status401Unauthorized, "Refresh token has expired or been revoked");
        }

        cookie.Write(context.Response, next.Token);
        return Results.Ok(new SignedIn(user, tokens.CreateAccessToken(user.Id)));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, RefreshCookie cookie, IRefreshSessionRepository sessions, CancellationToken cancellationToken)
    {
        var token = RefreshCookie.Read(context.Request);
        if (!string.IsNullOrEmpty(token))
        {
            await sessions.RevokeAsync(AuthTokens.Hash(token), cancellationToken);
        }

        cookie.Clear(context.Response);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(
        HttpContext context, RefreshCookie cookie, IRefreshSessionRepository sessions, CancellationToken cancellationToken)
    {
        await sessions.RevokeAllAsync(context.CurrentUser()!.Id, cancellationToken);
        cookie.Clear(context.Response);
        return Results.NoContent();
    }

    private static async Task<IResult> GetLoyaltyAsync(HttpContext context, IUserRepository users, CancellationToken cancellationToken)
    {
        var loyalty = await users.GetLoyaltyAsync(context.CurrentUser()!.Id, cancellationToken);
        return Results.Ok(loyalty ?? LoyaltySummary.Empty);
    }

    private static async Task<IResult> UpdateProfileAsync(HttpContext context, IUserRepository users, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (changes, issues) = AuthRequest.ReadProfile(body);
        if (changes is null)
        {
            return ErrorHandling.ValidationFailed(issues);
        }

        var user = await users.UpdateProfileAsync(context.CurrentUser()!.Id, changes, cancellationToken);
        return Results.Ok(new { user });
    }

    // Same message for an unknown email and a wrong password, so sign-in does not reveal who has an account.
    private static IResult InvalidCredentials() => ErrorHandling.Error(StatusCodes.Status401Unauthorized, "Invalid email or password");
}

/// <summary><c>{ user, accessToken }</c>. The key stays camelCase, as in Express, despite the snake_case policy.</summary>
internal sealed record SignedIn(User User, [property: JsonPropertyName("accessToken")] string AccessToken);

/// <summary>Starts a session after register or login: a stored refresh session, its cookie, and an access token.</summary>
internal sealed class SignIn(AuthTokens tokens, RefreshCookie cookie, IRefreshSessionRepository sessions)
{
    public async Task<IResult> StartAsync(HttpContext context, User user, int statusCode, CancellationToken cancellationToken)
    {
        var refresh = tokens.CreateRefreshToken(user.Id);
        await sessions.CreateAsync(refresh.Session, cancellationToken);
        cookie.Write(context.Response, refresh.Token);
        return Results.Json(new SignedIn(user, tokens.CreateAccessToken(user.Id)), statusCode: statusCode);
    }
}
