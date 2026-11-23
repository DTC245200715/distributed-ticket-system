using DistributedTicketSystem.AssignmentWorker.Data;
using DistributedTicketSystem.AssignmentWorker.Data.Entities;
using DistributedTicketSystem.AssignmentWorker.Messaging;
using DistributedTicketSystem.AssignmentWorker.Services;
using DistributedTicketSystem.Contracts.Constants;
using DistributedTicketSystem.Contracts.Enums;
using DistributedTicketSystem.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DistributedTicketSystem.AssignmentWorker.Tests;

public class TicketProcessingServiceIntegrationTests
{
    private class FakeMessagePublisher : IMessagePublisher
    {
        public List<(string RoutingKey, object Message, string? CorrelationId)> PublishedMessages { get; } = [];

        public Task PublishAsync<T>(string routingKey, T message, string? correlationId = null, CancellationToken ct = default)
        {
            PublishedMessages.Add((routingKey, message!, correlationId));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static WorkerDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new WorkerDbContext(options);
    }

    [Fact]
    public async Task ProcessTicketAsync_WhenTicketIsNew_ShouldAssignAndRecordHistoryAndPublishEvent()
    {
        // Arrange
        await using var db = CreateInMemoryDbContext();
        var publisher = new FakeMessagePublisher();
        var strategy = new RoundRobinAssignmentStrategy();
        var logger = NullLogger<TicketProcessingService>.Instance;

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            UserId = "customer_99",
            Title = "Lỗi nạp tiền",
            Description = "Bị trừ tiền nhưng không cộng số dư",
            Priority = TicketPriority.Urgent,
            Status = TicketStatus.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var service = new TicketProcessingService(db, strategy, publisher, logger);

        var ticketEvent = new TicketCreatedEvent
        {
            TicketId = ticket.Id,
            UserId = ticket.UserId,
            Title = ticket.Title,
            Description = ticket.Description,
            Priority = ticket.Priority,
            CorrelationId = "test-corr-001"
        };

        // Act
        var result = await service.ProcessTicketAsync(ticketEvent, CancellationToken.None);

        // Assert 1: Trả về thành công
        Assert.True(result);

        // Assert 2: Trạng thái trong DB được cập nhật sang Assigned
        var updatedTicket = await db.Tickets.Include(t => t.Histories).FirstAsync(t => t.Id == ticket.Id);
        Assert.Equal(TicketStatus.Assigned, updatedTicket.Status);
        Assert.Equal(RoundRobinAssignmentStrategy.GroupL3, updatedTicket.AssignedGroup);
        Assert.Equal("agent_hoang", updatedTicket.AssignedTo);

        // Assert 3: Bản ghi lịch sử được thêm vào
        Assert.Single(updatedTicket.Histories);
        var history = updatedTicket.Histories.First();
        Assert.Equal(TicketStatus.New, history.PreviousStatus);
        Assert.Equal(TicketStatus.Assigned, history.NewStatus);
        Assert.Equal("AssignmentWorker", history.ChangedBy);

        // Assert 4: Sự kiện TicketStatusChangedEvent được bắn lên RabbitMQ với routing key chuẩn
        Assert.Single(publisher.PublishedMessages);
        var (routingKey, publishedEvent, corrId) = publisher.PublishedMessages.First();
        Assert.Equal(RabbitMqConstants.RoutingKeyStatusChanged, routingKey);
        Assert.Equal("test-corr-001", corrId);

        var statusEvent = Assert.IsType<TicketStatusChangedEvent>(publishedEvent);
        Assert.Equal(ticket.Id, statusEvent.TicketId);
        Assert.Equal(TicketStatus.New, statusEvent.OldStatus);
        Assert.Equal(TicketStatus.Assigned, statusEvent.NewStatus);
        Assert.Equal("agent_hoang", statusEvent.AssignedTo);
        Assert.Equal(RoundRobinAssignmentStrategy.GroupL3, statusEvent.AssignedGroup);
    }

    [Fact]
    public async Task ProcessTicketAsync_WhenTicketIsAlreadyAssigned_ShouldBeIdempotentAndNotPublishDuplicate()
    {
        // Arrange: Ticket đã ở trạng thái Assigned
        await using var db = CreateInMemoryDbContext();
        var publisher = new FakeMessagePublisher();
        var strategy = new RoundRobinAssignmentStrategy();
        var logger = NullLogger<TicketProcessingService>.Instance;

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            UserId = "customer_88",
            Title = "Yêu cầu hoàn trả",
            Description = "Hoàn tiền dịch vụ",
            Priority = TicketPriority.High,
            Status = TicketStatus.Assigned,
            AssignedGroup = RoundRobinAssignmentStrategy.GroupL2,
            AssignedTo = "agent_lan",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var service = new TicketProcessingService(db, strategy, publisher, logger);

        var ticketEvent = new TicketCreatedEvent
        {
            TicketId = ticket.Id,
            UserId = ticket.UserId,
            Priority = ticket.Priority,
            CorrelationId = "test-corr-duplicate"
        };

        // Act: Gửi lại bản tin lần 2
        var result = await service.ProcessTicketAsync(ticketEvent, CancellationToken.None);

        // Assert: Vẫn trả về true (để Consumer Ack) nhưng không sửa đổi và không bắn thêm sự kiện
        Assert.True(result);

        var unchangedTicket = await db.Tickets.FirstAsync(t => t.Id == ticket.Id);
        Assert.Equal("agent_lan", unchangedTicket.AssignedTo);
        Assert.Empty(publisher.PublishedMessages); // Không được bắn trùng sự kiện
    }

    [Fact]
    public async Task ProcessTicketAsync_WhenTicketDoesNotExist_ShouldThrowInvalidOperationException()
    {
        // Arrange
        await using var db = CreateInMemoryDbContext();
        var publisher = new FakeMessagePublisher();
        var strategy = new RoundRobinAssignmentStrategy();
        var logger = NullLogger<TicketProcessingService>.Instance;

        var service = new TicketProcessingService(db, strategy, publisher, logger);

        var nonExistentEvent = new TicketCreatedEvent
        {
            TicketId = Guid.NewGuid(),
            UserId = "user_404",
            Priority = TicketPriority.Low,
            CorrelationId = "test-corr-404"
        };

        // Act & Assert: Quăng ngoại lệ để Consumer kích hoạt Retry 3 lần
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessTicketAsync(nonExistentEvent, CancellationToken.None));
    }
}
