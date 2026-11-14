using NexoraRent.Core.Catalog;

namespace NexoraRent.Api.Endpoints;

/// <summary><c>GET /locations</c> and <c>GET /extras</c>, used by the reservation form.</summary>
internal static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/locations", async (ICatalogRepository catalog, CancellationToken cancellationToken) =>
                Results.Ok(new { locations = await catalog.ListActiveLocationsAsync(cancellationToken) }))
            .WithTags("Catalog")
            .WithName("ListLocations");

        api.MapGet("/extras", async (ICatalogRepository catalog, CancellationToken cancellationToken) =>
                Results.Ok(new { extras = await catalog.ListActiveExtrasAsync(cancellationToken) }))
            .WithTags("Catalog")
            .WithName("ListExtras");

        return api;
    }
}
