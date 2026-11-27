using DistributedTicketSystem.NotificationService.Hubs;
using DistributedTicketSystem.NotificationService.Messaging;
using DistributedTicketSystem.NotificationService.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Logging ra Console
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// 2. Đăng ký dịch vụ SignalR phục vụ đẩy thông báo realtime qua WebSocket/SSE
builder.Services.AddSignalR();

// 3. Cấu hình CORS cho phép Web Client kết nối SignalR kèm Credentials
var allowedOriginsConfig = builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:3000";
var allowedOrigins = allowedOriginsConfig.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 4. Đăng ký Dispatcher đẩy thông báo qua SignalR và Background Consumer lắng nghe RabbitMQ
builder.Services.AddSingleton<INotificationDispatcher, SignalRNotificationDispatcher>();
builder.Services.AddHostedService<RabbitMqNotificationConsumer>();

var app = builder.Build();

app.UseCors();

// Map SignalR Hub tại đường dẫn /hubs/notifications
app.MapHub<NotificationHub>("/hubs/notifications");

// Endpoint kiểm tra sức khỏe dịch vụ
app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "NotificationService",
    timestamp = DateTime.UtcNow
}));

app.Run();
