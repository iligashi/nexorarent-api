using Dapper;
using MySqlConnector;
using NexoraRent.Core.Reviews;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Reviews;

/// <summary>Mirrors <c>server/src/routes/reviews.routes.js</c> in the Express API.</summary>
public sealed class ReviewRepository(MySqlConnectionFactory connections) : IReviewRepository
{
    public async Task<ReviewResult> CreateAsync(string userId, NewReview review, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var completed = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT id FROM reservations WHERE id = @reservationId AND user_id = @userId AND status = 'completed'",
            new { reservationId = review.ReservationId, userId }, cancellationToken: cancellationToken));
        if (completed is null)
        {
            return new ReviewResult(ReviewOutcome.ReservationNotCompleted, null);
        }

        var id = Guid.NewGuid().ToString();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO reviews (id, user_id, reservation_id, rating, comment) VALUES (@id, @userId, @reservationId, @rating, @comment)",
                new { id, userId, reservationId = review.ReservationId, rating = review.Rating, comment = review.Comment },
                cancellationToken: cancellationToken));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // reviews.reservation_id is unique: one review per reservation.
            return new ReviewResult(ReviewOutcome.AlreadyReviewed, null);
        }

        var created = await connection.QuerySingleAsync<Review>(new CommandDefinition(
            """
            SELECT id, user_id, reservation_id, rating, comment, is_approved, admin_reply, replied_at, created_at
            FROM reviews WHERE id = @id
            """,
            new { id }, cancellationToken: cancellationToken));
        return new ReviewResult(ReviewOutcome.Created, created);
    }
}
