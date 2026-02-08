using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo4j.Driver;

namespace MAS_BT.Services.Transport;

public sealed class Neo4jTransportGraphQuery : ITransportGraphQuery, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly string? _database;
    private readonly bool _ownsDriver;

    public Neo4jTransportGraphQuery(string uri, string user, string password, string? database = null)
    {
        if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("uri missing", nameof(uri));
        _database = database;
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user ?? string.Empty, password ?? string.Empty));
        _ownsDriver = true;
    }

    public Neo4jTransportGraphQuery(IDriver driver, string? database = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _database = database;
        _ownsDriver = false;
    }

    public async Task<IReadOnlyList<TransportEdge>> GetShortestPathAsync(string fromModuleId, string toModuleId, CancellationToken cancellationToken = default)
    {
        await using var session = string.IsNullOrWhiteSpace(_database)
            ? _driver.AsyncSession()
            : _driver.AsyncSession(o => o.WithDatabase(_database));

        // Strategy:
        // Only allow traversing ASSET_DISTANCE when the path includes a module that provides the 'Transport' capability.
        // Use a bounded two-segment shortest path via such a mid node to keep queries efficient.

                                var viaCypher = @"
                // find a mid node that provides Transport and compute bounded shortest paths from->mid and mid->to
                // Only consider mid nodes where source, mid and target have handover capabilities (Store or Retrieve)
                // label-agnostic match: find nodes by shell_id regardless of their labels (Asset, Module, ...)
                MATCH (from),(to)
                WHERE from.shell_id = $from AND to.shell_id = $to
                MATCH (mid)-[:PROVIDES_CAPABILITY]->(:Capability {idShort:'Transport'})
                WHERE mid <> from AND mid <> to
                        AND (
                                // source has a Store/Retrieve handover capability AND that capability exposes a transferRole/topologyType/dockRole
                                EXISTS {
                                    MATCH (from)-[:PROVIDES_CAPABILITY]->(cap:Capability)
                                    WHERE cap.idShort IN ['DockStoreHandoverCapability','DockRetrieveHandoverCapability']
                                        AND (
                                            EXISTS { MATCH (cap)-[:HAS_PROPERTY*0..4]->(p) WHERE 'Property' IN labels(p) AND p.idShort='transferRole' AND p.value IS NOT NULL }
                                            OR EXISTS { MATCH (cap)-[:HAS_PROPERTY*0..4]->(n) WHERE n.transferRole IS NOT NULL }
                                        )
                                    RETURN 1
                                }
                        )
                        AND (
                                // mid has Store/Retrieve handover capability with transferRole/topologyType/dockRole
                                EXISTS {
                                    MATCH (mid)-[:PROVIDES_CAPABILITY]->(capm:Capability)
                                    WHERE capm.idShort IN ['DockStoreHandoverCapability','DockRetrieveHandoverCapability']
                                        AND (
                                            EXISTS { MATCH (capm)-[:HAS_PROPERTY*0..4]->(p) WHERE 'Property' IN labels(p) AND p.idShort='transferRole' AND p.value IS NOT NULL }
                                            OR EXISTS { MATCH (capm)-[:HAS_PROPERTY*0..4]->(n) WHERE n.transferRole IS NOT NULL }
                                        )
                                    RETURN 1
                                }
                        )
                        AND (
                                // target has Store/Retrieve handover capability with transferRole/topologyType/dockRole
                                EXISTS {
                                    MATCH (to)-[:PROVIDES_CAPABILITY]->(capt:Capability)
                                    WHERE capt.idShort IN ['DockStoreHandoverCapability','DockRetrieveHandoverCapability']
                                        AND (
                                            EXISTS { MATCH (capt)-[:HAS_PROPERTY*0..4]->(p) WHERE 'Property' IN labels(p) AND p.idShort='transferRole' AND p.value IS NOT NULL }
                                            OR EXISTS { MATCH (capt)-[:HAS_PROPERTY*0..4]->(n) WHERE n.transferRole IS NOT NULL }
                                        )
                                    RETURN 1
                                }
                        )
                WITH from,to,mid LIMIT $midLimit
                MATCH p1=shortestPath((from)-[:ASSET_DISTANCE*..$maxSegHop]-(mid))
                MATCH p2=shortestPath((mid)-[:ASSET_DISTANCE*..$maxSegHop]-(to))
                WITH from,to,mid,p1,p2,
                                 (reduce(d=0,r IN relationships(p1) | d + r.distance) + reduce(d=0,r IN relationships(p2) | d + r.distance)) AS totalDistance,
                                 (
                                         [i IN range(0, size(nodes(p1)) - 2) | {from: nodes(p1)[i].shell_id, to: nodes(p1)[i+1].shell_id, distance: relationships(p1)[i].distance}]
                                         +
                                         [i IN range(0, size(nodes(p2)) - 2) | {from: nodes(p2)[i].shell_id, to: nodes(p2)[i+1].shell_id, distance: relationships(p2)[i].distance}]
                                 ) AS edges
                RETURN edges, totalDistance
                ORDER BY totalDistance ASC
                LIMIT 1
                ";

        // Neo4j does not accept parameters inside the variable-length pattern (e.g. '..$maxSegHop'),
        // so inject the segment hop limit as a literal into the Cypher string.
        var maxSegHop = 8;
        var viaCypherLiteral = viaCypher.Replace("$maxSegHop", maxSegHop.ToString());
        Console.WriteLine($"[DEBUG] Neo4jTransportGraphQuery: running viaCypher from={fromModuleId} to={toModuleId}");
        var viaResult = await session.RunAsync(viaCypherLiteral, new { from = fromModuleId, to = toModuleId, midLimit = 50 });
        var record = await viaResult.SingleOrDefaultAsync();
        Console.WriteLine($"[DEBUG] Neo4jTransportGraphQuery: viaCypher record_found={(record != null)}");

        // If no via-path exists that includes a Transport-capable module, try a fallback: compute
        // the simple shortest path between from and to over ASSET_DISTANCE edges regardless of capabilities.
        if (record == null)
        {
            Console.WriteLine($"[DEBUG] Neo4jTransportGraphQuery: no via-path found, trying fallback shortestPath");
            var fallbackCypher = @"
                MATCH (from),(to)
                WHERE from.shell_id = $from AND to.shell_id = $to
                MATCH p = shortestPath((from)-[:ASSET_DISTANCE*..$maxHop]-(to))
                RETURN [i IN range(0, size(nodes(p)) - 2) | {from: nodes(p)[i].shell_id, to: nodes(p)[i+1].shell_id, distance: relationships(p)[i].distance}] AS edges,
                       reduce(d=0, r IN relationships(p) | d + r.distance) AS totalDistance
                LIMIT 1
            ";

            var fallbackLiteral = fallbackCypher.Replace("$maxHop", maxSegHop.ToString());
            var fallbackResult = await session.RunAsync(fallbackLiteral, new { from = fromModuleId, to = toModuleId });
            record = await fallbackResult.SingleOrDefaultAsync();
            Console.WriteLine($"[DEBUG] Neo4jTransportGraphQuery: fallback record_found={(record != null)}");
            if (record == null) return Array.Empty<TransportEdge>();
        }

        var edgesValue = record["edges"]; // should be a list
        Console.WriteLine($"[DEBUG] Neo4jTransportGraphQuery: edgesValue is null? {edgesValue == null}");
        if (edgesValue is null) return Array.Empty<TransportEdge>();

        var list = new List<TransportEdge>();
        if (edgesValue is IEnumerable<object> objs)
        {
            foreach (var obj in objs)
            {
                if (obj is IReadOnlyDictionary<string, object> map)
                {
                    var from = map.TryGetValue("from", out var f) ? f?.ToString() ?? string.Empty : string.Empty;
                    var to = map.TryGetValue("to", out var t) ? t?.ToString() ?? string.Empty : string.Empty;
                    double distance = 0;
                    if (map.TryGetValue("distance", out var d) && d != null)
                    {
                        if (d is long l) distance = l;
                        else if (d is double db) distance = db;
                        else if (!double.TryParse(d.ToString(), out distance)) distance = 0;
                    }

                    list.Add(new TransportEdge(from, to, distance));
                }
            }
        }

        Console.WriteLine($"[DEBUG] Neo4jTransportGraphQuery: returning {list.Count} edges");
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsDriver)
        {
            await _driver.CloseAsync();
            _driver.Dispose();
        }
    }
}
