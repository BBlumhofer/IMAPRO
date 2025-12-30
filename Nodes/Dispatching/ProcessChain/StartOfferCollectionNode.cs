using System;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class StartOfferCollectionNode : BTNode
{
    public StartOfferCollectionNode() : base("StartOfferCollection") { }

    public override Task<NodeStatus> Execute()
    {
        var timeoutSeconds = Context.Get<int>("config.DispatchingAgent.OfferCollectionTimeoutSeconds");
        if (timeoutSeconds <= 0) timeoutSeconds = 10;
        Context.Set("ProcessChain.OfferCollectionStartedUtc", DateTime.UtcNow);
        Context.Set("ProcessChain.OfferTimeoutUtc", DateTime.UtcNow.AddSeconds(timeoutSeconds));

        Logger.LogInformation("StartOfferCollection: started collection window of {Seconds}s (cleared previous offers)", timeoutSeconds);
        return Task.FromResult(NodeStatus.Success);
    }
}
