using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using UAClient.Client;

namespace MAS_BT.Nodes.Locking;

/// <summary>
/// LockResource - Lockt ein Remote-Modul via OPC UA Lock
/// Verwendet RemoteModule.LockAsync() aus dem SkillSharp Client
/// </summary>
public class LockResourceNode : BTNode
{
    public string ResourceId { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    
    public LockResourceNode() : base("LockResource")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("LockResource: Attempting to lock module {ModuleName} (ResourceId: {ResourceId})", 
            ModuleName, ResourceId);
        
        try
        {
            // Hole RemoteServer aus Context
            var server = Context.Get<RemoteServer>("RemoteServer");
            if (server == null)
            {
                Logger.LogError("LockResource: No RemoteServer found in context. Connect first with ConnectToModule.");
                Set("locked", false);
                return NodeStatus.Failure;
            }

            // Hole UaClient Session
            var client = Context.Get<UaClient>("UaClient");
            if (client?.Session == null)
            {
                Logger.LogError("LockResource: No UaClient Session available");
                Set("locked", false);
                return NodeStatus.Failure;
            }

            // Finde Modul
            RemoteModule? module = null;
            if (!string.IsNullOrEmpty(ModuleName))
            {
                if (!server.Modules.TryGetValue(ModuleName, out module))
                {
                    Logger.LogError("LockResource: Module {ModuleName} not found", ModuleName);
                    Logger.LogDebug("Available modules: {Modules}", string.Join(", ", server.Modules.Keys));
                    Set("locked", false);
                    return NodeStatus.Failure;
                }
            }
            else
            {
                // Nimm erstes Modul mit Skills
                module = server.Modules.Values.FirstOrDefault(m => m.SkillSet.Count > 0);
                if (module == null)
                {
                    Logger.LogError("LockResource: No modules available on server");
                    Set("locked", false);
                    return NodeStatus.Failure;
                }
                Logger.LogDebug("LockResource: Using first module with skills: {ModuleName}", module.Name);
            }

            // Lock das Modul via OPC UA
            Logger.LogInformation("LockResource: Calling module.LockAsync() for {ModuleName}...", module.Name);
            
            var lockResult = await module.LockAsync(client.Session);
            
            if (lockResult.HasValue && lockResult.Value)
            {
                Logger.LogInformation("LockResource: Lock call succeeded for {ModuleName}", module.Name);
                
                // Speichere Lock-Status im Context
                Context.Set($"State_{ResourceId}_IsLocked", true);
                Context.Set($"State_{ResourceId}_LockOwner", Context.AgentId);
                Context.Set($"State_{module.Name}_IsLocked", true);
                Context.Set("locked", true);
                Context.Set("lockedModule", module.Name);
                
                return NodeStatus.Success;
            }
            else
            {
                Logger.LogError("LockResource: Failed to lock {ModuleName}. Lock returned {Result}", 
                    module.Name, lockResult?.ToString() ?? "null");
                Set("locked", false);
                return NodeStatus.Failure;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "LockResource: Error locking module");
            Set("locked", false);
            return NodeStatus.Failure;
        }
    }
}
