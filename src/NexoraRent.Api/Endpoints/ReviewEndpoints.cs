using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core.Reviews;

namespace NexoraRent.Api.Endpoints;

/// <summary>Ported from <c>server/src/routes/reviews.routes.js</c>.</summary>
internal static class ReviewEndpoints
{
    public static RouteGroupBuilder MapReviewEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/reviews", CreateAsync)
            .RequireAuthorization()
            .WithTags("Reviews")
            .WithName("CreateReview");

        return api;
    }

    private static async Task<IResult> CreateAsync(HttpContext context, IReviewRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (review, issues) = ReviewRequest.Read(body);
        if (review is null)
        {
            return ErrorHandling.ValidationFailed(issues);
        }

        var result = await repository.CreateAsync(context.CurrentUser()!.Id, review, cancellationToken);
        return result.Outcome switch
        {
            ReviewOutcome.Created => Results.Json(new { review = result.Review }, statusCode: StatusCodes.Status201Created),
            ReviewOutcome.AlreadyReviewed => ErrorHandling.Error(StatusCodes.Status409Conflict, "Already reviewed"),
            _ => ErrorHandling.Error(StatusCodes.Status400BadRequest, "Can only review completed reservations"),
        };
    }
}
