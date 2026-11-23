using System.Text.Json;
using DistributedTicketSystem.Contracts.Constants;
using DistributedTicketSystem.Contracts.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DistributedTicketSystem.AssignmentWorker.Messaging;

/// <summary>
/// Background Service chịu trách nhiệm kết nối RabbitMQ, quản lý QoS (Prefetch),
/// tiêu thụ message từ queue ticket.creation.queue, kiểm soát Manual Ack,
/// Retry hữu hạn và Dead-Letter Queue khi phát hiện message độc (Poison Message).
/// </summary>
public class RabbitMqConsumerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqConsumerService> _logger;

    private const int MaxRetryAttempts = 3;
    private const ushort PrefetchCount = 10;
    private const string DeadLetterExchange = "ticket.dlx";
    private const string DeadLetterQueue = "ticket.creation.dlq";
    private const string DeadLetterRoutingKey = "ticket.creation.deadletter";

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<RabbitMqConsumerService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RabbitMQ Consumer] Bắt đầu khởi chạy tiến trình lắng nghe...");

        // Vòng lặp kết nối có khả năng tự phục hồi khi RabbitMQ chưa sẵn sàng
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await InitializeRabbitMqAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("[RabbitMQ Consumer] Chưa kết nối được Broker ({Message}). Thử lại sau 5 giây...", ex.Message);
                await Task.Delay(5000, stoppingToken);
            }
        }

        if (stoppingToken.IsCancellationRequested || _channel is null)
        {
            return;
        }

        // Thiết lập Consumer bất đồng bộ
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            await HandleMessageAsync(ea, stoppingToken);
        };

        var queueName = _configuration["RabbitMQ:CreationQueue"] ?? RabbitMqConstants.CreationQueue;

        await _channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false, // Bắt buộc Manual Ack để đảm bảo không mất dữ liệu
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[RabbitMQ Consumer] Đang tích cực lắng nghe hàng đợi '{Queue}' (Prefetch: {Prefetch})...", queueName, PrefetchCount);

        // Chờ tín hiệu dừng ứng dụng
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task InitializeRabbitMqAsync(CancellationToken ct)
    {
        var hostName = _configuration["RabbitMQ:HostName"] ?? "localhost";
        var portStr = _configuration["RabbitMQ:Port"] ?? "5672";
        var userName = _configuration["RabbitMQ:UserName"] ?? "guest";
        var password = _configuration["RabbitMQ:Password"] ?? "guest";
        var port = int.TryParse(portStr, out var p) ? p : 5672;

        _logger.LogInformation("[RabbitMQ Consumer] Kết nối tới {Host}:{Port}...", hostName, port);

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

        // 1. Thiết lập QoS (Prefetch Count = 10)
        // Tránh Broker đẩy ồ ạt message vào RAM, hỗ trợ cân bằng tải khi chạy nhiều Worker
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: PrefetchCount,
            @global: false,
            cancellationToken: ct);

        // 2. Khai báo Topology Exchange và Queue tiếp nhận nếu chưa tồn tại
        var exchangeName = _configuration["RabbitMQ:ExchangeName"] ?? RabbitMqConstants.ExchangeName;
        var creationQueue = _configuration["RabbitMQ:CreationQueue"] ?? RabbitMqConstants.CreationQueue;

        await _channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await _channel.QueueDeclareAsync(
            queue: creationQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await _channel.QueueBindAsync(
            queue: creationQueue,
            exchange: exchangeName,
            routingKey: RabbitMqConstants.RoutingKeyCreated,
            arguments: null,
            cancellationToken: ct);

        // 3. Khai báo Dead-Letter Topology để chứa message độc (Poison Messages)
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

        _logger.LogInformation("[RabbitMQ Consumer] Đã khởi tạo Topology Dead-Letter Exchange '{Dlx}' và Queue '{Dlq}'.",
            DeadLetterExchange, DeadLetterQueue);
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var deliveryTag = ea.DeliveryTag;
        var correlationId = ea.BasicProperties?.CorrelationId ?? Guid.NewGuid().ToString("N");
        var rawBody = ea.Body.ToArray();

        _logger.LogInformation("[CorrelationId: {CorrId}] Nhận được message mới (DeliveryTag: {Tag}, Size: {Bytes} bytes)",
            correlationId, deliveryTag, rawBody.Length);

        TicketCreatedEvent? ticketEvent;
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            ticketEvent = JsonSerializer.Deserialize<TicketCreatedEvent>(rawBody, jsonOptions);

            if (ticketEvent is null || ticketEvent.TicketId == Guid.Empty)
            {
                throw new JsonException("Message rỗng hoặc thiếu trường TicketId hợp lệ.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CorrelationId: {CorrId}] Message không thể deserialize hoặc sai cấu trúc. Chuyển vào Dead-Letter Queue...", correlationId);
            await RouteToDeadLetterQueueAsync(rawBody, correlationId, "DeserializationFailed", ex.Message, ct);
            await _channel!.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        // Thực hiện xử lý nghiệp vụ kèm cơ chế Retry hữu hạn (Max 3 lần)
        var attempt = 0;
        var isSuccess = false;

        while (attempt < MaxRetryAttempts && !isSuccess && !ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ITicketProcessingService>();

                _logger.LogInformation("[CorrelationId: {CorrId}] Bắt đầu xử lý Ticket '{TicketId}' (Lần thử {Attempt}/{Max})",
                    correlationId, ticketEvent.TicketId, attempt, MaxRetryAttempts);

                isSuccess = await processor.ProcessTicketAsync(ticketEvent, ct);

                if (isSuccess)
                {
                    await _channel!.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
                    _logger.LogInformation("[CorrelationId: {CorrId}] Xử lý thành công Ticket '{TicketId}'. Đã gửi BasicAck.",
                        correlationId, ticketEvent.TicketId);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CorrelationId: {CorrId}] Lỗi ở lần thử {Attempt}/{Max} cho Ticket '{TicketId}': {Message}",
                    correlationId, attempt, MaxRetryAttempts, ticketEvent.TicketId, ex.Message);

                if (attempt < MaxRetryAttempts)
                {
                    // Chờ 1 giây trước khi thử lại
                    await Task.Delay(1000 * attempt, ct);
                }
            }
        }

        // Nếu đã thử tối đa MaxRetryAttempts lần mà vẫn thất bại
        _logger.LogCritical("[CorrelationId: {CorrId}] Đã thử lại {Max} lần nhưng xử lý thất bại cho Ticket '{TicketId}'. Đưa vào Dead-Letter Queue...",
            correlationId, MaxRetryAttempts, ticketEvent.TicketId);

        await RouteToDeadLetterQueueAsync(rawBody, correlationId, "MaxRetriesExhausted", $"Thất bại sau {MaxRetryAttempts} lần xử lý", ct);
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
                    ["x-original-queue"] = RabbitMqConstants.CreationQueue
                }
            };

            await _channel.BasicPublishAsync(
                exchange: DeadLetterExchange,
                routingKey: DeadLetterRoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            _logger.LogWarning("[CorrelationId: {CorrId}] Đã lưu trữ message độc vào Dead-Letter Queue '{Dlq}' thành công.",
                correlationId, DeadLetterQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CorrelationId: {CorrId}] Lỗi nghiêm trọng khi đẩy message vào Dead-Letter Queue: {Message}",
                correlationId, ex.Message);
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
