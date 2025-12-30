using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models;
using BaSyx.Models.AdminShell;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// Sends a process chain request (ASK) with RequiredCapability and ProductIdentification as interaction elements.
/// </summary>
public class SendProcessChainRequestNode : BTNode
{
    public int TimeoutSeconds { get; set; } = 30;

    public SendProcessChainRequestNode() : base("SendProcessChainRequest")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("SendProcessChainRequest: MessagingClient missing or disconnected");
            return NodeStatus.Failure;
        }

        var capability = Context.Get<CapabilityDescriptionSubmodel>("CapabilityDescriptionSubmodel")
                        ?? Context.Get<CapabilityDescriptionSubmodel>("AAS.Submodel.CapabilityDescription");
        var productId = Context.Get<ProductIdentificationSubmodel>("ProductIdentificationSubmodel")
                        ?? Context.Get<ProductIdentificationSubmodel>("AAS.Submodel.ProductIdentification");

        if (capability == null || productId == null)
        {
            Logger.LogError("SendProcessChainRequest: Missing CapabilityDescription or ProductIdentification submodel");
            return NodeStatus.Failure;
        }

        var ns = Context.Get<string>("config.Namespace") ?? "phuket";
        var topic = $"/{ns}/ManufacturingSequence/Request";

        try
        {
            var interactionElements = new List<SubmodelElement>
            {
                capability.CapabilitySet,
                WrapSubmodel(productId, "ProductIdentification")
            };

            // If an AssetLocation submodel is available on the blackboard, include it in the request
            SubmodelElement? assetElement = null;

            // 1) Prefer strongly-typed AssetLocation stored under ProcessChain.AssetLocation
            try
            {
                assetElement = Context.Get<AasSharpClient.Models.AssetLocation>("ProcessChain.AssetLocation");
            }
            catch { }

            // 2) Legacy keys (read untyped and inspect runtime type)
            if (assetElement == null)
            {
                try
                {
                    object? raw = Context.Get("AssetLocationSubmodel") ?? Context.Get("AAS.Submodel.AssetLocation") ?? Context.Get("AssetLocation");
                    if (raw is AasSharpClient.Models.AssetLocation typed)
                    {
                        assetElement = typed;
                    }
                    else if (raw is SubmodelElementCollection coll)
                    {
                        assetElement = coll;
                    }
                    else if (raw is Submodel sm)
                    {
                        var candidate = sm.SubmodelElements?.Values?.OfType<SubmodelElementCollection>()
                            .FirstOrDefault(c => string.Equals(c.IdShort, "AssetLocation", StringComparison.OrdinalIgnoreCase));
                        if (candidate != null)
                        {
                            assetElement = candidate;
                        }
                    }
                }
                catch { }
            }

            // 3) Negotiation context raw SMC
            if (assetElement == null)
            {
                try
                {
                    var negotiation = Context.Get<MAS_BT.Nodes.Dispatching.ProcessChain.ProcessChainNegotiationContext>("ProcessChain.Negotiation");
                    if (negotiation?.AssetLocation != null)
                    {
                        assetElement = negotiation.AssetLocation;
                    }
                }
                catch { }
            }

            // 4) Extract from loaded AssetLocation Submodel
            if (assetElement == null)
            {
                try
                {
                    var submodel = Context.Get<Submodel>("AssetLocationSubmodel") ?? Context.Get<Submodel>("AAS.Submodel.AssetLocation");
                    if (submodel?.SubmodelElements?.Values != null)
                    {
                        var candidate = submodel.SubmodelElements.Values
                            .OfType<SubmodelElementCollection>()
                            .FirstOrDefault(c => string.Equals(c.IdShort, "AssetLocation", StringComparison.OrdinalIgnoreCase));
                        if (candidate != null)
                        {
                            assetElement = candidate;
                        }
                        else
                        {
                            var wrap = new SubmodelElementCollection("AssetLocation");
                            foreach (var e in submodel.SubmodelElements.Values)
                            {
                                wrap.Add(e);
                            }
                            assetElement = wrap;
                        }
                    }
                }
                catch { }
            }

            if (assetElement != null)
            {
                interactionElements.Add(assetElement);
            }

            var convId = Guid.NewGuid().ToString();
            var builder = new I40MessageBuilder()
                .From(Context.AgentId, Context.AgentRole)
                .To("Broadcast", "System")
                .WithType(I40MessageTypes.CALL_FOR_PROPOSAL, I40MessageTypeSubtypes.ProcessChain)
                .WithConversationId(convId)
                .AddElements(interactionElements);

            var message = builder.Build();

            // Log interaction elements for debugging to ensure AssetLocation presence
            try
            {
                Logger.LogInformation("SendProcessChainRequest: prepared {Count} interaction elements", interactionElements.Count);
                foreach (var el in interactionElements)
                {
                    switch (el)
                    {
                        case SubmodelElementCollection coll:
                            Logger.LogInformation("  - Collection IdShort={IdShort} Values={ValuesCount}", coll.IdShort, coll.Values == null ? 0 : coll.Values.Count());
                            break;
                        case Property prop:
                            Logger.LogInformation("  - Property IdShort={IdShort} Value={Value}", prop.IdShort, prop.Value);
                            break;
                        default:
                            Logger.LogInformation("  - Element Type={Type} IdShort={IdShort}", el.GetType().Name, (el as SubmodelElement)?.IdShort);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "SendProcessChainRequest: failed to log interaction elements");
            }

            await client.PublishAsync(message, topic);
            Context.Set("ConversationId", convId);
            Logger.LogInformation("SendProcessChainRequest: Sent ASK to topic {Topic} with ConversationId {ConversationId}", topic, convId);

            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendProcessChainRequest: Failed to send request");
            return NodeStatus.Failure;
        }
    }

    private static SubmodelElementCollection WrapSubmodel(Submodel submodel, string idShort)
    {
        var collection = new SubmodelElementCollection(idShort);
        if (submodel.SubmodelElements?.Values != null)
        {
            foreach (var element in submodel.SubmodelElements.Values)
            {
                collection.Add(element);
            }
        }

        return collection;
    }
}
