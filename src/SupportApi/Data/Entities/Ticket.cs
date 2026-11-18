using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.SupportApi.Data.Entities;

/// <summary>
/// Đại diện cho bảng Tickets trong cơ sở dữ liệu PostgreSQL.
/// Lưu trữ toàn bộ thông tin của yêu cầu hỗ trợ khách hàng.
/// </summary>
public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã khách hàng gửi yêu cầu hỗ trợ.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Tiêu đề tóm tắt yêu cầu.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết vấn đề cần hỗ trợ.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Mức độ ưu tiên: Low, Medium, High, Urgent.
    /// </summary>
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    /// <summary>
    /// Trạng thái vòng đời: New -> Assigned -> InProgress -> Resolved.
    /// </summary>
    public TicketStatus Status { get; set; } = TicketStatus.New;

    /// <summary>
    /// Nhân viên được phân công xử lý (nullable khi mới tạo ở trạng thái New).
    /// </summary>
    public string? AssignedTo { get; set; }

    /// <summary>
    /// Nhóm chịu trách nhiệm (ví dụ: L1_Helpdesk_Support, L2_Senior_Support, L3_Critical_Support).
    /// </summary>
    public string? AssignedGroup { get; set; }

    /// <summary>
    /// Thời điểm tạo yêu cầu (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật trạng thái/thông tin cuối cùng (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Khóa lạc quan (Concurrency Token) để ngăn xung đột khi nhiều tiến trình cùng cập nhật 1 ticket.
    /// PostgreSQL xmin system column ánh xạ thành uint.
    /// </summary>
    public uint Version { get; set; }

    /// <summary>
    /// Danh sách toàn bộ lịch sử thay đổi của ticket (Quan hệ 1-N).
    /// </summary>
    public List<TicketHistory> Histories { get; set; } = [];
}
