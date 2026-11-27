using Dapper;
using NexoraRent.Core;
using NexoraRent.Core.Reviews;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Reviews;

/// <summary>Mirrors <c>server/src/routes/admin.reviews.routes.js</c> in the Express API.</summary>
public sealed class AdminReviewRepository(MySqlConnectionFactory connections) : IAdminReviewRepository
{
    private const string SelectReview =
        "SELECT id, user_id, reservation_id, rating, comment, is_approved, admin_reply, replied_at, created_at FROM reviews WHERE id = @id";

    public async Task<AdminReviewPage> ListAsync(ReviewStatusFilter status, Paging paging, CancellationToken cancellationToken)
    {
        // Fixed SQL per filter value; nothing from the request is put into the statement.
        var where = status switch
        {
            ReviewStatusFilter.Pending => "WHERE rv.is_approved = false",
            ReviewStatusFilter.Approved => "WHERE rv.is_approved = true",
            _ => string.Empty,
        };

        await using var connection = await connections.OpenAsync(cancellationToken);

        var reviews = await connection.QueryAsync<AdminReview>(new CommandDefinition(
            $"""
            SELECT rv.id, rv.user_id, rv.reservation_id, rv.rating, rv.comment, rv.is_approved,
                   rv.admin_reply, rv.replied_at, rv.created_at,
                   u.first_name, u.last_name,
                   res.reservation_no,
                   c.brand, c.model
            FROM reviews rv
            LEFT JOIN users u ON u.id = rv.user_id
            LEFT JOIN reservations res ON res.id = rv.reservation_id
            LEFT JOIN cars c ON c.id = res.car_id
            {where}
            ORDER BY rv.created_at DESC
            LIMIT @Limit OFFSET @Offset
            """,
            new { paging.Limit, paging.Offset }, cancellationToken: cancellationToken));

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM reviews rv {where}", cancellationToken: cancellationToken));

        return new AdminReviewPage([.. reviews], total);
    }

    public async Task<Review?> SetApprovedAsync(string id, bool approved, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reviews SET is_approved = @approved WHERE id = @id", new { id, approved }, cancellationToken: cancellationToken));
        return await connection.QuerySingleOrDefaultAsync<Review>(new CommandDefinition(SelectReview, new { id }, cancellationToken: cancellationToken));
    }

    public async Task<Review?> ReplyAsync(string id, string reply, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reviews SET admin_reply = @reply, replied_at = NOW() WHERE id = @id", new { id, reply }, cancellationToken: cancellationToken));
        return await connection.QuerySingleOrDefaultAsync<Review>(new CommandDefinition(SelectReview, new { id }, cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM reviews WHERE id = @id", new { id }, cancellationToken: cancellationToken));
        return deleted > 0;
    }
}
