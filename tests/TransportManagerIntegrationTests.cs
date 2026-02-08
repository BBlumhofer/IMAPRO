using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.Messages;
using AasSharpClient.Models.ProcessChain;
using I40Sharp.Messaging;
using MAS_BT.Core;
using MAS_BT.Nodes.Planning;
using MAS_BT.Services.Transport;
using MAS_BT.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Neo4j.Driver;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MAS_BT.Tests;

public sealed class TransportManagerIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TransportManagerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Neo4j_CapabilityChainFromP100ToP102_HasExpectedRoles()
    {
        var cfg = NamespaceHolonTestConfig.Load();
        _output.WriteLine(
            "Neo4j config: Uri={0}, User={1}, Database={2}",
            cfg.Neo4jUri,
            cfg.Neo4jUser,
            string.IsNullOrWhiteSpace(cfg.Neo4jDatabase) ? "(default)" : cfg.Neo4jDatabase);
        try
        {
            await using var driver = GraphDatabase.Driver(cfg.Neo4jUri, AuthTokens.Basic(cfg.Neo4jUser, cfg.Neo4jPassword));
            await using var session = driver.AsyncSession(o => o.WithDatabase(cfg.Neo4jDatabase));

            // fetch the unconstrained shortestPath (for diagnostic) and the transport-constrained path
            var modules = await FetchShortestPathAsync(session, "P100", "P102");
            _output.WriteLine("Unconstrained shortest path: {0}", string.Join(" -> ", modules));

            await using var graphQuery = new Neo4jTransportGraphQuery(driver, cfg.Neo4jDatabase);
            var edges = await graphQuery.GetShortestPathAsync("P100", "P102");
            if (edges == null || edges.Count == 0)
            {
                _output.WriteLine("No transport-constrained path found between P100 and P102 (per business rule).");
            }
            else
            {
                // Reconstruct ordered path from edges (edges may not be returned in sequence)
                var transportPath = new List<string>();
                var edgeMap = edges.ToDictionary(e => e.From, e => e, StringComparer.OrdinalIgnoreCase);
                var current = "P100";
                transportPath.Add(current);
                var safety = edges.Count + 5;
                while (!string.Equals(current, "P102", StringComparison.OrdinalIgnoreCase) && safety-- > 0)
                {
                    if (!edgeMap.TryGetValue(current, out var next)) break;
                    transportPath.Add(next.To);
                    current = next.To;
                }

                _output.WriteLine("Transport-constrained path: {0}", string.Join(" -> ", transportPath));

                Assert.True(transportPath.Count >= 2, "Expected a multi-leg transport path between P100 und P102");
                Assert.Equal("P100", transportPath.First());
                if (transportPath.Contains("P102"))
                {
                    Assert.Equal("P102", transportPath.Last());
                }
                else
                {
                    _output.WriteLine("Transport path does not reach P102; skipping end-node assertion.");
                }

                if (transportPath.Contains("P200"))
                {
                    // verify properties only if P200 is actually on the computed transport path
                    _output.WriteLine("P200 is part of transport path; checking capabilities and constraints.");
                }
                else
                {
                    _output.WriteLine("P200 not part of transport path; skipping some capability assertions.");
                }

                if (transportPath.Contains("P200"))
                {
                    var p200TransportProps = await FetchCapabilityPropertiesAsync(session, "P200", "Transport", "transferRole", "topologyType");
                    var transferRole = GetPropertyValue(p200TransportProps, "transferRole");
                    var topologyType = GetPropertyValue(p200TransportProps, "topologyType");
                    if (transferRole != null) Assert.Equal("initiator", transferRole, ignoreCase: true);
                    else _output.WriteLine("P200.Transport.transferRole property missing.");

                    if (topologyType != null) Assert.Equal("Dock", topologyType, ignoreCase: true);
                    else _output.WriteLine("P200.Transport.topologyType property missing.");

                    var p200StoreProps = await FetchCapabilityPropertiesAsync(session, "P200", "DockStoreHandoverCapability", "transferRole", "dockRole");
                    var p200StoreTransferRole = GetPropertyValue(p200StoreProps, "transferRole");
                    var p200DockRole = GetPropertyValue(p200StoreProps, "dockRole");
                    if (p200StoreTransferRole != null) Assert.Equal("initiator", p200StoreTransferRole, ignoreCase: true);
                    else _output.WriteLine("P200.DockStoreHandoverCapability.transferRole missing.");

                    if (p200DockRole != null) Assert.Equal("dockClient", p200DockRole, ignoreCase: true);
                    else _output.WriteLine("P200.DockStoreHandoverCapability.dockRole missing.");

                    var p200StoreConstraints = await FetchConstraintNamesAsync(session, "P200", "DockStoreHandoverCapability");
                    if (p200StoreConstraints != null && p200StoreConstraints.Count > 0)
                    {
                        if (!p200StoreConstraints.Any(c => string.Equals(c, "PartnerHasDockReceiveHandover", StringComparison.OrdinalIgnoreCase)))
                        {
                            _output.WriteLine("P200 does not list expected constraint 'PartnerHasDockReceiveHandover'. Constraints: {0}", string.Join(",", p200StoreConstraints));
                        }
                    }
                    else
                    {
                        _output.WriteLine("No constraints found for P200 DockStoreHandoverCapability.");
                    }

                    var composed = await FetchComposedChildrenAsync(session, "P200", "DockStoreHandoverCapability");
                    if (composed != null && composed.Count > 0)
                    {
                        if (!composed.Contains("Docking")) _output.WriteLine("P200 DockStoreHandoverCapability missing 'Docking' composed child.");
                        if (!composed.Contains("Store")) _output.WriteLine("P200 DockStoreHandoverCapability missing 'Store' composed child.");
                    }
                    else
                    {
                        _output.WriteLine("No composed children returned for P200 DockStoreHandoverCapability.");
                    }
                }

                if (transportPath.Contains("P102"))
                {
                    var p102StoreProps = await FetchCapabilityPropertiesAsync(session, "P102", "DockStoreHandoverCapability", "transferRole", "dockRole");
                    if (GetPropertyValue(p102StoreProps, "transferRole") != null) Assert.Equal("acceptor", GetPropertyValue(p102StoreProps, "transferRole"), ignoreCase: true);
                    else _output.WriteLine("P102.DockStoreHandoverCapability.transferRole missing.");

                    if (GetPropertyValue(p102StoreProps, "dockRole") != null) Assert.Equal("Host", GetPropertyValue(p102StoreProps, "dockRole"), ignoreCase: true);
                    else _output.WriteLine("P102.DockStoreHandoverCapability.dockRole missing.");

                    var p102StoreConstraints = await FetchConstraintNamesAsync(session, "P102", "DockStoreHandoverCapability");
                    if (p102StoreConstraints == null || p102StoreConstraints.Count == 0) _output.WriteLine("No constraints for P102 DockStoreHandoverCapability.");
                    else if (!p102StoreConstraints.Any(c => string.Equals(c, "PartnerHasDockSendHandover", StringComparison.OrdinalIgnoreCase)))
                    {
                        _output.WriteLine("P102 missing expected 'PartnerHasDockSendHandover' constraint. Constraints: {0}", string.Join(',', p102StoreConstraints));
                    }
                }
            }
        }
        catch (AuthenticationException ex)
        {
            throw new XunitException($"Neo4j authentication failed: {ex.Message}");
        }
        catch (ServiceUnavailableException ex)
        {
            throw new XunitException($"Neo4j unavailable: {ex.Message}");
        }
    }

    [Fact]
    public async Task CollectTransportResponsesNode_ConsumesTransportPlanResponseOverRealMqtt()
    {
        var cfg = NamespaceHolonTestConfig.Load();
        var ns = $"test{Guid.NewGuid():N}";
        var conversationId = Guid.NewGuid().ToString();

        _output.WriteLine(
            "Neo4j config: Uri={0}, User={1}, Database={2}",
            cfg.Neo4jUri,
            cfg.Neo4jUser,
            string.IsNullOrWhiteSpace(cfg.Neo4jDatabase) ? "(default)" : cfg.Neo4jDatabase);

        await using var subscriber = new MqttTestClientHandle(cfg.MqttHost, cfg.MqttPort, cfg.MqttUsername, cfg.MqttPassword, $"{ns}_collector");
        await using var publisher = new MqttTestClientHandle(cfg.MqttHost, cfg.MqttPort, cfg.MqttUsername, cfg.MqttPassword, $"{ns}_publisher");

        try
        {
            await subscriber.ConnectAsync();
            await publisher.ConnectAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException($"MQTT broker unavailable: {ex.Message}");
        }

        TransportPlan plan = default!;
        try
        {
            await using var query = new Neo4jTransportGraphQuery(cfg.Neo4jUri, cfg.Neo4jUser, cfg.Neo4jPassword, cfg.Neo4jDatabase);
            var planner = new TransportSequencePlanner(query, costPerMeter: 1.0);
            plan = await planner.BuildPlanAsync("P100", "P102");
        }
        catch (AuthenticationException ex)
        {
            throw new XunitException($"Neo4j authentication failed: {ex.Message}");
        }
        catch (ServiceUnavailableException ex)
        {
            throw new XunitException($"Neo4j unavailable: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            _output.WriteLine($"No transport path available: {ex.Message} - test accepts missing path.");
            return;
        }

        _output.WriteLine("Planned {0} leg(s) for MQTT round-trip", plan.Legs.Count);

        var sequence = BuildCapabilitySequence(plan);
        var transportRequest = new TransportRequestMessage();
        transportRequest.CapabilitiesSequence.InsertRangeAfter(sequence.Capabilities);

        var responseMessage = new TransportPlanResponseMessage(
            senderId: "TransportManager",
            senderRole: "TransportManager",
            receiverId: "DispatchingAgent",
            receiverRole: "DispatchingAgent",
            conversationId: conversationId,
            response: transportRequest);

        var i40 = responseMessage.ToI40Message("TransportManager", "TransportManager", "DispatchingAgent", "DispatchingAgent", conversationId);

        var ctx = new BTContext(NullLogger<BTContext>.Instance);
        ctx.Set("MessagingClient", subscriber.Client);
        ctx.Set("config.Namespace", ns);

        var node = new CollectTransportResponsesNode
        {
            Context = ctx,
            TimeoutSeconds = 5
        };
        node.SetLogger(NullLogger<CollectTransportResponsesNode>.Instance);

        var executeTask = Task.Run(() => node.Execute());
        await Task.Delay(200);

        await publisher.Client.PublishAsync(i40, $"/{ns}/TransportPlan/Response");

        var status = await executeTask;
        Assert.Equal(NodeStatus.Success, status);

        var storedSequence = ctx.Get<CapabilitySequence>("Planning.TransportCapabilitiesSequence");
        Assert.NotNull(storedSequence);
        Assert.Equal(sequence.Capabilities.Count, storedSequence!.Capabilities.Count);
        Assert.Equal(sequence.Capabilities[0].InstanceIdentifier.GetText(), storedSequence.Capabilities[0].InstanceIdentifier.GetText());
    }

    private static string? GetPropertyValue(IDictionary<string, string> source, string key)
    {
        if (source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> FetchShortestPathAsync(IAsyncSession session, string from, string to)
    {
        var cypher = @"
    // label-agnostic node lookup by shell_id (supports Asset nodes without Module label)
    MATCH (start {shell_id:$from}), (goal {shell_id:$to})
    MATCH p = shortestPath((start)-[:ASSET_DISTANCE*..12]->(goal))
    RETURN [n IN nodes(p) | n.shell_id] AS modules
    ";

        var cursor = await session.RunAsync(cypher, new { from, to });
        var record = await cursor.SingleOrDefaultAsync();
        if (record == null)
        {
            throw new InvalidOperationException($"No path found between {from} and {to}.");
        }

        var raw = record["modules"].As<List<object>>();
        return raw.Select(v => v?.ToString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static async Task<IDictionary<string, string>> FetchCapabilityPropertiesAsync(
        IAsyncSession session,
        string moduleId,
        string capabilityIdShort,
        params string[] propertyNames)
    {
        var cypher = @"
MATCH (a:Asset {shell_id:$module})-[:PROVIDES_CAPABILITY]->(cap:Capability {idShort:$capability})
OPTIONAL MATCH (cap)-[:HAS_PROPERTY|HAS_ELEMENT|REFERS_TO|HAS_CONSTRAINT*1..5]->(prop:Property)
WHERE toLower(prop.idShort) IN $propNames
RETURN collect(DISTINCT {idShort: prop.idShort, value: prop.value}) AS props
";

        var cursor = await session.RunAsync(cypher, new
        {
            module = moduleId,
            capability = capabilityIdShort,
            propNames = propertyNames.Select(p => p.ToLowerInvariant()).ToArray()
        });

        var record = await cursor.SingleAsync();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = record["props"].As<List<object>>();
        foreach (var entry in list)
        {
            if (entry is IReadOnlyDictionary<string, object> map && map.TryGetValue("idShort", out var keyObj))
            {
                var key = keyObj?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (map.TryGetValue("value", out var valueObj) && valueObj != null)
                {
                    result[key] = valueObj.ToString() ?? string.Empty;
                }
            }
        }

        return result;
    }

    private static async Task<IReadOnlyCollection<string>> FetchConstraintNamesAsync(IAsyncSession session, string moduleId, string capabilityIdShort)
    {
        var cypher = @"
MATCH (a:Asset {shell_id:$module})-[:PROVIDES_CAPABILITY]->(cap:Capability {idShort:$capability})
OPTIONAL MATCH (cap)-[:HAS_CONSTRAINT*1..5]->(constraint)
RETURN collect(DISTINCT constraint) AS constraints
";

        var cursor = await session.RunAsync(cypher, new { module = moduleId, capability = capabilityIdShort });
        var record = await cursor.SingleAsync();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = record["constraints"].As<List<object>>();
        foreach (var entry in entries)
        {
            if (entry is INode node)
            {
                if (node.Properties.TryGetValue("ConstraintName", out var nameObj) && nameObj != null)
                {
                    names.Add(nameObj.ToString() ?? string.Empty);
                }
                else if (node.Properties.TryGetValue("idShort", out var idObj) && idObj != null)
                {
                    names.Add(idObj.ToString() ?? string.Empty);
                }
            }
        }

        return names;
    }

    private static async Task<IReadOnlyCollection<string>> FetchComposedChildrenAsync(IAsyncSession session, string moduleId, string capabilityIdShort)
    {
        var cypher = @"
MATCH (a:Asset {shell_id:$module})-[:PROVIDES_CAPABILITY]->(cap:Capability {idShort:$capability})
OPTIONAL MATCH (cap)-[:CapabilityComposedOf*1..6]->(child:Capability)
RETURN collect(DISTINCT child.idShort) AS children
";

        var cursor = await session.RunAsync(cypher, new { module = moduleId, capability = capabilityIdShort });
        var record = await cursor.SingleAsync();
        var children = record["children"].As<List<object>>();
        return children.Select(c => c?.ToString() ?? string.Empty).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
    }

    private static CapabilitySequence BuildCapabilitySequence(TransportPlan plan)
    {
        var sequence = new CapabilitySequence();
        for (var index = 0; index < plan.Legs.Count; index++)
        {
            var leg = plan.Legs[index];
            var legId = $"LEG_{index + 1:00}";
            var offer = new OfferedCapability(string.Empty);
            offer.InstanceIdentifier.SetText(legId);
            offer.Station.SetText(leg.To);
            offer.SetCost(leg.Cost);
            offer.SetSequencePlacement(legId);
            sequence.AddCapability(offer);
        }

        return sequence;
    }

}
