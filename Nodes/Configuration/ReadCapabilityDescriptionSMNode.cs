using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// ReadCapabilityDescriptionSM - Reads the CapabilityDescription submodel from the AAS
/// </summary>
public class ReadCapabilityDescriptionSMNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadCapabilityDescriptionSMNode() : base("ReadCapabilityDescriptionSM")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("ReadCapabilityDescriptionSM: Reading capabilities for {AgentId}", AgentId);
        
        try
        {
            // TODO: Integration mit AAS-Sharp-Client
            // var aasClient = Context.Get<AASClient>("AASClient");
            // var shell = await aasClient.GetShellById(AgentId);
            // var capabilitySM = shell.GetSubmodelById("CapabilityDescription");
            
            // Mockup
            var capabilitySM = new
            {
                IdShort = "CapabilityDescription",
                Capabilities = new[]
                {
                    new { Name = "Assembly", Type = "Production" },
                    new { Name = "QualityCheck", Type = "Inspection" }
                }
            };
            
            Context.Set("capabilitySM", capabilitySM);
            
            Logger.LogInformation("ReadCapabilityDescriptionSM: Successfully read capabilities for {AgentId}", AgentId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadCapabilityDescriptionSM: Error reading capabilities for {AgentId}", AgentId);
            return NodeStatus.Failure;
        }
    }
}
