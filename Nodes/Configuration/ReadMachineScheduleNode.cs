using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// ReadMachineSchedule - Reads the ActualSchedule and InitialSchedule from the machine's schedule submodel
/// </summary>
public class ReadMachineScheduleNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadMachineScheduleNode() : base("ReadMachineSchedule")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("ReadMachineSchedule: Reading schedule for {AgentId}", AgentId);
        
        try
        {
            // TODO: Integration mit AAS-Sharp-Client
            
            // Mockup
            var schedule = new
            {
                IdShort = "MachineSchedule",
                InitialSchedule = new { Steps = new object[] { } },
                ActualSchedule = new 
                { 
                    BookedSlots = new object[] { },
                    TentativeSlots = new object[] { },
                    LastTimeUpdated = DateTime.UtcNow
                }
            };
            
            Context.Set("schedule", schedule);
            
            Logger.LogInformation("ReadMachineSchedule: Successfully read schedule for {AgentId}", AgentId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadMachineSchedule: Error reading schedule for {AgentId}", AgentId);
            return NodeStatus.Failure;
        }
    }
}
