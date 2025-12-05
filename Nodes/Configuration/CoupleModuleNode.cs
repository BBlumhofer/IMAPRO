using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// CoupleModule - Registriert das Modul im System für Multi-Modul-Umgebungen
/// </summary>
public class CoupleModuleNode : BTNode
{
    public string ModuleId { get; set; } = string.Empty;
    
    public CoupleModuleNode() : base("CoupleModule")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("CoupleModule: Coupling module {ModuleId}", ModuleId);
        
        try
        {
            // Prüfe ob bereits gecouplet
            var isCoupled = Context.Get<bool?>($"Module_{ModuleId}_Coupled") ?? false;
            
            if (isCoupled)
            {
                Logger.LogDebug("CoupleModule: Module {ModuleId} already coupled", ModuleId);
                Set("coupled", true);
                return NodeStatus.Success;
            }
            
            // TODO: Integration mit OPC UA / System Registry
            // - Registriere Modul in zentraler Registry
            // - Sende Coupling-Nachricht via MQTT
            // - Aktualisiere Neighbor-Liste
            
            // Mockup: Setze Coupling Status
            Context.Set($"Module_{ModuleId}_Coupled", true);
            
            // Füge zu CoupledModules Liste hinzu
            var coupledModules = Context.Get<List<string>>("CoupledModules") ?? new List<string>();
            if (!coupledModules.Contains(ModuleId))
            {
                coupledModules.Add(ModuleId);
                Context.Set("CoupledModules", coupledModules);
            }
            
            Set("coupled", true);
            Logger.LogInformation("CoupleModule: Successfully coupled module {ModuleId}", ModuleId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "CoupleModule: Error coupling module {ModuleId}", ModuleId);
            Set("coupled", false);
            return NodeStatus.Failure;
        }
    }
}
