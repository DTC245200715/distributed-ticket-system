using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.AssignmentWorker.Data.Entities;

/// <summary>
/// Đại diện cho bảng TicketHistories ghi nhận lịch sử thay đổi của ticket.
/// </summary>
public class TicketHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public TicketStatus? PreviousStatus { get; set; }

    public TicketStatus NewStatus { get; set; }

    public string? AssignedTo { get; set; }

    public string? AssignedGroup { get; set; }

    public string ChangedBy { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
