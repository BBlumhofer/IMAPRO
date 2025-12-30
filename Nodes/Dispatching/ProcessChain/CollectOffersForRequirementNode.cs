using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.ManufacturingSequence;
using AasSharpClient.Models.ProcessChain;
using BaSyx.Models.AdminShell;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

/// <summary>
/// Sammelt Offers/Refusals für EIN Requirement (CurrentRequirement)
/// </summary>
public class CollectOffersForRequirementNode : BTNode
{
    private readonly ConcurrentQueue<I40Message> _incoming = new();
    private readonly HashSet<string> _respondedModules = new(StringComparer.OrdinalIgnoreCase);
    private bool _subscribed;
    private DateTime _startTime;
    private List<string>? _expectedModuleIds;
    
    public int TimeoutSeconds { get; set; } = 60;
    
    public CollectOffersForRequirementNode() : base("CollectOffersForRequirement") { }
    
    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        var requirement = Context.Get<CapabilityRequirement>("CurrentRequirement");
        
        if (client == null || ctx == null || requirement == null)
        {
            Logger.LogError("CollectOffersForRequirement: missing client, context, or requirement");
            return NodeStatus.Failure;
        }
        
        if (!_subscribed)
        {
            _expectedModuleIds = Context.Get<List<string>>("CurrentRequirement.ExpectedResponders") ?? new List<string>();
            _startTime = DateTime.UtcNow;
            _respondedModules.Clear();
            
            // WICHTIG: Erst nach ExpectedModuleIds setzen, bevor wir Callbacks registrieren!
            // Sonst könnten Nachrichten reinkommen während wir noch nicht bereit sind.
            
            // Check inbox for messages that already arrived (race condition fix)
            var existingMessages = client.DequeueMatchingAll((msg, topic) =>
            {
                var convId = msg.Frame?.ConversationId;
                return !string.IsNullOrWhiteSpace(convId) && 
                       string.Equals(convId, ctx.ConversationId, StringComparison.OrdinalIgnoreCase);
            });
            
            if (existingMessages.Count > 0)
            {
                Logger.LogInformation("CollectOffersForRequirement: found {Count} messages already in inbox for ConversationId={ConvId}",
                    existingMessages.Count, ctx.ConversationId);
                foreach (var (msg, _) in existingMessages)
                {
                    _incoming.Enqueue(msg);
                }
            }
            
            // Subscribe zu künftigen Responses
            client.OnConversation(ctx.ConversationId, msg => _incoming.Enqueue(msg));
            
            var ns = Context.Get<string>("config.Namespace") ?? "phuket";
            var responseTopic = $"/{ns}/{Context.AgentId}/OfferedCapability/Response";
            try
            {
                await client.SubscribeAsync(responseTopic).ConfigureAwait(false);
                Logger.LogInformation("CollectOffersForRequirement: subscribed to {Topic}", responseTopic);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "CollectOffersForRequirement: failed to subscribe to {Topic}", responseTopic);
            }
            
            _subscribed = true;
            
            Logger.LogInformation("CollectOffersForRequirement: waiting for {Count} modules (Requirement={RequirementId})",
                _expectedModuleIds.Count, requirement.RequirementId);
        }
        
        // Verarbeite eingehende Nachrichten
        var msgCount = _incoming.Count;
        if (msgCount > 0)
        {
            Logger.LogDebug("CollectOffersForRequirement: processing {Count} queued messages", msgCount);
        }
        
        while (_incoming.TryDequeue(out var msg))
        {
            Logger.LogDebug("CollectOffersForRequirement: message from {Sender} type={Type} conversationId={ConvId}",
                msg.Frame?.Sender?.Identification?.Id, msg.Frame?.Type, msg.Frame?.ConversationId);
            
            var senderId = msg.Frame?.Sender?.Identification?.Id;
            if (string.IsNullOrWhiteSpace(senderId))
            {
                Logger.LogWarning("CollectOffersForRequirement: skipping message with empty senderId");
                continue;
            }
            
            var msgType = msg.Frame?.Type;
            var msgTypeLower = msgType?.ToLowerInvariant() ?? string.Empty;

            // Refusal? accept both "refuse" and "refusal" variants (case-insensitive)
            if (msgTypeLower.StartsWith("refus", StringComparison.OrdinalIgnoreCase))
            {
                if (_respondedModules.Add(senderId))
                {
                    Logger.LogInformation("CollectOffersForRequirement: {Module} refused (Requirement={RequirementId}, type={Type})",
                        senderId, requirement.RequirementId, msgType);
                }
                continue;
            }

            // Offer? accept "propose"/"proposal" variants
            if (!msgTypeLower.StartsWith("propos", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogDebug("CollectOffersForRequirement: skipping non-proposal message type={Type}", msgType);
                continue;
            }
            
            // Parse Offer - simplified: Just count as responded for now
            // Full offer parsing is complex and can be added later
            if (_respondedModules.Add(senderId))
            {
                Logger.LogInformation("CollectOffersForRequirement: {Module} sent proposal (Requirement={RequirementId})",
                    senderId, requirement.RequirementId);
                
                // Mark that we got a response - BuildProcessChainResponse will handle empty offers
            }
        }
        
        // Prüfe Timeout
        var elapsed = DateTime.UtcNow - _startTime;
        if (elapsed.TotalSeconds >= TimeoutSeconds)
        {
            Logger.LogWarning("CollectOffersForRequirement: timeout after {Elapsed}s (Requirement={RequirementId}, Responded={Responded}/{Expected})",
                (int)elapsed.TotalSeconds, requirement.RequirementId, _respondedModules.Count, _expectedModuleIds?.Count ?? 0);
            Reset();
            return NodeStatus.Success; // Timeout ist OK, gehe weiter
        }
        
        // Alle Module geantwortet?
        var allResponded = _expectedModuleIds != null 
            && _expectedModuleIds.Count > 0 
            && _expectedModuleIds.All(id => _respondedModules.Contains(id));
        
        if (allResponded)
        {
            Logger.LogInformation("CollectOffersForRequirement: all {Count} modules responded (Requirement={RequirementId}, Offers={OfferCount})",
                _expectedModuleIds.Count, requirement.RequirementId, 
                requirement.CapabilityOffers.Count + requirement.OfferedCapabilitySequences.Count);
            Reset();
            return NodeStatus.Success;
        }
        
        // Noch am Warten
        return NodeStatus.Running;
    }
    
    private void Reset()
    {
        _subscribed = false;
        _respondedModules.Clear();
        _expectedModuleIds = null;
    }
    
    public override Task OnReset()
    {
        Reset();
        return Task.CompletedTask;
    }
}
