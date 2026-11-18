using System.Text.Json;
using DistributedTicketSystem.Contracts.Constants;
using RabbitMQ.Client;

namespace DistributedTicketSystem.SupportApi.Messaging;

/// <summary>
/// Lớp triển khai phát tán message qua RabbitMQ sử dụng thư viện RabbitMQ.Client 7.x (Async API).
/// Đảm bảo tính bền vững (Durable Exchange, Persistent Delivery Mode).
/// </summary>
public class RabbitMqPublisher : IMessagePublisher
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private string _exchangeName = RabbitMqConstants.ExchangeName;
    private bool _isInitialized;

    public RabbitMqPublisher(IConfiguration configuration, ILogger<RabbitMqPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isInitialized && _channel is { IsOpen: true })
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_isInitialized && _channel is { IsOpen: true })
            {
                return;
            }

            var hostName = _configuration["RabbitMQ:HostName"] ?? "localhost";
            var portStr = _configuration["RabbitMQ:Port"] ?? "5672";
            var userName = _configuration["RabbitMQ:UserName"] ?? "guest";
            var password = _configuration["RabbitMQ:Password"] ?? "guest";
            _exchangeName = _configuration["RabbitMQ:ExchangeName"] ?? RabbitMqConstants.ExchangeName;

            var port = int.TryParse(portStr, out var p) ? p : 5672;

            _logger.LogInformation("Đang khởi tạo kết nối RabbitMQ tới {Host}:{Port} (User: {User})...", hostName, port, userName);

            var factory = new ConnectionFactory
            {
                HostName = hostName,
                Port = port,
                UserName = userName,
                Password = password,
                AutomaticRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken: ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            // Khai báo Topic Exchange bền vững (durable = true)
            await _channel.ExchangeDeclareAsync(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            // Khai báo sẵn các Queue và Bindings chuẩn của hệ thống để tránh mất message
            await _channel.QueueDeclareAsync(
                queue: RabbitMqConstants.CreationQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            await _channel.QueueBindAsync(
                queue: RabbitMqConstants.CreationQueue,
                exchange: _exchangeName,
                routingKey: RabbitMqConstants.RoutingKeyCreated,
                arguments: null,
                cancellationToken: ct);

            await _channel.QueueDeclareAsync(
                queue: RabbitMqConstants.NotificationQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            await _channel.QueueBindAsync(
                queue: RabbitMqConstants.NotificationQueue,
                exchange: _exchangeName,
                routingKey: RabbitMqConstants.RoutingKeyStatusChanged,
                arguments: null,
                cancellationToken: ct);

            _isInitialized = true;
            _logger.LogInformation("Khởi tạo kết nối RabbitMQ và Topology Exchange '{Exchange}' thành công.", _exchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi kết nối hoặc khai báo Topology trên RabbitMQ: {Message}", ex.Message);
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishAsync<T>(string routingKey, T message, string? correlationId = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (_channel is null)
        {
            throw new InvalidOperationException("RabbitMQ Channel chưa được khởi tạo thành công.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        // Đảm bảo message có tính bền vững: DeliveryModes.Persistent (lưu ổ đĩa chống mất khi broker sập)
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await _channel.BasicPublishAsync(
            exchange: _exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);

        _logger.LogInformation("Đã publish message type {Type} vào exchange '{Exchange}' với routing key '{RoutingKey}' (CorrelationId: {CorrelationId})",
            typeof(T).Name, _exchangeName, routingKey, properties.CorrelationId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
