using System.Collections.Generic;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class RecordExpectedRespondersNode : BTNode
{
    public RecordExpectedRespondersNode() : base("RecordExpectedResponders") { }

    public override Task<NodeStatus> Execute()
    {
        var candidates = Context.Get<List<string>>("ProcessChain.CurrentCandidates") ?? new List<string>();
        var filtered = new List<string>(candidates);
        Context.Set("ProcessChain.ExpectedOfferResponders", filtered);
        Logger.LogInformation("RecordExpectedResponders: recorded {Count} expected responders", filtered.Count);
        return Task.FromResult(NodeStatus.Success);
    }
}
