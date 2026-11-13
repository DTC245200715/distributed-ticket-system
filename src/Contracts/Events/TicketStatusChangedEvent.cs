using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.Contracts.Events;

/// <summary>
/// Sự kiện phát sinh khi trạng thái hoặc người/nhóm xử lý của ticket thay đổi.
/// Được tiêu thụ bởi Notification Service để đẩy thông báo realtime tới Client.
/// </summary>
public record TicketStatusChangedEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid TicketId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public TicketStatus OldStatus { get; init; }
    public TicketStatus NewStatus { get; init; }
    public string? AssignedTo { get; init; }
    public string? AssignedGroup { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
