using DistributedTicketSystem.Contracts.Events;

namespace DistributedTicketSystem.NotificationService.Services;

/// <summary>
/// Giao diện phân phối thông báo khi nhận được sự kiện thay đổi trạng thái ticket.
/// Tách biệt tầng vận chuyển RabbitMQ khỏi tầng đẩy SignalR để dễ dàng viết Unit Test.
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(TicketStatusChangedEvent statusEvent, CancellationToken ct = default);
}
