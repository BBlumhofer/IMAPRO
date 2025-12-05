using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Locking;

/// <summary>
/// UnlockResource - Unlocks resource by setting /State/isLocked = false
/// </summary>
public class UnlockResourceNode : BTNode
{
    public string ResourceId { get; set; } = string.Empty;
    
    public UnlockResourceNode() : base("UnlockResource")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("UnlockResource: Attempting to unlock {ResourceId}", ResourceId);
        
        try
        {
            var isLocked = Context.Get<bool?>($"State_{ResourceId}_IsLocked") ?? false;
            
            if (!isLocked)
            {
                Logger.LogDebug("UnlockResource: Resource {ResourceId} already unlocked", ResourceId);
                Context.Set("unlocked", true);
                return NodeStatus.Success;
            }
            
            // Prüfe ob wir der Owner sind
            var owner = Context.Get<string>($"State_{ResourceId}_LockOwner") ?? string.Empty;
            if (owner != Context.AgentId)
            {
                Logger.LogWarning("UnlockResource: Cannot unlock {ResourceId} - owned by {Owner}, not {Us}", 
                    ResourceId, owner, Context.AgentId);
                return NodeStatus.Failure;
            }
            
            // TODO: Integration mit OPC UA + MQTT
            
            Context.Set($"State_{ResourceId}_IsLocked", false);
            Context.Set($"State_{ResourceId}_LockOwner", string.Empty);
            Context.Set("unlocked", true);
            
            Logger.LogInformation("UnlockResource: Successfully unlocked {ResourceId}", ResourceId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "UnlockResource: Error unlocking {ResourceId}", ResourceId);
            return NodeStatus.Failure;
        }
    }
}
