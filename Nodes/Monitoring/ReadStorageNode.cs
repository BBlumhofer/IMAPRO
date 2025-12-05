using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using UAClient.Client;

namespace MAS_BT.Nodes.Monitoring;

/// <summary>
/// ReadStorage - Liest Storage-Komponente vom Remote-Modul aus
/// </summary>
public class ReadStorageNode : BTNode
{
    public string ModuleName { get; set; } = string.Empty;

    public ReadStorageNode() : base("ReadStorage")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("ReadStorage: Reading storage from module {ModuleName}", ModuleName);

        try
        {
            // Hole RemoteServer aus Context
            var server = Context.Get<RemoteServer>("RemoteServer");
            if (server == null)
            {
                Logger.LogError("ReadStorage: No RemoteServer found in context");
                return NodeStatus.Failure;
            }

            // Finde Modul
            RemoteModule? module = null;
            if (!string.IsNullOrEmpty(ModuleName))
            {
                if (!server.Modules.TryGetValue(ModuleName, out module))
                {
                    Logger.LogError("ReadStorage: Module {ModuleName} not found", ModuleName);
                    return NodeStatus.Failure;
                }
            }
            else
            {
                module = server.Modules.Values.FirstOrDefault(m => m.SkillSet.Count > 0);
                if (module == null)
                {
                    Logger.LogError("ReadStorage: No modules available");
                    return NodeStatus.Failure;
                }
            }

            // Prüfe ob Modul Storage-Komponenten hat
            if (module.Storages.Count == 0)
            {
                Logger.LogWarning("ReadStorage: Module {ModuleName} has no Storage components", module.Name);
                Set("hasStorage", false);
                return NodeStatus.Success; // Nicht alle Module haben Storage
            }

            Logger.LogInformation("ReadStorage: Found {StorageCount} Storage component(s)", module.Storages.Count);

            // Lese alle Storage-Komponenten
            var allStorageData = new List<Dictionary<string, object>>();

            foreach (var storageKv in module.Storages)
            {
                var storage = storageKv.Value;
                var storageName = storageKv.Key;

                Logger.LogInformation("ReadStorage: Processing storage '{StorageName}' with {SlotCount} slots", 
                    storageName, storage.Slots.Count);

                // Lese Storage-Informationen
                var storageInfo = new Dictionary<string, object>
                {
                    ["StorageName"] = storageName,
                    ["ModuleName"] = module.Name,
                    ["SlotCount"] = storage.Slots.Count,
                    ["FreeSlots"] = storage.Slots.Count(s => s.Value.Variables
                        .TryGetValue("IsSlotEmpty", out var emptyVar) && 
                        emptyVar.Value?.ToString()?.ToLower() == "true")
                };

                // Lese Details für jeden Slot
                var slotsData = new List<Dictionary<string, object>>();
                foreach (var slot in storage.Slots.Values)
                {
                    var slotData = new Dictionary<string, object>
                    {
                        ["SlotName"] = slot.Name
                    };

                    // Lese Slot-Variablen
                    if (slot.Variables.TryGetValue("IsSlotEmpty", out var isEmpty))
                        slotData["IsEmpty"] = isEmpty.Value?.ToString() ?? "unknown";

                    if (slot.Variables.TryGetValue("CarrierID", out var carrierId))
                        slotData["CarrierID"] = carrierId.Value?.ToString() ?? "";

                    if (slot.Variables.TryGetValue("ProductID", out var productId))
                        slotData["ProductID"] = productId.Value?.ToString() ?? "";

                    if (slot.Variables.TryGetValue("ProductType", out var productType))
                        slotData["ProductType"] = productType.Value?.ToString() ?? "";

                    if (slot.Variables.TryGetValue("CarrierType", out var carrierType))
                        slotData["CarrierType"] = carrierType.Value?.ToString() ?? "";

                    slotsData.Add(slotData);

                    Logger.LogDebug("ReadStorage: Slot {SlotName} - Empty: {IsEmpty}, ProductID: {ProductID}",
                        slot.Name,
                        slotData["IsEmpty"],
                        slotData.GetValueOrDefault("ProductID", ""));
                }

                storageInfo["Slots"] = slotsData;
                allStorageData.Add(storageInfo);

                // Speichere im Context (pro Storage)
                Context.Set($"Storage_{module.Name}_{storageName}", storageInfo);
                Context.Set($"Storage_{module.Name}_{storageName}_Slots", slotsData);
                Context.Set($"Storage_{module.Name}_{storageName}_SlotCount", storage.Slots.Count);
                Context.Set($"Storage_{module.Name}_{storageName}_FreeSlots", storageInfo["FreeSlots"]);

                Logger.LogInformation("ReadStorage: Storage '{StorageName}' data saved - {SlotCount} slots, {FreeSlots} free",
                    storageName, storageInfo["SlotCount"], storageInfo["FreeSlots"]);
            }

            // Speichere Gesamtübersicht im Context
            Context.Set("hasStorage", true);
            Context.Set($"Storage_{module.Name}_All", allStorageData);
            Context.Set($"Storage_{module.Name}_Count", module.Storages.Count);

            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadStorage: Error reading storage");
            return NodeStatus.Failure;
        }
    }
}
