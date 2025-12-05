using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// ReadShell - Loads the entire AAS for initialization or re-synchronization
/// </summary>
public class ReadShellNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadShellNode() : base("ReadShell")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("ReadShell: Reading complete AAS for {AgentId}", AgentId);
        
        try
        {
            // TODO: Integration mit AAS-Sharp-Client
            
            // Mockup
            var shell = new
            {
                Id = AgentId,
                IdShort = "AssemblyStation01",
                Submodels = new[] { "Nameplate", "Skills", "CapabilityDescription", "MachineSchedule" }
            };
            
            Context.Set("shell", shell);
            
            Logger.LogInformation("ReadShell: Successfully read AAS for {AgentId}", AgentId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadShell: Error reading AAS for {AgentId}", AgentId);
            return NodeStatus.Failure;
        }
    }
}
