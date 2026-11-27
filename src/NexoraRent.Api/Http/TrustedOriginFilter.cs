namespace NexoraRent.Api.Http;

/// <summary>
/// Port of <c>requireTrustedOrigin</c> in Express. The refresh and logout routes act on a
/// cookie the browser sends by itself, so they only accept requests whose <c>Origin</c> is one of
/// the CLIENT_URL origins (or, in development, any http://localhost or 127.0.0.1 port). This is the
/// CSRF guard for those routes; SameSite on the cookie is the second layer.
/// </summary>
internal sealed class TrustedOriginFilter(IConfiguration configuration, IHostEnvironment environment) : IEndpointFilter
{
    private readonly HashSet<string> _allowed = new(LegacyEnvironment.ClientOrigins(configuration), StringComparer.Ordinal);
    private readonly bool _allowLocalhost = environment.IsDevelopment();

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var origin = context.HttpContext.Request.Headers.Origin.ToString();
        return IsTrusted(origin)
            ? next(context)
            : ValueTask.FromResult<object?>(ErrorHandling.Error(StatusCodes.Status403Forbidden, "Request origin is not allowed"));
    }

    private bool IsTrusted(string origin)
    {
        if (origin.Length == 0)
        {
            return false;
        }

        if (_allowed.Contains(origin))
        {
            return true;
        }

        return _allowLocalhost
            && Uri.TryCreate(origin, UriKind.Absolute, out var url)
            && url.Scheme == Uri.UriSchemeHttp
            && url.Host is "localhost" or "127.0.0.1";
    }
}
