using System.Linq;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class SelectCandidatesNode : BTNode
{
    public SelectCandidatesNode() : base("SelectCandidates") { }

    public override Task<NodeStatus> Execute()
    {
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        var state = Context.Get<DispatchingState>("DispatchingState");
        if (ctx == null || state == null)
        {
            Logger.LogError("SelectCandidates: missing context or state");
            return Task.FromResult(NodeStatus.Failure);
        }

        var requirement = Context.Get<CapabilityRequirement>("CurrentRequirement");
        if (requirement == null)
        {
            // Fallback: pick next unfulfilled requirement from negotiation context
            try
            {
                // Prefer first unfulfilled requirement
                var fallback = ctx.Requirements?.FirstOrDefault(r => r != null && (r.CapabilityOffers.Count == 0 && r.OfferedCapabilitySequences.Count == 0));
                if (fallback == null && ctx.Requirements?.Count > 0)
                {
                    // As ultimate fallback pick the first requirement
                    fallback = ctx.Requirements[0];
                }

                if (fallback != null)
                {
                    requirement = fallback;
                    Context.Set("CurrentRequirement", requirement);
                    Logger.LogInformation("SelectCandidates: CurrentRequirement was missing, selected fallback RequirementId={Req}", requirement.RequirementId);
                }
            }
            catch
            {
                // ignore
            }
        }

        if (requirement == null)
        {
            Logger.LogError("SelectCandidates: CurrentRequirement missing and no fallback available");
            return Task.FromResult(NodeStatus.Failure);
        }

        var candidates = state.Modules
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.ModuleId))
            .Where(m => m.Capabilities != null && m.Capabilities.Any(c => string.Equals(c, requirement.Capability, System.StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.ModuleId)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            var regex = new Regex(@"^P\d{3}$");

            candidates = state.Modules
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.ModuleId))
                .Where(m => regex.IsMatch(m.ModuleId))
                .Select(m => m.ModuleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        Context.Set("ProcessChain.CurrentCandidates", candidates);
        Logger.LogInformation("SelectCandidates: selected {Count} candidates for capability {Cap}", candidates.Count, requirement.Capability);
        return Task.FromResult(NodeStatus.Success);
    }
}
