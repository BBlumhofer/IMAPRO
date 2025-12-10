using System;
using System.Text.Json;
using AasSharpClient.Messages;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching
{
    /// <summary>
    /// Ensures a dispatching state is available in the BT context and pre-populated from config.
    /// </summary>
    public class InitializeDispatchingStateNode : BTNode
    {
        public InitializeDispatchingStateNode() : base("InitializeDispatchingState") { }

        public override Task<NodeStatus> Execute()
        {
            var state = Context.Get<DispatchingState>("DispatchingState") ?? new DispatchingState();
            Context.Set("DispatchingState", state);

            try
            {
                var modulesElement = Context.Get<JsonElement>("config.DispatchingAgent.Modules");
                if (modulesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var moduleElem in modulesElement.EnumerateArray())
                    {
                        var module = ParseModule(moduleElem);
                        if (module != null)
                        {
                            state.Upsert(module);
                        }
                    }
                }

                Logger.LogInformation("InitializeDispatchingState: loaded {Count} modules from config", state.Modules.Count);
                return Task.FromResult(NodeStatus.Success);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "InitializeDispatchingState: failed to load modules from config");
                return Task.FromResult(NodeStatus.Failure);
            }
        }

        private DispatchingModuleInfo? ParseModule(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            var moduleId = element.TryGetProperty("ModuleId", out var idElem) && idElem.ValueKind == JsonValueKind.String
                ? idElem.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(moduleId))
                return null;

            var info = new DispatchingModuleInfo
            {
                ModuleId = moduleId!,
                AasId = element.TryGetProperty("AasId", out var aasElem) && aasElem.ValueKind == JsonValueKind.String
                    ? aasElem.GetString()
                    : null,
                LastRegistrationUtc = DateTime.UtcNow
            };

            if (element.TryGetProperty("Capabilities", out var capsElem) && capsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var cap in capsElem.EnumerateArray())
                {
                    if (cap.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cap.GetString()))
                    {
                        info.Capabilities.Add(cap.GetString()!);
                    }
                }
            }

            if (element.TryGetProperty("Neighbors", out var neighElem) && neighElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var neighbor in neighElem.EnumerateArray())
                {
                    if (neighbor.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(neighbor.GetString()))
                    {
                        info.Neighbors.Add(neighbor.GetString()!);
                    }
                }
            }

            return info;
        }
    }
}
