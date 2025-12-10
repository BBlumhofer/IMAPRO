using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Planning;

/// <summary>
/// AwaitOfferDecision - stub: waits for accept/deny/require decision, uses context override if present.
/// </summary>
public class AwaitOfferDecisionNode : BTNode
{
    public int TimeoutMs { get; set; } = 2000;

    public AwaitOfferDecisionNode() : base("AwaitOfferDecision") {}

    public override Task<NodeStatus> Execute()
    {
        var decision = Context.Get<string>("OfferDecision");

        if (string.IsNullOrWhiteSpace(decision))
        {
            // keep waiting without emitting noisy logs
            return Task.FromResult(NodeStatus.Running);
        }

        Logger.LogInformation("AwaitOfferDecision: decision={Decision} (timeoutMs={Timeout})", decision, TimeoutMs);
        return Task.FromResult(NodeStatus.Success);
    }
}
