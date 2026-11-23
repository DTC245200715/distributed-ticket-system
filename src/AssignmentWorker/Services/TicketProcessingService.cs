using DistributedTicketSystem.AssignmentWorker.Data;
using DistributedTicketSystem.AssignmentWorker.Data.Entities;
using DistributedTicketSystem.AssignmentWorker.Messaging;
using DistributedTicketSystem.Contracts.Constants;
using DistributedTicketSystem.Contracts.Enums;
using DistributedTicketSystem.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace DistributedTicketSystem.AssignmentWorker.Services;

/// <summary>
/// Dịch vụ xử lý nghiệp vụ chính của Worker:
/// Kiểm tra Idempotency, tính toán phân công, mở Transaction cập nhật PostgreSQL,
/// kiểm soát tranh chấp đồng thời (Optimistic Concurrency), ghi nhận TicketHistory
/// và phát sự kiện TicketStatusChangedEvent sang RabbitMQ.
/// </summary>
public class TicketProcessingService : ITicketProcessingService
{
    private readonly WorkerDbContext _db;
    private readonly IAssignmentStrategy _assignmentStrategy;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<TicketProcessingService> _logger;

    public TicketProcessingService(
        WorkerDbContext db,
        IAssignmentStrategy assignmentStrategy,
        IMessagePublisher publisher,
        ILogger<TicketProcessingService> logger)
    {
        _db = db;
        _assignmentStrategy = assignmentStrategy;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<bool> ProcessTicketAsync(TicketCreatedEvent ticketEvent, CancellationToken ct)
    {
        _logger.LogInformation("[ProcessingService] [CorrelationId: {CorrId}] Bắt đầu xử lý Ticket '{TicketId}' (UserId: '{UserId}', Priority: {Priority})",
            ticketEvent.CorrelationId, ticketEvent.TicketId, ticketEvent.UserId, ticketEvent.Priority);

        // 1. Mở Database Transaction để đảm bảo tính toàn vẹn (ACID)
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        Ticket ticket;
        AssignmentResult assignment;
        TicketStatus oldStatus;
        DateTime changedAt;

        try
        {
            // 2. Tra cứu Ticket từ PostgreSQL
            var existingTicket = await _db.Tickets
                .FirstOrDefaultAsync(t => t.Id == ticketEvent.TicketId, ct);

            if (existingTicket is null)
            {
                _logger.LogWarning("[ProcessingService] [CorrelationId: {CorrId}] Không tìm thấy Ticket '{TicketId}' trong PostgreSQL. Yêu cầu retry...",
                    ticketEvent.CorrelationId, ticketEvent.TicketId);
                throw new InvalidOperationException($"Ticket '{ticketEvent.TicketId}' chưa sẵn sàng trong PostgreSQL.");
            }

            ticket = existingTicket;

            // 3. Kiểm tra tính Bất biến / Lũy kế (Idempotency)
            if (ticket.Status != TicketStatus.New)
            {
                _logger.LogWarning("[Idempotent] [CorrelationId: {CorrId}] Ticket '{TicketId}' hiện đã ở trạng thái '{Status}'. Bỏ qua việc gán lại để bảo toàn dữ liệu.",
                    ticketEvent.CorrelationId, ticket.Id, ticket.Status);

                return true;
            }

            // 4. Áp dụng thuật toán Priority + Round-Robin thuần
            assignment = _assignmentStrategy.Assign(ticketEvent.Priority);

            _logger.LogInformation("[Assignment] [CorrelationId: {CorrId}] Đã phân công Ticket '{TicketId}': Nhóm '{Group}', Nhân viên '{Agent}'",
                ticketEvent.CorrelationId, ticket.Id, assignment.Group, assignment.Agent);

            oldStatus = ticket.Status;
            changedAt = DateTime.UtcNow;

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

            // 8. Commit Transaction vào PostgreSQL
            await transaction.CommitAsync(ct);

            _logger.LogInformation("[DB Commit] [CorrelationId: {CorrId}] Đã lưu thành công Ticket '{TicketId}' sang trạng thái 'Assigned' trong PostgreSQL.",
                ticketEvent.CorrelationId, ticket.Id);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogWarning(ex, "[Concurrency] [CorrelationId: {CorrId}] Phát hiện xung đột phiên bản khi cập nhật Ticket '{TicketId}'.",
                ticketEvent.CorrelationId, ticketEvent.TicketId);

            var currentTicket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketEvent.TicketId, ct);
            if (currentTicket is not null && currentTicket.Status != TicketStatus.New)
            {
                _logger.LogInformation("[Concurrency Resolved] [CorrelationId: {CorrId}] Ticket '{TicketId}' đã được cập nhật sang '{Status}' bởi tiến trình song song.",
                    ticketEvent.CorrelationId, ticketEvent.TicketId, currentTicket.Status);
                return true;
            }

            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "[ProcessingService] [CorrelationId: {CorrId}] Lỗi cập nhật PostgreSQL cho Ticket '{TicketId}': {Message}",
                ticketEvent.CorrelationId, ticketEvent.TicketId, ex.Message);
            throw;
        }

        // =========================================================================
        // 9. PHÁT SỰ KIỆN TICKET STATUS CHANGED LÊN RABBITMQ (POST-COMMIT)
        // =========================================================================
        var statusChangedEvent = new TicketStatusChangedEvent
        {
            EventId = Guid.NewGuid(),
            TicketId = ticket.Id,
            UserId = ticket.UserId,
            OldStatus = oldStatus,
            NewStatus = TicketStatus.Assigned,
            AssignedTo = assignment.Agent,
            AssignedGroup = assignment.Group,
            ChangedBy = "AssignmentWorker",
            Reason = "Tự động phân công theo quy tắc Priority + Round-Robin",
            ChangedAt = changedAt,
            CorrelationId = ticketEvent.CorrelationId
        };

        try
        {
            await _publisher.PublishAsync(
                routingKey: RabbitMqConstants.RoutingKeyStatusChanged,
                message: statusChangedEvent,
                correlationId: ticketEvent.CorrelationId,
                ct: ct);

            _logger.LogInformation("[Event Published] [CorrelationId: {CorrId}] Đã phát sự kiện TicketStatusChangedEvent cho Ticket '{TicketId}' vào exchange '{Exchange}'.",
                ticketEvent.CorrelationId, ticket.Id, RabbitMqConstants.ExchangeName);
        }
        catch (Exception ex)
        {
            // GIẢI PHÁP CHO BÀI TẬP LỚN KHI GẶP VẤN ĐỀ DUAL-WRITE:
            // Dữ liệu trong DB đã commit an toàn. Nếu broker tạm thời ngắt kết nối,
            // ta ghi log mức Critical để cảnh báo nhưng KHÔNG ném ngoại lệ làm crash worker,
            // bảo đảm tính nhất quán trạng thái dữ liệu (Eventual Consistency).
            _logger.LogCritical(ex, "[Event Publish Failed] [CorrelationId: {CorrId}] Không thể phát sự kiện TicketStatusChangedEvent cho Ticket '{TicketId}': {Message}. Dữ liệu trong DB vẫn an toàn.",
                ticketEvent.CorrelationId, ticket.Id, ex.Message);
        }

        return true;
    }
}
