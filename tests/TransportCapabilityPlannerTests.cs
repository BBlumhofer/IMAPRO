using System;
using System.Collections.Generic;
using System.Linq;
using MAS_BT.Services.Transport;
using Xunit;
using Xunit.Abstractions;

namespace MAS_BT.Tests;

public class TransportCapabilityPlannerTests
{
    private readonly ITestOutputHelper _output;

    public TransportCapabilityPlannerTests(ITestOutputHelper output)
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
    public void BuildSequence_WithTransportModule_AddsTransportLeg()
    {
        var catalog = new TransportCapabilityCatalog(new[]
        {
            Spec("P100", "DockRetrieveHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "acceptor")),
                "PartnerHasDockSendHandover"),
            Spec("P200", "DockStoreHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "initiator")),
                "PartnerHasDockReceiveHandover"),
            Spec("P200", "Transport",
                Props(("transferRole", "initiator"))),
            Spec("P200", "DockRetrieveHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "initiator")),
                "PartnerHasDockReceiveHandover"),
            Spec("P101", "DockStoreHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "acceptor")),
                "PartnerHasDockSendHandover")
        });

        var planner = new TransportCapabilityPlanner(catalog);
        var path = new[] { "P100", "P200", "P101" };

        Log("Module path: {0}", string.Join(" -> ", path));

        var sequence = planner.BuildSequence(path);

        Log("Sequence legs ({0}): {1}",
            sequence.Legs.Count,
            string.Join(", ", sequence.Legs.Select(l => $"{l.ModuleId}:{l.CapabilityIdShort}")));

        Assert.Equal(3, sequence.Legs.Count);
        Assert.Equal(("P200", "DockStoreHandoverCapability"), (sequence.Legs[0].ModuleId, sequence.Legs[0].CapabilityIdShort));
        Assert.Equal(("P200", "Transport"), (sequence.Legs[1].ModuleId, sequence.Legs[1].CapabilityIdShort));
        Assert.Equal(("P200", "DockRetrieveHandoverCapability"), (sequence.Legs[2].ModuleId, sequence.Legs[2].CapabilityIdShort));
    }

    [Fact]
    public void BuildSequence_WithPassiveTransport_SkipsTransportLeg()
    {
        var catalog = new TransportCapabilityCatalog(new[]
        {
            Spec("P100", "DockRetrieveHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "initiator")),
                "PartnerHasDockReceiveHandover"),
            Spec("P201", "DockStoreHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "acceptor")),
                "PartnerHasDockSendHandover"),
            Spec("P201", "Transport",
                Props(("transferRole", "acceptor"))),
            Spec("P201", "DockRetrieveHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "acceptor")),
                "PartnerHasDockSendHandover"),
            Spec("P101", "DockStoreHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "initiator")),
                "PartnerHasDockReceiveHandover")
        });

        var planner = new TransportCapabilityPlanner(catalog);
        var path = new[] { "P100", "P201", "P101" };

        Log("Module path: {0}", string.Join(" -> ", path));

        var sequence = planner.BuildSequence(path);

        Log("Sequence legs ({0}): {1}",
            sequence.Legs.Count,
            string.Join(", ", sequence.Legs.Select(l => $"{l.ModuleId}:{l.CapabilityIdShort}")));

        Assert.Equal(2, sequence.Legs.Count);
        Assert.Equal(("P100", "DockRetrieveHandoverCapability"), (sequence.Legs[0].ModuleId, sequence.Legs[0].CapabilityIdShort));
        Assert.Equal(("P101", "DockStoreHandoverCapability"), (sequence.Legs[1].ModuleId, sequence.Legs[1].CapabilityIdShort));
    }

    [Fact]
    public void BuildSequence_ThrowsWhenCapabilityMissing()
    {
        var catalog = new TransportCapabilityCatalog(new[]
        {
            Spec("P100", "DockRetrieveHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "acceptor")),
                "PartnerHasDockSendHandover"),
            Spec("P200", "DockStoreHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "initiator")),
                "PartnerHasDockReceiveHandover")
        });

        var planner = new TransportCapabilityPlanner(catalog);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            planner.BuildSequence(new[] { "P100", "P200", "P101" }));

        Log("Expected failure: {0}", ex.Message);
        Assert.Contains("Missing capability 'DockRetrieveHandoverCapability' for module 'P200'", ex.Message);
    }

    [Fact]
    public void BuildSequence_ThrowsWhenInitiatorMissingPartnerConstraint()
    {
        var catalog = new TransportCapabilityCatalog(new[]
        {
            Spec("P100", "DockRetrieveHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "acceptor")),
                "PartnerHasDockSendHandover"),
            Spec("P200", "DockStoreHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "initiator"))),
            Spec("P101", "DockStoreHandoverCapability",
                Props(("topologyType", "Dock"), ("transferRole", "acceptor")),
                "PartnerHasDockSendHandover")
        });

        var planner = new TransportCapabilityPlanner(catalog);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            planner.BuildSequence(new[] { "P100", "P200", "P101" }));

        Log("Expected failure: {0}", ex.Message);
        Assert.Contains("PartnerHasDockReceiveHandover", ex.Message);
    }

    private static TransportCapabilitySpec Spec(
        string moduleId,
        string capabilityIdShort,
        IReadOnlyDictionary<string, string?> properties,
        params string[] constraints)
    {
        return new TransportCapabilitySpec(
            moduleId,
            capabilityIdShort,
            properties,
            constraints ?? Array.Empty<string>());
    }

    private static IReadOnlyDictionary<string, string?> Props(params (string key, string? value)[] props)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in props)
        {
            dict[key] = value;
        }

        return dict;
    }
}
