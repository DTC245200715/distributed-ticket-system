using DistributedTicketSystem.Contracts.Events;

namespace DistributedTicketSystem.AssignmentWorker.Messaging;

/// <summary>
/// Giao diện xử lý nghiệp vụ khi nhận được sự kiện tạo ticket.
/// </summary>
public interface ITicketProcessingService
{
    Task<bool> ProcessTicketAsync(TicketCreatedEvent ticketEvent, CancellationToken ct);
}
