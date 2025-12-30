using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using AasSharpClient.Models;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.ProcessChain;
using BaSyx.Models.AdminShell;
using I40Sharp.Messaging.Models;
using I40Sharp.Messaging;
using MAS_BT.Nodes.Planning.ProcessChain;

namespace MAS_BT.Nodes.Planning;

/// <summary>
/// EvaluateRequestTransportResponse - stub: marks transport request as accepted or failed based on context flag.
/// </summary>
public class EvaluateRequestTransportResponseNode : BTNode
{
    public string RefusalReason { get; set; } = "transport_not_available";
    public int TimeoutSeconds { get; set; } = 5;

    public EvaluateRequestTransportResponseNode() : base("EvaluateRequestTransportResponse") {}

    public override Task<NodeStatus> Execute()
    {
        // Default optimistic
        var transportOk = Context.Get<bool?>("TransportAccepted") ?? true;

        // Try to extract transport offers from last received message if present
        try
        {
            var last = Context.Get<I40Message>("LastReceivedMessage");
            if (last == null)
            {
                Logger.LogWarning("EvaluateRequestTransportResponse: no transport response available");
                Context.Set("RefusalReason", "transport_no_response");
                Context.Set("TransportAccepted", false);
                return Task.FromResult(NodeStatus.Failure);
            }

            var offers = ExtractTransportOffers(last);
            if (offers == null || offers.Count == 0)
            {
                Logger.LogWarning("EvaluateRequestTransportResponse: transport response malformed or contains no offers");
                Context.Set("RefusalReason", "transport_format_error");
                Context.Set("TransportAccepted", false);
                return Task.FromResult(NodeStatus.Failure);
            }


            transportOk = true;
            Context.Set("TransportAccepted", true);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "EvaluateRequestTransportResponse: failed to extract transport offers from last message");
        }

        // Collection of transport responses is handled by a separate CollectTransportResponses node.
        // Here we only examine `LastReceivedMessage` which should be set by that node.

        if (!transportOk)
        {
            Logger.LogWarning("EvaluateRequestTransportResponse: transport denied");
            Context.Set("RefusalReason", RefusalReason);
            return Task.FromResult(NodeStatus.Failure);
        }

        Logger.LogInformation("EvaluateRequestTransportResponse: transport accepted");
        Context.Set("TransportAccepted", true);
        return Task.FromResult(NodeStatus.Success);
    }

    private List<OfferedCapability> ExtractTransportOffers(I40Message? message)
    {
        var offers = new List<OfferedCapability>();
        if (message?.InteractionElements == null) return offers;

        foreach (var element in message.InteractionElements)
        {
            CollectOfferedCapabilities(element, offers);
        }

        // NOTE: do NOT synthesize missing identifiers or other fields here.
        // If the incoming offers are malformed (missing required fields), the caller
        // should treat the response as invalid and refuse the transport request.

        return offers;
    }

    private void CollectOfferedCapabilities(ISubmodelElement? element, IList<OfferedCapability> sink)
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

    private OfferedCapability CreateOfferedCapabilityFromCollection(SubmodelElementCollection source)
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

    private AasSharpClient.Models.Action MaterializeAction(SubmodelElementCollection source)
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

    private static bool IsTransportResponseFrame(string? frameType)
    {
        if (string.IsNullOrWhiteSpace(frameType))
        {
            return false;
        }

        var slashIndex = frameType.IndexOf('/');
        var primary = slashIndex >= 0 ? frameType[..slashIndex] : frameType;
        var subtypeToken = slashIndex >= 0 ? frameType[(slashIndex + 1)..] : string.Empty;

        if (!string.Equals(primary, I40MessageTypes.CONSENT, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(primary, I40MessageTypes.INFORM_CONFIRM, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(subtypeToken))
        {
            return false;
        }

        return I40MessageTypeSubtypesExtensions.TryParse(subtypeToken, out var parsed)
               && parsed == I40MessageTypeSubtypes.TransportRequest;
    }
}
