using System;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class BuildConversationIdNode : BTNode
{
    public BuildConversationIdNode() : base("BuildConversationId") { }

    public override Task<NodeStatus> Execute()
    {
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        if (ctx == null)
        {
            Logger.LogError("BuildConversationId: negotiation context missing");
            return Task.FromResult(NodeStatus.Failure);
        }
        if (!string.IsNullOrWhiteSpace(ctx.ConversationId))
        {
            Logger.LogDebug("BuildConversationId: existing ConversationId preserved: {Conv}", ctx.ConversationId);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ctx.ProductId))
            {
                Logger.LogWarning("BuildConversationId: ProductId missing in context; generating fallback id");
            }

            ctx.ConversationId = $"{ctx.ProductId ?? Guid.NewGuid().ToString()}#{Guid.NewGuid().ToString()}";
            Logger.LogInformation("BuildConversationId: generated ConversationId={Conv}", ctx.ConversationId);
        }
        Context.Set("ProcessChain.ConversationId", ctx.ConversationId);
        return Task.FromResult(NodeStatus.Success);
    }
}
