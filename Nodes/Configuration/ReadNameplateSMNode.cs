using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// ReadNameplateSM - Reads machine or product metadata from AAS
/// </summary>
public class ReadNameplateSMNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadNameplateSMNode() : base("ReadNameplateSM")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("ReadNameplateSM: Reading nameplate for {AgentId}", AgentId);
        
        try
        {
            // TODO: Integration mit AAS-Sharp-Client
            // var aasClient = Context.Get<AASClient>("AASClient");
            // var shell = await aasClient.GetShellById(AgentId);
            // var nameplate = shell.GetSubmodelById("Nameplate");
            
            // Mockup
            var nameplate = new
            {
                IdShort = "Nameplate",
                ManufacturerName = "ACME Industries",
                ManufacturerProductDesignation = "Assembly Station Pro",
                SerialNumber = "AS-2024-001",
                YearOfConstruction = "2024"
            };
            
            Context.Set("nameplate", nameplate);
            
            Logger.LogInformation("ReadNameplateSM: Successfully read nameplate for {AgentId}", AgentId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadNameplateSM: Error reading nameplate for {AgentId}", AgentId);
            return NodeStatus.Failure;
        }
    }
}
