using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using UAClient.Client;

namespace MAS_BT.Nodes.ModuleHolon;

/// <summary>
/// Reads the neighbors from the RemoteServer/RemoteModule (SkillClient) and exposes them in the BT context
/// under the key "Neighbors" so other nodes (e.g. PublishNeighbors) can use them.
/// </summary>
public class ReadNeighborsFromRemoteNode : BTNode
{
    public string ModuleName { get; set; } = string.Empty;

    public ReadNeighborsFromRemoteNode() : base("ReadNeighborsFromRemote") { }

    public override Task<NodeStatus> Execute()
    {
        try
        {
            var server = Context.Get<RemoteServer>("RemoteServer");
            if (server == null)
            {
                Logger.LogError("ReadNeighborsFromRemote: No RemoteServer in context");
                return Task.FromResult(NodeStatus.Failure);
            }

            var resolvedModuleName = ResolvePlaceholders(ModuleName);
            if (string.IsNullOrWhiteSpace(resolvedModuleName))
            {
                resolvedModuleName = Context.Get<string>("config.Agent.ModuleName") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;
            }

            if (!server.Modules.TryGetValue(resolvedModuleName, out var module))
            {
                Logger.LogWarning("ReadNeighborsFromRemote: Module '{Module}' not found on RemoteServer", resolvedModuleName);
                return Task.FromResult(NodeStatus.Failure);
            }

            var neigh = module.GetNeighborIds();
            Context.Set("Neighbors", new List<string>(neigh));
            Logger.LogInformation("ReadNeighborsFromRemote: loaded {Count} neighbors for module {Module}", neigh.Count, resolvedModuleName);
            return Task.FromResult(NodeStatus.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadNeighborsFromRemote: error while reading neighbors");
            return Task.FromResult(NodeStatus.Failure);
        }
    }
}
