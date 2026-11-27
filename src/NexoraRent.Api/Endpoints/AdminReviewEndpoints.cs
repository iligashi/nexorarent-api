using System.Text.Json;
using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core;
using NexoraRent.Core.Reviews;
using NexoraRent.Core.Validation;

namespace NexoraRent.Api.Endpoints;

/// <summary>
/// Ported from <c>server/src/routes/admin.reviews.routes.js</c>. Staff can list reviews;
/// approving, rejecting, replying and deleting need a manager or owner.
/// </summary>
internal static class AdminReviewEndpoints
{
    private const int DefaultLimit = 20;
    private const string NotFound = "Review not found";

    public static RouteGroupBuilder MapAdminReviewEndpoints(this RouteGroupBuilder api)
    {
        var reviews = api.MapGroup("/admin/reviews").WithTags("Admin: reviews").RequireAdmin();

        reviews.MapGet("/", ListAsync).WithName("AdminListReviews");
        reviews.MapPut("/{id}/approve", (string id, IAdminReviewRepository repository, CancellationToken cancellationToken) =>
                SetApprovedAsync(id, approved: true, repository, cancellationToken))
            .RequireManager().WithName("AdminApproveReview");
        reviews.MapPut("/{id}/reject", (string id, IAdminReviewRepository repository, CancellationToken cancellationToken) =>
                SetApprovedAsync(id, approved: false, repository, cancellationToken))
            .RequireManager().WithName("AdminRejectReview");
        reviews.MapPut("/{id}/reply", ReplyAsync).RequireManager().WithName("AdminReplyToReview");
        reviews.MapDelete("/{id}", DeleteAsync).RequireManager().WithName("AdminDeleteReview");

        return api;
    }

    private static async Task<IResult> ListAsync(
        string? status, string? page, string? limit, IAdminReviewRepository repository, CancellationToken cancellationToken)
    {
        var paging = Paging.FromRaw(page, limit, DefaultLimit);
        var filter = status switch
        {
            "pending" => ReviewStatusFilter.Pending,
            "approved" => ReviewStatusFilter.Approved,
            _ => ReviewStatusFilter.All,
        };

        var result = await repository.ListAsync(filter, paging, cancellationToken);
        return Results.Ok(new
        {
            reviews = result.Reviews,
            pagination = new
            {
                page = paging.Page,
                limit = paging.Limit,
                total = result.Total,
                pages = (int)Math.Ceiling(result.Total / (double)paging.Limit),
            },
        });
    }

    private static async Task<IResult> SetApprovedAsync(string id, bool approved, IAdminReviewRepository repository, CancellationToken cancellationToken)
    {
        var review = await repository.SetApprovedAsync(id, approved, cancellationToken);
        return review is null ? ErrorHandling.Error(StatusCodes.Status404NotFound, NotFound) : Results.Ok(new { review });
    }

    private static async Task<IResult> ReplyAsync(string id, HttpContext context, IAdminReviewRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        // Express: `if (!admin_reply || !admin_reply.trim())`. A non-string reply crashes Express
        // with a 500; here it is the same 400 as a missing one.
        var reply = body.TryGetProperty("admin_reply", out var value) && value.ValueKind == JsonValueKind.String
            ? JsonValidator.JsTrim(value.GetString()!)
            : string.Empty;
        if (reply.Length == 0)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, "admin_reply is required");
        }

        var review = await repository.ReplyAsync(id, reply, cancellationToken);
        return review is null ? ErrorHandling.Error(StatusCodes.Status404NotFound, NotFound) : Results.Ok(new { review });
    }

    private static async Task<IResult> DeleteAsync(string id, IAdminReviewRepository repository, CancellationToken cancellationToken) =>
        await repository.DeleteAsync(id, cancellationToken)
            ? Results.Ok(new { message = "Review deleted" })
            : ErrorHandling.Error(StatusCodes.Status404NotFound, NotFound);
}
