using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Configuration;

public class ReadCapabilityDescriptionNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadCapabilityDescriptionNode() : base("ReadCapabilityDescription") {}
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("ReadCapabilityDescription: Loading for {AgentId}", AgentId);
        // TODO: Implementation
        await Task.Delay(10);
        return NodeStatus.Success;
    }
}
