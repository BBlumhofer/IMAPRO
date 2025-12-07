using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using UAClient.Client;

namespace MAS_BT.Nodes.Monitoring;

/// <summary>
/// CheckErrorState - Erkennt Fehler in Modulen oder Skills (unerwartete Halted States)
/// Iteriert durch alle Skills und prüft auf Halted State als Fehlerindikat
/// </summary>
public class CheckErrorStateNode : BTNode
{
    public string ModuleName { get; set; } = string.Empty;
    public int? ExpectedError { get; set; }

    public CheckErrorStateNode() : base("CheckErrorState")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        // Resolve Placeholders zur Laufzeit (z.B. {MachineName} → "ScrewingStation")
        var resolvedModuleName = ResolvePlaceholders(ModuleName);
        
        Logger.LogDebug("CheckErrorState: Checking error state of module {ModuleName}", resolvedModuleName);

        try
        {
            var server = Context.Get<RemoteServer>("RemoteServer");
            if (server == null)
            {
                Logger.LogError("CheckErrorState: No RemoteServer in context");
                return NodeStatus.Failure;
            }

            if (!server.Modules.TryGetValue(resolvedModuleName, out var module))
            {
                Logger.LogError("CheckErrorState: Module {ModuleName} not found", resolvedModuleName);
                return NodeStatus.Failure;
            }

            bool hasError = false;
            var haltedSkills = new List<string>();

            // Prüfe alle Skills auf Halted State
            foreach (var skill in module.SkillSet.Values)
            {
                if (skill.CurrentState == UAClient.Common.SkillStates.Halted)
                {
                    hasError = true;
                    haltedSkills.Add(skill.Name);
                }
            }

            Set($"module_{resolvedModuleName}_has_error", hasError);
            Context.Set($"State_{resolvedModuleName}_HasError", hasError);

            if (hasError)
            {
                var skillList = string.Join(", ", haltedSkills);
                Logger.LogError("CheckErrorState: Errors detected in module {ModuleName}. Halted skills: {Skills}", 
                    resolvedModuleName, string.IsNullOrEmpty(skillList) ? "unknown" : skillList);
            }

            return !hasError ? NodeStatus.Success : NodeStatus.Failure;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "CheckErrorState: Error checking error state of {ModuleName}", resolvedModuleName);
            return NodeStatus.Failure;
        }
    }
}
