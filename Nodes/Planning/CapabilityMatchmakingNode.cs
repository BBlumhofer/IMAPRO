using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Planning;

/// <summary>
/// CapabilityMatchmaking - stub: logs requested capability and succeeds.
/// </summary>
public class CapabilityMatchmakingNode : BTNode
{
    public string RequiredCapability { get; set; } = string.Empty;
    public string RefusalReason { get; set; } = "capability_not_found";

    public CapabilityMatchmakingNode() : base("CapabilityMatchmaking") {}

    public override Task<NodeStatus> Execute()
    {
        var capability = ResolvePlaceholders(RequiredCapability);
        if (string.IsNullOrWhiteSpace(capability))
        {
            // wait until a capability is provided
            return Task.FromResult(NodeStatus.Running);
        }

        // Stub success: store capability as matched
        Logger.LogInformation("CapabilityMatchmaking: capability={Capability}", capability);
        Context.Set("LastMatchedCapability", capability);
        Context.Set("MatchedCapability", capability);
        return Task.FromResult(NodeStatus.Success);
    }
}
