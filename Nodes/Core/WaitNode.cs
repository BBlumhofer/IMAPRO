using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Core;

/// <summary>
/// Wait - Wartet eine bestimmte Zeit
/// Einfache Utility-Node für Delays
/// </summary>
public class WaitNode : BTNode
{
    public int DelayMs { get; set; } = 1000;

    public WaitNode() : base("Wait")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("Wait: Waiting {DelayMs}ms", DelayMs);
        await Task.Delay(DelayMs);
        return NodeStatus.Success;
    }
}
