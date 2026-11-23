using DistributedTicketSystem.AssignmentWorker.Data;
using DistributedTicketSystem.AssignmentWorker.Data.Entities;
using DistributedTicketSystem.AssignmentWorker.Messaging;
using DistributedTicketSystem.Contracts.Enums;
using DistributedTicketSystem.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace DistributedTicketSystem.AssignmentWorker.Services;

/// <summary>
/// Dịch vụ xử lý nghiệp vụ chính của Worker:
/// Kiểm tra Idempotency, tính toán phân công, mở Transaction cập nhật PostgreSQL,
/// kiểm soát tranh chấp đồng thời (Optimistic Concurrency) và ghi nhận TicketHistory.
/// </summary>
public class TicketProcessingService : ITicketProcessingService
{
    private readonly WorkerDbContext _db;
    private readonly IAssignmentStrategy _assignmentStrategy;
    private readonly ILogger<TicketProcessingService> _logger;

    public TicketProcessingService(
        WorkerDbContext db,
        IAssignmentStrategy assignmentStrategy,
        ILogger<TicketProcessingService> logger)
    {
        _db = db;
        _assignmentStrategy = assignmentStrategy;
        _logger = logger;
    }

    public async Task<bool> ProcessTicketAsync(TicketCreatedEvent ticketEvent, CancellationToken ct)
    {
        _logger.LogInformation("[ProcessingService] Bắt đầu xử lý gán Ticket '{TicketId}' (UserId: '{UserId}', Priority: {Priority})",
            ticketEvent.TicketId, ticketEvent.UserId, ticketEvent.Priority);

        // 1. Mở Database Transaction để đảm bảo tính toàn vẹn (ACID)
        // Cả việc cập nhật Ticket và tạo TicketHistory phải cùng thành công hoặc cùng rollback
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // 2. Tra cứu Ticket từ PostgreSQL
            var ticket = await _db.Tickets
                .FirstOrDefaultAsync(t => t.Id == ticketEvent.TicketId, ct);

            if (ticket is null)
            {
                // Nếu không tìm thấy ticket trong DB, quăng ngoại lệ để Consumer kích hoạt cơ chế Retry
                // (Phòng ngừa trường hợp trễ mạng giữa lúc API ghi DB và bắn message)
                _logger.LogWarning("[ProcessingService] Không tìm thấy Ticket '{TicketId}' trong PostgreSQL. Yêu cầu retry...",
                    ticketEvent.TicketId);
                throw new InvalidOperationException($"Ticket '{ticketEvent.TicketId}' chưa sẵn sàng trong PostgreSQL.");
            }

            // 3. Kiểm tra tính Bất biến / Lũy kế (Idempotency)
            // Nếu ticket này đã được gán trước đó (Status != New), bỏ qua không gán lại
            if (ticket.Status != TicketStatus.New)
            {
                _logger.LogWarning("[Idempotent] Ticket '{TicketId}' hiện đã ở trạng thái '{Status}' (đã gán cho '{Agent}' thuộc '{Group}'). Bỏ qua việc gán lại để bảo toàn dữ liệu.",
                    ticket.Id, ticket.Status, ticket.AssignedTo, ticket.AssignedGroup);

                // Trả về true để Consumer gửi BasicAck gỡ bỏ message trùng lặp khỏi hàng đợi
                return true;
            }

            // 4. Áp dụng thuật toán Priority + Round-Robin thuần
            var assignment = _assignmentStrategy.Assign(ticketEvent.Priority);

            _logger.LogInformation("[Assignment] Đã chọn phân công cho Ticket '{TicketId}': Nhóm '{Group}', Nhân viên '{Agent}'",
                ticket.Id, assignment.Group, assignment.Agent);

            var oldStatus = ticket.Status;
            var changedAt = DateTime.UtcNow;

            // 5. Cập nhật thực thể Ticket
            ticket.Status = TicketStatus.Assigned;
            ticket.AssignedGroup = assignment.Group;
            ticket.AssignedTo = assignment.Agent;
            ticket.UpdatedAt = changedAt;

            // 6. Tạo bản ghi lịch sử thay đổi (Audit History)
            var history = new TicketHistory
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                PreviousStatus = oldStatus,
                NewStatus = TicketStatus.Assigned,
                AssignedGroup = assignment.Group,
                AssignedTo = assignment.Agent,
                ChangedBy = "AssignmentWorker",
                Reason = "Tự động phân công theo quy tắc Priority + Round-Robin",
                ChangedAt = changedAt
            };

            _db.TicketHistories.Add(history);

            // 7. Lưu thay đổi xuống Database (kèm kiểm tra Concurrency Token Version)
            await _db.SaveChangesAsync(ct);

            // 8. Commit Transaction
            await transaction.CommitAsync(ct);

            _logger.LogInformation("[DB Commit] Đã cập nhật thành công Ticket '{TicketId}' sang trạng thái 'Assigned' trong PostgreSQL.",
                ticket.Id);

            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogWarning(ex, "[Concurrency] Phát hiện xung đột phiên bản dữ liệu khi cập nhật Ticket '{TicketId}'. Tiến trình khác có thể đã cập nhật.",
                ticketEvent.TicketId);

            // Kiểm tra lại xem ticket đã được cập nhật chưa
            var currentTicket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketEvent.TicketId, ct);
            if (currentTicket is not null && currentTicket.Status != TicketStatus.New)
            {
                _logger.LogInformation("[Concurrency Resolved] Ticket '{TicketId}' đã được cập nhật sang '{Status}' bởi tiến trình song song. Hoàn tất an toàn.",
                    ticketEvent.TicketId, currentTicket.Status);
                return true;
            }

            throw; // Quăng lỗi để Consumer retry nếu xung đột chưa được giải quyết
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "[ProcessingService] Lỗi khi xử lý cập nhật PostgreSQL cho Ticket '{TicketId}': {Message}",
                ticketEvent.TicketId, ex.Message);
            throw;
        }
    }
}
