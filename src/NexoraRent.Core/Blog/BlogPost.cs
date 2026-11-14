namespace NexoraRent.Core.Blog;

public sealed record BlogPost
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Slug { get; init; }
    public required string Content { get; init; }
    public string? Excerpt { get; init; }
    public string? CoverImage { get; init; }
    public string? AuthorId { get; init; }
    public bool IsPublished { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>"First Last" of the author, or null when the post has no author.</summary>
    public string? AuthorName { get; init; }
}

public sealed record BlogPage(IReadOnlyList<BlogPost> Posts, int Total);

public interface IBlogRepository
{
    /// <summary>Published posts, newest first.</summary>
    Task<BlogPage> ListPublishedAsync(Paging paging, CancellationToken cancellationToken);

    /// <summary>A published post by slug, or null.</summary>
    Task<BlogPost?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken);
}
