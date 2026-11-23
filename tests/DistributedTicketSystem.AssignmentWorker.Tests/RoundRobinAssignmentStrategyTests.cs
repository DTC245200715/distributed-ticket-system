using DistributedTicketSystem.AssignmentWorker.Services;
using DistributedTicketSystem.Contracts.Enums;
using Xunit;

namespace DistributedTicketSystem.AssignmentWorker.Tests;

public class RoundRobinAssignmentStrategyTests
{
    [Theory]
    [InlineData(TicketPriority.Urgent, RoundRobinAssignmentStrategy.GroupL3)]
    [InlineData(TicketPriority.High, RoundRobinAssignmentStrategy.GroupL2)]
    [InlineData(TicketPriority.Medium, RoundRobinAssignmentStrategy.GroupL1)]
    [InlineData(TicketPriority.Low, RoundRobinAssignmentStrategy.GroupL1)]
    public void Assign_ShouldRouteToCorrectGroup_BasedOnPriority(TicketPriority priority, string expectedGroup)
    {
        // Arrange
        var strategy = new RoundRobinAssignmentStrategy();

        // Act
        var result = strategy.Assign(priority);

        // Assert
        Assert.Equal(expectedGroup, result.Group);
    }

    [Fact]
    public void Assign_UrgentTickets_ShouldRotateStrictlyBetweenL3Agents()
    {
        // Arrange
        var strategy = new RoundRobinAssignmentStrategy();

        // Act
        var first = strategy.Assign(TicketPriority.Urgent);
        var second = strategy.Assign(TicketPriority.Urgent);
        var third = strategy.Assign(TicketPriority.Urgent);
        var fourth = strategy.Assign(TicketPriority.Urgent);

        // Assert
        Assert.Equal("agent_hoang", first.Agent);
        Assert.Equal("agent_dung", second.Agent);
        Assert.Equal("agent_hoang", third.Agent);
        Assert.Equal("agent_dung", fourth.Agent);
    }

    [Fact]
    public void Assign_MediumTickets_ShouldRotateStrictlyAcrossThreeL1Agents()
    {
        // Arrange
        var strategy = new RoundRobinAssignmentStrategy();

        // Act
        var a1 = strategy.Assign(TicketPriority.Medium);
        var a2 = strategy.Assign(TicketPriority.Medium);
        var a3 = strategy.Assign(TicketPriority.Medium);
        var a4 = strategy.Assign(TicketPriority.Medium);

        // Assert
        Assert.Equal("agent_an", a1.Agent);
        Assert.Equal("agent_binh", a2.Agent);
        Assert.Equal("agent_chi", a3.Agent);
        Assert.Equal("agent_an", a4.Agent);
    }

    [Fact]
    public void Assign_WhenAgentIsUnavailable_ShouldSkipAndAssignRemainingAgent()
    {
        // Arrange
        var strategy = new RoundRobinAssignmentStrategy();
        var unavailable = new HashSet<string> { "agent_hoang" };

        // Act
        var r1 = strategy.Assign(TicketPriority.Urgent, unavailable);
        var r2 = strategy.Assign(TicketPriority.Urgent, unavailable);

        // Assert - Do agent_hoang nghỉ, chỉ còn agent_dung nhận việc
        Assert.Equal("agent_dung", r1.Agent);
        Assert.Equal("agent_dung", r2.Agent);
    }

    [Fact]
    public void Assign_WhenAllAgentsInGroupAreUnavailable_ShouldEscalateToNextGroup()
    {
        // Arrange: Cả 2 nhân viên L3 đều vắng mặt
        var strategy = new RoundRobinAssignmentStrategy();
        var unavailable = new HashSet<string> { "agent_hoang", "agent_dung" };

        // Act
        var result = strategy.Assign(TicketPriority.Urgent, unavailable);

        // Assert: Leo thang sang nhóm L2_Senior_Support
        Assert.Equal(RoundRobinAssignmentStrategy.GroupL2, result.Group);
        Assert.Contains(result.Agent, new[] { "agent_lan", "agent_minh" });
    }

    [Fact]
    public void Assign_ConcurrentMultiThreadedCalls_ShouldRemainThreadSafeAndFair()
    {
        // Arrange
        var strategy = new RoundRobinAssignmentStrategy();
        const int totalRequests = 200; // Số chẵn chia hết cho 2 người nhóm L3
        var results = new string[totalRequests];

        // Act: 200 tác vụ chạy song song trên nhiều luồng cùng gọi Assign
        Parallel.For(0, totalRequests, i =>
        {
            var res = strategy.Assign(TicketPriority.Urgent);
            results[i] = res.Agent;
        });

        // Assert: Mỗi nhân viên phải nhận chính xác 100 ticket (50% - 50%)
        var hoangCount = results.Count(a => a == "agent_hoang");
        var dungCount = results.Count(a => a == "agent_dung");

        Assert.Equal(100, hoangCount);
        Assert.Equal(100, dungCount);
    }
}
