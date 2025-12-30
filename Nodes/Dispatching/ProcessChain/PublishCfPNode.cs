using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AasSharpClient.Models.Messages;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class PublishCfPNode : BTNode
{
    public PublishCfPNode() : base("PublishCfP") { }

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        if (client == null || ctx == null)
        {
            Logger.LogError("PublishCfP: missing client or negotiation context");
            return NodeStatus.Failure;
        }

        var candidates = Context.Get<List<string>>("ProcessChain.CurrentCandidates") ?? new List<string>();
        var cfpsByTarget = Context.Get<Dictionary<string, List<I40Message>>>("ProcessChain.CfPsByTarget") ?? new Dictionary<string, List<I40Message>>(StringComparer.OrdinalIgnoreCase);
        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace");
        var topic = $"/{ns}/ModuleHolon/broadcast/OfferedCapability/Request";

        foreach (var tgt in candidates)
        {
            try
            {
                var requirement = Context.Get<CapabilityRequirement>("CurrentRequirement");
                // Try to include capability description/container and asset location as in original dispatcher
                var state = Context.Get<DispatchingState>("DispatchingState");
                string? desc = null;
                try { if (state != null) state.TryGetCapabilityDescription(requirement.Capability, out desc); } catch { }

                var cfpMessage = new CapabilityCallForProposalMessage(
                    Context.AgentId,
                    Context.AgentRole,
                    tgt,
                    "ModuleHolon",
                    ctx.ConversationId,
                    I40Sharp.Messaging.Models.I40MessageTypeSubtypes.ManufacturingSequence,
                    requirement.Capability,
                    requirement.RequirementId,
                    ctx.ProductId,
                    capabilityDescription: desc,
                    capabilityContainer: requirement.CapabilityContainer,
                    assetLocation: ctx.AssetLocation);

                var cfp = cfpMessage.ToI40Message();
                await cfpMessage.PublishAsync(client, topic).ConfigureAwait(false);
                Logger.LogInformation("PublishCfP: published CfP Conv={Conv} to {Target} Capability={Cap} Topic={Topic}", ctx.ConversationId, tgt, requirement.Capability, topic);
                // Cache for potential reissue (simple one per target)
                if (!cfpsByTarget.TryGetValue(tgt, out var l))
                {
                    l = new System.Collections.Generic.List<I40Message>();
                    cfpsByTarget[tgt] = l;
                }
                l.Add(cfp);
                Context.Set("ProcessChain.CfPsByTarget", cfpsByTarget);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "PublishCfP: failed to publish CfP to {Target}", tgt);
            }
        }

        return NodeStatus.Success;
    }
}
