namespace DistributedTicketSystem.Contracts.Constants;

/// <summary>
/// Các hằng số cấu hình RabbitMQ Topology dùng chung cho toàn bộ hệ thống.
/// </summary>
public static class RabbitMqConstants
{
    /// <summary>
    /// Tên Exchange trung tâm phân phối toàn bộ sự kiện liên quan đến Ticket.
    /// </summary>
    public const string ExchangeName = "ticket.exchange";

    /// <summary>
    /// Hàng đợi tiếp nhận ticket mới để Assignment Worker xử lý phân loại/phân công.
    /// </summary>
    public const string CreationQueue = "ticket.creation.queue";

    /// <summary>
    /// Hàng đợi tiếp nhận sự kiện thay đổi trạng thái/phân công để Notification Service đẩy SignalR.
    /// </summary>
    public const string NotificationQueue = "ticket.notification.queue";

    /// <summary>
    /// Routing key khi ticket mới được tạo thành công trên Support API.
    /// </summary>
    public const string RoutingKeyCreated = "ticket.created";

    /// <summary>
    /// Routing key khi trạng thái hoặc người xử lý của ticket thay đổi.
    /// </summary>
    public const string RoutingKeyStatusChanged = "ticket.status.changed";
}
