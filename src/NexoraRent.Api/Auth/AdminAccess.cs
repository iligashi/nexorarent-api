using NexoraRent.Api.Http;
using NexoraRent.Core.Users;

namespace NexoraRent.Api.Auth;

/// <summary>
/// Port of <c>requireAdmin</c> and <c>requireManager</c> in <c>server/src/middleware/admin.js</c>.
/// The access token is checked first (401 with the <c>authenticate</c> messages), then the role
/// (403 with the Express messages). Roles are read from the database on every request, so a
/// demoted user loses access at once instead of when their token expires.
/// </summary>
internal static class AdminAccess
{
    private static readonly IReadOnlyList<string> ManagerRoles = ["manager", "owner"];

    /// <summary>Staff, managers and owners.</summary>
    public static RouteGroupBuilder RequireAdmin(this RouteGroupBuilder group)
    {
        group.RequireAuthorization().AddEndpointFilter(new RoleFilter(Roles.Admin, "Admin access required"));
        return group;
    }

    /// <summary>Managers and owners only.</summary>
    public static RouteHandlerBuilder RequireManager(this RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter(new RoleFilter(ManagerRoles, "Manager access required"));

    private sealed class RoleFilter(IReadOnlyList<string> roles, string message) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var user = context.HttpContext.CurrentUser();
            if (user is null)
            {
                return ValueTask.FromResult<object?>(ErrorHandling.Error(StatusCodes.Status401Unauthorized, "Authentication required"));
            }

            return roles.Contains(user.Role)
                ? next(context)
                : ValueTask.FromResult<object?>(ErrorHandling.Error(StatusCodes.Status403Forbidden, message));
        }
    }
}
