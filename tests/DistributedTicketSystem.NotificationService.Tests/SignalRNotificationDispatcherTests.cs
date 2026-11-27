using DistributedTicketSystem.Contracts.Enums;
using DistributedTicketSystem.Contracts.Events;
using DistributedTicketSystem.NotificationService.Hubs;
using DistributedTicketSystem.NotificationService.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DistributedTicketSystem.NotificationService.Tests;

public class SignalRNotificationDispatcherTests
{
    private sealed class FakeClientProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> SentCalls { get; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            SentCalls.Add((method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubClients : IHubClients
    {
        public Dictionary<string, FakeClientProxy> GroupProxies { get; } = new();

        public IClientProxy Group(string groupName)
        {
            if (!GroupProxies.TryGetValue(groupName, out var proxy))
            {
                proxy = new FakeClientProxy();
                GroupProxies[groupName] = proxy;
            }
            return proxy;
        }

        public IClientProxy All => throw new NotImplementedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public IClientProxy Client(string connectionId) => throw new NotImplementedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
        public IClientProxy User(string userId) => throw new NotImplementedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
    }

    private sealed class FakeHubContext(FakeHubClients clients) : IHubContext<NotificationHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups => throw new NotImplementedException();
    }

    [Fact]
    public async Task DispatchAsync_WhenUserIdIsValid_ShouldSendNotificationToUserGroup()
    {
        // Arrange
        var fakeClients = new FakeHubClients();
        var hubContext = new FakeHubContext(fakeClients);
        var dispatcher = new SignalRNotificationDispatcher(hubContext, NullLogger<SignalRNotificationDispatcher>.Instance);

        var statusEvent = new TicketStatusChangedEvent
        {
            TicketId = Guid.NewGuid(),
            UserId = "  customer_01  ",
            OldStatus = TicketStatus.New,
            NewStatus = TicketStatus.Assigned,
            AssignedTo = "agent_hoang",
            AssignedGroup = "L3_Critical_Support",
            ChangedBy = "AssignmentWorker",
            CorrelationId = "corr-notif-001"
        };

        // Act
        await dispatcher.DispatchAsync(statusEvent);

        // Assert
        Assert.True(fakeClients.GroupProxies.ContainsKey("customer_01"));
        var calls = fakeClients.GroupProxies["customer_01"].SentCalls;
        Assert.Single(calls);
        Assert.Equal(NotificationHub.ReceiveTicketNotificationMethod, calls[0].Method);
        var payload = Assert.IsType<TicketStatusChangedEvent>(calls[0].Args[0]);
        Assert.Equal(statusEvent.TicketId, payload.TicketId);
        Assert.Equal("agent_hoang", payload.AssignedTo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispatchAsync_WhenUserIdIsEmptyOrWhitespace_ShouldSkipWithoutCallingSignalR(string invalidUserId)
    {
        // Arrange
        var fakeClients = new FakeHubClients();
        var hubContext = new FakeHubContext(fakeClients);
        var dispatcher = new SignalRNotificationDispatcher(hubContext, NullLogger<SignalRNotificationDispatcher>.Instance);

        var statusEvent = new TicketStatusChangedEvent
        {
            TicketId = Guid.NewGuid(),
            UserId = invalidUserId,
            OldStatus = TicketStatus.New,
            NewStatus = TicketStatus.Assigned
        };

        // Act
        await dispatcher.DispatchAsync(statusEvent);

        // Assert
        Assert.Empty(fakeClients.GroupProxies);
    }
}
