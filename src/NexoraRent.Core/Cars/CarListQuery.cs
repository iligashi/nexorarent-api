using System.Globalization;

namespace NexoraRent.Core.Cars;

public enum CarSort
{
    /// <summary>Admin-defined order, then newest. Used for unknown sort values.</summary>
    Default,
    Newest,
    PriceAsc,
    PriceDesc,
}

/// <summary>Validated, normalized filters for <c>GET /api/v1/cars</c>.</summary>
public sealed record CarListQuery
{
    public const int DefaultLimit = 12;
    public const int MaxLimit = 100;

    public string? Category { get; init; }
    public string? Fuel { get; init; }
    public string? Transmission { get; init; }
    public decimal? PriceMin { get; init; }
    public decimal? PriceMax { get; init; }
    public string? Search { get; init; }
    public CarSort Sort { get; init; } = CarSort.Newest;
    public int Page { get; init; } = 1;
    public int Limit { get; init; } = DefaultLimit;

    public int Offset => (Page - 1) * Limit;

    /// <summary>
    /// Builds a query from raw query-string values. Invalid numbers are ignored
    /// rather than rejected, like the Express API, but page and limit are
    /// clamped so a client cannot request unbounded pages.
    /// </summary>
    public static CarListQuery FromRaw(
        string? category = null,
        string? fuel = null,
        string? transmission = null,
        string? priceMin = null,
        string? priceMax = null,
        string? search = null,
        string? sort = null,
        string? page = null,
        string? limit = null)
    {
        return new CarListQuery
        {
            Category = Blank(category),
            Fuel = Blank(fuel),
            Transmission = Blank(transmission),
            PriceMin = ParseDecimal(priceMin),
            PriceMax = ParseDecimal(priceMax),
            Search = Blank(search),
            Sort = ParseSort(sort),
            Page = Math.Max(1, ParseInt(page) ?? 1),
            Limit = Math.Clamp(ParseInt(limit) ?? DefaultLimit, 1, MaxLimit),
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static CarSort ParseSort(string? value) => value switch
    {
        null or "" or "newest" => CarSort.Newest,
        "price_asc" => CarSort.PriceAsc,
        "price_desc" => CarSort.PriceDesc,
        _ => CarSort.Default,
    };
}

public sealed record CarPage(IReadOnlyList<Car> Cars, int Total);
