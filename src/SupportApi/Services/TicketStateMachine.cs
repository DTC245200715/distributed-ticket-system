using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.SupportApi.Services;

/// <summary>
/// Bộ kiểm soát vòng đời trạng thái của Ticket (State Machine).
/// Đảm bảo tính nhất quán nghiệp vụ: New -> Assigned -> InProgress -> Resolved.
/// </summary>
public static class TicketStateMachine
{
    /// <summary>
    /// Kiểm tra tính hợp lệ của việc chuyển từ trạng thái hiện tại sang trạng thái mới.
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) ValidateTransition(TicketStatus currentStatus, TicketStatus newStatus)
    {
        if (currentStatus == newStatus)
        {
            return (false, $"Ticket hiện đã ở trạng thái '{currentStatus}'.");
        }

        if (currentStatus == TicketStatus.Resolved)
        {
            return (false, "Ticket đã ở trạng thái 'Resolved' (đã giải quyết), không thể thay đổi thêm.");
        }

        switch (currentStatus)
        {
            case TicketStatus.New:
                if (newStatus != TicketStatus.Assigned)
                {
                    return (false, $"Từ trạng thái 'New' chỉ được chuyển sang 'Assigned'. Không thể chuyển trực tiếp sang '{newStatus}'.");
                }
                break;

            case TicketStatus.Assigned:
                if (newStatus != TicketStatus.InProgress)
                {
                    return (false, $"Từ trạng thái 'Assigned' chỉ được chuyển sang 'InProgress' khi bắt đầu xử lý. Không thể nhảy cóc sang '{newStatus}'.");
                }
                break;

            case TicketStatus.InProgress:
                if (newStatus != TicketStatus.Resolved)
                {
                    return (false, $"Từ trạng thái 'InProgress' chỉ được chuyển sang 'Resolved' để hoàn thành ticket. Không hợp lệ với '{newStatus}'.");
                }
                break;

            default:
                return (false, $"Trạng thái không xác định: '{currentStatus}'.");
        }

        return (true, null);
    }
}
