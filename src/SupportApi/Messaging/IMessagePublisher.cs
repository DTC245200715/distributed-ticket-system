namespace DistributedTicketSystem.SupportApi.Messaging;

/// <summary>
/// Giao diện phát tán sự kiện/message vào RabbitMQ message broker.
/// </summary>
public interface IMessagePublisher : IAsyncDisposable
{
    /// <summary>
    /// Phát một sự kiện vào Exchange với routing key tương ứng.
    /// </summary>
    Task PublishAsync<T>(string routingKey, T message, string? correlationId = null, CancellationToken ct = default);
}
