using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using AasSharpClient.Models;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.ProcessChain;
using BaSyx.Models.AdminShell;
using I40Sharp.Messaging.Models;

namespace MAS_BT.Nodes.Planning;

public class CollectTransportResponsesNode : BTNode
{
    public int TimeoutSeconds { get; set; } = 5;

    public CollectTransportResponsesNode() : base("CollectTransportResponses") { }

    public override Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace");
        if (client == null || string.IsNullOrWhiteSpace(ns))
        {
            Logger.LogError("CollectTransportResponses: missing MessagingClient or namespace");
            return Task.FromResult(NodeStatus.Failure);
        }

        var responseTopic = $"/{ns}/TransportPlan/Response";
        try { client.SubscribeAsync(responseTopic).Wait(1000); } catch { }

        var queue = new ConcurrentQueue<I40Message>();
        void onMsg(I40Message? m)
        {
            try
            {
                if (m == null) return;
                queue.Enqueue(m);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "CollectTransportResponses: onMsg handler failed");
            }
        }

        try { client.OnMessage(onMsg); } catch { }

        var deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds <= 0 ? 5 : TimeoutSeconds);
        while (DateTime.UtcNow <= deadline)
        {
            if (queue.TryDequeue(out var msg))
            {
                try
                {
                    // Try to extract OfferedCapability elements from the transport response
                    var offers = ExtractTransportOffers(msg);
                    // Always expose the raw message for downstream evaluation
                    Context.Set("LastReceivedMessage", msg);
                    // Try to parse any TransportRequestMessage elements and expose their CapabilitySequence
                    try
                    {
                        foreach (var element in msg.InteractionElements ?? new System.Collections.Generic.List<ISubmodelElement>())
                        {
                            if (element is SubmodelElementCollection coll)
                            {
                                try
                                {
                                    var tr = new AasSharpClient.Models.Messages.TransportRequestMessage(coll);
                                    if (tr?.CapabilitiesSequence != null && tr.CapabilitiesSequence.Count > 0)
                                    {
                                        Context.Set("Planning.TransportCapabilitiesSequence", tr.CapabilitiesSequence);
                                        Logger.LogDebug("CollectTransportResponses: parsed TransportRequestMessage with {Count} capability-sequence items", tr.CapabilitiesSequence.Count);
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                    if (offers != null && offers.Count > 0)
                    {
                        Context.Set("Planning.TransportOffers", offers);
                        Logger.LogInformation("CollectTransportResponses: extracted {Count} transport offer(s); continuing", offers.Count);
                    }
                    else
                    {
                        Logger.LogInformation("CollectTransportResponses: received transport response without offers; forwarding raw message");
                    }

                    return Task.FromResult(NodeStatus.Success);
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "CollectTransportResponses: failed to set LastReceivedMessage");
                    return Task.FromResult(NodeStatus.Failure);
                }
            }

            Thread.Sleep(100);
        }

        Logger.LogInformation("CollectTransportResponses: timeout waiting for transport response ({Timeout}s)", TimeoutSeconds);
        return Task.FromResult(NodeStatus.Failure);
    }

    private static System.Collections.Generic.List<OfferedCapability> ExtractTransportOffers(I40Message? message)
    {
        var offers = new System.Collections.Generic.List<OfferedCapability>();
        if (message?.InteractionElements == null) return offers;

        foreach (var element in message.InteractionElements)
        {
            CollectOfferedCapabilities(element, offers);
        }

        return offers;
    }

    private static void CollectOfferedCapabilities(ISubmodelElement? element, System.Collections.Generic.IList<OfferedCapability> sink)
    {
        if (element == null) return;

        switch (element)
        {
            case OfferedCapability offer:
                sink.Add(offer);
                break;
            case SubmodelElementCollection collection when LooksLikeOfferedCapability(collection):
                sink.Add(CreateOfferedCapabilityFromCollection(collection));
                break;
            case SubmodelElementCollection collection when collection.Values != null:
                foreach (var child in collection.Values)
                {
                    CollectOfferedCapabilities(child, sink);
                }
                break;
            case SubmodelElementList list:
                foreach (var child in list)
                {
                    CollectOfferedCapabilities(child, sink);
                }
                break;
        }
    }

    private static bool LooksLikeOfferedCapability(SubmodelElementCollection collection)
    {
        if (collection == null) return false;
        var values = collection.Values ?? Array.Empty<ISubmodelElement>();
        var hasInstanceId = values.OfType<Property>()
            .Any(p => string.Equals(p.IdShort, OfferedCapability.InstanceIdentifierIdShort, StringComparison.OrdinalIgnoreCase));
        var hasReference = values.OfType<ReferenceElement>()
            .Any(r => string.Equals(r.IdShort, OfferedCapability.OfferedCapabilityReferenceIdShort, StringComparison.OrdinalIgnoreCase));
        return hasInstanceId || hasReference;
    }

    private static OfferedCapability CreateOfferedCapabilityFromCollection(SubmodelElementCollection source)
    {
        var offer = new OfferedCapability(string.Empty);
        if (source == null) return offer;

        offer.SemanticId = source.SemanticId;
        offer.Description = source.Description;
        offer.Qualifiers = source.Qualifiers;

        var children = source.Values ?? Array.Empty<ISubmodelElement>();
        foreach (var child in children)
        {
            switch (child)
            {
                case ReferenceElement reference when string.Equals(reference.IdShort, OfferedCapability.OfferedCapabilityReferenceIdShort, StringComparison.OrdinalIgnoreCase):
                    offer.OfferedCapabilityReference.Value = reference.Value;
                    break;
                case Property prop when string.Equals(prop.IdShort, OfferedCapability.InstanceIdentifierIdShort, StringComparison.OrdinalIgnoreCase):
                    offer.InstanceIdentifier.SetText(AasValueUnwrap.UnwrapToString(prop.Value) ?? string.Empty);
                    break;
                case Property prop when string.Equals(prop.IdShort, OfferedCapability.StationIdShort, StringComparison.OrdinalIgnoreCase):
                    offer.Station.SetText(AasValueUnwrap.UnwrapToString(prop.Value) ?? string.Empty);
                    break;
                case Property prop when string.Equals(prop.IdShort, OfferedCapability.MatchingScoreIdShort, StringComparison.OrdinalIgnoreCase):
                    offer.MatchingScore.Value = new PropertyValue<double>(ParseDouble(AasValueUnwrap.Unwrap(prop.Value)));
                    break;
                case Property prop when string.Equals(prop.IdShort, OfferedCapability.CostIdShort, StringComparison.OrdinalIgnoreCase):
                    offer.SetCost(ParseDouble(AasValueUnwrap.Unwrap(prop.Value)));
                    break;
                case Property prop when string.Equals(prop.IdShort, OfferedCapability.SequencePlacementIdShort, StringComparison.OrdinalIgnoreCase):
                    offer.SequencePlacement.SetText(AasValueUnwrap.UnwrapToString(prop.Value) ?? string.Empty);
                    break;
                case SubmodelElementCollection collection when string.Equals(collection.IdShort, OfferedCapability.EarliestSchedulingInformationIdShort, StringComparison.OrdinalIgnoreCase):
                    CopyScheduling(collection, offer);
                    break;
                case SubmodelElementList list when string.Equals(list.IdShort, OfferedCapability.ActionsIdShort, StringComparison.OrdinalIgnoreCase):
                    for (var ai = 0; ai < list.Count; ai++)
                    {
                        var actionElement = list[ai];
                        if (actionElement is AasSharpClient.Models.Action action)
                        {
                            offer.AddAction(action);
                            continue;
                        }

                        if (actionElement is SubmodelElementCollection actionCollection)
                        {
                            var mat = MaterializeAction(actionCollection);
                            offer.AddAction(mat);
                            continue;
                        }

                        if (actionElement is Property || actionElement is ReferenceElement || actionElement is SubmodelElementList || actionElement is ISubmodelElement)
                        {
                            var synthetic = new SubmodelElementCollection("Action");
                            var j = ai;
                            for (; j < list.Count; j++)
                            {
                                var el = list[j];
                                if (el is SubmodelElementCollection) break;
                                synthetic.Add(el);
                            }

                            ai = j - 1;
                            var mat = MaterializeAction(synthetic);
                            offer.AddAction(mat);
                            continue;
                        }
                    }
                    break;
            }
        }

        return offer;
    }

    private static AasSharpClient.Models.Action MaterializeAction(SubmodelElementCollection source)
    {
        var title = FindPropertyValue(source, "ActionTitle") ?? "Action";
        var machineName = FindPropertyValue(source, "MachineName") ?? string.Empty;

        var statusRaw = FindPropertyValue(source, "Status") ?? string.Empty;
        var status = ActionStatusEnum.PLANNED;
        if (!string.IsNullOrWhiteSpace(statusRaw) && Enum.TryParse<ActionStatusEnum>(statusRaw, ignoreCase: true, out var parsed))
        {
            status = parsed;
        }

        InputParameters? inputParameters = null;
        try
        {
            var ip = source.Values?.OfType<SubmodelElementCollection>()
                .FirstOrDefault(c => string.Equals(c.IdShort, "InputParameters", StringComparison.OrdinalIgnoreCase));
            if (ip != null)
            {
                inputParameters = new InputParameters();
                foreach (var prop in ip.OfType<Property>())
                {
                    var key = prop.IdShort ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    var value = AasValueUnwrap.UnwrapToString(prop.Value) ?? string.Empty;
                    inputParameters.SetParameter(key, value);
                }
            }
        }
        catch { }

        return new AasSharpClient.Models.Action(
            idShort: "",
            actionTitle: title,
            status: status,
            inputParameters: inputParameters,
            finalResultData: null,
            preconditions: null,
            skillReference: null,
            machineName: machineName);
    }

    private static string? FindPropertyValue(SubmodelElementCollection? collection, string idShort)
    {
        if (collection?.Values == null) return null;
        foreach (var element in collection.Values)
        {
            if (element is Property prop && string.Equals(prop.IdShort, idShort, StringComparison.OrdinalIgnoreCase))
            {
                return AasValueUnwrap.UnwrapToString(prop.Value);
            }
        }
        return null;
    }

    private static void CopyScheduling(SubmodelElementCollection scheduling, OfferedCapability offer)
    {
        var properties = scheduling.Values?.OfType<Property>() ?? Array.Empty<Property>();
        DateTime? start = null; DateTime? end = null; TimeSpan? setup = null; TimeSpan? cycle = null;
        foreach (var property in properties)
        {
            var value = AasValueUnwrap.UnwrapToString(property.Value);
            if (string.IsNullOrWhiteSpace(value)) continue;
            switch (property.IdShort)
            {
                case "StartDateTime":
                    if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var ps)) start = ps;
                    break;
                case "EndDateTime":
                    if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var pe)) end = pe;
                    break;
                case "SetupTime": if (TimeSpan.TryParse(value, out var s)) setup = s; break;
                case "CycleTime": if (TimeSpan.TryParse(value, out var c)) cycle = c; break;
            }
        }
        if (start.HasValue && end.HasValue && setup.HasValue && cycle.HasValue) offer.SetEarliestScheduling(start.Value, end.Value, setup.Value, cycle.Value);
    }

    private static double ParseDouble(object? value)
    {
        if (value == null) return 0;
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is decimal m) return (double)m;
        if (value is int i) return i;
        if (value is long l) return l;
        var text = value.ToString();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0;
    }

}
