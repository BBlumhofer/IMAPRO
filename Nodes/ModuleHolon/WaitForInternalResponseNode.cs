using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;

namespace MAS_BT.Nodes.ModuleHolon;

public class WaitForInternalResponseNode : BTNode
{
    public int TimeoutSeconds { get; set; } = 10;

    public WaitForInternalResponseNode() : base("WaitForInternalResponse") {}

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null)
        {
            Logger.LogError("WaitForInternalResponse: MessagingClient not found");
            return NodeStatus.Failure;
        }

        var conv = Context.Get<string>("ForwardedConversationId")
                   ?? Context.Get<string>("ConversationId")
                   ?? Context.Get<I40Message>("LastReceivedMessage")?.Frame?.ConversationId;
        if (string.IsNullOrWhiteSpace(conv))
        {
            Logger.LogWarning("WaitForInternalResponse: no conversation id");
            return NodeStatus.Failure;
        }

        var queue = new ConcurrentQueue<I40Message>();
        client.OnConversation(conv, m => queue.Enqueue(m));

        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalSeconds < TimeoutSeconds)
        {
            if (queue.TryDequeue(out var m))
            {
                Context.Set("PlanningResponse", m);
                Context.Set("LastReceivedMessage", m);
                Logger.LogInformation("WaitForInternalResponse: got response for conv {Conv}", conv);
                return NodeStatus.Success;
            }
            await Task.Delay(100);
        }

        Logger.LogWarning("WaitForInternalResponse: timeout after {Timeout}s for conv {Conv}", TimeoutSeconds, conv);
        return NodeStatus.Failure;
    }
}
