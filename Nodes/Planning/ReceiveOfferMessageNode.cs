using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Planning;

/// <summary>
/// ReceiveOfferMessage - stub: reads an offer decision from context; defaults to ACCEPT.
/// </summary>
public class ReceiveOfferMessageNode : BTNode
{
    public ReceiveOfferMessageNode() : base("ReceiveOfferMessage") {}

    public override Task<NodeStatus> Execute()
    {
        var decision = Context.Get<string>("OfferDecision");

        if (string.IsNullOrWhiteSpace(decision))
        {
            // nothing to do yet; keep the sequence running without spamming logs
            return Task.FromResult(NodeStatus.Running);
        }

        Logger.LogInformation("ReceiveOfferMessage: decision={Decision}", decision);
        return Task.FromResult(NodeStatus.Success);
    }
}
