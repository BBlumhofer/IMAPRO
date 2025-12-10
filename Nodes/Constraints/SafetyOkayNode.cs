using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Constraints;

/// <summary>
/// SafetyOkay - Prüft Sicherheitssensoren und OPC UA Safety-Flags
/// Stellt sicher, dass keine Skill-Ausführung bei unsicheren Bedingungen beginnt
/// </summary>
public class SafetyOkayNode : BTNode
{
    /// <summary>
    /// Sicherheitszone die geprüft wird
    /// </summary>
    public string ZoneId { get; set; } = "";
    
    /// <summary>
    /// Modul-ID
    /// </summary>
    public string ModuleId { get; set; } = "";
    
    /// <summary>
    /// Kritische Safety-Checks (Failure stoppt sofort)
    /// </summary>
    public bool CriticalCheck { get; set; } = true;

    public SafetyOkayNode() : base("SafetyOkay")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        var moduleId = !string.IsNullOrEmpty(ModuleId) ? ModuleId : Context.AgentId ?? "UnknownModule";
        var zoneId = !string.IsNullOrEmpty(ZoneId) ? ZoneId : moduleId;
        
        Logger.LogDebug("SafetyOkay: Checking safety for zone '{ZoneId}' in module '{ModuleId}'", zoneId, moduleId);
        
        try
        {
            // 1. Emergency Stop prüfen
            if (await CheckEmergencyStop(moduleId))
            {
                Logger.LogError("SafetyOkay: EMERGENCY STOP active in module '{ModuleId}'!", moduleId);
                Context.Set($"safety_ok_{zoneId}", false);
                return NodeStatus.Failure;
            }

            // 2. Safety Gate prüfen
            if (CriticalCheck && await CheckSafetyGate(zoneId))
            {
                Logger.LogWarning("SafetyOkay: Safety gate OPEN in zone '{ZoneId}'", zoneId);
                Context.Set($"safety_ok_{zoneId}", false);
                return NodeStatus.Failure;
            }

            // 3. Light Curtain prüfen
            if (CriticalCheck && await CheckLightCurtain(zoneId))
            {
                Logger.LogWarning("SafetyOkay: Light curtain interrupted in zone '{ZoneId}'", zoneId);
                Context.Set($"safety_ok_{zoneId}", false);
                return NodeStatus.Failure;
            }

            // 4. Safety PLC Status prüfen
            if (!await CheckSafetyPlcStatus(moduleId))
            {
                Logger.LogError("SafetyOkay: Safety PLC reports unsafe condition in module '{ModuleId}'", moduleId);
                Context.Set($"safety_ok_{zoneId}", false);
                return NodeStatus.Failure;
            }

            // 5. Operator Mode prüfen (manueller Modus kann Einschränkungen haben)
            if (await CheckOperatorMode(moduleId))
            {
                Logger.LogInformation("SafetyOkay: Operator mode active in module '{ModuleId}' - reduced speed required", moduleId);
            }

            Context.Set($"safety_ok_{zoneId}", true);
            Logger.LogInformation("SafetyOkay: Zone '{ZoneId}' is SAFE", zoneId);
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SafetyOkay: Error checking safety status");
            return NodeStatus.Failure;
        }
    }
    
    private Task<bool> CheckEmergencyStop(string moduleId)
    {
        // OPC UA: /Safety/EmergencyStop
        if (Context.Has($"emergency_stop_{moduleId}"))
        {
            return Task.FromResult(Context.Get<bool>($"emergency_stop_{moduleId}"));
        }
        
        // TODO: OPC UA Abfrage wenn Client verfügbar
        return Task.FromResult(false);
    }
    
    private Task<bool> CheckSafetyGate(string zoneId)
    {
        // OPC UA: /Safety/Zones/{zoneId}/GateOpen
        if (Context.Has($"safety_gate_open_{zoneId}"))
        {
            return Task.FromResult(Context.Get<bool>($"safety_gate_open_{zoneId}"));
        }
        return Task.FromResult(false);
    }
    
    private Task<bool> CheckLightCurtain(string zoneId)
    {
        // OPC UA: /Safety/Zones/{zoneId}/LightCurtain
        if (Context.Has($"light_curtain_interrupted_{zoneId}"))
        {
            return Task.FromResult(Context.Get<bool>($"light_curtain_interrupted_{zoneId}"));
        }
        return Task.FromResult(false);
    }
    
    private Task<bool> CheckSafetyPlcStatus(string moduleId)
    {
        // OPC UA: /Safety/PlcStatus
        if (Context.Has($"safety_plc_ok_{moduleId}"))
        {
            return Task.FromResult(Context.Get<bool>($"safety_plc_ok_{moduleId}"));
        }
        // Standardmäßig OK wenn keine Info vorhanden
        return Task.FromResult(true);
    }
    
    private Task<bool> CheckOperatorMode(string moduleId)
    {
        // OPC UA: /Mode/OperatorActive
        if (Context.Has($"operator_mode_{moduleId}"))
        {
            return Task.FromResult(Context.Get<bool>($"operator_mode_{moduleId}"));
        }
        return Task.FromResult(false);
    }
}
