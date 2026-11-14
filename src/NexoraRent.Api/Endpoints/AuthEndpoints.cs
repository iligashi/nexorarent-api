using NexoraRent.Api.Auth;

namespace NexoraRent.Api.Endpoints;

internal static class AuthEndpoints
{
    /// <summary>
    /// Only <c>GET /auth/me</c> for now: it proves that tokens issued by Express
    /// are accepted here. Login, refresh and logout stay on Express until the
    /// refresh-token sessions are migrated.
    /// </summary>
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/me", (HttpContext context) => Results.Ok(new { user = context.CurrentUser() }))
            .RequireAuthorization()
            .WithName("GetCurrentUser");

        return api;
    }
}
