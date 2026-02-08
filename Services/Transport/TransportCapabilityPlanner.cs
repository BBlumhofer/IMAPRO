using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using AasSharpClient.Models.Messages;

namespace MAS_BT.Services.Transport;

public sealed record TransportCapabilitySpec(
    string ModuleId,
    string CapabilityIdShort,
    IReadOnlyDictionary<string, string?> Properties,
    IReadOnlyCollection<string> Constraints,
    string? ReferenceJson = null)
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

    public TransportCapabilitySequence BuildSequence(
        IReadOnlyList<string> modulePath,
        TransportRequestMessage request)
    {
        if (modulePath == null || modulePath.Count < 2)
        {
            throw new ArgumentException("Module path requires at least two nodes.", nameof(modulePath));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var requestedPayloadKg = Math.Max(1, request.AmountValue);
        var legs = new List<TransportCapabilityLeg>();

        for (var i = 0; i < modulePath.Count - 1; i++)
        {
            var from = modulePath[i];
            var to = modulePath[i + 1];

            var fromRetrieve = RequireCapability(from, "DockRetrieveHandoverCapability");
            var toStore = RequireCapability(to, "DockStoreHandoverCapability");

            ValidateRetrieveCapability(from);
            ValidateStoreCapability(to);
            ValidateDockingCapability(from);
            ValidateDockingCapability(to);

            var activeHandover = ValidateHandoverLeg(fromRetrieve, toStore);
            legs.Add(new TransportCapabilityLeg(activeHandover.ModuleId, activeHandover.CapabilityIdShort));

            var hasNextEdge = i + 1 < modulePath.Count - 1;
            if (hasNextEdge && _catalog.HasCapability(to, "Transport"))
            {
                var transport = RequireCapability(to, "Transport");
                ValidateTransportCapability(transport, fromRetrieve, toStore, requestedPayloadKg);
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

    private TransportCapabilitySpec ValidateHandoverLeg(
        TransportCapabilitySpec fromRetrieve,
        TransportCapabilitySpec toStore)
    {
        var activeHandover = SelectActiveHandover(fromRetrieve, toStore);
        var passiveHandover = ReferenceEquals(activeHandover, fromRetrieve) ? toStore : fromRetrieve;

        var fromRole = GetRequiredProperty(fromRetrieve, "transferRole");
        var toRole = GetRequiredProperty(toStore, "transferRole");

        EnsurePartnerRoleExpectation(fromRetrieve, toRole);
        EnsurePartnerRoleExpectation(toStore, fromRole);
        EnsureTopologyMatch(fromRetrieve, toStore);

        var fromCarriers = ParseStringList(fromRetrieve.GetProperty("loadCarrierTypes"));
        var toCarriers = ParseStringList(toStore.GetProperty("loadCarrierTypes"));
        EnsureLoadCarrierCompatibility(
            fromCarriers,
            toCarriers,
            $"{fromRetrieve.ModuleId}:{fromRetrieve.CapabilityIdShort}",
            $"{toStore.ModuleId}:{toStore.CapabilityIdShort}");

        if (string.Equals(activeHandover.GetProperty("transferRole"), "initiator", StringComparison.OrdinalIgnoreCase))
        {
            if (!activeHandover.HasConstraint("PartnerHasDockReceiveHandover"))
            {
                throw new InvalidOperationException(
                    $"Capability '{activeHandover.CapabilityIdShort}' on '{activeHandover.ModuleId}' requires PartnerHasDockReceiveHandover.");
            }
        }

        if (string.Equals(passiveHandover.GetProperty("transferRole"), "acceptor", StringComparison.OrdinalIgnoreCase))
        {
            if (!passiveHandover.HasConstraint("PartnerHasDockSendHandover"))
            {
                throw new InvalidOperationException(
                    $"Capability '{passiveHandover.CapabilityIdShort}' on '{passiveHandover.ModuleId}' requires PartnerHasDockSendHandover.");
            }
        }

        return activeHandover;
    }

    private void ValidateRetrieveCapability(string moduleId)
    {
        var retrieve = RequireCapability(moduleId, "Retrieve");
        var direction = GetRequiredProperty(retrieve, "direction");
        if (!string.Equals(direction, "Outbound", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Retrieve capability on '{moduleId}' must have direction 'Outbound'.");
        }

        EnsureAllowedTopology(retrieve, "allowedTopologyTypes", "Dock");
    }

    private void ValidateStoreCapability(string moduleId)
    {
        var store = RequireCapability(moduleId, "Store");
        var direction = GetRequiredProperty(store, "direction");
        if (!string.Equals(direction, "Inbound", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Store capability on '{moduleId}' must have direction 'Inbound'.");
        }

        EnsureAllowedTopology(store, "allowedTopologyTypes", "Dock");
    }

    private void ValidateDockingCapability(string moduleId)
    {
        var docking = RequireCapability(moduleId, "Docking");
        var dockRole = docking.GetProperty("dockRole");
        if (string.Equals(dockRole, "Host", StringComparison.OrdinalIgnoreCase))
        {
            var capacity = ParseNullableDouble(docking.GetProperty("capacity"));
            if (!capacity.HasValue || capacity.Value <= 0)
            {
                throw new InvalidOperationException(
                    $"Docking capability on '{moduleId}' must declare capacity > 0 for dock hosts.");
            }
        }
    }

    private static void EnsureAllowedTopology(
        TransportCapabilitySpec spec,
        string propertyName,
        string requiredTopology)
    {
        var values = ParseStringList(spec.GetProperty(propertyName));
        if (values.Count == 0 || !values.Any(v => string.Equals(v, requiredTopology, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Capability '{spec.CapabilityIdShort}' on '{spec.ModuleId}' must allow topology '{requiredTopology}' via '{propertyName}'.");
        }
    }

    private static void EnsurePartnerRoleExpectation(
        TransportCapabilitySpec capability,
        string partnerTransferRole)
    {
        var requiredRoles = ParseStringList(capability.GetProperty("requiredPartnerRole"));
        if (requiredRoles.Count == 0)
        {
            return;
        }

        if (!ContainsValue(requiredRoles, partnerTransferRole))
        {
            throw new InvalidOperationException(
                $"Capability '{capability.CapabilityIdShort}' on '{capability.ModuleId}' requires partner role(s) [{string.Join(", ", requiredRoles)}] but partner exposes '{partnerTransferRole}'.");
        }
    }

    private static void EnsureTopologyMatch(
        TransportCapabilitySpec fromRetrieve,
        TransportCapabilitySpec toStore)
    {
        var fromTopology = GetRequiredProperty(fromRetrieve, "topologyType");
        var toTopology = GetRequiredProperty(toStore, "topologyType");
        if (!string.Equals(fromTopology, toTopology, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Capabilities '{fromRetrieve.CapabilityIdShort}'/{fromRetrieve.ModuleId} and '{toStore.CapabilityIdShort}'/{toStore.ModuleId} declare mismatching topology types '{fromTopology}' vs '{toTopology}'.");
        }

        if (!string.Equals(fromTopology, "Dock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Topology '{fromTopology}' is not supported for transport handovers between '{fromRetrieve.ModuleId}' and '{toStore.ModuleId}'.");
        }
    }

    private static void EnsureLoadCarrierCompatibility(
        IReadOnlyCollection<string> first,
        IReadOnlyCollection<string> second,
        string firstLabel,
        string secondLabel,
        string? customMessage = null)
    {
        if (first.Count == 0 || second.Count == 0)
        {
            return;
        }

        var intersection = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
        intersection.IntersectWith(second);
        if (intersection.Count == 0)
        {
            throw new InvalidOperationException(
                customMessage ?? $"Capabilities '{firstLabel}' and '{secondLabel}' do not share compatible loadCarrierTypes.");
        }
    }

    private static void ValidateTransportCapability(
        TransportCapabilitySpec transport,
        TransportCapabilitySpec outboundRetrieve,
        TransportCapabilitySpec inboundStore,
        double requestedPayloadKg)
    {
        var transferRole = transport.GetProperty("transferRole");
        if (!string.IsNullOrWhiteSpace(transferRole) &&
            !string.Equals(transferRole, "initiator", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(transferRole, "acceptor", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Transport capability on '{transport.ModuleId}' has unsupported transferRole '{transferRole}'.");
        }

        EnsureAllowedTopology(transport, "allowedTopologyTypes", "Dock");

        var transportCarriers = ParseStringList(transport.GetProperty("loadCarrierTypes"));
        var outboundCarriers = ParseStringList(outboundRetrieve.GetProperty("loadCarrierTypes"));
        var inboundCarriers = ParseStringList(inboundStore.GetProperty("loadCarrierTypes"));

        EnsureLoadCarrierCompatibility(
            transportCarriers,
            outboundCarriers,
            $"{transport.ModuleId}:{transport.CapabilityIdShort}",
            $"{outboundRetrieve.ModuleId}:{outboundRetrieve.CapabilityIdShort}",
            "Transport capability loadCarrierTypes incompatible with outbound module.");

        EnsureLoadCarrierCompatibility(
            transportCarriers,
            inboundCarriers,
            $"{transport.ModuleId}:{transport.CapabilityIdShort}",
            $"{inboundStore.ModuleId}:{inboundStore.CapabilityIdShort}",
            "Transport capability loadCarrierTypes incompatible with inbound module.");

        var payloadLimit = ParseNullableDouble(transport.GetProperty("maxPayloadKg"));
        if (payloadLimit.HasValue && payloadLimit.Value < requestedPayloadKg)
        {
            throw new InvalidOperationException(
                $"Transport capability '{transport.CapabilityIdShort}' on '{transport.ModuleId}' cannot move payload {requestedPayloadKg}kg (max {payloadLimit.Value}kg).");
        }
    }

    private static TransportCapabilitySpec SelectActiveHandover(
        TransportCapabilitySpec fromRetrieve,
        TransportCapabilitySpec toStore)
    {
        var fromRole = GetRequiredProperty(fromRetrieve, "transferRole");
        var toRole = GetRequiredProperty(toStore, "transferRole");

        var fromInitiator = string.Equals(fromRole, "initiator", StringComparison.OrdinalIgnoreCase);
        var toInitiator = string.Equals(toRole, "initiator", StringComparison.OrdinalIgnoreCase);

        if (fromInitiator == toInitiator)
        {
            throw new InvalidOperationException(
                $"Cannot determine active handover between '{fromRetrieve.ModuleId}' and '{toStore.ModuleId}'.");
        }

        return fromInitiator ? fromRetrieve : toStore;
    }

    private static string GetRequiredProperty(TransportCapabilitySpec spec, string propertyName)
    {
        var value = spec.GetProperty(propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Capability '{spec.CapabilityIdShort}' on '{spec.ModuleId}' missing required property '{propertyName}'.");
        }

        return value;
    }

    private static IReadOnlyCollection<string> ParseStringList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        raw = raw.Trim();
        if (raw.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(raw);
                if (parsed != null)
                {
                    return parsed
                        .Select(NormalizeToken)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();
                }
            }
            catch
            {
                raw = raw.Trim('[', ']');
            }
        }

        return raw
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    private static string NormalizeToken(string? token)
    {
        return token?
            .Trim()
            .Trim('\"')
            .Trim('[')
            .Trim(']')
            ?? string.Empty;
    }

    private static bool ContainsValue(IReadOnlyCollection<string> values, string candidate)
    {
        return values.Any(v => string.Equals(v, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static double? ParseNullableDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(raw, out parsed))
        {
            return parsed;
        }

        return null;
    }
}
