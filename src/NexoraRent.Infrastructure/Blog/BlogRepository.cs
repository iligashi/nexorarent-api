using Dapper;
using NexoraRent.Core;
using NexoraRent.Core.Blog;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Blog;

/// <summary>Mirrors the public routes in <c>blog.routes.js</c> in the Express API.</summary>
public sealed class BlogRepository(MySqlConnectionFactory connections) : IBlogRepository
{
    private const string SelectPost = """
        SELECT bp.id, bp.title, bp.slug, bp.content, bp.excerpt, bp.cover_image, bp.author_id,
               bp.is_published, bp.published_at, bp.created_at, bp.updated_at,
               CONCAT(u.first_name, ' ', u.last_name) AS author_name
        FROM blog_posts bp
        LEFT JOIN users u ON u.id = bp.author_id
        """;

    public async Task<BlogPage> ListPublishedAsync(Paging paging, CancellationToken cancellationToken)
    {
        // id is a tie-breaker so posts published at the same time keep a stable order across pages.
        const string sql = $"""
            {SelectPost}
            WHERE bp.is_published = true
            ORDER BY bp.published_at DESC, bp.id
            LIMIT @Limit OFFSET @Offset;

            SELECT COUNT(*) FROM blog_posts WHERE is_published = true;
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql, new { paging.Limit, paging.Offset }, cancellationToken: cancellationToken));

        var posts = (await results.ReadAsync<BlogPost>()).AsList();
        var total = await results.ReadSingleAsync<int>();
        return new BlogPage(posts, total);
    }

    public async Task<BlogPost?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        const string sql = $"""
            {SelectPost}
            WHERE bp.slug = @slug AND bp.is_published = true
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<BlogPost>(
            new CommandDefinition(sql, new { slug }, cancellationToken: cancellationToken));
    }
}
