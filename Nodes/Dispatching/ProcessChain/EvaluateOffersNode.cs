using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class EvaluateOffersNode : BTNode
{
    public EvaluateOffersNode() : base("EvaluateOffers") { }

    public override Task<NodeStatus> Execute()
    {
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        var current = Context.Get<CapabilityRequirement>("CurrentRequirement");
        if (ctx == null || current == null)
        {
            Logger.LogError("EvaluateOffers: missing context or current requirement");
            return Task.FromResult(NodeStatus.Failure);
        }

        if (current.CapabilityOffers.Count > 0)
        {
            // Choose first offer as selection (simple policy)
            var selected = current.CapabilityOffers[0];
            Context.Set("ProcessChain.SelectedOffer", selected);
            string inst = "<unknown>";
            try
            {
                inst = AasSharpClient.Models.Helpers.AasValueUnwrap.UnwrapToString(selected.InstanceIdentifier?.Value) ?? "<unknown>";
            }
            catch { }
            Logger.LogInformation("EvaluateOffers: selected offer InstanceIdentifier={Inst} for requirement {Req}", inst, current.RequirementId);
        }
        else
        {
            Logger.LogInformation("EvaluateOffers: no offers for requirement {Req}", current.RequirementId);
        }

        return Task.FromResult(NodeStatus.Success);
    }
}
