using NexoraRent.Api.Http;
using NexoraRent.Core;
using NexoraRent.Core.Blog;

namespace NexoraRent.Api.Endpoints;

/// <summary>Public blog endpoints, compatible with <c>/api/v1/blog</c> in the Express API.</summary>
internal static class BlogEndpoints
{
    private const int DefaultLimit = 10;

    public static RouteGroupBuilder MapBlogEndpoints(this RouteGroupBuilder api)
    {
        var blog = api.MapGroup("/blog").WithTags("Blog");

        blog.MapGet("/", async (IBlogRepository posts, CancellationToken cancellationToken, string? page = null, string? limit = null) =>
            {
                var result = await posts.ListPublishedAsync(Paging.FromRaw(page, limit, DefaultLimit), cancellationToken);
                return Results.Ok(new { posts = result.Posts, total = result.Total });
            })
            .WithName("ListBlogPosts");

        blog.MapGet("/{slug}", async (string slug, IBlogRepository posts, CancellationToken cancellationToken) =>
            {
                var post = await posts.GetPublishedBySlugAsync(slug, cancellationToken);
                return post is null
                    ? ErrorHandling.Error(StatusCodes.Status404NotFound, "Post not found")
                    : Results.Ok(new { post });
            })
            .WithName("GetBlogPost");

        return api;
    }
}
