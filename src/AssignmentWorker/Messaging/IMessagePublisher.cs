namespace DistributedTicketSystem.AssignmentWorker.Messaging;

/// <summary>
/// Giao diện phát tán sự kiện lên Message Broker (RabbitMQ).
/// </summary>
public interface IMessagePublisher : IAsyncDisposable
{
    Task PublishAsync<T>(string routingKey, T message, string? correlationId = null, CancellationToken ct = default);
}
