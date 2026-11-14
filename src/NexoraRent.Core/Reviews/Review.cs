namespace NexoraRent.Core.Reviews;

/// <summary>A row of the <c>reviews</c> table.</summary>
public sealed record Review
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public string? ReservationId { get; init; }
    public int? Rating { get; init; }
    public string? Comment { get; init; }
    public bool? IsApproved { get; init; }
    public string? AdminReply { get; init; }
    public DateTime? RepliedAt { get; init; }
    public DateTime? CreatedAt { get; init; }
}

public sealed record NewReview(string ReservationId, int Rating, string? Comment);

public enum ReviewOutcome
{
    Created,
    ReservationNotCompleted,
    AlreadyReviewed,
}

public sealed record ReviewResult(ReviewOutcome Outcome, Review? Review);

public interface IReviewRepository
{
    /// <summary>Reviews a completed reservation of the user. A reservation can be reviewed once.</summary>
    Task<ReviewResult> CreateAsync(string userId, NewReview review, CancellationToken cancellationToken);
}
