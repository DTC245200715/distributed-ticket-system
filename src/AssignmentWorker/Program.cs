using DistributedTicketSystem.AssignmentWorker.Data;
using DistributedTicketSystem.AssignmentWorker.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// 1. Cấu hình Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// 2. Cấu hình DbContext kết nối PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=ticket_support_db;Username=postgres;Password=postgres_secure_password_2026";

builder.Services.AddDbContext<WorkerDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    });
});

// 3. Đăng ký Ticket Processing Service (tạm thời dùng Stub, sẽ thay thế ở Bước 4 & 5)
builder.Services.AddScoped<ITicketProcessingService, StubTicketProcessingService>();

// 4. Đăng ký RabbitMQ Consumer Background Service
builder.Services.AddHostedService<RabbitMqConsumerService>();

var host = builder.Build();
host.Run();
