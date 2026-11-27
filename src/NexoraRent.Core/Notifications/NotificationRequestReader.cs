using System.Text.Json;

namespace NexoraRent.Core.Notifications;

/// <summary>Reads the bodies of the <c>/admin/notifications</c> writes.</summary>
public static class NotificationRequestReader
{
    public const string SendRequiredMessage = "type and recipient required";
    public const string NoFieldsMessage = "No fields";

    private const int MaxTypeLength = 50;
    private const int MaxRecipientLength = 255;
    private const int MaxIdLength = 36;
    private const int MaxTemplateLength = 65_535;

    public static (NotificationTemplateChanges? Changes, string? Error) ReadTemplateChanges(JsonElement body)
    {
        var reader = new BodyReader(body);
        var changes = new NotificationTemplateChanges();

        if (reader.Has("template_body", out _)) changes = changes with { TemplateBody = reader.RequiredText("template_body", MaxTemplateLength)! };
        if (reader.Has("is_active", out _)) changes = changes with { IsActive = reader.Flag("is_active") };

        if (reader.Error is not null)
        {
            return (null, reader.Error);
        }

        return changes.IsEmpty ? (null, NoFieldsMessage) : (changes, null);
    }

    public static (NotificationRequest? Request, string? Error) ReadSend(JsonElement body)
    {
        var reader = new BodyReader(body);

        // `if (!type || !recipient)`
        if (!reader.IsTruthy("type") || !reader.IsTruthy("recipient"))
        {
            return (null, SendRequiredMessage);
        }

        var type = reader.RequiredText("type", MaxTypeLength);
        var recipient = reader.RequiredText("recipient", MaxRecipientLength);
        var reservationId = reader.OptionalText("reservation_id", MaxIdLength);

        return reader.Error is null ? (new NotificationRequest(type!, recipient!, reservationId), null) : (null, reader.Error);
    }
}
