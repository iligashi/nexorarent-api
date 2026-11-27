using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core;
using NexoraRent.Core.Notifications;

namespace NexoraRent.Api.Endpoints;

/// <summary>
/// Ported from <c>server/src/routes/admin.notifications.routes.js</c>. Staff can read templates, the
/// log and the stats; changing templates and preparing messages needs a manager or owner.
/// </summary>
internal static class AdminNotificationEndpoints
{
    private const int DefaultLogLimit = 30;

    public static RouteGroupBuilder MapAdminNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/admin/notifications").WithTags("Admin: notifications").RequireAdmin();

        notifications.MapGet("/templates", ListTemplatesAsync).WithName("AdminListNotificationTemplates");
        notifications.MapPut("/templates/{id}", UpdateTemplateAsync).RequireManager().WithName("AdminUpdateNotificationTemplate");
        notifications.MapGet("/log", ListLogAsync).WithName("AdminNotificationLog");
        notifications.MapPost("/send", SendAsync).RequireManager().WithName("AdminSendNotification");
        notifications.MapGet("/stats", StatsAsync).WithName("AdminNotificationStats");

        return api;
    }

    private static async Task<IResult> ListTemplatesAsync(INotificationRepository repository, CancellationToken cancellationToken) =>
        Results.Ok(new { templates = await repository.ListTemplatesAsync(cancellationToken) });

    private static async Task<IResult> UpdateTemplateAsync(
        string id, HttpContext context, INotificationRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (changes, error) = NotificationRequestReader.ReadTemplateChanges(body);
        if (changes is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        // Express answers 200 with `{}` for a missing template; a 404 tells the client it is gone.
        return await repository.UpdateTemplateAsync(id, changes, cancellationToken) is { } template
            ? Results.Ok(new { template })
            : ErrorHandling.Error(StatusCodes.Status404NotFound, "Template not found");
    }

    private static async Task<IResult> ListLogAsync(
        string? page, string? limit, string? status, INotificationRepository repository, CancellationToken cancellationToken)
    {
        // `if (status)`: an empty status lists everything.
        var result = await repository.ListLogAsync(
            string.IsNullOrEmpty(status) ? null : status, Paging.FromRaw(page, limit, DefaultLogLimit), cancellationToken);
        return Results.Ok(new { notifications = result.Notifications, total = result.Total });
    }

    private static async Task<IResult> SendAsync(HttpContext context, INotificationSender sender, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (request, error) = NotificationRequestReader.ReadSend(body);
        if (request is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        var result = await sender.SendAsync(request.Type, request.Recipient, request.ReservationId, userId: null, extraData: null, cancellationToken);
        return result.Status switch
        {
            // Express answers 200 with `success: false` when no template is active.
            NotificationSendStatus.NoTemplate => Results.Ok(new { success = false, error = "No active template found" }),
            NotificationSendStatus.ReservationNotFound => ErrorHandling.Error(StatusCodes.Status404NotFound, "Reservation not found"),
            _ => Results.Ok(new { success = true, message = result.Message, whatsapp_url = result.WhatsAppUrl, log_id = result.LogId }),
        };
    }

    private static async Task<IResult> StatsAsync(INotificationRepository repository, CancellationToken cancellationToken) =>
        Results.Ok(await repository.StatsAsync(cancellationToken));
}
