using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Configuration;

public class ReadNameplateNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadNameplateNode() : base("ReadNameplate") {}
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("ReadNameplate: Loading for {AgentId}", AgentId);
        // TODO: Implementation
        await Task.Delay(10);
        return NodeStatus.Success;
    }
}
