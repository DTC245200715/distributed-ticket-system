using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.SupportApi.Data.Entities;

/// <summary>
/// Đại diện cho bảng TicketHistories trong cơ sở dữ liệu PostgreSQL.
/// Ghi vết từng lần thay đổi trạng thái, người xử lý hoặc lý do cập nhật.
/// </summary>
public class TicketHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Khóa ngoại tham chiếu đến Ticket.
    /// </summary>
    public Guid TicketId { get; set; }

    /// <summary>
    /// Trạng thái trước khi thay đổi (null khi tạo mới lần đầu).
    /// </summary>
    public TicketStatus? PreviousStatus { get; set; }

    /// <summary>
    /// Trạng thái mới sau khi thay đổi.
    /// </summary>
    public TicketStatus NewStatus { get; set; }

    /// <summary>
    /// Nhân viên xử lý tại thời điểm ghi nhận lịch sử.
    /// </summary>
    public string? AssignedTo { get; set; }

    /// <summary>
    /// Nhóm xử lý tại thời điểm ghi nhận lịch sử.
    /// </summary>
    public string? AssignedGroup { get; set; }

    /// <summary>
    /// Ai là người thực hiện thay đổi (khách hàng, nhân viên, hoặc AssignmentWorker).
    /// </summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>
    /// Lý do thực hiện thay đổi trạng thái.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Thời điểm thay đổi diễn ra (UTC).
    /// </summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property trỏ về Ticket cha.
    /// </summary>
    public Ticket? Ticket { get; set; }
}
