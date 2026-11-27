using Microsoft.Extensions.Logging;
using NexoraRent.Core.Notifications;

namespace NexoraRent.Infrastructure.Notifications;

/// <summary>
/// <c>sendNotification</c>: fills the active WhatsApp template and logs the message as sent. Nothing
/// is sent from the server; the admin opens the returned wa.me link.
/// </summary>
public sealed partial class NotificationSender(INotificationRepository repository, ILogger<NotificationSender> logger) : INotificationSender
{
    public async Task<NotificationSendResult> SendAsync(
        string type, string recipient, string? reservationId, string? userId,
        IReadOnlyDictionary<string, string?>? extraData, CancellationToken cancellationToken)
    {
        var template = await repository.ActiveWhatsAppTemplateAsync(type, cancellationToken);
        if (template is null)
        {
            return new NotificationSendResult(NotificationSendStatus.NoTemplate);
        }

        ReservationNotificationData? reservation = null;
        if (reservationId is not null)
        {
            // Express renders without the booking and then fails on the log row's foreign key (a 500).
            reservation = await repository.ReservationDataAsync(reservationId, cancellationToken);
            if (reservation is null)
            {
                return new NotificationSendResult(NotificationSendStatus.ReservationNotFound);
            }
        }

        var message = WhatsAppMessage.Render(template, WhatsAppMessage.TemplateData(extraData, reservation));
        var link = WhatsAppMessage.Link(recipient, message);
        // Express logs a manual send without a user, so the log showed no customer name for it.
        userId ??= reservation?.UserId;
        var logId = await repository.LogSentAsync(type, recipient, reservationId, userId, cancellationToken);

        LogCreated(logger, type, reservationId, userId);
        return new NotificationSendResult(NotificationSendStatus.Sent, message, link, logId);
    }

    // The same event name and fields as Express's `whatsapp_notification_created`; no phone number or text.
    [LoggerMessage(Level = LogLevel.Information, Message = "whatsapp_notification_created {Type} {ReservationId} {UserId}")]
    private static partial void LogCreated(ILogger logger, string type, string? reservationId, string? userId);
}
