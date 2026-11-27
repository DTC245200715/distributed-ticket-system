using DistributedTicketSystem.Contracts.Events;
using DistributedTicketSystem.NotificationService.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DistributedTicketSystem.NotificationService.Services;

/// <summary>
/// Triển khai đẩy thông báo realtime tới đúng nhóm khách hàng (userId) thông qua SignalR Hub.
/// </summary>
public class SignalRNotificationDispatcher(
    IHubContext<NotificationHub> hubContext,
    ILogger<SignalRNotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task DispatchAsync(TicketStatusChangedEvent statusEvent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(statusEvent.UserId))
        {
            logger.LogWarning("[Notification] [CorrelationId: {CorrId}] Sự kiện Ticket '{TicketId}' thiếu UserId, bỏ qua bước đẩy SignalR.",
                statusEvent.CorrelationId, statusEvent.TicketId);
            return;
        }

        var targetGroup = statusEvent.UserId.Trim();

        await hubContext.Clients
            .Group(targetGroup)
            .SendAsync(NotificationHub.ReceiveTicketNotificationMethod, statusEvent, ct);

        logger.LogInformation(
            "[SignalR Push] [CorrelationId: {CorrId}] Đã đẩy thông báo Ticket '{TicketId}' ({OldStatus} -> {NewStatus}, Agent: '{Agent}') tới nhóm '{Group}'.",
            statusEvent.CorrelationId,
            statusEvent.TicketId,
            statusEvent.OldStatus,
            statusEvent.NewStatus,
            statusEvent.AssignedTo ?? "N/A",
            targetGroup);
    }
}
