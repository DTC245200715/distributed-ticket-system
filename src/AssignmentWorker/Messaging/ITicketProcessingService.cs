using System.Text;
using System.Text.Json;
using DistributedTicketSystem.Contracts.Constants;
using DistributedTicketSystem.Contracts.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DistributedTicketSystem.AssignmentWorker.Messaging;

/// <summary>
/// Giao diện xử lý nghiệp vụ khi nhận được sự kiện tạo ticket.
/// Sẽ được triển khai chi tiết ở Bước 4 (Thuật toán) và Bước 5 (Cập nhật DB).
/// </summary>
public interface ITicketProcessingService
{
    Task<bool> ProcessTicketAsync(TicketCreatedEvent ticketEvent, CancellationToken ct);
}

/// <summary>
/// Lớp triển khai tạm thời cho Bước 3 để kiểm thử luồng nhận message, deserialize và Ack.
/// </summary>
public class StubTicketProcessingService(ILogger<StubTicketProcessingService> logger) : ITicketProcessingService
{
    public Task<bool> ProcessTicketAsync(TicketCreatedEvent ticketEvent, CancellationToken ct)
    {
        logger.LogInformation("[StubProcessor] Đã nhận TicketId: '{TicketId}', UserId: '{UserId}', Priority: {Priority}, Tiêu đề: '{Title}'",
            ticketEvent.TicketId, ticketEvent.UserId, ticketEvent.Priority, ticketEvent.Title);
        return Task.FromResult(true);
    }
}
