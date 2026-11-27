using System.Security.Claims;
using DistributedTicketSystem.NotificationService.Hubs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DistributedTicketSystem.NotificationService.Tests;

public class NotificationHubTests
{
    private sealed class FakeGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];
        public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    [Fact]
    public async Task JoinUserGroup_WhenUserIdIsValid_ShouldAddConnectionToTrimmedGroup()
    {
        // Arrange
        var groups = new FakeGroupManager();
        var hub = new NotificationHub(NullLogger<NotificationHub>.Instance)
        {
            Groups = groups,
            Context = new FakeHubCallerContext("conn-123")
        };

        // Act
        await hub.JoinUserGroup("  customer_01 ");

        // Assert
        Assert.Single(groups.Added);
        Assert.Equal(("conn-123", "customer_01"), groups.Added[0]);
    }

    [Fact]
    public async Task JoinUserGroup_WhenUserIdIsEmpty_ShouldNotAddConnectionToGroup()
    {
        // Arrange
        var groups = new FakeGroupManager();
        var hub = new NotificationHub(NullLogger<NotificationHub>.Instance)
        {
            Groups = groups,
            Context = new FakeHubCallerContext("conn-123")
        };

        // Act
        await hub.JoinUserGroup("   ");

        // Assert
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task LeaveUserGroup_WhenUserIdIsValid_ShouldRemoveConnectionFromGroup()
    {
        // Arrange
        var groups = new FakeGroupManager();
        var hub = new NotificationHub(NullLogger<NotificationHub>.Instance)
        {
            Groups = groups,
            Context = new FakeHubCallerContext("conn-123")
        };

        // Act
        await hub.LeaveUserGroup("customer_01");

        // Assert
        Assert.Single(groups.Removed);
        Assert.Equal(("conn-123", "customer_01"), groups.Removed[0]);
    }
}
