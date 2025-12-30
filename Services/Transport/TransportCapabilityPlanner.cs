using System;
using System.Collections.Generic;
using System.Linq;

namespace MAS_BT.Services.Transport;

public sealed record TransportCapabilitySpec(
    string ModuleId,
    string CapabilityIdShort,
    IReadOnlyDictionary<string, string?> Properties,
    IReadOnlyCollection<string> Constraints)
{
    public string? GetProperty(string idShort)
    {
        if (Properties == null || string.IsNullOrWhiteSpace(idShort))
        {
            return null;
        }

        return Properties.TryGetValue(idShort, out var value) ? value : null;
    }

    public bool HasConstraint(string constraintName)
    {
        if (Constraints == null || string.IsNullOrWhiteSpace(constraintName))
        {
            return false;
        }

        return Constraints.Contains(constraintName, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record TransportCapabilityLeg(string ModuleId, string CapabilityIdShort);

public sealed record TransportCapabilitySequence(IReadOnlyList<TransportCapabilityLeg> Legs);

public sealed class TransportCapabilityCatalog
{
    private readonly Dictionary<string, Dictionary<string, TransportCapabilitySpec>> _capabilities;

    public TransportCapabilityCatalog(IEnumerable<TransportCapabilitySpec> specs)
    {
        _capabilities = new Dictionary<string, Dictionary<string, TransportCapabilitySpec>>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in specs ?? Array.Empty<TransportCapabilitySpec>())
        {
            if (!_capabilities.TryGetValue(spec.ModuleId, out var byCap))
            {
                byCap = new Dictionary<string, TransportCapabilitySpec>(StringComparer.OrdinalIgnoreCase);
                _capabilities[spec.ModuleId] = byCap;
            }

            byCap[spec.CapabilityIdShort] = spec;
        }
    }

    public bool TryGetCapability(string moduleId, string capabilityIdShort, out TransportCapabilitySpec spec)
    {
        spec = default!;
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(capabilityIdShort))
        {
            return false;
        }

        if (!_capabilities.TryGetValue(moduleId, out var byCap))
        {
            return false;
        }

        return byCap.TryGetValue(capabilityIdShort, out spec);
    }

    public bool HasCapability(string moduleId, string capabilityIdShort)
    {
        return TryGetCapability(moduleId, capabilityIdShort, out _);
    }
}

public sealed class TransportCapabilityPlanner
{
    private readonly TransportCapabilityCatalog _catalog;

    public TransportCapabilityPlanner(TransportCapabilityCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public TransportCapabilitySequence BuildSequence(IReadOnlyList<string> modulePath)
    {
        if (modulePath == null || modulePath.Count < 2)
        {
            throw new ArgumentException("Module path requires at least two nodes.", nameof(modulePath));
        }

        var legs = new List<TransportCapabilityLeg>();
        for (var i = 0; i < modulePath.Count - 1; i++)
        {
            var from = modulePath[i];
            var to = modulePath[i + 1];

            var fromRetrieve = RequireCapability(from, "DockRetrieveHandoverCapability");
            var toStore = RequireCapability(to, "DockStoreHandoverCapability");

            ValidateHandoverCapability(fromRetrieve);
            ValidateHandoverCapability(toStore);

            var activeHandover = SelectActiveHandover(fromRetrieve, toStore);
            legs.Add(new TransportCapabilityLeg(activeHandover.ModuleId, activeHandover.CapabilityIdShort));

            var hasNextEdge = i + 1 < modulePath.Count - 1;
            if (hasNextEdge && _catalog.HasCapability(to, "Transport"))
            {
                var transport = RequireCapability(to, "Transport");
                ValidateTransportCapability(transport);
                var transferRole = transport.GetProperty("transferRole");
                if (string.Equals(transferRole, "initiator", StringComparison.OrdinalIgnoreCase))
                {
                    legs.Add(new TransportCapabilityLeg(to, transport.CapabilityIdShort));
                }
            }
        }

        return new TransportCapabilitySequence(legs);
    }

    private TransportCapabilitySpec RequireCapability(string moduleId, string capabilityIdShort)
    {
        if (!_catalog.TryGetCapability(moduleId, capabilityIdShort, out var spec))
        {
            throw new InvalidOperationException(
                $"Missing capability '{capabilityIdShort}' for module '{moduleId}'.");
        }

        return spec;
    }

    private static void ValidateHandoverCapability(TransportCapabilitySpec spec)
    {
        var topology = spec.GetProperty("topologyType");
        if (!string.IsNullOrWhiteSpace(topology) &&
            !string.Equals(topology, "Dock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Capability '{spec.CapabilityIdShort}' on '{spec.ModuleId}' has unsupported topologyType '{topology}'.");
        }

        var transferRole = spec.GetProperty("transferRole");
        if (string.Equals(transferRole, "initiator", StringComparison.OrdinalIgnoreCase))
        {
            if (!spec.HasConstraint("PartnerHasDockReceiveHandover"))
            {
                throw new InvalidOperationException(
                    $"Capability '{spec.CapabilityIdShort}' on '{spec.ModuleId}' requires PartnerHasDockReceiveHandover.");
            }
        }
        else if (string.Equals(transferRole, "acceptor", StringComparison.OrdinalIgnoreCase))
        {
            if (!spec.HasConstraint("PartnerHasDockSendHandover"))
            {
                throw new InvalidOperationException(
                    $"Capability '{spec.CapabilityIdShort}' on '{spec.ModuleId}' requires PartnerHasDockSendHandover.");
            }
        }
    }

    private static void ValidateTransportCapability(TransportCapabilitySpec spec)
    {
        var transferRole = spec.GetProperty("transferRole");
        if (!string.IsNullOrWhiteSpace(transferRole) &&
            !string.Equals(transferRole, "initiator", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(transferRole, "acceptor", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Transport capability on '{spec.ModuleId}' has unsupported transferRole '{transferRole}'.");
        }
    }

    private static TransportCapabilitySpec SelectActiveHandover(
        TransportCapabilitySpec fromRetrieve,
        TransportCapabilitySpec toStore)
    {
        var fromRole = fromRetrieve.GetProperty("transferRole");
        var toRole = toStore.GetProperty("transferRole");

        var fromInitiator = string.Equals(fromRole, "initiator", StringComparison.OrdinalIgnoreCase);
        var toInitiator = string.Equals(toRole, "initiator", StringComparison.OrdinalIgnoreCase);

        if (fromInitiator == toInitiator)
        {
            throw new InvalidOperationException(
                $"Cannot determine active handover between '{fromRetrieve.ModuleId}' and '{toStore.ModuleId}'.");
        }

        return fromInitiator ? fromRetrieve : toStore;
    }
}
