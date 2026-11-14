using System.Globalization;

namespace NexoraRent.Core;

/// <summary>
/// Page and page size from the query string. Invalid values fall back to the
/// defaults; page is at least 1 and the size is clamped to 1..<see cref="MaxLimit"/>.
/// </summary>
public sealed record Paging(int Page, int Limit)
{
    public const int MaxLimit = 100;

    public int Offset => (Page - 1) * Limit;

    public static Paging FromRaw(string? page, string? limit, int defaultLimit) => new(
        Math.Max(1, ParseInt(page) ?? 1),
        Math.Clamp(ParseInt(limit) ?? defaultLimit, 1, MaxLimit));

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}
