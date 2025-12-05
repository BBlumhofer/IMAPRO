using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using UAClient.Client;

namespace MAS_BT.Nodes.Locking;

/// <summary>
/// CheckLockStatus - Prüft ob das Modul von uns gelockt ist
/// Verwendet die neue IsLockedByUs Property aus RemoteModule
/// </summary>
public class CheckLockStatusNode : BTNode
{
    public string ModuleName { get; set; } = string.Empty;

    public CheckLockStatusNode() : base("CheckLockStatus")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        try
        {
            var server = Context.Get<RemoteServer>("RemoteServer");
            if (server == null)
            {
                Logger.LogError("CheckLockStatus: No RemoteServer in context");
                return NodeStatus.Failure;
            }

            var client = Context.Get<UaClient>("UaClient");
            if (client?.Session == null)
            {
                Logger.LogError("CheckLockStatus: No UaClient Session available");
                return NodeStatus.Failure;
            }

            // Finde Modul
            RemoteModule? module = null;
            if (!string.IsNullOrEmpty(ModuleName))
            {
                if (!server.Modules.TryGetValue(ModuleName, out module))
                {
                    Logger.LogError("CheckLockStatus: Module {ModuleName} not found", ModuleName);
                    return NodeStatus.Failure;
                }
            }
            else
            {
                module = server.Modules.Values.FirstOrDefault(m => m.SkillSet.Count > 0);
                if (module == null)
                {
                    Logger.LogError("CheckLockStatus: No modules available");
                    return NodeStatus.Failure;
                }
            }

            // Prüfe Lock-Status mit der neuen IsLockedByUs Property
            if (module.Lock == null)
            {
                Logger.LogWarning("CheckLockStatus: Module {ModuleName} has no Lock", module.Name);
                Set("lockStatus", false);
                return NodeStatus.Failure;
            }

            // Verwende die neue IsLockedByUs Property vom RemoteModule
            if (module.IsLockedByUs)
            {
                Logger.LogDebug("CheckLockStatus: Module {ModuleName} is locked by us", module.Name);
                Set("lockStatus", true);
                Set("lockedByUs", true);
                return NodeStatus.Success;
            }
            else
            {
                Logger.LogWarning("CheckLockStatus: Module {ModuleName} is NOT locked by us", module.Name);
                Set("lockStatus", false);
                Set("lockedByUs", false);
                return NodeStatus.Failure;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "CheckLockStatus: Error checking lock status");
            Set("lockStatus", false);
            Set("lockedByUs", false);
            return NodeStatus.Failure;
        }
    }
}
