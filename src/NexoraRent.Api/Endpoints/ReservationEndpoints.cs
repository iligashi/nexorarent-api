using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core.Reservations;

namespace NexoraRent.Api.Endpoints;

/// <summary>
/// Customer reservations, ported from <c>server/src/routes/reservations.routes.js</c>.
/// Admin reservation routes stay on Express for now; both use the same per-car lock.
/// </summary>
internal static class ReservationEndpoints
{
    public static RouteGroupBuilder MapReservationEndpoints(this RouteGroupBuilder api)
    {
        var reservations = api.MapGroup("/reservations")
            .WithTags("Reservations")
            .RequireRateLimiting(RateLimiting.ReservationsPolicy);

        // Guests can book too: a valid token makes it the user's booking, anything else is a guest booking.
        reservations.MapPost("/", CreateAsync).WithName("CreateReservation");

        reservations.MapGet("/mine", async (HttpContext context, IReservationRepository repository, CancellationToken cancellationToken) =>
                Results.Ok(new { reservations = await repository.ListForUserAsync(context.CurrentUser()!.Id, cancellationToken) }))
            .RequireAuthorization()
            .WithName("ListMyReservations");

        reservations.MapGet("/{id}", async (string id, HttpContext context, IReservationRepository repository, CancellationToken cancellationToken) =>
            {
                var user = context.CurrentUser()!;
                var reservation = await repository.GetForUserAsync(id, user.Id, user.Role, cancellationToken);
                return reservation is null
                    ? ErrorHandling.Error(StatusCodes.Status404NotFound, "Reservation not found")
                    : Results.Ok(new { reservation });
            })
            .RequireAuthorization()
            .WithName("GetReservation");

        reservations.MapPut("/{id}/cancel", async (string id, HttpContext context, IReservationRepository repository, CancellationToken cancellationToken) =>
            {
                var reservation = await repository.CancelAsync(id, context.CurrentUser()!.Id, cancellationToken);
                return reservation is null
                    ? ErrorHandling.Error(StatusCodes.Status404NotFound, "Reservation not found or cannot be cancelled")
                    : Results.Ok(new { reservation });
            })
            .RequireAuthorization()
            .WithName("CancelReservation");

        return api;
    }

    private static async Task<IResult> CreateAsync(HttpContext context, IReservationRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var user = context.CurrentUser();
        var (request, issues) = ReservationRequest.Read(body, isGuest: user is null);
        if (request is null)
        {
            return ErrorHandling.ValidationFailed(issues);
        }

        try
        {
            var reservation = await repository.CreateAsync(request, user?.Id, cancellationToken);
            return Results.Json(new { reservation }, statusCode: StatusCodes.Status201Created);
        }
        catch (ReservationException exception)
        {
            return ErrorHandling.Error(StatusCode(exception.Failure), exception.Message);
        }
    }

    private static int StatusCode(ReservationFailure failure) => failure switch
    {
        ReservationFailure.NotFound => StatusCodes.Status404NotFound,
        ReservationFailure.Conflict => StatusCodes.Status409Conflict,
        ReservationFailure.Busy => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };
}
