using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MAS_BT.Services.Transport;

public sealed class TransportSequencePlanner
{
    private readonly ITransportGraphQuery _graphQuery;
    private readonly double _costPerMeter;

    public TransportSequencePlanner(ITransportGraphQuery graphQuery, double costPerMeter)
    {
        _graphQuery = graphQuery ?? throw new ArgumentNullException(nameof(graphQuery));
        if (costPerMeter < 0) throw new ArgumentOutOfRangeException(nameof(costPerMeter));
        _costPerMeter = costPerMeter;
    }

    public async Task<TransportPlan> BuildPlanAsync(
        string fromModuleId,
        string toModuleId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromModuleId))
        {
            throw new ArgumentException("fromModuleId missing", nameof(fromModuleId));
        }
        if (string.IsNullOrWhiteSpace(toModuleId))
        {
            throw new ArgumentException("toModuleId missing", nameof(toModuleId));
        }

        Console.WriteLine($"[DEBUG] TransportSequencePlanner.BuildPlanAsync from={fromModuleId} to={toModuleId}");
        var edges = await _graphQuery.GetShortestPathAsync(fromModuleId, toModuleId, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"[DEBUG] GraphQuery returned edges_count={(edges?.Count ?? 0)}");

        if (edges == null || edges.Count == 0)
        {
            Console.WriteLine($"[DEBUG] No path found from '{fromModuleId}' to '{toModuleId}'.");
            throw new InvalidOperationException($"No path found from '{fromModuleId}' to '{toModuleId}'.");
        }

        var legs = new List<TransportLeg>(edges.Count);
        foreach (var edge in edges)
        {
            var cost = edge.Distance * _costPerMeter;
            legs.Add(new TransportLeg(edge.From, edge.To, edge.Distance, cost));
        }

        var total = legs.Sum(l => l.Cost);
        Console.WriteLine($"[DEBUG] Built plan legs={legs.Count} totalCost={total}");
        return new TransportPlan(legs, total);
    }
}
