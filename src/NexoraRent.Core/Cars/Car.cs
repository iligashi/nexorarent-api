using System.Text.Json.Serialization;

namespace NexoraRent.Core.Cars;

/// <summary>
/// A car as returned by the public cars endpoints. Property names are written
/// as snake_case JSON so the response matches the existing Next.js client.
/// <para>
/// The VIN and license plate are left out on purpose: they identify a physical
/// vehicle, and only admin endpoints may return them.
/// </para>
/// </summary>
public sealed record Car
{
    public required string Id { get; init; }
    public required string Brand { get; init; }
    public required string Model { get; init; }
    public int Year { get; init; }
    public required string Slug { get; init; }
    public required string Category { get; init; }
    public required string Fuel { get; init; }
    public required string Transmission { get; init; }
    public int? Seats { get; init; }
    public int? Doors { get; init; }
    public int? Horsepower { get; init; }
    public int? EngineCc { get; init; }
    public string? Color { get; init; }
    public int? Mileage { get; init; }
    public decimal PricePerDay { get; init; }
    public decimal? PricePerWeek { get; init; }
    public decimal? Deposit { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Features { get; init; } = [];
    public bool IsAvailable { get; init; }
    public bool IsFeatured { get; init; }
    public int? SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>Primary image URL. Only set by the list and featured endpoints.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Image { get; init; }

    /// <summary>Average approved rating. Not set by the featured endpoint.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AvgRating { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReviewCount { get; init; }

    /// <summary>Gallery. Only set by the car detail endpoint.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CarImage>? Images { get; init; }

    /// <summary>Latest approved reviews. Only set by the car detail endpoint.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CarReview>? Reviews { get; init; }
}

public sealed record CarImage
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public string? AltText { get; init; }
    public bool IsPrimary { get; init; }
    public int SortOrder { get; init; }
}

public sealed record CarReview
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public string? ReservationId { get; init; }
    public int? Rating { get; init; }
    public string? Comment { get; init; }
    public bool IsApproved { get; init; }
    public string? AdminReply { get; init; }
    public DateTime? RepliedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
