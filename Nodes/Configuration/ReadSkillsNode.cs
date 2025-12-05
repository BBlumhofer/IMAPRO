using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Configuration;

public class ReadSkillsNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadSkillsNode() : base("ReadSkills") {}
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("ReadSkills: Loading for {AgentId}", AgentId);
        // TODO: Implementation
        await Task.Delay(10);
        return NodeStatus.Success;
    }
}
