using Microsoft.AspNetCore.SignalR;

namespace DistributedTicketSystem.NotificationService.Hubs;

/// <summary>
/// SignalR Hub quản lý kết nối realtime của Web Client theo từng nhóm người dùng (userId).
/// Cho phép khách hàng nhận thông báo tức thời khi ticket của họ thay đổi trạng thái hoặc được phân công.
/// </summary>
public class NotificationHub(ILogger<NotificationHub> logger) : Hub
{
    /// <summary>
    /// Tên sự kiện phía Client lắng nghe qua connection.on("ReceiveTicketNotification", ...).
    /// </summary>
    public const string ReceiveTicketNotificationMethod = "ReceiveTicketNotification";

    /// <summary>
    /// Tự động đưa kết nối vào nhóm userId nếu Client truyền ?userId=... trên query string khi bắt tay.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var normalizedUserId = userId.Trim();
            await Groups.AddToGroupAsync(Context.ConnectionId, normalizedUserId);
            logger.LogInformation("[SignalR] Kết nối '{ConnectionId}' đã tự động tham gia nhóm '{UserId}'.",
                Context.ConnectionId, normalizedUserId);
        }
        else
        {
            logger.LogInformation("[SignalR] Kết nối mới '{ConnectionId}' (chưa truyền userId qua query string).",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Ghi nhận log khi Client ngắt kết nối. SignalR tự động thu hồi ConnectionId khỏi các Group.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
        {
            logger.LogWarning(exception, "[SignalR] Kết nối '{ConnectionId}' bị ngắt bất thường: {Message}",
                Context.ConnectionId, exception.Message);
        }
        else
        {
            logger.LogInformation("[SignalR] Kết nối '{ConnectionId}' đã đóng.", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Cho phép Client chủ động đăng ký nhận thông báo cho một userId (hữu ích khi đổi user trên giao diện).
    /// </summary>
    public async Task JoinUserGroup(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("[SignalR] Kết nối '{ConnectionId}' gọi JoinUserGroup với userId rỗng.", Context.ConnectionId);
            return;
        }

        var normalizedUserId = userId.Trim();
        await Groups.AddToGroupAsync(Context.ConnectionId, normalizedUserId);
        logger.LogInformation("[SignalR] Kết nối '{ConnectionId}' đã tham gia nhóm '{UserId}' qua JoinUserGroup.",
            Context.ConnectionId, normalizedUserId);
    }

    /// <summary>
    /// Cho phép Client rời khỏi nhóm userId trước khi chuyển sang theo dõi userId khác.
    /// </summary>
    public async Task LeaveUserGroup(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var normalizedUserId = userId.Trim();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, normalizedUserId);
        logger.LogInformation("[SignalR] Kết nối '{ConnectionId}' đã rời nhóm '{UserId}'.",
            Context.ConnectionId, normalizedUserId);
    }
}
