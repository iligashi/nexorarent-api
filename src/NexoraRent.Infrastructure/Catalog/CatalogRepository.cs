using Dapper;
using NexoraRent.Core.Catalog;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Catalog;

/// <summary>Mirrors <c>locations.routes.js</c> and <c>extras.routes.js</c> in the Express API.</summary>
public sealed class CatalogRepository(MySqlConnectionFactory connections) : ICatalogRepository
{
    public async Task<IReadOnlyList<Location>> ListActiveLocationsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, address, city, lat, lng, is_active
            FROM locations WHERE is_active = true ORDER BY name, id
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Location>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Extra>> ListActiveExtrasAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, description, price_per_day, icon, is_active
            FROM extras WHERE is_active = true ORDER BY name, id
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Extra>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
