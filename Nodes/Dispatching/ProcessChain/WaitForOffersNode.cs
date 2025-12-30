using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models.ProcessChain;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models;
using BaSyx.Models.AdminShell;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class WaitForOffersNode : BTNode
{
    public WaitForOffersNode() : base("WaitForOffers") { }

    private readonly ConcurrentQueue<I40Message> _incoming = new();
    private readonly HashSet<string> _respondedModules = new(StringComparer.OrdinalIgnoreCase);
    private bool _subscribed = false;
    private DateTime _startTime;
    private int _timeoutSeconds = 10;

    public override Task<NodeStatus> Execute()
    {
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        if (ctx == null)
        {
            Logger.LogError("WaitForOffers: negotiation context missing");
            return Task.FromResult(NodeStatus.Failure);
        }

        var current = Context.Get<CapabilityRequirement>("CurrentRequirement");
        if (current == null)
        {
            Logger.LogError("WaitForOffers: CurrentRequirement missing");
            return Task.FromResult(NodeStatus.Failure);
        }

        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null)
        {
            Logger.LogError("WaitForOffers: MessagingClient missing");
            return Task.FromResult(NodeStatus.Failure);
        }

        // Initialize subscription on first tick
        if (!_subscribed)
        {
            _timeoutSeconds = Context.Get<int>("config.DispatchingAgent.OfferCollectionTimeoutSeconds");
            if (_timeoutSeconds <= 0) _timeoutSeconds = 10;
            _startTime = DateTime.UtcNow;
            _respondedModules.Clear();

            // Drain any messages already in the inbox matching the conversation
            try
            {
                var existing = client.DequeueMatchingAll((msg, topic) =>
                {
                    var conv = msg.Frame?.ConversationId;
                    if (string.IsNullOrWhiteSpace(conv) || !string.Equals(conv, Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation")?.ConversationId, StringComparison.OrdinalIgnoreCase))
                        return false;
                    var t = msg.Frame?.Type ?? string.Empty;
                    var baseType = t.Split('/')[0].ToLowerInvariant();
                    return baseType.StartsWith("propos") || baseType.StartsWith("refus");
                });

                foreach (var (msg, _) in existing)
                {
                    _incoming.Enqueue(msg);
                }
                if (existing.Count > 0) Logger.LogInformation("WaitForOffers: drained {Count} pre-existing messages for conv", existing.Count);
            }
            catch { }

            // Register conversation callback and subscribe to response topic
            var convId = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation")?.ConversationId;
            client.OnConversation(convId, msg =>
            {
                try
                {
                    var t = msg.Frame?.Type ?? string.Empty;
                    var baseType = t.Split('/')[0].ToLowerInvariant();

                    if (baseType.StartsWith("propos") || baseType.StartsWith("refus"))
                    {
                        _incoming.Enqueue(msg);
                    }
                    else
                    {
                        Logger.LogDebug("WaitForOffers: ignored non-proposal message on conv {Conv} type={Type}", convId, t);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "WaitForOffers: OnConversation callback error");
                }
            });
            var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
            var responseTopic = $"/{ns}/{Context.AgentId}/OfferedCapability/Response";
            try
            {
                client.SubscribeAsync(responseTopic).Wait();
                Logger.LogInformation("WaitForOffers: subscribed to {Topic}", responseTopic);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "WaitForOffers: subscribe to {Topic} failed", responseTopic);
            }

            _subscribed = true;
        }

        // If we already have at least one offer, proceed
        if (current.CapabilityOffers.Count > 0 || current.OfferedCapabilitySequences.Count > 0)
        {
            return Task.FromResult(NodeStatus.Success);
        }

        // Check timeout
        // Process queued incoming messages
        while (_incoming.TryDequeue(out var msg))
        {
            try
            {
                var msgType = msg.Frame?.Type ?? string.Empty;
                var sender = msg.Frame?.Sender?.Identification?.Id ?? string.Empty;
                var baseType = msgType.Split('/')[0];
                var low = baseType.ToLowerInvariant();

                if (low.StartsWith("refus"))
                {
                    if (_respondedModules.Add(sender)) Logger.LogInformation("WaitForOffers: refusal from {Sender} (Req={Req})", sender, current.RequirementId);
                    continue;
                }

                if (!low.StartsWith("propos"))
                {
                    Logger.LogDebug("WaitForOffers: skipping non-proposal message type={Type}", msgType);
                    continue;
                }

                // Simple parse: try to extract an instance identifier / offer id
                string? instId = null;
                try
                {
                    if (msg.InteractionElements != null)
                    {
                        foreach (var el in msg.InteractionElements)
                        {
                            if (el is IProperty p)
                            {
                                if (string.Equals(p.IdShort, "OfferId", StringComparison.OrdinalIgnoreCase) || string.Equals(p.IdShort, "InstanceIdentifier", StringComparison.OrdinalIgnoreCase))
                                {
                                    instId = AasValueUnwrap.UnwrapToString(p.Value) ?? instId;
                                }
                                if (string.Equals(p.IdShort, "Capability", StringComparison.OrdinalIgnoreCase))
                                {
                                    // could check capability match
                                }
                            }
                        }
                    }
                }
                catch { }

                // Create a minimal OfferedCapability and register it
                try
                {
                    var offer = new OfferedCapability("OfferedCapability");
                    offer.InstanceIdentifier.Value = new BaSyx.Models.AdminShell.PropertyValue<string>(instId ?? Guid.NewGuid().ToString());
                    current.AddOffer(offer);
                    _respondedModules.Add(sender);
                    Logger.LogInformation("WaitForOffers: recorded simple offer {OfferId} from {Sender} for Req={Req}", instId ?? "<gen>", sender, current.RequirementId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "WaitForOffers: failed to record offer from {Sender}", sender);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "WaitForOffers: failed to process incoming message");
            }
        }

        // Check timeout based on local start time
        var elapsed = DateTime.UtcNow - _startTime;
        if (elapsed.TotalSeconds >= _timeoutSeconds)
        {
            Logger.LogInformation("WaitForOffers: timeout reached after {Sec}s for requirement {Req}", (int)elapsed.TotalSeconds, current.RequirementId);
            _subscribed = false;
            return Task.FromResult(NodeStatus.Success);
        }

        // Still waiting
        return Task.FromResult(NodeStatus.Running);
    }
}
