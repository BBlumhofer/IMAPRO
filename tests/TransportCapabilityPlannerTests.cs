using System;
using System.Collections.Generic;
using System.Linq;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.Messages;
using MAS_BT.Services.Transport;
using Xunit;
using Xunit.Abstractions;

namespace MAS_BT.Tests;

public class TransportCapabilityPlannerTests
{
    private const string LoadCarriers = "[\"WST_A\"]";
    private const string DockTopologyList = "[\"Dock\"]";

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
            DockRetrieveSpec("P100", "acceptor"),
            RetrieveSpec("P100"),
            DockingSpec("P100", "Host"),

            DockStoreSpec("P200", "initiator"),
            StoreSpec("P200"),
            DockRetrieveSpec("P200", "initiator"),
            RetrieveSpec("P200"),
            DockingSpec("P200", "Host"),
            TransportSpec("P200", "initiator", "25"),

            DockStoreSpec("P101", "acceptor"),
            StoreSpec("P101"),
            DockingSpec("P101", "Host")
        });

        var planner = new TransportCapabilityPlanner(catalog);
        var path = new[] { "P100", "P200", "P101" };
        var request = CreateRequest("P100", "P101", amount: 5);

        Log("Module path: {0}", string.Join(" -> ", path));

        var sequence = planner.BuildSequence(path, request);

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
            DockRetrieveSpec("P100", "initiator"),
            RetrieveSpec("P100"),
            DockingSpec("P100", "Host"),

            DockStoreSpec("P201", "acceptor"),
            StoreSpec("P201"),
            DockRetrieveSpec("P201", "acceptor"),
            RetrieveSpec("P201"),
            DockingSpec("P201", "Host"),
            TransportSpec("P201", "acceptor"),

            DockStoreSpec("P101", "initiator"),
            StoreSpec("P101"),
            DockingSpec("P101", "Host")
        });

        var planner = new TransportCapabilityPlanner(catalog);
        var path = new[] { "P100", "P201", "P101" };
        var request = CreateRequest("P100", "P101");

        Log("Module path: {0}", string.Join(" -> ", path));

        var sequence = planner.BuildSequence(path, request);

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
            DockRetrieveSpec("P100", "acceptor"),
            RetrieveSpec("P100"),
            DockingSpec("P100", "Host"),

            // Missing DockRetrieve on P200 to trigger the failure
            DockStoreSpec("P200", "initiator"),
            StoreSpec("P200"),
            DockingSpec("P200", "Host"),
            TransportSpec("P200", "initiator"),

            DockStoreSpec("P101", "acceptor"),
            StoreSpec("P101"),
            DockingSpec("P101", "Host")
        });

        var planner = new TransportCapabilityPlanner(catalog);
        var request = CreateRequest("P100", "P101");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            planner.BuildSequence(new[] { "P100", "P200", "P101" }, request));

        Log("Expected failure: {0}", ex.Message);
        Assert.Contains("Missing capability 'DockRetrieveHandoverCapability'", ex.Message);
    }

    [Fact]
    public void BuildSequence_ThrowsWhenInitiatorMissingPartnerConstraint()
    {
        var catalog = new TransportCapabilityCatalog(new[]
        {
            DockRetrieveSpec("P100", "acceptor"),
            RetrieveSpec("P100"),
            DockingSpec("P100", "Host"),

            Spec(
                "P200",
                "DockStoreHandoverCapability",
                DockHandoverProps("initiator")), // missing PartnerHasDockReceiveHandover constraint
            StoreSpec("P200"),
            DockRetrieveSpec("P200", "initiator"),
            RetrieveSpec("P200"),
            DockingSpec("P200", "Host"),

            DockStoreSpec("P101", "acceptor"),
            StoreSpec("P101"),
            DockingSpec("P101", "Host")
        });

        var planner = new TransportCapabilityPlanner(catalog);
        var request = CreateRequest("P100", "P101");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            planner.BuildSequence(new[] { "P100", "P200", "P101" }, request));

        Log("Expected failure: {0}", ex.Message);
        Assert.Contains("PartnerHasDockReceiveHandover", ex.Message);
    }

    private static TransportCapabilitySpec DockRetrieveSpec(string moduleId, string transferRole)
    {
        var constraint = string.Equals(transferRole, "initiator", StringComparison.OrdinalIgnoreCase)
            ? "PartnerHasDockReceiveHandover"
            : "PartnerHasDockSendHandover";

        return Spec(moduleId, "DockRetrieveHandoverCapability", DockHandoverProps(transferRole), constraint);
    }

    private static TransportCapabilitySpec DockStoreSpec(string moduleId, string transferRole)
    {
        var constraint = string.Equals(transferRole, "initiator", StringComparison.OrdinalIgnoreCase)
            ? "PartnerHasDockReceiveHandover"
            : "PartnerHasDockSendHandover";

        return Spec(moduleId, "DockStoreHandoverCapability", DockHandoverProps(transferRole), constraint);
    }

    private static TransportCapabilitySpec RetrieveSpec(string moduleId)
    {
        return Spec(moduleId, "Retrieve", Props(
            ("direction", "Outbound"),
            ("allowedTopologyTypes", DockTopologyList),
            ("requiredPartnerRole", "[\"acceptor\"]"),
            ("loadCarrierTypes", LoadCarriers)));
    }

    private static TransportCapabilitySpec StoreSpec(string moduleId)
    {
        return Spec(moduleId, "Store", Props(
            ("direction", "Inbound"),
            ("allowedTopologyTypes", DockTopologyList),
            ("requiredPartnerRole", "[\"initiator\"]"),
            ("loadCarrierTypes", LoadCarriers)));
    }

    private static TransportCapabilitySpec DockingSpec(string moduleId, string dockRole, string capacity = "1")
    {
        return Spec(moduleId, "Docking", Props(
            ("dockRole", dockRole),
            ("capacity", capacity),
            ("loadCarrierTypes", LoadCarriers)));
    }

    private static TransportCapabilitySpec TransportSpec(string moduleId, string transferRole, string maxPayload = "10")
    {
        return Spec(moduleId, "Transport", Props(
            ("transferRole", transferRole),
            ("allowedTopologyTypes", DockTopologyList),
            ("loadCarrierTypes", LoadCarriers),
            ("maxPayloadKg", maxPayload)));
    }

    private static IReadOnlyDictionary<string, string?> DockHandoverProps(string transferRole)
    {
        return Props(
            ("topologyType", "Dock"),
            ("transferRole", transferRole),
            ("requiredPartnerRole", PartnerRoleValue(transferRole)),
            ("loadCarrierTypes", LoadCarriers));
    }

    private static string PartnerRoleValue(string transferRole)
    {
        return string.Equals(transferRole, "initiator", StringComparison.OrdinalIgnoreCase)
            ? "[\"acceptor\"]"
            : "[\"initiator\"]";
    }

    private static TransportRequestMessage CreateRequest(string start, string goal, int amount = 1)
    {
        var request = new TransportRequestMessage();
        request.TransportStartStation.SetText(start);
        request.TransportGoalStation.SetText(goal);
        request.SetAmount(amount);
        request.SetIdentifierType(TransportRequestMessage.IdentifierTypeEnum.ProductId);
        request.IdentifierValue.SetText("TEST-PRODUCT");
        return request;
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
            constraints ?? Array.Empty<string>(),
            null);
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
