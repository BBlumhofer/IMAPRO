using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using AasSharpClient.Models;
using ActionModel = AasSharpClient.Models.Action;

namespace MAS_BT.Nodes.Planning;

/// <summary>
/// ApplyOfferDecision - updates schedule/offer status based on OfferDecision (ACCEPT/DENY/REQUIRE).
/// </summary>
public class ApplyOfferDecisionNode : BTNode
{
    public ApplyOfferDecisionNode() : base("ApplyOfferDecision") {}

    public override Task<NodeStatus> Execute()
    {
        var decisionRaw = Context.Get<string>("OfferDecision");

        if (string.IsNullOrWhiteSpace(decisionRaw))
        {
            return Task.FromResult(NodeStatus.Running);
        }

        var decision = decisionRaw.ToUpperInvariant();
        var action = Context.Get<ActionModel>("CurrentPlanAction");

        switch (decision)
        {
            case "ACCEPT":
                Context.Set("OfferStatus", "tentative");
                action?.SetStatus(ActionStatusEnum.PLANNED);
                Logger.LogInformation("ApplyOfferDecision: offer accepted -> tentative");
                break;
            case "REQUIRE":
                Context.Set("OfferStatus", "booked");
                action?.SetStatus(ActionStatusEnum.PLANNED);
                Logger.LogInformation("ApplyOfferDecision: requirement received -> booked");
                break;
            case "DENY":
                Context.Set("OfferStatus", "declined");
                Context.Set("PendingOffer", null);
                action?.SetStatus(ActionStatusEnum.PRECONDITION_FAILED);
                Logger.LogInformation("ApplyOfferDecision: offer denied -> removed");
                break;
            default:
                Logger.LogWarning("ApplyOfferDecision: unknown decision {Decision}, keeping pending", decisionRaw);
                break;
        }

        // consume decision so the loop stays idle until a new one arrives
        Context.Set("OfferDecision", null);

        return Task.FromResult(NodeStatus.Success);
    }
}
