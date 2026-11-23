using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.AssignmentWorker.Services;

/// <summary>
/// Kết quả sau khi tính toán phân công nhóm và nhân viên xử lý.
/// </summary>
public record AssignmentResult(string Group, string Agent);

/// <summary>
/// Giao diện cho chiến lược phân công ticket.
/// Được thiết kế thành lớp thuần (Pure logic), hoàn toàn không phụ thuộc vào Database hay Message Broker.
/// </summary>
public interface IAssignmentStrategy
{
    /// <summary>
    /// Phân công nhóm và nhân viên dựa trên mức độ ưu tiên và danh sách nhân viên không khả dụng (nếu có).
    /// </summary>
    AssignmentResult Assign(TicketPriority priority, IReadOnlySet<string>? unavailableAgents = null);
}
