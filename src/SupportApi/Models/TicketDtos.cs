using DistributedTicketSystem.Contracts.Enums;
using DistributedTicketSystem.SupportApi.Data.Entities;

namespace DistributedTicketSystem.SupportApi.Models;

/// <summary>
/// Dữ liệu đầu vào khi khách hàng tạo yêu cầu hỗ trợ mới (POST /tickets).
/// </summary>
public record CreateTicketRequest(
    string UserId,
    string Title,
    string Description,
    TicketPriority Priority
);

/// <summary>
/// Dữ liệu đầu vào khi cập nhật trạng thái yêu cầu (PATCH /tickets/{id}/status).
/// </summary>
public record UpdateTicketStatusRequest(
    TicketStatus NewStatus,
    string ChangedBy,
    string? Reason,
    string? AssignedTo = null,
    string? AssignedGroup = null
);

/// <summary>
/// Dữ liệu trả về rút gọn cho danh sách ticket (GET /tickets, GET /users/{userId}/tickets).
/// </summary>
public record TicketResponse(
    Guid Id,
    string UserId,
    string Title,
    string Description,
    TicketPriority Priority,
    TicketStatus Status,
    string? AssignedTo,
    string? AssignedGroup,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static TicketResponse FromEntity(Ticket ticket) =>
        new(
            ticket.Id,
            ticket.UserId,
            ticket.Title,
            ticket.Description,
            ticket.Priority,
            ticket.Status,
            ticket.AssignedTo,
            ticket.AssignedGroup,
            ticket.CreatedAt,
            ticket.UpdatedAt
        );
}

/// <summary>
/// Dữ liệu lịch sử thay đổi của ticket.
/// </summary>
public record TicketHistoryResponse(
    Guid Id,
    Guid TicketId,
    TicketStatus? PreviousStatus,
    TicketStatus NewStatus,
    string? AssignedTo,
    string? AssignedGroup,
    string ChangedBy,
    string? Reason,
    DateTime ChangedAt
)
{
    public static TicketHistoryResponse FromEntity(TicketHistory history) =>
        new(
            history.Id,
            history.TicketId,
            history.PreviousStatus,
            history.NewStatus,
            history.AssignedTo,
            history.AssignedGroup,
            history.ChangedBy,
            history.Reason,
            history.ChangedAt
        );
}

/// <summary>
/// Dữ liệu trả về chi tiết của 1 ticket kèm toàn bộ lịch sử (GET /tickets/{id}).
/// </summary>
public record TicketDetailResponse(
    Guid Id,
    string UserId,
    string Title,
    string Description,
    TicketPriority Priority,
    TicketStatus Status,
    string? AssignedTo,
    string? AssignedGroup,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<TicketHistoryResponse> Histories
)
{
    public static TicketDetailResponse FromEntity(Ticket ticket) =>
        new(
            ticket.Id,
            ticket.UserId,
            ticket.Title,
            ticket.Description,
            ticket.Priority,
            ticket.Status,
            ticket.AssignedTo,
            ticket.AssignedGroup,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.Histories.OrderByDescending(h => h.ChangedAt).Select(TicketHistoryResponse.FromEntity).ToList()
        );
}
