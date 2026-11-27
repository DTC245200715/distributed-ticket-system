using System.Text.Json;
using DistributedTicketSystem.Contracts.Constants;
using DistributedTicketSystem.Contracts.Events;
using DistributedTicketSystem.NotificationService.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DistributedTicketSystem.NotificationService.Messaging;

/// <summary>
/// Background Service lắng nghe hàng đợi ticket.notification.queue trên RabbitMQ,
/// giải mã sự kiện TicketStatusChangedEvent và chuyển cho INotificationDispatcher đẩy qua SignalR.
/// Tích hợp QoS Prefetch, Manual Ack, Retry hữu hạn và Dead-Letter Queue (DLQ).
/// </summary>
public class RabbitMqNotificationConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<RabbitMqNotificationConsumer> _logger;

    private const int MaxRetryAttempts = 3;
    private const ushort PrefetchCount = 10;
    private const string DeadLetterExchange = "ticket.dlx";
    private const string DeadLetterQueue = "ticket.notification.dlq";
    private const string DeadLetterRoutingKey = "ticket.notification.deadletter";

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqNotificationConsumer(
        IConfiguration configuration,
        INotificationDispatcher dispatcher,
        ILogger<RabbitMqNotificationConsumer> logger)
    {
        _configuration = configuration;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Notification Consumer] Đang khởi động tiến trình lắng nghe RabbitMQ...");

        // Vòng lặp kết nối tự phục hồi khi Broker khởi động chậm hơn Service
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await InitializeRabbitMqAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("[Notification Consumer] Chưa kết nối được RabbitMQ ({Message}). Thử lại sau 5 giây...", ex.Message);
                await Task.Delay(5000, stoppingToken);
            }
        }

        if (stoppingToken.IsCancellationRequested || _channel is null)
        {
            return;
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            await HandleMessageAsync(ea, stoppingToken);
        };

        var queueName = _configuration["RabbitMQ:NotificationQueue"] ?? RabbitMqConstants.NotificationQueue;

        await _channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[Notification Consumer] Đang tích cực lắng nghe hàng đợi '{Queue}' (Prefetch: {Prefetch})...",
            queueName, PrefetchCount);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task InitializeRabbitMqAsync(CancellationToken ct)
    {
        var hostName = _configuration["RabbitMQ:HostName"] ?? "localhost";
        var portStr = _configuration["RabbitMQ:Port"] ?? "5672";
        var userName = _configuration["RabbitMQ:UserName"] ?? "guest";
        var password = _configuration["RabbitMQ:Password"] ?? "guest";
        var exchangeName = _configuration["RabbitMQ:ExchangeName"] ?? RabbitMqConstants.ExchangeName;
        var notificationQueue = _configuration["RabbitMQ:NotificationQueue"] ?? RabbitMqConstants.NotificationQueue;
        var port = int.TryParse(portStr, out var p) ? p : 5672;

        _logger.LogInformation("[Notification Consumer] Kết nối tới {Host}:{Port}...", hostName, port);

        var factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            AutomaticRecoveryEnabled = true
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        // 1. Thiết lập QoS Prefetch Count = 10
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: PrefetchCount,
            @global: false,
            cancellationToken: ct);

        // 2. Self-healing Topology: Đảm bảo Exchange, Queue và Binding luôn tồn tại
        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await _channel.QueueDeclareAsync(
            queue: notificationQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await _channel.QueueBindAsync(
            queue: notificationQueue,
            exchange: exchangeName,
            routingKey: RabbitMqConstants.RoutingKeyStatusChanged,
            arguments: null,
            cancellationToken: ct);

        // 3. Khai báo Dead-Letter Queue riêng cho Notification Service
        await _channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await _channel.QueueDeclareAsync(
            queue: DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await _channel.QueueBindAsync(
            queue: DeadLetterQueue,
            exchange: DeadLetterExchange,
            routingKey: DeadLetterRoutingKey,
            arguments: null,
            cancellationToken: ct);

        _logger.LogInformation("[Notification Consumer] Đã sẵn sàng Topology '{Queue}' và DLQ '{Dlq}'.",
            notificationQueue, DeadLetterQueue);
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var deliveryTag = ea.DeliveryTag;
        var correlationId = ea.BasicProperties?.CorrelationId ?? Guid.NewGuid().ToString("N");
        var rawBody = ea.Body.ToArray();

        TicketStatusChangedEvent? statusEvent;
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            statusEvent = JsonSerializer.Deserialize<TicketStatusChangedEvent>(rawBody, jsonOptions);

            if (statusEvent is null || statusEvent.TicketId == Guid.Empty)
            {
                throw new JsonException("Payload sự kiện rỗng hoặc thiếu TicketId hợp lệ.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CorrelationId: {CorrId}] Message sai cấu trúc, chuyển vào Dead-Letter Queue '{Dlq}'.",
                correlationId, DeadLetterQueue);
            await RouteToDeadLetterQueueAsync(rawBody, correlationId, "DeserializationFailed", ex.Message, ct);
            await _channel!.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        // Thử đẩy thông báo qua SignalR với cơ chế Retry hữu hạn (tối đa 3 lần)
        for (var attempt = 1; attempt <= MaxRetryAttempts && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await _dispatcher.DispatchAsync(statusEvent, ct);
                await _channel!.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CorrelationId: {CorrId}] Lỗi khi đẩy thông báo Ticket '{TicketId}' (Lần {Attempt}/{Max}): {Message}",
                    correlationId, statusEvent.TicketId, attempt, MaxRetryAttempts, ex.Message);

                if (attempt < MaxRetryAttempts)
                {
                    await Task.Delay(1000 * attempt, ct);
                }
            }
        }

        _logger.LogCritical("[CorrelationId: {CorrId}] Đẩy thông báo thất bại sau {Max} lần cho Ticket '{TicketId}'. Chuyển sang DLQ.",
            correlationId, MaxRetryAttempts, statusEvent.TicketId);

        await RouteToDeadLetterQueueAsync(rawBody, correlationId, "MaxRetriesExhausted",
            $"Không thể đẩy SignalR sau {MaxRetryAttempts} lần thử", ct);
        await _channel!.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
    }

    private async Task RouteToDeadLetterQueueAsync(byte[] body, string correlationId, string reason, string errorDetail, CancellationToken ct)
    {
        if (_channel is null) return;

        try
        {
            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json",
                CorrelationId = correlationId,
                Headers = new Dictionary<string, object?>
                {
                    ["x-death-reason"] = reason,
                    ["x-death-detail"] = errorDetail,
                    ["x-failed-at"] = DateTime.UtcNow.ToString("o"),
                    ["x-original-queue"] = RabbitMqConstants.NotificationQueue
                }
            };

            await _channel.BasicPublishAsync(
                exchange: DeadLetterExchange,
                routingKey: DeadLetterRoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CorrelationId: {CorrId}] Lỗi khi ghi vào DLQ '{Dlq}': {Message}",
                correlationId, DeadLetterQueue, ex.Message);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
