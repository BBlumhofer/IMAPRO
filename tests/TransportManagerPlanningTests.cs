using System;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.Messages;
using AasSharpClient.Models.ProcessChain;
using BaSyx.Models.AdminShell;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using MAS_BT.Nodes.Planning;
using MAS_BT.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace MAS_BT.Tests;

public class TransportManagerPlanningTests
{
    private readonly ITestOutputHelper _output;

    public TransportManagerPlanningTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CapabilityCallForProposalMessage_BuildsMessageWithCapabilityAndRequirement()
    {
        var msg = new CapabilityCallForProposalMessage(
            senderId: "TransportManager",
            senderRole: "TransportManager",
            receiverId: "P300",
            receiverRole: "ModuleHolon",
            conversationId: "conv-123",
            subtype: I40MessageTypeSubtypes.ManufacturingSequence,
            capability: "DockRetrieveHandoverCapability",
            requirementId: "LEG_01",
            productId: "Product-42");

        var i40 = msg.ToI40Message();

        _output.WriteLine("I40 type={0} conv={1}", i40.Frame?.Type, i40.Frame?.ConversationId);
        foreach (var el in i40.InteractionElements)
        {
            _output.WriteLine("Element idShort={0} type={1}", el.IdShort, el.GetType().Name);
        }

        Assert.NotNull(i40.Frame);
        Assert.Equal($"{I40MessageTypes.CALL_FOR_PROPOSAL}/{I40MessageTypeSubtypes.ManufacturingSequence}", i40.Frame!.Type);
        Assert.Equal("conv-123", i40.Frame.ConversationId);

        var capability = i40.InteractionElements.OfType<Property>().FirstOrDefault(p => p.IdShort == "Capability");
        var requirement = i40.InteractionElements.OfType<Property>().FirstOrDefault(p => p.IdShort == "RequirementId");
        var product = i40.InteractionElements.OfType<Property>().FirstOrDefault(p => p.IdShort == "ProductId");

        Assert.Equal("DockRetrieveHandoverCapability", capability?.Value?.Value?.ToString());
        Assert.Equal("LEG_01", requirement?.Value?.Value?.ToString());
        Assert.Equal("Product-42", product?.Value?.Value?.ToString());
    }

    [Fact]
    public async Task CollectTransportResponses_StoresCapabilitySequenceFromResponse()
    {
        InMemoryTransport.ResetAll();
        var transport = new InMemoryTransport();
        var client = new MessagingClient(transport, "tm/tests");
        await client.ConnectAsync();

        var ctx = new BTContext(NullLogger<BTContext>.Instance);
        ctx.Set("MessagingClient", client);
        ctx.Set("config.Namespace", "test");

        var node = new CollectTransportResponsesNode
        {
            Context = ctx,
            TimeoutSeconds = 2
        };
        node.SetLogger(NullLogger<CollectTransportResponsesNode>.Instance);

        var sequence = new CapabilitySequence();
        sequence.AddCapability(BuildOffer("LEG_01", "P300", cost: 5));
        sequence.AddCapability(BuildOffer("LEG_02", "P400", cost: 8));

        _output.WriteLine("Sequence items: {0}", string.Join(", ", sequence.Capabilities.Select(c => c.InstanceIdentifier.GetText())));

        var response = new TransportRequestMessage();
        response.CapabilitiesSequence.InsertRangeAfter(sequence.Capabilities);

        var responseMsg = new TransportPlanResponseMessage(
            senderId: "TransportManager",
            senderRole: "TransportManager",
            receiverId: "P101",
            receiverRole: "PlanningHolon",
            conversationId: "conv-seq",
            response: response);

        var executeTask = Task.Run(() => node.Execute());
        await Task.Delay(50);
        var responseI40 = responseMsg.ToI40Message("TransportManager", "TransportManager", "P101", "PlanningHolon", "conv-seq");
        _output.WriteLine("Publishing response type={0} conv={1}", responseI40.Frame?.Type, responseI40.Frame?.ConversationId);
        await client.PublishAsync(responseI40,
            "/test/TransportPlan/Response");

        var status = await executeTask;
        Assert.Equal(NodeStatus.Success, status);

        var stored = ctx.Get<CapabilitySequence>("Planning.TransportCapabilitiesSequence");
        Assert.NotNull(stored);
        Assert.Equal(2, stored!.Count);
        Assert.Equal("LEG_01", stored.Capabilities[0].InstanceIdentifier.GetText());
        Assert.Equal("LEG_02", stored.Capabilities[1].InstanceIdentifier.GetText());
    }

    [Fact]
    public async Task CollectTransportResponses_ExtractsOfferedCapabilitiesFromGenericCollections()
    {
        InMemoryTransport.ResetAll();
        var transport = new InMemoryTransport();
        var client = new MessagingClient(transport, "tm/tests");
        await client.ConnectAsync();

        var ctx = new BTContext(NullLogger<BTContext>.Instance);
        ctx.Set("MessagingClient", client);
        ctx.Set("config.Namespace", "test");

        var node = new CollectTransportResponsesNode
        {
            Context = ctx,
            TimeoutSeconds = 2
        };
        node.SetLogger(NullLogger<CollectTransportResponsesNode>.Instance);

        var offerCollection = new SubmodelElementCollection("OfferedCapability");
        offerCollection.Add(new ReferenceElement(OfferedCapability.OfferedCapabilityReferenceIdShort));
        offerCollection.Add(new Property<string>(OfferedCapability.InstanceIdentifierIdShort)
        {
            Value = new PropertyValue<string>("LEG_X")
        });
        offerCollection.Add(new Property<string>(OfferedCapability.StationIdShort)
        {
            Value = new PropertyValue<string>("P401")
        });
        offerCollection.Add(new Property<double>(OfferedCapability.CostIdShort)
        {
            Value = new PropertyValue<double>(12.5)
        });

        var i40 = new I40MessageBuilder()
            .From("TransportManager", "TransportManager")
            .To("P101", "PlanningHolon")
            .WithType(I40MessageTypes.CONSENT, I40MessageTypeSubtypes.TransportRequest)
            .WithConversationId("conv-offer")
            .AddElement(offerCollection)
            .Build();

        _output.WriteLine("Publishing generic offer collection conv={0}", i40.Frame?.ConversationId);
        var executeTask = Task.Run(() => node.Execute());
        await Task.Delay(50);
        await client.PublishAsync(i40, "/test/TransportPlan/Response");

        var status = await executeTask;
        Assert.Equal(NodeStatus.Success, status);

        var offers = ctx.Get<System.Collections.Generic.List<OfferedCapability>>("Planning.TransportOffers");
        Assert.NotNull(offers);
        Assert.Single(offers!);
        Assert.Equal("LEG_X", offers![0].InstanceIdentifier.GetText());
        Assert.Equal("P401", offers[0].Station.GetText());
        Assert.Equal(12.5, offers[0].Cost.GetDoubleValue());
    }

    [Fact]
    public void TransportPlanResponseMessage_BuildsConsentResponseWithSequence()
    {
        var sequence = new CapabilitySequence();
        sequence.AddCapability(BuildOffer("LEG_01", "P300", cost: 5));

        var response = new TransportRequestMessage();
        response.CapabilitiesSequence.InsertRangeAfter(sequence.Capabilities);

        var msg = new TransportPlanResponseMessage(
            senderId: "TransportManager",
            senderRole: "TransportManager",
            receiverId: "P101",
            receiverRole: "PlanningHolon",
            conversationId: "conv-resp",
            response: response);

        var i40 = msg.ToI40Message("TransportManager", "TransportManager", "P101", "PlanningHolon", "conv-resp");
        _output.WriteLine("Response type={0} conv={1}", i40.Frame?.Type, i40.Frame?.ConversationId);
        Assert.NotNull(i40.Frame);
        Assert.Equal($"{I40MessageTypes.CONSENT}/{I40MessageTypeSubtypes.TransportRequest}", i40.Frame!.Type);

        var transportRequest = i40.InteractionElements.OfType<SubmodelElementCollection>()
            .FirstOrDefault(e => string.Equals(e.IdShort, "TransportRequest", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(transportRequest);

        var sequenceList = transportRequest!.Values?.OfType<SubmodelElementList>()
            .FirstOrDefault(l => string.Equals(l.IdShort, TransportRequestMessage.CapabilitiesSequenceIdShort, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sequenceList);
        Assert.Single(sequenceList!);
    }

    private static OfferedCapability BuildOffer(string id, string station, double cost)
    {
        var offer = new OfferedCapability(string.Empty);
        offer.InstanceIdentifier.SetText(id);
        offer.Station.SetText(station);
        offer.SetCost(cost);
        offer.SetSequencePlacement(id);
        return offer;
    }
}
