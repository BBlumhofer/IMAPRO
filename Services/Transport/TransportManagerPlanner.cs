using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AasSharpClient.Models.Messages;

namespace MAS_BT.Services.Transport;

public interface ITransportManagerPlanner
{
    Task<TransportManagerPlanResult> BuildPlanAsync(
        TransportRequestMessage request,
        CancellationToken cancellationToken = default);
}

public sealed record TransportManagerPlanResult(
    TransportPlan Plan,
    TransportCapabilitySequence CapabilitySequence,
    TransportCapabilityCatalog Catalog,
    IReadOnlyList<string> ModulePath);

public sealed class TransportManagerPlanner : ITransportManagerPlanner
{
    private readonly TransportSequencePlanner _sequencePlanner;
    private readonly ITransportCapabilityCatalogProvider _catalogProvider;

    public TransportManagerPlanner(
        ITransportGraphQuery graphQuery,
        ITransportCapabilityCatalogProvider catalogProvider,
        double costPerMeter)
    {
        if (graphQuery == null) throw new ArgumentNullException(nameof(graphQuery));
        if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
        _sequencePlanner = new TransportSequencePlanner(graphQuery, costPerMeter);
        _catalogProvider = catalogProvider;
    }

    public async Task<TransportManagerPlanResult> BuildPlanAsync(
        TransportRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var startModuleId = request.TransportStartStationText;
        var targetModuleId = request.TransportGoalStationText;
        if (string.IsNullOrWhiteSpace(startModuleId))
        {
            throw new InvalidOperationException("Transport request missing TransportStartStation.");
        }

        if (string.IsNullOrWhiteSpace(targetModuleId))
        {
            throw new InvalidOperationException("Transport request missing TransportGoalStation.");
        }

        var plan = await _sequencePlanner
            .BuildPlanAsync(startModuleId, targetModuleId, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"[DEBUG] TransportManagerPlanner: built transport plan between {startModuleId} -> {targetModuleId}");

        var modulePath = BuildModulePath(startModuleId, plan.Legs);
        Console.WriteLine($"[DEBUG] ModulePath: {string.Join("->", modulePath)}");
        var catalog = await _catalogProvider
            .GetCatalogAsync(modulePath, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"[DEBUG] Catalog retrieved for modules: {string.Join(',', modulePath)}");

        var capabilityPlanner = new TransportCapabilityPlanner(catalog);
        var capabilitySequence = capabilityPlanner.BuildSequence(modulePath, request);

        return new TransportManagerPlanResult(plan, capabilitySequence, catalog, modulePath);
    }

    private static IReadOnlyList<string> BuildModulePath(string requestedStart, IReadOnlyList<TransportLeg> legs)
    {
        var path = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedStart))
        {
            path.Add(requestedStart);
        }

        if (legs == null || legs.Count == 0)
        {
            return path;
        }

        if (path.Count == 0)
        {
            path.Add(legs[0].From);
        }

        foreach (var leg in legs)
        {
            if (path.Count == 0 || !string.Equals(path[^1], leg.From, StringComparison.OrdinalIgnoreCase))
            {
                path.Add(leg.From);
            }

            path.Add(leg.To);
        }

        return path;
    }
}
