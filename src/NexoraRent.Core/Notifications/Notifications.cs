namespace NexoraRent.Core.Notifications;

/// <summary>A row of <c>notification_templates</c>.</summary>
public sealed record NotificationTemplate
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Channel { get; init; }
    public required string TemplateName { get; init; }
    public required string TemplateBody { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>A row of <c>notification_log</c> with its reservation number and customer name ("" when there is none).</summary>
public sealed record NotificationLogEntry
{
    public required string Id { get; init; }
    public string? ReservationId { get; init; }
    public string? UserId { get; init; }
    public required string Type { get; init; }
    public required string Channel { get; init; }
    public required string Recipient { get; init; }
    public string? Status { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? SentAt { get; init; }
    public DateTime? CreatedAt { get; init; }
    public required string ReservationNo { get; init; }
    public required string CustomerName { get; init; }
}

public sealed record NotificationLogPage(IReadOnlyList<NotificationLogEntry> Notifications, int Total);

/// <summary>Settable properties, so Dapper converts the COUNT and SUM column types itself.</summary>
public sealed record NotificationStats
{
    public int Total { get; init; }
    public int Sent { get; init; }
    public int Failed { get; init; }
    public int Pending { get; init; }
}

/// <summary>The fields sent to <c>PUT /admin/notifications/templates/:id</c>.</summary>
public sealed record NotificationTemplateChanges
{
    public FieldChange<string> TemplateBody { get; init; }
    public FieldChange<bool> IsActive { get; init; }

    public bool IsEmpty => !TemplateBody.IsSet && !IsActive.IsSet;
}

/// <summary>A manual send from the admin page.</summary>
public sealed record NotificationRequest(string Type, string Recipient, string? ReservationId);

/// <summary>What a reservation contributes to a template (the query in <c>sendNotification</c>).</summary>
public sealed record ReservationNotificationData
{
    public required string ReservationNo { get; init; }

    /// <summary>The registered customer, or null for a guest booking.</summary>
    public string? UserId { get; init; }

    public string? CustomerName { get; init; }
    public string? GuestName { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public DateTime PickupDate { get; init; }
    public DateTime DropoffDate { get; init; }
    public string? PickupLocation { get; init; }
    public string? DropoffLocation { get; init; }
    public decimal TotalPrice { get; init; }
    public string? Status { get; init; }
}

public enum NotificationSendStatus
{
    Sent,
    NoTemplate,
    ReservationNotFound,
}

/// <summary>A prepared WhatsApp message: its text, the wa.me link that sends it, and its log row.</summary>
public sealed record NotificationSendResult(NotificationSendStatus Status, string? Message = null, string? WhatsAppUrl = null, string? LogId = null);

/// <summary>Mirrors <c>server/src/routes/admin.notifications.routes.js</c>.</summary>
public interface INotificationRepository
{
    Task<IReadOnlyList<NotificationTemplate>> ListTemplatesAsync(CancellationToken cancellationToken);

    /// <summary>Null when there is no such template.</summary>
    Task<NotificationTemplate?> UpdateTemplateAsync(string id, NotificationTemplateChanges changes, CancellationToken cancellationToken);

    /// <param name="status">Compared with <c>notification_log.status</c>; null lists every entry.</param>
    Task<NotificationLogPage> ListLogAsync(string? status, Paging paging, CancellationToken cancellationToken);

    Task<NotificationStats> StatsAsync(CancellationToken cancellationToken);

    /// <summary>The active WhatsApp template body for a type, or null.</summary>
    Task<string?> ActiveWhatsAppTemplateAsync(string type, CancellationToken cancellationToken);

    Task<ReservationNotificationData?> ReservationDataAsync(string reservationId, CancellationToken cancellationToken);

    /// <summary>Logs a WhatsApp message as sent and returns the log id.</summary>
    Task<string> LogSentAsync(string type, string recipient, string? reservationId, string? userId, CancellationToken cancellationToken);
}

/// <summary>Prepares WhatsApp messages; the admin page and reservation status changes both use it.</summary>
public interface INotificationSender
{
    /// <param name="extraData">Template values that the reservation's values override.</param>
    Task<NotificationSendResult> SendAsync(
        string type, string recipient, string? reservationId, string? userId,
        IReadOnlyDictionary<string, string?>? extraData, CancellationToken cancellationToken);
}
