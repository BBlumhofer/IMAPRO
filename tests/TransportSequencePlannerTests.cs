using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MAS_BT.Services.Transport;
using Xunit;
using Xunit.Abstractions;

namespace MAS_BT.Tests;

public class TransportSequencePlannerTests
{
    private readonly ITestOutputHelper _output;

    public TransportSequencePlannerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void Log(string format, params object[] args)
    {
        var message = string.Format(format, args);
        _output.WriteLine(message);
        Console.WriteLine(message);
    }

    [Fact]
    public async Task BuildPlanAsync_ReturnsLegsWithCostFromDistance()
    {
        var graph = new InMemoryTransportGraphQuery(new[]
        {
            new TransportEdge("P100", "P101", 5.0)
        });

        Log("Graph edges: {0}", string.Join(", ", graph.Edges.Select(e => $"{e.From}->{e.To}({e.Distance})")));

        var planner = new TransportSequencePlanner(graph, costPerMeter: 2.0);

        var plan = await planner.BuildPlanAsync("P100", "P101");

        Log("Plan total cost: {0}", plan.TotalCost);
        for (var i = 0; i < plan.Legs.Count; i++)
        {
            var leg = plan.Legs[i];
            Log("Leg {0}: {1}->{2}, distance={3}, cost={4}", i + 1, leg.From, leg.To, leg.Distance, leg.Cost);
        }

        Assert.Single(plan.Legs);
        Assert.Equal(10.0, plan.TotalCost);
        Assert.Equal(("P100", "P101"), (plan.Legs[0].From, plan.Legs[0].To));
        Assert.Equal(10.0, plan.Legs[0].Cost);
    }

    [Fact]
    public async Task BuildPlanAsync_ThrowsWhenNoPath()
    {
        var graph = new InMemoryTransportGraphQuery(Array.Empty<TransportEdge>());
        var planner = new TransportSequencePlanner(graph, costPerMeter: 1.0);

        Log("Graph edges: <empty>");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => planner.BuildPlanAsync("P300", "P999"));
        Log("Expected failure: {0}", ex.Message);
        Assert.Contains("No path found", ex.Message);
    }

    private sealed class InMemoryTransportGraphQuery : ITransportGraphQuery
    {
        private readonly List<TransportEdge> _edges;

        public InMemoryTransportGraphQuery(IEnumerable<TransportEdge> edges)
        {
            _edges = edges?.ToList() ?? new List<TransportEdge>();
        }

        public IReadOnlyList<TransportEdge> Edges => _edges;

        public Task<IReadOnlyList<TransportEdge>> GetShortestPathAsync(
            string fromModuleId,
            string toModuleId,
            CancellationToken cancellationToken = default)
        {
            // Simple linear path mock: return edges starting from "from" until "to" is reached.
            var result = new List<TransportEdge>();
            var current = fromModuleId;
            var safety = _edges.Count + 1;
            while (!string.Equals(current, toModuleId, StringComparison.OrdinalIgnoreCase) && safety-- > 0)
            {
                var next = _edges.FirstOrDefault(e => string.Equals(e.From, current, StringComparison.OrdinalIgnoreCase));
                if (next == null)
                {
                    return Task.FromResult<IReadOnlyList<TransportEdge>>(Array.Empty<TransportEdge>());
                }

                result.Add(next);
                current = next.To;
            }

            if (!string.Equals(current, toModuleId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IReadOnlyList<TransportEdge>>(Array.Empty<TransportEdge>());
            }

            return Task.FromResult<IReadOnlyList<TransportEdge>>(result);
        }
    }
}
