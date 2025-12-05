using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes;

/// <summary>
/// CheckReadyState - Prüft ob Modul bereit ist
/// </summary>
public class CheckReadyStateNode : BTNode
{
    public string OpcNode { get; set; } = string.Empty;
    
    public CheckReadyStateNode() : base("CheckReadyState")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckReadyState: Reading {Node}", OpcNode);
        
        // OPC UA Read Logic hier (mockup für jetzt)
        var isReady = Context.Get<bool?>($"State_{OpcNode}_IsReady") ?? false;
        
        Context.Set("isReady", isReady);
        
        Logger.LogInformation("CheckReadyState: Module {Node} ready={Ready}", OpcNode, isReady);
        
        return isReady ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckLockedState - Prüft Lock-Status des Moduls
/// </summary>
public class CheckLockedStateNode : BTNode
{
    public string OpcNode { get; set; } = string.Empty;
    public bool ExpectLocked { get; set; } = false;
    
    public CheckLockedStateNode() : base("CheckLockedState")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckLockedState: Reading {Node}, expect locked={Expect}", OpcNode, ExpectLocked);
        
        var isLocked = Context.Get<bool?>($"State_{OpcNode}_IsLocked") ?? false;
        
        Context.Set("result", isLocked == ExpectLocked);
        
        Logger.LogInformation("CheckLockedState: Module {Node} locked={Locked}, expected={Expected}, match={Match}", 
            OpcNode, isLocked, ExpectLocked, isLocked == ExpectLocked);
        
        return (isLocked == ExpectLocked) ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckErrorState - Prüft Fehlercode des Moduls
/// </summary>
public class CheckErrorStateNode : BTNode
{
    public string OpcNode { get; set; } = string.Empty;
    public int? ExpectedError { get; set; } = null;
    
    public CheckErrorStateNode() : base("CheckErrorState")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckErrorState: Reading {Node}", OpcNode);
        
        var errorCode = Context.Get<int?>($"State_{OpcNode}_ErrorCode") ?? 0;
        
        bool hasError;
        
        if (ExpectedError.HasValue)
        {
            // Prüfe auf spezifischen Fehler
            hasError = errorCode == ExpectedError.Value;
            Logger.LogInformation("CheckErrorState: Module {Node} error={Code}, expected={Expected}, match={Match}", 
                OpcNode, errorCode, ExpectedError.Value, hasError);
        }
        else
        {
            // Prüfe ob irgendein Fehler vorliegt
            hasError = errorCode != 0;
            Logger.LogInformation("CheckErrorState: Module {Node} has error={HasError}, code={Code}", 
                OpcNode, hasError, errorCode);
        }
        
        Context.Set("hasError", hasError);
        
        return hasError ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckAlarmHistory - Prüft Alarm-Historie
/// </summary>
public class CheckAlarmHistoryNode : BTNode
{
    public string AlarmType { get; set; } = string.Empty;
    public TimeSpan TimeRange { get; set; } = TimeSpan.FromMinutes(5);
    
    public CheckAlarmHistoryNode() : base("CheckAlarmHistory")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckAlarmHistory: Checking {AlarmType} in last {Range}", AlarmType, TimeRange);
        
        var cutoffTime = DateTime.UtcNow - TimeRange;
        
        // Mockup: Lese Alarm-History aus Context
        var alarmHistory = Context.Get<List<(DateTime, string)>>("AlarmHistory") ?? new();
        
        var hasAlarm = alarmHistory.Any(a => 
            a.Item1 >= cutoffTime && 
            (string.IsNullOrEmpty(AlarmType) || a.Item2 == AlarmType));
        
        Context.Set("hasAlarm", hasAlarm);
        
        Logger.LogInformation("CheckAlarmHistory: Found alarm={HasAlarm} for type={Type} in range={Range}", 
            hasAlarm, AlarmType, TimeRange);
        
        return hasAlarm ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckInventory - Prüft Inventar-Verfügbarkeit
/// </summary>
public class CheckInventoryNode : BTNode
{
    public string ModuleId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public int MinAmount { get; set; } = 1;
    
    public CheckInventoryNode() : base("CheckInventory")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckInventory: Module={Module}, Item={Item}, Min={Min}", 
            ModuleId, ItemId, MinAmount);
        
        // Lese Inventar aus Context
        var inventoryKey = $"Inventory_{ModuleId}";
        var inventory = Context.Get<Dictionary<string, int>>(inventoryKey) ?? new();
        
        var available = inventory.TryGetValue(ItemId, out var amount) && amount >= MinAmount;
        
        Context.Set("available", available);
        
        Logger.LogInformation("CheckInventory: Module={Module}, Item={Item}, Available={Amount}/{Min}, result={Available}", 
            ModuleId, ItemId, inventory.GetValueOrDefault(ItemId, 0), MinAmount, available);
        
        return available ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckToolAvailability - Prüft Werkzeugverfügbarkeit
/// </summary>
public class CheckToolAvailabilityNode : BTNode
{
    public string ModuleId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    
    public CheckToolAvailabilityNode() : base("CheckToolAvailability")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckToolAvailability: Module={Module}, Tool={Tool}", ModuleId, ToolId);
        
        // Lese Tool-Liste aus Context
        var toolKey = $"Tools_{ModuleId}";
        var tools = Context.Get<HashSet<string>>(toolKey) ?? new();
        
        var available = tools.Contains(ToolId);
        
        Context.Set("available", available);
        
        Logger.LogInformation("CheckToolAvailability: Module={Module}, Tool={Tool}, Available={Available}", 
            ModuleId, ToolId, available);
        
        return available ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// RefreshStateMessage - Aktualisiert alle Modulzustände
/// </summary>
public class RefreshStateMessageNode : BTNode
{
    public string ModuleId { get; set; } = string.Empty;
    
    public RefreshStateMessageNode() : base("RefreshStateMessage")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("RefreshStateMessage: Module={Module}", ModuleId);
        
        // Simuliere OPC UA Read für alle State-Nodes
        var stateMessage = new
        {
            ModuleId,
            IsReady = Context.Get<bool?>($"State_{ModuleId}_IsReady") ?? false,
            IsLocked = Context.Get<bool?>($"State_{ModuleId}_IsLocked") ?? false,
            ErrorCode = Context.Get<int?>($"State_{ModuleId}_ErrorCode") ?? 0,
            Timestamp = DateTime.UtcNow
        };
        
        Context.Set("stateMessage", stateMessage);
        
        Logger.LogInformation("RefreshStateMessage: Module={Module}, Ready={Ready}, Locked={Locked}, Error={Error}", 
            ModuleId, stateMessage.IsReady, stateMessage.IsLocked, stateMessage.ErrorCode);
        
        return NodeStatus.Success;
    }
}

/// <summary>
/// CheckScheduleFreshness - Prüft Aktualität des Schedules
/// </summary>
public class CheckScheduleFreshnessNode : BTNode
{
    public string MachineId { get; set; } = string.Empty;
    public int MaxAgeMs { get; set; } = 60000; // 1 Minute default
    
    public CheckScheduleFreshnessNode() : base("CheckScheduleFreshness")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckScheduleFreshness: Machine={Machine}, MaxAge={MaxAge}ms", MachineId, MaxAgeMs);
        
        var scheduleKey = $"Schedule_{MachineId}_LastUpdated";
        var lastUpdated = Context.Get<DateTime?>(scheduleKey);
        
        if (!lastUpdated.HasValue)
        {
            Logger.LogWarning("CheckScheduleFreshness: No schedule found for machine={Machine}", MachineId);
            Context.Set("isFresh", false);
            return NodeStatus.Failure;
        }
        
        var age = DateTime.UtcNow - lastUpdated.Value;
        var isFresh = age.TotalMilliseconds <= MaxAgeMs;
        
        Context.Set("isFresh", isFresh);
        
        Logger.LogInformation("CheckScheduleFreshness: Machine={Machine}, Age={Age}ms, MaxAge={MaxAge}ms, Fresh={Fresh}", 
            MachineId, age.TotalMilliseconds, MaxAgeMs, isFresh);
        
        return isFresh ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckTimeDrift - Prüft Zeitsynchronisation mit NTP
/// </summary>
public class CheckTimeDriftNode : BTNode
{
    public DateTime NtpTime { get; set; } = DateTime.UtcNow;
    public int MaxDriftMs { get; set; } = 1000; // 1 Sekunde default
    
    public CheckTimeDriftNode() : base("CheckTimeDrift")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        var localTime = DateTime.UtcNow;
        var drift = Math.Abs((localTime - NtpTime).TotalMilliseconds);
        
        var isSynchronized = drift <= MaxDriftMs;
        
        Context.Set("isSynchronized", isSynchronized);
        
        Logger.LogInformation("CheckTimeDrift: LocalTime={Local}, NtpTime={Ntp}, Drift={Drift}ms, MaxDrift={Max}ms, Synchronized={Sync}", 
            localTime, NtpTime, drift, MaxDriftMs, isSynchronized);
        
        if (!isSynchronized)
        {
            Logger.LogWarning("CheckTimeDrift: Time drift too large: {Drift}ms > {Max}ms", drift, MaxDriftMs);
        }
        
        return isSynchronized ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckNeighborAvailability - Prüft Verfügbarkeit eines Nachbarmoduls
/// </summary>
public class CheckNeighborAvailabilityNode : BTNode
{
    public string NeighborId { get; set; } = string.Empty;
    
    public CheckNeighborAvailabilityNode() : base("CheckNeighborAvailability")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckNeighborAvailability: Neighbor={Neighbor}", NeighborId);
        
        var neighborsKey = $"Neighbors_{Context.AgentId}";
        var neighbors = Context.Get<Dictionary<string, object>>(neighborsKey) ?? new();
        
        if (!neighbors.ContainsKey(NeighborId))
        {
            Logger.LogWarning("CheckNeighborAvailability: Neighbor {Neighbor} not found", NeighborId);
            Context.Set("available", false);
            return NodeStatus.Failure;
        }
        
        // Prüfe Ready + Not Locked
        var isReady = Context.Get<bool?>($"State_{NeighborId}_IsReady") ?? false;
        var isLocked = Context.Get<bool?>($"State_{NeighborId}_IsLocked") ?? false;
        var available = isReady && !isLocked;
        
        Context.Set("available", available);
        
        Logger.LogInformation("CheckNeighborAvailability: Neighbor={Neighbor}, Ready={Ready}, Locked={Locked}, Available={Available}", 
            NeighborId, isReady, isLocked, available);
        
        return available ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckTransportArrival - Prüft ob Transport angekommen ist
/// </summary>
public class CheckTransportArrivalNode : BTNode
{
    public string TransportRequestId { get; set; } = string.Empty;
    
    public CheckTransportArrivalNode() : base("CheckTransportArrival")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckTransportArrival: RequestId={RequestId}", TransportRequestId);
        
        var transportKey = $"Transport_{TransportRequestId}_Status";
        var status = Context.Get<string>(transportKey) ?? "Unknown";
        
        var arrived = status == "Arrived" || status == "Completed";
        
        Context.Set("arrived", arrived);
        
        Logger.LogInformation("CheckTransportArrival: RequestId={RequestId}, Status={Status}, Arrived={Arrived}", 
            TransportRequestId, status, arrived);
        
        return arrived ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckCurrentSchedule - Validiert Schedule-Konsistenz
/// </summary>
public class CheckCurrentScheduleNode : BTNode
{
    public object? ScheduleRef { get; set; }
    
    public CheckCurrentScheduleNode() : base("CheckCurrentSchedule")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckCurrentSchedule: Validating schedule");
        
        // Mockup: Vergleiche erwartetes mit aktuellem Schedule
        var currentSchedule = Context.Get<object>("CurrentSchedule");
        
        bool isValid = currentSchedule != null && ScheduleRef != null;
        
        Context.Set("isValid", isValid);
        
        Logger.LogInformation("CheckCurrentSchedule: Valid={Valid}", isValid);
        
        return isValid ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckEarliestStartTime - Prüft ob Task schon starten kann
/// </summary>
public class CheckEarliestStartTimeNode : BTNode
{
    public string TaskId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.MinValue;
    
    public CheckEarliestStartTimeNode() : base("CheckEarliestStartTime")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckEarliestStartTime: Task={Task}, EarliestStart={Time}", TaskId, Timestamp);
        
        var now = DateTime.UtcNow;
        var canStart = now >= Timestamp;
        
        Context.Set("canStart", canStart);
        
        if (!canStart)
        {
            var waitTime = Timestamp - now;
            Logger.LogDebug("CheckEarliestStartTime: Task={Task} must wait {Wait}", TaskId, waitTime);
        }
        else
        {
            Logger.LogInformation("CheckEarliestStartTime: Task={Task} can start now", TaskId);
        }
        
        return canStart ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckDeadlineFeasible - Prüft ob Deadline noch erreichbar ist
/// </summary>
public class CheckDeadlineFeasibleNode : BTNode
{
    public string TaskId { get; set; } = string.Empty;
    public DateTime Deadline { get; set; } = DateTime.MaxValue;
    
    public CheckDeadlineFeasibleNode() : base("CheckDeadlineFeasible")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckDeadlineFeasible: Task={Task}, Deadline={Deadline}", TaskId, Deadline);
        
        var now = DateTime.UtcNow;
        var taskDurationKey = $"Task_{TaskId}_EstimatedDuration";
        var estimatedDuration = Context.Get<TimeSpan?>(taskDurationKey) ?? TimeSpan.FromMinutes(10);
        
        var earliestCompletion = now + estimatedDuration;
        var feasible = earliestCompletion <= Deadline;
        
        Context.Set("feasible", feasible);
        
        Logger.LogInformation("CheckDeadlineFeasible: Task={Task}, Now={Now}, EstCompletion={Est}, Deadline={Deadline}, Feasible={Feasible}", 
            TaskId, now, earliestCompletion, Deadline, feasible);
        
        if (!feasible)
        {
            var overrun = earliestCompletion - Deadline;
            Logger.LogWarning("CheckDeadlineFeasible: Task={Task} will miss deadline by {Overrun}", TaskId, overrun);
        }
        
        return feasible ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// CheckModuleCapacity - Prüft ob Modul genug Kapazität hat
/// </summary>
public class CheckModuleCapacityNode : BTNode
{
    public string ModuleId { get; set; } = string.Empty;
    public int RequiredCapacity { get; set; } = 1;
    
    public CheckModuleCapacityNode() : base("CheckModuleCapacity")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("CheckModuleCapacity: Module={Module}, Required={Required}", ModuleId, RequiredCapacity);
        
        var capacityKey = $"Module_{ModuleId}_AvailableCapacity";
        var availableCapacity = Context.Get<int?>(capacityKey) ?? 0;
        
        var sufficient = availableCapacity >= RequiredCapacity;
        
        Context.Set("sufficient", sufficient);
        
        Logger.LogInformation("CheckModuleCapacity: Module={Module}, Available={Available}, Required={Required}, Sufficient={Sufficient}", 
            ModuleId, availableCapacity, RequiredCapacity, sufficient);
        
        return sufficient ? NodeStatus.Success : NodeStatus.Failure;
    }
}
