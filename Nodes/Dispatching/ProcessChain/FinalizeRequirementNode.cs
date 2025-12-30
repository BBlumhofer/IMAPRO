using System.Collections.Generic;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class FinalizeRequirementNode : BTNode
{
    public FinalizeRequirementNode() : base("FinalizeRequirement") { }

    public override Task<NodeStatus> Execute()
    {
        var current = Context.Get<CapabilityRequirement>("CurrentRequirement");
        if (current == null)
        {
            Logger.LogError("FinalizeRequirement: CurrentRequirement missing");
            return Task.FromResult(NodeStatus.Failure);
        }

        var completedMap = Context.Get<Dictionary<string, bool>>("ProcessChain.RequirementCompleted") ?? new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        completedMap[current.RequirementId] = true;
        Context.Set("ProcessChain.RequirementCompleted", completedMap);

        // Notify sequential coordinator if present
        var coordinator = Context.Get<SequentialRequirementCoordinator>("ProcessChain.SequentialCoordinator");
        if (coordinator != null)
        {
            coordinator.MarkRequirementCompleted(current.RequirementId);
            Logger.LogInformation("FinalizeRequirement: notified sequential coordinator for {Req}", current.RequirementId);
        }
        // Do NOT remove the requirement from the negotiation list here.
        // Keep the requirement in place and mark it as completed so the response builder
        // can still assemble the final ProcessChain/ManufacturingSequence from recorded offers.
        // Any iterator adjustments are handled by the GetNextRequirement node via markers.

        Logger.LogInformation("FinalizeRequirement: finalized requirement {Req}", current.RequirementId);
        return Task.FromResult(NodeStatus.Success);
    }
}
