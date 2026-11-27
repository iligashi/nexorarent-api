namespace NexoraRent.Core.Reviews;

/// <summary>A review in the admin list, with who wrote it and which car it is about.</summary>
public sealed record AdminReview
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
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? ReservationNo { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
}

/// <summary>The <c>status</c> filter of <c>GET /admin/reviews</c>. Any other value lists all reviews.</summary>
public enum ReviewStatusFilter
{
    All,
    Pending,
    Approved,
}

public sealed record AdminReviewPage(IReadOnlyList<AdminReview> Reviews, int Total);

/// <summary>Mirrors <c>server/src/routes/admin.reviews.routes.js</c>.</summary>
public interface IAdminReviewRepository
{
    Task<AdminReviewPage> ListAsync(ReviewStatusFilter status, Paging paging, CancellationToken cancellationToken);

    /// <summary>Sets <c>is_approved</c> and returns the review, or null when there is none with that id.</summary>
    Task<Review?> SetApprovedAsync(string id, bool approved, CancellationToken cancellationToken);

    Task<Review?> ReplyAsync(string id, string reply, CancellationToken cancellationToken);

    /// <summary>False when there is no review with that id.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}
