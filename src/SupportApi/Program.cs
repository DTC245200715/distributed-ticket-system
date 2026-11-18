using DistributedTicketSystem.Contracts.Constants;
using DistributedTicketSystem.Contracts.Enums;
using DistributedTicketSystem.Contracts.Events;
using DistributedTicketSystem.SupportApi.Data;
using DistributedTicketSystem.SupportApi.Data.Entities;
using DistributedTicketSystem.SupportApi.Messaging;
using DistributedTicketSystem.SupportApi.Models;
using DistributedTicketSystem.SupportApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Đăng ký Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// 2. Cấu hình DbContext kết nối PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=ticket_support_db;Username=postgres;Password=postgres_secure_password_2026";

builder.Services.AddDbContext<SupportDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        // Tự động retry khi mạng chập chờn
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
    });
});

// 3. Đăng ký RabbitMQ Publisher (Singleton)
builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

// 4. Cấu hình CORS cho phép Client Web kết nối có credentials
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

var app = builder.Build();

app.UseCors();

// Tự động kiểm tra và khởi tạo Database Schema khi khởi động
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
    try
    {
        logger.LogInformation("Đang kiểm tra và áp dụng Migration Database PostgreSQL...");
        await db.Database.EnsureCreatedAsync();
        logger.LogInformation("Database PostgreSQL đã sẵn sàng hoạt động.");
    }
    catch (Exception ex)
    {
        logger.LogWarning("Chưa kết nối được Database ngay lúc này ({Message}). Migration sẽ tự hoàn tất khi Database khởi động.", ex.Message);
    }
}

// ==============================================================================
// 5 ENDPOINTS REST MINIMAL API THEO ĐẶC TẢ ĐỀ TÀI
// ==============================================================================

// 1. POST /tickets: Tạo yêu cầu hỗ trợ mới
app.MapPost("/tickets", async (
    [FromBody] CreateTicketRequest request,
    [FromServices] SupportDbContext db,
    [FromServices] IMessagePublisher publisher,
    [FromServices] ILogger<Program> logger,
    CancellationToken ct) =>
{
    // Kiểm tra dữ liệu đầu vào (Validation)
    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        return Results.BadRequest(new { error = "Mã người dùng (UserId) không được để trống." });
    }

    if (string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest(new { error = "Tiêu đề yêu cầu (Title) không được để trống." });
    }

    if (string.IsNullOrWhiteSpace(request.Description))
    {
        return Results.BadRequest(new { error = "Nội dung chi tiết (Description) không được để trống." });
    }

    logger.LogInformation("[API] Tiếp nhận yêu cầu tạo ticket từ UserId: '{UserId}', Tiêu đề: '{Title}', Mức ưu tiên: {Priority}",
        request.UserId, request.Title, request.Priority);

    var ticket = new Ticket
    {
        Id = Guid.NewGuid(),
        UserId = request.UserId.Trim(),
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        Priority = request.Priority,
        Status = TicketStatus.New,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // Tạo bản ghi lịch sử ban đầu
    var initialHistory = new TicketHistory
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        PreviousStatus = null,
        NewStatus = TicketStatus.New,
        ChangedBy = request.UserId,
        Reason = "Tiếp nhận yêu cầu hỗ trợ mới từ khách hàng",
        ChangedAt = DateTime.UtcNow
    };

    ticket.Histories.Add(initialHistory);

    db.Tickets.Add(ticket);
    await db.SaveChangesAsync(ct);

    logger.LogInformation("[DB] Đã lưu Ticket '{TicketId}' vào PostgreSQL với trạng thái '{Status}'", ticket.Id, ticket.Status);

    // Bắn sự kiện TicketCreatedEvent vào RabbitMQ Topic Exchange (Routing key: ticket.created)
    var createdEvent = new TicketCreatedEvent
    {
        TicketId = ticket.Id,
        UserId = ticket.UserId,
        Title = ticket.Title,
        Description = ticket.Description,
        Priority = ticket.Priority,
        CreatedAt = ticket.CreatedAt
    };

    try
    {
        await publisher.PublishAsync(RabbitMqConstants.RoutingKeyCreated, createdEvent, ct: ct);
        logger.LogInformation("[RabbitMQ] Đã phát sự kiện TicketCreatedEvent cho Ticket '{TicketId}'", ticket.Id);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[RabbitMQ] Lỗi khi gửi sự kiện cho Ticket '{TicketId}': {Message}", ticket.Id, ex.Message);
        // Ticket đã được lưu an toàn trong DB
    }

    return Results.Created($"/tickets/{ticket.Id}", TicketResponse.FromEntity(ticket));
});

// 2. GET /tickets: Tra cứu danh sách yêu cầu (hỗ trợ lọc status, priority)
app.MapGet("/tickets", async (
    [FromQuery] TicketStatus? status,
    [FromQuery] TicketPriority? priority,
    [FromServices] SupportDbContext db,
    CancellationToken ct) =>
{
    var query = db.Tickets.AsNoTracking().AsQueryable();

    if (status.HasValue)
    {
        query = query.Where(t => t.Status == status.Value);
    }

    if (priority.HasValue)
    {
        query = query.Where(t => t.Priority == priority.Value);
    }

    var tickets = await query
        .OrderByDescending(t => t.CreatedAt)
        .Select(t => TicketResponse.FromEntity(t))
        .ToListAsync(ct);

    return Results.Ok(tickets);
});

// 3. GET /tickets/{id}: Tra cứu chi tiết một yêu cầu kèm toàn bộ lịch sử thay đổi
app.MapGet("/tickets/{id:guid}", async (
    Guid id,
    [FromServices] SupportDbContext db,
    CancellationToken ct) =>
{
    var ticket = await db.Tickets
        .AsNoTracking()
        .Include(t => t.Histories)
        .FirstOrDefaultAsync(t => t.Id == id, ct);

    if (ticket is null)
    {
        return Results.NotFound(new { error = $"Không tìm thấy yêu cầu hỗ trợ có ID: '{id}'" });
    }

    return Results.Ok(TicketDetailResponse.FromEntity(ticket));
});

// 4. PATCH /tickets/{id}/status: Cập nhật trạng thái yêu cầu
app.MapPatch("/tickets/{id:guid}/status", async (
    Guid id,
    [FromBody] UpdateTicketStatusRequest request,
    [FromServices] SupportDbContext db,
    [FromServices] IMessagePublisher publisher,
    [FromServices] ILogger<Program> logger,
    CancellationToken ct) =>
{
    var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (ticket is null)
    {
        return Results.NotFound(new { error = $"Không tìm thấy yêu cầu hỗ trợ có ID: '{id}'" });
    }

    // Kiểm tra tính hợp lệ bằng State Machine
    var (isValid, errorMessage) = TicketStateMachine.ValidateTransition(ticket.Status, request.NewStatus);
    if (!isValid)
    {
        logger.LogWarning("[Validation] Vi phạm luồng trạng thái cho Ticket '{TicketId}': {Error}", id, errorMessage);
        return Results.BadRequest(new { error = errorMessage });
    }

    var oldStatus = ticket.Status;
    ticket.Status = request.NewStatus;
    ticket.UpdatedAt = DateTime.UtcNow;

    if (!string.IsNullOrWhiteSpace(request.AssignedTo))
    {
        ticket.AssignedTo = request.AssignedTo.Trim();
    }

    if (!string.IsNullOrWhiteSpace(request.AssignedGroup))
    {
        ticket.AssignedGroup = request.AssignedGroup.Trim();
    }

    // Ghi nhận lịch sử thay đổi
    var history = new TicketHistory
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        PreviousStatus = oldStatus,
        NewStatus = request.NewStatus,
        AssignedTo = ticket.AssignedTo,
        AssignedGroup = ticket.AssignedGroup,
        ChangedBy = string.IsNullOrWhiteSpace(request.ChangedBy) ? "SupportStaff" : request.ChangedBy.Trim(),
        Reason = request.Reason ?? $"Chuyển trạng thái từ '{oldStatus}' sang '{request.NewStatus}'",
        ChangedAt = DateTime.UtcNow
    };

    db.TicketHistories.Add(history);
    await db.SaveChangesAsync(ct);

    logger.LogInformation("[DB] Đã cập nhật Ticket '{TicketId}' từ '{OldStatus}' sang '{NewStatus}' bởi '{ChangedBy}'",
        ticket.Id, oldStatus, ticket.Status, history.ChangedBy);

    // Bắn sự kiện TicketStatusChangedEvent vào RabbitMQ Topic Exchange (Routing key: ticket.status.changed)
    var statusChangedEvent = new TicketStatusChangedEvent
    {
        TicketId = ticket.Id,
        UserId = ticket.UserId,
        OldStatus = oldStatus,
        NewStatus = ticket.Status,
        AssignedTo = ticket.AssignedTo,
        AssignedGroup = ticket.AssignedGroup,
        ChangedBy = history.ChangedBy,
        Reason = history.Reason,
        ChangedAt = history.ChangedAt
    };

    try
    {
        await publisher.PublishAsync(RabbitMqConstants.RoutingKeyStatusChanged, statusChangedEvent, ct: ct);
        logger.LogInformation("[RabbitMQ] Đã phát sự kiện TicketStatusChangedEvent cho Ticket '{TicketId}'", ticket.Id);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[RabbitMQ] Lỗi khi gửi sự kiện thay đổi trạng thái cho Ticket '{TicketId}': {Message}", ticket.Id, ex.Message);
    }

    return Results.Ok(TicketResponse.FromEntity(ticket));
});

// 5. GET /users/{userId}/tickets: Tra cứu tất cả yêu cầu của một người dùng
app.MapGet("/users/{userId}/tickets", async (
    string userId,
    [FromServices] SupportDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.BadRequest(new { error = "UserId không được để trống." });
    }

    var tickets = await db.Tickets
        .AsNoTracking()
        .Where(t => t.UserId == userId.Trim())
        .OrderByDescending(t => t.CreatedAt)
        .Select(t => TicketResponse.FromEntity(t))
        .ToListAsync(ct);

    return Results.Ok(tickets);
});

// Endpoint kiểm tra sức khỏe đơn giản
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "SupportApi", timestamp = DateTime.UtcNow }));

app.Run();
