using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using BaSyx.Models.AdminShell;
using AasSharpClient.Models.Messages;

namespace MAS_BT.Nodes.ModuleHolon;

public class ModuleHolonRegistrationNode : BTNode
{
    public ModuleHolonRegistrationNode() : base("ModuleHolonRegistration") {}

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("ModuleHolonRegistration: MessagingClient unavailable");
            return NodeStatus.Failure;
        }

        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
        var moduleNameConfig = Context.Get<string>("config.Agent.ModuleName")
                             ?? Context.Get<string>("config.OPCUA.ModuleName")
                             ?? Context.Get<string>("ModuleName");
        var moduleId = Context.Get<string>("config.Agent.ModuleId")
                       ?? moduleNameConfig
                       ?? Context.Get<string>("ModuleId")
                       ?? Context.AgentId;
        var moduleName = moduleNameConfig ?? moduleId;
        var cacheKey = Context.Get<string>("config.Agent.ModuleName")
                      ?? Context.Get<string>("ModuleId")
                      ?? Context.AgentId;
        var topic = $"/{ns}/DispatchingAgent/Register";

        try
        {
            var builder = new I40MessageBuilder()
                .From(Context.AgentId, string.IsNullOrWhiteSpace(Context.AgentRole) ? "ModuleHolon" : Context.AgentRole)
                .To("DispatchingAgent", null)
                .WithType("moduleRegistration")
                .WithConversationId(Guid.NewGuid().ToString());

            builder.AddElement(CreateStringProperty("ModuleId", moduleId));
            builder.AddElement(CreateStringProperty("ModuleName", moduleName));

            var contextInventory = GetContextInventory();
            var contextNeighbors = GetContextNeighbors();

            var inventoryCache = Context.Get<CachedInventoryData>($"InventoryCache_{cacheKey}");
            var neighborsCache = Context.Get<CachedNeighborsData>($"NeighborsCache_{cacheKey}");

            // Read configurable timeouts from config (ms)
            var cfgInitialMs = Context.Get<int>("config.Agent.InitialSnapshotTimeoutMs");
            if (cfgInitialMs <= 0) cfgInitialMs = Context.Get<int>("config.InitialSnapshotTimeoutMs");
            if (cfgInitialMs <= 0) cfgInitialMs = 3000;

            var cfgPollMs = Context.Get<int>("config.Agent.SnapshotPollDelayMs");
            if (cfgPollMs <= 0) cfgPollMs = Context.Get<int>("config.SnapshotPollDelayMs");
            if (cfgPollMs <= 0) cfgPollMs = 200;

            var initialRegistrationCompleted = Context.Get<bool>("ModuleHolonRegistrationInitialized");
            var waitForInventory = !HasInventoryData(contextInventory, inventoryCache);
            var waitForNeighbors = !HasNeighborData(contextNeighbors, neighborsCache);

            if (!initialRegistrationCompleted && (waitForInventory || waitForNeighbors))
            {
                Logger.LogInformation(
                    "ModuleHolonRegistration: waiting up to {TimeoutMs} ms for initial {MissingParts} snapshot(s)",
                    cfgInitialMs,
                    DescribeMissing(waitForInventory, waitForNeighbors));

                await WaitForInitialSnapshots(cacheKey, waitForInventory, waitForNeighbors, TimeSpan.FromMilliseconds(cfgInitialMs), TimeSpan.FromMilliseconds(cfgPollMs));

                contextInventory = GetContextInventory();
                contextNeighbors = GetContextNeighbors();
                inventoryCache = Context.Get<CachedInventoryData>($"InventoryCache_{cacheKey}");
                neighborsCache = Context.Get<CachedNeighborsData>($"NeighborsCache_{cacheKey}");
            }

            var updatedCandidates = new List<DateTime>();
            try
            {
                if (contextInventory != null)
                {
                    Logger.LogDebug("ModuleHolonRegistration: context inventory present: storages={Count}", contextInventory.Count);
                    if (contextInventory.Count > 0)
                    {
                        var s = contextInventory[0];
                        Logger.LogDebug("ModuleHolonRegistration: context sample storage='{Name}' slots={Slots}", s.Name, s.Slots?.Count ?? 0);
                        if (s.Slots != null && s.Slots.Count > 0)
                        {
                            var c = s.Slots[0].Content;
                            Logger.LogDebug("ModuleHolonRegistration: sample slot content: CarrierID='{CarrierID}', ProductID='{ProductID}', ProductType='{ProductType}'", c.CarrierID, c.ProductID, c.ProductType);
                        }
                    }
                }
                else if (inventoryCache != null)
                {
                    Logger.LogDebug("ModuleHolonRegistration: using cached inventory: storages={Count}", inventoryCache.StorageUnits.Count);
                    if (inventoryCache.StorageUnits.Count > 0)
                    {
                        var s2 = inventoryCache.StorageUnits[0];
                        Logger.LogDebug("ModuleHolonRegistration: cached sample storage='{Name}' slots={Slots}", s2.Name, s2.Slots?.Count ?? 0);
                        if (s2.Slots != null && s2.Slots.Count > 0)
                        {
                            var c2 = s2.Slots[0].Content;
                            Logger.LogDebug("ModuleHolonRegistration: cached sample slot content: CarrierID='{CarrierID}', ProductID='{ProductID}', ProductType='{ProductType}'", c2.CarrierID, c2.ProductID, c2.ProductType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "ModuleHolonRegistration: failed to log inventory sample");
            }
            if (contextInventory != null && contextInventory.Count > 0)
            {
                updatedCandidates.Add(DateTime.UtcNow);
                builder.AddElement(BuildInventoryElement(contextInventory));
            }
            else if (inventoryCache != null)
            {
                updatedCandidates.Add(inventoryCache.UpdatedAtUtc);
                builder.AddElement(BuildInventoryElement(inventoryCache.StorageUnits));
            }

            if (contextNeighbors != null && contextNeighbors.Count > 0)
            {
                updatedCandidates.Add(DateTime.UtcNow);
                builder.AddElement(BuildNeighborsCollection(contextNeighbors));
            }
            else if (neighborsCache != null)
            {
                updatedCandidates.Add(neighborsCache.UpdatedAtUtc);
                builder.AddElement(BuildNeighborsCollection(neighborsCache.Neighbors));
            }

            var updatedTimestamp = updatedCandidates.Count > 0 ? updatedCandidates.Max() : DateTime.UtcNow;
            builder.AddElement(CreateStringProperty("UpdatedTimestamp", updatedTimestamp.ToString("O")));

            var msg = builder.Build();
            await client.PublishAsync(msg, topic);
            Logger.LogInformation("ModuleHolonRegistration: sent registration for {ModuleId} to {Topic}", moduleId, topic);

            if (!initialRegistrationCompleted)
            {
                Context.Set("ModuleHolonRegistrationInitialized", true);
            }

            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ModuleHolonRegistration: failed to send registration");
            return NodeStatus.Failure;
        }
    }

    private static Property<string> CreateStringProperty(string idShort, string value)
    {
        return new Property<string>(idShort)
        {
            Value = new PropertyValue<string>(value ?? string.Empty)
        };
    }

    private static SubmodelElement BuildInventoryElement(IReadOnlyList<StorageUnit> storageUnits)
    {
        return new InventoryMessage(storageUnits?.ToList() ?? new List<StorageUnit>());
    }

    private List<StorageUnit>? GetContextInventory()
    {
        return Context.Has("ModuleInventory")
            ? Context.Get<List<StorageUnit>>("ModuleInventory")
            : null;
    }

    private List<string>? GetContextNeighbors()
    {
        return Context.Has("Neighbors")
            ? Context.Get<List<string>>("Neighbors")
            : null;
    }

    private static bool HasInventoryData(ICollection<StorageUnit>? contextInventory, CachedInventoryData? cache)
    {
        return (contextInventory != null && contextInventory.Count > 0)
               || (cache?.StorageUnits != null && cache.StorageUnits.Count > 0);
    }

    private static bool HasNeighborData(ICollection<string>? contextNeighbors, CachedNeighborsData? cache)
    {
        return (contextNeighbors != null && contextNeighbors.Count > 0)
               || (cache?.Neighbors != null && cache.Neighbors.Count > 0);
    }

    private async Task WaitForInitialSnapshots(string cacheKey, bool waitForInventory, bool waitForNeighbors, TimeSpan timeout, TimeSpan pollDelay)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var inventoryReady = !waitForInventory || HasInventoryData(GetContextInventory(), Context.Get<CachedInventoryData>($"InventoryCache_{cacheKey}"));
            var neighborsReady = !waitForNeighbors || HasNeighborData(GetContextNeighbors(), Context.Get<CachedNeighborsData>($"NeighborsCache_{cacheKey}"));

            if (inventoryReady && neighborsReady)
            {
                return;
            }

            await Task.Delay(pollDelay);
        }
    }

    private static string DescribeMissing(bool waitForInventory, bool waitForNeighbors)
    {
        if (waitForInventory && waitForNeighbors)
        {
            return "inventory & neighbor";
        }

        if (waitForInventory)
        {
            return "inventory";
        }

        return "neighbor";
    }

    private static SubmodelElementCollection BuildNeighborsCollection(IReadOnlyList<string> neighbors)
    {
        var collection = new SubmodelElementCollection("Neighbors");
        if (neighbors == null || neighbors.Count == 0)
        {
            return collection;
        }

        for (var i = 0; i < neighbors.Count; i++)
        {
            collection.Add(CreateStringProperty($"Neighbor_{i}", neighbors[i] ?? string.Empty));
        }

        return collection;
    }
}
