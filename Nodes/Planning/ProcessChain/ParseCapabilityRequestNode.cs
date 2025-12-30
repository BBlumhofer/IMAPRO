using System;
using System.Threading.Tasks;
using MAS_BT.Core;
using BaSyx.Models.AdminShell;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging.Models;

namespace MAS_BT.Nodes.Planning.ProcessChain;

public class ParseCapabilityRequestNode : BTNode
{
    public ParseCapabilityRequestNode() : base("ParseCapabilityRequest") { }

    public override Task<NodeStatus> Execute()
    {
        var incoming = Context.Get<I40Message>("LastReceivedMessage");
        if (incoming == null)
        {
            Logger.LogDebug("ParseCapabilityRequest: no incoming message");
            return Task.FromResult(NodeStatus.Failure);
        }

        var request = CapabilityRequestContext.FromMessage(incoming);
        if (string.IsNullOrWhiteSpace(request.Capability))
        {
            Logger.LogDebug("ParseCapabilityRequest: capability missing in CfP message");
            return Task.FromResult(NodeStatus.Failure);
        }

        // Consume the current message (we rely on the WaitForMessage node to dequeue from the inbox).
        // After parsing, clear CurrentMessage/LastReceivedMessage so the message isn't reprocessed.

        Context.Set("Planning.CapabilityRequest", request);
        Context.Set("RequiredCapability", request.Capability);
        Context.Set("RequesterId", request.RequesterId);
        Context.Set("RequesterRole", request.RequesterRole);
        Context.Set("ConversationId", request.ConversationId);
        
        // After parsing, clear the message from the blackboard so downstream WaitForMessage/other nodes
        // will not see it again in this agent's context. The message object was not copied.
        try
        {
            Context.Set("LastReceivedMessage", (I40Message?)null);
            Context.Set("CurrentMessage", (I40Message?)null);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "ParseCapabilityRequest: failed to clear message context after parsing");
        }

        Logger.LogInformation("ParseCapabilityRequest: conv={Conv} capability={Capability} requirement={Requirement} requester={Requester}/{Role}", request.ConversationId, request.Capability, request.RequirementId, request.RequesterId, request.RequesterRole);
        return Task.FromResult(NodeStatus.Success);
    }
}
