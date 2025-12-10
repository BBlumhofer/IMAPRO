using System;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;

namespace MAS_BT.Nodes.ModuleHolon;

public class ForwardToInternalNode : BTNode
{
    public string TargetTopic { get; set; } = string.Empty;

    public ForwardToInternalNode() : base("ForwardToInternal") {}

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        var msg = Context.Get<I40Message>("LastReceivedMessage");
        if (client == null || msg == null)
        {
            Logger.LogError("ForwardToInternal: missing client or message");
            return NodeStatus.Failure;
        }

        var topic = Resolve(TargetTopic);
        if (string.IsNullOrWhiteSpace(topic))
        {
            Logger.LogError("ForwardToInternal: TargetTopic empty");
            return NodeStatus.Failure;
        }

        await client.PublishAsync(msg, topic);
        Logger.LogInformation("ForwardToInternal: forwarded conversation {Conv} to {Topic}", msg.Frame?.ConversationId, topic);
        Context.Set("ForwardedConversationId", msg.Frame?.ConversationId ?? string.Empty);
        return NodeStatus.Success;
    }

    private string Resolve(string template)
    {
        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
        var moduleId = Context.Get<string>("config.Agent.ModuleName") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;
        return template
            .Replace("{Namespace}", ns)
            .Replace("{ModuleId}", moduleId);
    }
}
