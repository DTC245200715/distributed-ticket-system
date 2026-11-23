using System.Collections.Concurrent;
using DistributedTicketSystem.Contracts.Enums;

namespace DistributedTicketSystem.AssignmentWorker.Services;

/// <summary>
/// Chiến lược phân công kết hợp Mức độ ưu tiên (Priority-based) và Xoay vòng công bằng (Round-Robin).
/// Lớp thuần POCO không phụ thuộc hạ tầng, bảo đảm an toàn đa luồng (Thread-safe)
/// và xử lý đầy đủ các trường hợp biên: nhân viên vắng mặt, nhóm rỗng và tie-break đồng thời.
/// </summary>
public class RoundRobinAssignmentStrategy : IAssignmentStrategy
{
    public const string GroupL3 = "L3_Critical_Support";
    public const string GroupL2 = "L2_Senior_Support";
    public const string GroupL1 = "L1_Helpdesk_Support";
    public const string FallbackAgent = "system_support_pool";

    // Danh sách nhân viên mặc định theo quy ước thiết kế trong docs/design.md
    private readonly Dictionary<string, IReadOnlyList<string>> _groupStaffs;

    // Bộ đếm vị trí xoay vòng cho từng nhóm, quản lý an toàn đa luồng
    private readonly ConcurrentDictionary<string, StrongBoxCounter> _roundRobinCounters = new();

    public RoundRobinAssignmentStrategy(Dictionary<string, IReadOnlyList<string>>? customGroupStaffs = null)
    {
        _groupStaffs = customGroupStaffs ?? new Dictionary<string, IReadOnlyList<string>>
        {
            [GroupL3] = ["agent_hoang", "agent_dung"],
            [GroupL2] = ["agent_lan", "agent_minh"],
            [GroupL1] = ["agent_an", "agent_binh", "agent_chi"]
        };

        foreach (var group in _groupStaffs.Keys)
        {
            _roundRobinCounters[group] = new StrongBoxCounter();
        }
    }

    public AssignmentResult Assign(TicketPriority priority, IReadOnlySet<string>? unavailableAgents = null)
    {
        // 1. Phân nhóm mục tiêu dựa trên Priority
        var targetGroup = ResolvePrimaryGroup(priority);

        // 2. Lấy danh sách nhân viên khả dụng trong nhóm mục tiêu
        var availableAgents = GetAvailableAgents(targetGroup, unavailableAgents);

        // 3. Xử lý trường hợp nhóm rỗng hoặc tất cả nhân viên trong nhóm vắng mặt (Escalation / Fallback)
        if (availableAgents.Count == 0)
        {
            var fallback = ResolveFallback(targetGroup, unavailableAgents);
            if (fallback != null)
            {
                return fallback;
            }

            // Trường hợp hy hữu: toàn bộ nhân viên ở mọi nhóm đều không khả dụng
            return new AssignmentResult(targetGroup, FallbackAgent);
        }

        // 4. Cơ chế Round-Robin + Tie-break an toàn đa luồng
        var assignedAgent = SelectAgentRoundRobin(targetGroup, availableAgents);

        return new AssignmentResult(targetGroup, assignedAgent);
    }

    private static string ResolvePrimaryGroup(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => GroupL3,
        TicketPriority.High => GroupL2,
        TicketPriority.Medium or TicketPriority.Low => GroupL1,
        _ => GroupL1
    };

    private List<string> GetAvailableAgents(string group, IReadOnlySet<string>? unavailableAgents)
    {
        if (!_groupStaffs.TryGetValue(group, out var allAgents))
        {
            return [];
        }

        if (unavailableAgents == null || unavailableAgents.Count == 0)
        {
            return [.. allAgents];
        }

        return allAgents.Where(agent => !unavailableAgents.Contains(agent)).ToList();
    }

    private string SelectAgentRoundRobin(string group, List<string> availableAgents)
    {
        var counterBox = _roundRobinCounters.GetOrAdd(group, _ => new StrongBoxCounter());

        // Sử dụng phép tăng nguyên tử Interlocked.Increment để xử lý Tie-Break khi nhiều luồng cùng gọi tới
        var counter = Interlocked.Increment(ref counterBox.Value);

        // Phép chia lấy dư với số nguyên không âm để xoay vòng tuần tự, công bằng (bắt đầu từ index 0)
        var index = (int)(((uint)counter - 1) % (uint)availableAgents.Count);

        return availableAgents[index];
    }

    /// <summary>
    /// Xử lý leo thang/hỗ trợ chéo khi nhóm chính không có ai trực (Empty Group / Unavailable Staff).
    /// Thứ tự ưu tiên hỗ trợ:
    /// - L3 (Sự cố khẩn cấp) không có người -> Nhờ L2 (Senior) -> Nhờ L1.
    /// - L2 (Nâng cao) không có người -> Nhờ L3 -> Nhờ L1.
    /// - L1 (Cơ bản) không có người -> Nhờ L2 -> Nhờ L3.
    /// </summary>
    private AssignmentResult? ResolveFallback(string targetGroup, IReadOnlySet<string>? unavailableAgents)
    {
        var fallbackOrder = targetGroup switch
        {
            GroupL3 => new[] { GroupL2, GroupL1 },
            GroupL2 => new[] { GroupL3, GroupL1 },
            _ => new[] { GroupL2, GroupL3 }
        };

        foreach (var group in fallbackOrder)
        {
            var agents = GetAvailableAgents(group, unavailableAgents);
            if (agents.Count > 0)
            {
                var agent = SelectAgentRoundRobin(group, agents);
                // Giữ nguyên nhóm trách nhiệm targetGroup hoặc gán agent của nhóm hỗ trợ
                return new AssignmentResult(group, agent);
            }
        }

        return null;
    }

    /// <summary>
    /// Wrapper lưu trữ biến đếm nguyên tử trong ConcurrentDictionary.
    /// </summary>
    private sealed class StrongBoxCounter
    {
        public int Value;
    }
}
