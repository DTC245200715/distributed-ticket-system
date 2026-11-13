using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.Contracts.Events;

/// <summary>
/// Sự kiện phát sinh khi một yêu cầu hỗ trợ mới được tạo thành công trong Support API.
/// </summary>
public record TicketCreatedEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid TicketId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TicketPriority Priority { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
