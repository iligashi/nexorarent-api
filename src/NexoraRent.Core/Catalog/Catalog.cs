namespace NexoraRent.Core.Catalog;

/// <summary>A pickup or dropoff location (<c>locations</c> table).</summary>
public sealed record Location
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public decimal? Lat { get; init; }
    public decimal? Lng { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>An optional add-on such as a child seat or GPS (<c>extras</c> table).</summary>
public sealed record Extra
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal PricePerDay { get; init; }
    public string? Icon { get; init; }
    public bool IsActive { get; init; }
}

public interface ICatalogRepository
{
    /// <summary>Active locations, ordered by name.</summary>
    Task<IReadOnlyList<Location>> ListActiveLocationsAsync(CancellationToken cancellationToken);

    /// <summary>Active extras, ordered by name.</summary>
    Task<IReadOnlyList<Extra>> ListActiveExtrasAsync(CancellationToken cancellationToken);
}
