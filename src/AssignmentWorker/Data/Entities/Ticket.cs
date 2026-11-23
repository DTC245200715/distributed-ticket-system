using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.AssignmentWorker.Data.Entities;

/// <summary>
/// Đại diện cho bảng Tickets trong PostgreSQL từ góc nhìn của Assignment Worker.
/// </summary>
public class Ticket
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public TicketPriority Priority { get; set; }

    public TicketStatus Status { get; set; }

    public string? AssignedTo { get; set; }

    public string? AssignedGroup { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Concurrency token ánh xạ trường hệ thống xmin của PostgreSQL để khóa lạc quan.
    /// </summary>
    public uint Version { get; set; }

    public List<TicketHistory> Histories { get; set; } = [];
}
