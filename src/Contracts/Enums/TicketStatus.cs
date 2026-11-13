namespace DistributedTicketSystem.Contracts.Enums;

/// <summary>
/// Trạng thái vòng đời của yêu cầu hỗ trợ (State Machine).
/// Luồng hợp lệ: New -> Assigned -> InProgress -> Resolved.
/// </summary>
public enum TicketStatus
{
    New = 1,
    Assigned = 2,
    InProgress = 3,
    Resolved = 4
}
