using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MAS_BT.Core;
using MAS_BT.Utilities;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
// fingerprinting removed — reverted to simple forwarding logic

namespace MAS_BT.Nodes.ModuleHolon;

/// <summary>
/// Robust forwarding node that listens for role-based broadcast CfPs and forwards them to internal planning agent topics.
/// Uses the new generic topic pattern: /{namespace}/ModuleHolon/broadcast/OfferedCapability/Request
/// </summary>
public class ForwardCapabilityRequestsNode : BTNode
{
    public string TargetTopicTemplate { get; set; } = "/{Namespace}/{ModuleId}/OfferedCapability/Request";
    public string ExpectedSenderRole { get; set; } = "Dispatching";
    public string BroadcastTopicTemplate { get; set; } = "/{Namespace}/ModuleHolon/broadcast/OfferedCapability/Request";
    public string InternalResponseTopicTemplate { get; set; } = "/{Namespace}/{ModuleId}/OfferedCapability/Response";

    private readonly ConcurrentDictionary<string, (string requesterId, string requesterRole)> _originalRequesters = new(StringComparer.OrdinalIgnoreCase);
    // previously had a recent-fingerprint cache here; removed per revert request
    private bool _listenerRegistered;
    private readonly object _listenerLock = new();
    private Action<I40Message, string>? _broadcastCallback;
    private Action<I40Message, string>? _internalResponseCallback;

    public ForwardCapabilityRequestsNode() : base("ForwardCapabilityRequests")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("ForwardCapabilityRequests: MessagingClient unavailable");
            return NodeStatus.Failure;
        }
        // Ensure the persistent bridge listener is registered; the listener itself performs the forwarding.
        EnsureListener(client);
        return NodeStatus.Success;
    }

    private void EnsureListener(MessagingClient client)
    {
        var globalKey = "ForwardCapabilityRequests.ListenerRegistered";
        // If another node instance already registered the listener for this agent, skip registration.
        try
        {
            if (Context.Get<bool>(globalKey))
            {
                return;
            }
        }
        catch { }

        lock (_listenerLock)
        {
            if (_listenerRegistered || Context.Get<bool>(globalKey))
            {
                return;
            }

        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";

        // Subscribe and register a thin topic-bridge style callback for broadcast CfPs
        var broadcastTopic = BroadcastTopicTemplate.Replace("{Namespace}", ns);
        try
        {
            client.SubscribeAsync(broadcastTopic).GetAwaiter().GetResult();
            _broadcastCallback = (message, topic) =>
            {
                try
                {
                    if (message == null) return;
                    if (!MatchesDispatcherOffer(message)) return;

                    // Prevent forwarding messages sent by this agent (avoid self-forward loops)
                    var senderId = message.Frame?.Sender?.Identification?.Id;
                    if (!string.IsNullOrWhiteSpace(senderId) && string.Equals(senderId, Context.AgentId, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    // Simple bridge: forward to internal target topic without waiting
                    var moduleId = Context.Get<string>("config.Agent.ModuleId")
                                   ?? Context.Get<string>("config.Agent.AgentId")
                                   ?? Context.AgentId
                                   ?? string.Empty;
                    var targetTopic = TargetTopicTemplate.Replace("{Namespace}", ns).Replace("{ModuleId}", moduleId);
                    var incomingType = message.Frame?.Type ?? string.Empty;
                    var isManufacturing = incomingType.IndexOf("ManufacturingSequence", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isManufacturing && targetTopic.IndexOf("ProcessChain", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetTopic = targetTopic.Replace("ProcessChain", "ManufacturingSequence");
                    }

                    var planningAgentId = moduleId;
                    var forwardedMessage = MessageTargetingHelper.CloneWithNewReceiver(message, planningAgentId, "PlanningHolon");

                    var conversationId = message.Frame?.ConversationId;
                    var originalRequesterId = message.Frame?.Sender?.Identification?.Id;
                    var originalRequesterRole = message.Frame?.Sender?.Role?.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(conversationId) && !string.IsNullOrWhiteSpace(originalRequesterId))
                    {
                        _originalRequesters[conversationId] = (originalRequesterId, originalRequesterRole);
                        try { Context.Set($"OriginalRequester_{conversationId}", originalRequesterId); if (!string.IsNullOrWhiteSpace(originalRequesterRole)) Context.Set($"OriginalRequesterRole_{conversationId}", originalRequesterRole); } catch { }
                    }

                    _ = client.PublishAsync(forwardedMessage, targetTopic);
                    Logger.LogInformation("ForwardCapabilityRequests: bridged CfP from {Sender} to {Topic}", senderId, targetTopic);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "ForwardCapabilityRequests: exception in broadcast bridge callback");
                }
            };
            client.OnTopic(broadcastTopic, _broadcastCallback);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ForwardCapabilityRequests: failed to subscribe/register broadcast topic {Topic}", broadcastTopic);
        }

        // Subscribe and register a thin bridge for internal Planning responses -> external requester
        try
        {
            var moduleId = Context.Get<string>("config.Agent.ModuleId")
                           ?? Context.Get<string>("config.Agent.AgentId")
                           ?? Context.AgentId
                           ?? string.Empty;
            var internalResponseTopic = InternalResponseTopicTemplate.Replace("{Namespace}", ns).Replace("{ModuleId}", moduleId);
            client.SubscribeAsync(internalResponseTopic).GetAwaiter().GetResult();
            _internalResponseCallback = (message, topic) =>
            {
                try
                {
                    if (message == null) return;
                    var t = message.Frame?.Type ?? string.Empty;
                    var baseType = t.Split('/')[0].ToLowerInvariant();
                    // Only forward proposals/refusals
                    if (!baseType.StartsWith("propos") && !baseType.StartsWith("refus")) return;

                    var conv = message.Frame?.ConversationId;
                    if (string.IsNullOrWhiteSpace(conv)) return;
                    if (!_originalRequesters.TryGetValue(conv, out var orig)) return;

                    var receiverId = orig.requesterId;
                    var receiverRole = string.IsNullOrWhiteSpace(orig.requesterRole) ? "Dispatching" : orig.requesterRole;

                    // Ensure receiver is set on message
                    try
                    {
                        if (message.Frame?.Receiver == null)
                        {
                            message.Frame.Receiver = new I40Sharp.Messaging.Models.Participant();
                        }
                        message.Frame.Receiver.Identification = message.Frame.Receiver.Identification ?? new I40Sharp.Messaging.Models.Identification();
                        message.Frame.Receiver.Identification.Id = receiverId;
                        message.Frame.Receiver.Role = new I40Sharp.Messaging.Models.Role { Name = receiverRole };
                    }
                    catch { }

                    var outTopic = $"/{ns}/{receiverId}/OfferedCapability/Response";
                    _ = client.PublishAsync(message, outTopic);
                    Logger.LogInformation("ForwardCapabilityRequests: bridged internal response conv={Conv} to external requester {Receiver} on {Topic}", conv, receiverId, outTopic);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "ForwardCapabilityRequests: exception in internal response bridge callback");
                }
            };
            client.OnTopic(internalResponseTopic, _internalResponseCallback);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ForwardCapabilityRequests: failed to subscribe/register internal response topic");
        }

            _listenerRegistered = true;
            try { Context.Set(globalKey, true); } catch { }
            var moduleIdLog = Context.Get<string>("config.Agent.ModuleId") ?? Context.AgentId ?? string.Empty;
            var agentIdLog = Context.Get<string>("config.Agent.AgentId") ?? Context.AgentId ?? string.Empty;
            Logger.LogInformation("ForwardCapabilityRequests: registered CfP listener for namespace {Namespace} (Agent={Agent} Module={Module})", ns, agentIdLog, moduleIdLog);
        }
    }

    private bool MatchesDispatcherOffer(I40Message message)
    {
        if (message?.Frame == null)
        {
            return false;
        }

        var msgType = message.Frame.Type ?? string.Empty;

        // Accept generic CallForProposal
        if (string.Equals(msgType, I40MessageTypes.CALL_FOR_PROPOSAL, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Accept CallForProposal subtypes (ProcessChain, ManufacturingSequence, OfferedCapability, etc.)
        var prefix = I40MessageTypes.CALL_FOR_PROPOSAL + "/";
        if (msgType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public override Task OnReset()
    {
        try
        {
            var client = Context.Get<MessagingClient>("MessagingClient");
            var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
            var broadcastTopic = BroadcastTopicTemplate.Replace("{Namespace}", ns);
            var moduleId = Context.Get<string>("config.Agent.ModuleId")
                           ?? Context.Get<string>("config.Agent.AgentId")
                           ?? Context.AgentId
                           ?? string.Empty;
            var internalResponseTopic = InternalResponseTopicTemplate.Replace("{Namespace}", ns).Replace("{ModuleId}", moduleId);

            // Intentionally do NOT unregister topic callbacks here. The listener is intended to be long-lived
            // for the lifetime of the agent process. Repeated BT resets caused repeated unsubscribe/subscribe
            // cycles which led to duplicate registrations. Keep callbacks active.
        }
        catch { }

        // keep registrations intact; only clear transient per-conversation state
        _originalRequesters.Clear();
        return Task.CompletedTask;
    }

    // ComputeFingerprint removed
}

