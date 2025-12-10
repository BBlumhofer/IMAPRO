using System;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;

namespace MAS_BT.Nodes.ModuleHolon;

public class SubscribeModuleHolonTopicsNode : BTNode
{
    public SubscribeModuleHolonTopicsNode() : base("SubscribeModuleHolonTopics") {}

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("SubscribeModuleHolonTopics: MessagingClient unavailable");
            return NodeStatus.Failure;
        }

        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
        var moduleId = Context.Get<string>("config.Agent.ModuleName") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;

        var topics = new[]
        {
            $"/{ns}/DispatchingAgent/Offers",
            $"/{ns}/{moduleId}/ScheduleAction",
            $"/{ns}/{moduleId}/BookingConfirmation",
            $"/{ns}/{moduleId}/TransportPlan",
            $"/{ns}/{moduleId}/register"
        };

        var ok = 0;
        foreach (var topic in topics)
        {
            try
            {
                await client.SubscribeAsync(topic);
                ok++;
                Logger.LogInformation("SubscribeModuleHolonTopics: subscribed {Topic}", topic);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "SubscribeModuleHolonTopics: failed to subscribe {Topic}", topic);
            }
        }

        return ok > 0 ? NodeStatus.Success : NodeStatus.Failure;
    }
}
