using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using UAClient.Client;
using UAClient.Common;

namespace MAS_BT.Nodes.Skills;

/// <summary>
/// ExecuteSkill - Führt einen Skill auf einem Remote-Modul aus
/// Nutzt den RemoteServer aus dem Context (muss vorher via ConnectToModule verbunden sein)
/// Verwendet RemoteSkill.ExecuteAsync() mit automatischem Reset bei Halted State
/// </summary>
public class ExecuteSkillNode : BTNode
{
    public string SkillName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public bool WaitForCompletion { get; set; } = true;
    public bool ResetAfterCompletion { get; set; } = true;
    public bool ResetBeforeIfHalted { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 60;

    public ExecuteSkillNode() : base("ExecuteSkill")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("ExecuteSkill: Executing {SkillName} on module {ModuleName}", SkillName, ModuleName);

        try
        {
            // Hole RemoteServer aus Context (wurde von ConnectToModule gesetzt)
            var server = Context.Get<RemoteServer>("RemoteServer");
            if (server == null)
            {
                Logger.LogError("ExecuteSkill: No RemoteServer found in context. Connect first with ConnectToModule.");
                Set("started", false);
                return NodeStatus.Failure;
            }

            // Finde Modul (wenn nicht angegeben, nimm erstes verfügbares)
            RemoteModule? module = null;
            if (!string.IsNullOrEmpty(ModuleName))
            {
                if (!server.Modules.TryGetValue(ModuleName, out module))
                {
                    Logger.LogError("ExecuteSkill: Module {ModuleName} not found", ModuleName);
                    Logger.LogDebug("Available modules: {Modules}", string.Join(", ", server.Modules.Keys));
                    Set("started", false);
                    return NodeStatus.Failure;
                }
            }
            else
            {
                // Nimm erstes Modul MIT Skills (nicht nur irgendein Modul)
                module = server.Modules.Values.FirstOrDefault(m => m.SkillSet.Count > 0);
                if (module == null)
                {
                    Logger.LogError("ExecuteSkill: No modules with skills available on server");
                    Set("started", false);
                    return NodeStatus.Failure;
                }
                Logger.LogDebug("ExecuteSkill: Using first module with skills: {ModuleName} ({SkillCount} skills)", 
                    module.Name, module.SkillSet.Count);
            }

            // Prüfe ob Modul gelockt ist
            var isLocked = Context.Get<bool>($"State_{module.Name}_IsLocked");
            if (!isLocked)
            {
                Logger.LogWarning("ExecuteSkill: Module {ModuleName} is not locked! Lock the module first.", module.Name);
                // Versuche trotzdem fortzufahren - OPC UA Server könnte Lock anders behandeln
            }

            // Finde Skill (SkillSet ist das Dictionary in RemoteModule!)
            if (!module.SkillSet.TryGetValue(SkillName, out var skill))
            {
                Logger.LogError("ExecuteSkill: Skill {SkillName} not found on module {ModuleName}", SkillName, module.Name);
                Logger.LogDebug("Available skills: {Skills}", string.Join(", ", module.SkillSet.Keys));
                Set("started", false);
                return NodeStatus.Failure;
            }

            Logger.LogInformation("ExecuteSkill: Skill {SkillName} found. Current state: {State}", 
                SkillName, skill.CurrentState);

            // Verwende module.StartAsync() wie im Integration Test
            // Das macht: Reset (falls nötig) → Start → Wait for Completion
            Logger.LogInformation("ExecuteSkill: Starting skill {SkillName} via module.StartAsync()...", SkillName);
            
            var timeout = TimeSpan.FromSeconds(TimeoutSeconds);
            await module.StartAsync(reset: ResetBeforeIfHalted, timeout: timeout);

            // Speichere Ergebnis
            Set("started", true);
            Set("lastExecutedSkill", SkillName);
            Set($"skill_{SkillName}_state", skill.CurrentState.ToString());
            
            Logger.LogInformation("ExecuteSkill: Module {ModuleName} started successfully. Final state: {State}", 
                module.Name, skill.CurrentState);
            
            return NodeStatus.Success;
        }
        catch (TimeoutException tex)
        {
            Logger.LogError(tex, "ExecuteSkill: Timeout executing skill {SkillName}", SkillName);
            Set("started", false);
            return NodeStatus.Failure;
        }
        catch (InvalidOperationException ioe)
        {
            Logger.LogError(ioe, "ExecuteSkill: Invalid state for skill {SkillName}", SkillName);
            Set("started", false);
            return NodeStatus.Failure;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ExecuteSkill: Error executing skill {SkillName}", SkillName);
            Set("started", false);
            return NodeStatus.Failure;
        }
    }
}
