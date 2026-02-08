using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo4j.Driver;

namespace MAS_BT.Services.Transport;

public sealed class Neo4jTransportCapabilityCatalogProvider : ITransportCapabilityCatalogProvider
{
    private static readonly string[] RelevantCapabilities =
    {
        "DockStoreHandoverCapability",
        "DockRetrieveHandoverCapability",
        "Docking",
        "Store",
        "Retrieve",
        "Transport"
    };

    private readonly IDriver _driver;
    private readonly string? _database;

    public Neo4jTransportCapabilityCatalogProvider(IDriver driver, string? database = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _database = database;
    }

    public async Task<TransportCapabilityCatalog> GetCatalogAsync(
        IEnumerable<string> moduleIds,
        CancellationToken cancellationToken = default)
    {
        if (moduleIds == null)
        {
            throw new ArgumentNullException(nameof(moduleIds));
        }

        var moduleList = moduleIds
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (moduleList.Length == 0)
        {
            throw new ArgumentException("No module ids provided.", nameof(moduleIds));
        }

        await using var session = string.IsNullOrWhiteSpace(_database)
            ? _driver.AsyncSession()
            : _driver.AsyncSession(o => o.WithDatabase(_database));

                var query = @"
MATCH (entity)-[:PROVIDES_CAPABILITY]->(cap:Capability)
WHERE entity.shell_id IN $moduleIds AND cap.idShort IN $capabilities
    // label-agnostic: some graphs use :Asset, others use :Module; match only by shell_id
OPTIONAL MATCH (cap)-[:HAS_PROPERTY|HAS_ELEMENT|REFERS_TO|HAS_CONSTRAINT*1..4]->(prop)
WHERE prop.idShort IS NOT NULL
OPTIONAL MATCH (cap)-[:HAS_CONSTRAINT*1..4]->(constraint)
RETURN entity.shell_id AS moduleId,
             cap.idShort AS capabilityIdShort,
             collect(DISTINCT { idShort: prop.idShort, value: coalesce(prop.value, prop.transferRole, prop.topologyType, prop.dockRole, prop.direction) , allProps: properties(prop) }) AS props,
             collect(DISTINCT constraint) AS constraints,
             cap.Reference AS reference
";

        var cursor = await session.RunAsync(query, new
        {
            moduleIds = moduleList,
            capabilities = RelevantCapabilities
        }).ConfigureAwait(false);

        var specs = new List<TransportCapabilitySpec>();
        while (await cursor.FetchAsync().ConfigureAwait(false))
        {
            var record = cursor.Current;
            var moduleId = record["moduleId"].As<string?>();
            var capabilityIdShort = record["capabilityIdShort"].As<string?>();
            if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(capabilityIdShort))
            {
                continue;
            }

            var properties = ExtractProperties(record["props"]);
            var constraints = ExtractConstraints(record["constraints"]);
            var reference = record.Keys.Contains("reference") ? record["reference"].As<string?>() : null;

            specs.Add(new TransportCapabilitySpec(
                moduleId!,
                capabilityIdShort!,
                properties,
                constraints,
                reference));
        }

        // Debug: dump returned capability specs for troubleshooting
        try
        {
            Console.WriteLine($"[DEBUG] Neo4jTransportCapabilityCatalogProvider: returning {specs.Count} specs");
            foreach (var s in specs)
            {
                Console.WriteLine($"[DEBUG] Spec module={s.ModuleId} capability={s.CapabilityIdShort}");
                if (s.Properties != null && s.Properties.Count > 0)
                {
                    foreach (var kv in s.Properties)
                    {
                        Console.WriteLine($"[DEBUG]   prop: {kv.Key} = {kv.Value}");
                    }
                }
                if (s.Constraints != null && s.Constraints.Count > 0)
                {
                    Console.WriteLine($"[DEBUG]   constraints: {string.Join(',', s.Constraints)}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Neo4jTransportCapabilityCatalogProvider: failed to print specs: {ex}");
        }

        return new TransportCapabilityCatalog(specs);
    }

    private static IReadOnlyDictionary<string, string?> ExtractProperties(object value)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (value is not IEnumerable<object> entries)
        {
            return result;
        }

        foreach (var entry in entries)
        {
            if (entry is not IReadOnlyDictionary<string, object> map)
            {
                continue;
            }

            if (!map.TryGetValue("idShort", out var idObj))
            {
                continue;
            }

            var key = idObj?.ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // Try direct value first
            var valueText = map.TryGetValue("value", out var valueObj) && valueObj != null
                ? valueObj?.ToString()
                : null;

            // If no direct value, check allProps map returned from Cypher for nested node properties
            if (string.IsNullOrWhiteSpace(valueText) && map.TryGetValue("allProps", out var allPropsObj) && allPropsObj is IReadOnlyDictionary<string, object> allProps)
            {
                // common candidate keys that may hold the semantic value
                foreach (var candidate in new[] { "transferRole", "topologyType", "dockRole", "direction", "requiredPartnerRole", "allowedTopologyTypes", "value" })
                {
                    if (allProps.TryGetValue(candidate, out var cand) && cand != null)
                    {
                        valueText = cand.ToString();
                        break;
                    }
                }
                // if still empty, try idShort->Property nested structure (e.g., value could be an array)
                if (string.IsNullOrWhiteSpace(valueText))
                {
                    // attempt to stringify entire properties map as JSON-like fallback
                    try
                    {
                        valueText = System.Text.Json.JsonSerializer.Serialize(allProps);
                    }
                    catch
                    {
                        valueText = null;
                    }
                }
            }

            result[key!] = valueText;
        }

        return result;
    }

    private static IReadOnlyCollection<string> ExtractConstraints(object value)
    {
        var constraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value is not IEnumerable<object> entries)
        {
            return constraints;
        }

        foreach (var entry in entries)
        {
            if (entry is not INode node)
            {
                continue;
            }

            if (node.Properties.TryGetValue("ConstraintName", out var constraintName) && constraintName != null)
            {
                var text = constraintName.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    constraints.Add(text!);
                    continue;
                }
            }

            if (node.Properties.TryGetValue("idShort", out var idShort) && idShort != null)
            {
                var text = idShort.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    constraints.Add(text!);
                }
            }
        }

        return constraints;
    }
}
