using Dapper;
using NexoraRent.Core;
using NexoraRent.Core.Notifications;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Notifications;

/// <summary>Mirrors <c>server/src/routes/admin.notifications.routes.js</c> in the Express API.</summary>
public sealed class NotificationRepository(MySqlConnectionFactory connections) : INotificationRepository
{
    private const string TemplateColumns = "id, type, channel, template_name, template_body, is_active";

    public async Task<IReadOnlyList<NotificationTemplate>> ListTemplatesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // `type` is an ENUM, so this sorts by its position in the enum, as in Express.
        var rows = await connection.QueryAsync<NotificationTemplate>(new CommandDefinition(
            $"SELECT {TemplateColumns} FROM notification_templates ORDER BY type", cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<NotificationTemplate?> UpdateTemplateAsync(string id, NotificationTemplateChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Each column comes from a fixed list; only the values are parameters.
        var sets = new List<string>();
        var parameters = new DynamicParameters(new { id });
        if (changes.TemplateBody.IsSet)
        {
            sets.Add("template_body = @template_body");
            parameters.Add("template_body", changes.TemplateBody.Value);
        }

        if (changes.IsActive.IsSet)
        {
            sets.Add("is_active = @is_active");
            parameters.Add("is_active", changes.IsActive.Value);
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE notification_templates SET {string.Join(", ", sets)} WHERE id = @id", parameters, cancellationToken: cancellationToken));
        return await connection.QuerySingleOrDefaultAsync<NotificationTemplate>(new CommandDefinition(
            $"SELECT {TemplateColumns} FROM notification_templates WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task<NotificationLogPage> ListLogAsync(string? status, Paging paging, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paging);

        // Fixed SQL either way; the status is a parameter.
        var where = status is null ? "" : "WHERE nl.status = @status";

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<NotificationLogEntry>(new CommandDefinition(
            $"""
            SELECT nl.id, nl.reservation_id, nl.user_id, nl.type, nl.channel, nl.recipient, nl.status,
                   nl.error_message, nl.sent_at, nl.created_at,
                   COALESCE(r.reservation_no, '') AS reservation_no,
                   COALESCE(CONCAT(u.first_name, ' ', u.last_name), '') AS customer_name
            FROM notification_log nl
            LEFT JOIN reservations r ON r.id = nl.reservation_id
            LEFT JOIN users u ON u.id = nl.user_id
            {where}
            ORDER BY nl.created_at DESC
            LIMIT @Limit OFFSET @Offset
            """,
            new { status, paging.Limit, paging.Offset },
            cancellationToken: cancellationToken));

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM notification_log nl {where}", new { status }, cancellationToken: cancellationToken));

        return new NotificationLogPage([.. rows], total);
    }

    public async Task<NotificationStats> StatsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<NotificationStats>(new CommandDefinition(
            """
            SELECT COUNT(*) AS Total,
                   COALESCE(SUM(status = 'sent'), 0) AS Sent,
                   COALESCE(SUM(status = 'failed'), 0) AS Failed,
                   COALESCE(SUM(status = 'pending'), 0) AS Pending
            FROM notification_log
            """,
            cancellationToken: cancellationToken));
    }

    public async Task<string?> ActiveWhatsAppTemplateAsync(string type, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT template_body FROM notification_templates WHERE type = @type AND channel = 'whatsapp' AND is_active = true LIMIT 1",
            new { type },
            cancellationToken: cancellationToken));
    }

    public async Task<ReservationNotificationData?> ReservationDataAsync(string reservationId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ReservationNotificationData>(new CommandDefinition(
            """
            SELECT r.reservation_no, r.guest_name, r.pickup_date, r.dropoff_date, r.total_price, r.status,
                   c.brand, c.model,
                   COALESCE(r.guest_name, CONCAT(u.first_name, ' ', u.last_name)) AS customer_name,
                   pl.name AS pickup_location, dl.name AS dropoff_location
            FROM reservations r
            JOIN cars c ON c.id = r.car_id
            LEFT JOIN users u ON u.id = r.user_id
            LEFT JOIN locations pl ON pl.id = r.pickup_location
            LEFT JOIN locations dl ON dl.id = r.dropoff_location
            WHERE r.id = @reservationId
            """,
            new { reservationId },
            cancellationToken: cancellationToken));
    }

    public async Task<string> LogSentAsync(string type, string recipient, string? reservationId, string? userId, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        await using var connection = await connections.OpenAsync(cancellationToken);

        // sent_at is a TIMESTAMP, which MySQL converts from the session time zone, so NOW() is right here.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO notification_log (id, reservation_id, user_id, type, channel, recipient, status, sent_at)
            VALUES (@id, @reservationId, @userId, @type, 'whatsapp', @recipient, 'sent', NOW())
            """,
            new { id, reservationId, userId, type, recipient },
            cancellationToken: cancellationToken));
        return id;
    }
}
