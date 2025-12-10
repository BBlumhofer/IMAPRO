using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using BaSyx.Models.AdminShell;

namespace MAS_BT.Nodes.ModuleHolon;

public class ModuleHolonRegistrationNode : BTNode
{
    public ModuleHolonRegistrationNode() : base("ModuleHolonRegistration") {}

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("ModuleHolonRegistration: MessagingClient unavailable");
            return NodeStatus.Failure;
        }

        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
        var moduleId = Context.Get<string>("config.Agent.ModuleId") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;
        var topic = $"/DispatchingAgent/{ns}/ModuleRegistration/";

        try
        {
            var builder = new I40MessageBuilder()
                .From(Context.AgentId, string.IsNullOrWhiteSpace(Context.AgentRole) ? "ModuleHolon" : Context.AgentRole)
                .To("DispatchingAgent", null)
                .WithType("moduleRegistration")
                .WithConversationId(Guid.NewGuid().ToString());

            var payload = new Property<string>("ModuleId")
            {
                Value = new PropertyValue<string>(moduleId)
            };
            builder.AddElement(payload);

            var msg = builder.Build();
            await client.PublishAsync(msg, topic);
            Logger.LogInformation("ModuleHolonRegistration: sent registration for {ModuleId} to {Topic}", moduleId, topic);
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ModuleHolonRegistration: failed to send registration");
            return NodeStatus.Failure;
        }
    }
}

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
        var moduleId = Context.Get<string>("config.Agent.ModuleId") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;

        var topics = new[]
        {
            $"/DispatchingAgent/{ns}/ModuleOffers/{moduleId}/Request/",
            $"/Modules/{moduleId}/ScheduleAction/",
            $"/Modules/{moduleId}/BookingConfirmation/",
            $"/Modules/{moduleId}/TransportPlan/",
            $"/ModuleHolon/{moduleId}/register"
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
        var moduleId = Context.Get<string>("config.Agent.ModuleId") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;
        return template
            .Replace("{Namespace}", ns)
            .Replace("{ModuleId}", moduleId);
    }
}

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

public class ReplyToDispatcherNode : BTNode
{
    public string ResponseTopicTemplate { get; set; } = string.Empty;

    public ReplyToDispatcherNode() : base("ReplyToDispatcher") {}

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        var msg = Context.Get<I40Message>("PlanningResponse") ?? Context.Get<I40Message>("LastReceivedMessage");
        if (client == null || msg == null)
        {
            Logger.LogError("ReplyToDispatcher: missing client or message");
            return NodeStatus.Failure;
        }

        var topic = Resolve(ResponseTopicTemplate);
        if (string.IsNullOrWhiteSpace(topic))
        {
            Logger.LogError("ReplyToDispatcher: ResponseTopicTemplate empty");
            return NodeStatus.Failure;
        }

        await client.PublishAsync(msg, topic);
        Logger.LogInformation("ReplyToDispatcher: sent response conv {Conv} to {Topic}", msg.Frame?.ConversationId, topic);
        return NodeStatus.Success;
    }

    private string Resolve(string template)
    {
        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
        var moduleId = Context.Get<string>("config.Agent.ModuleId") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;
        return template
            .Replace("{Namespace}", ns)
            .Replace("{ModuleId}", moduleId);
    }
}

public class WaitForSubHolonRegisterNode : BTNode
{
    public int TimeoutSeconds { get; set; } = 20;

    public WaitForSubHolonRegisterNode() : base("WaitForSubHolonRegister") {}

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null)
        {
            Logger.LogError("WaitForSubHolonRegister: MessagingClient not found");
            return NodeStatus.Failure;
        }

        var moduleId = Context.Get<string>("config.Agent.ModuleId") ?? Context.Get<string>("ModuleId") ?? Context.AgentId;
        var topic = $"/ModuleHolon/{moduleId}/register";

        var queue = new ConcurrentQueue<I40Message>();
        await client.SubscribeAsync(topic);
        client.OnMessage(m =>
        {
            if (string.Equals(m?.Frame?.Type, "subHolonRegister", StringComparison.OrdinalIgnoreCase))
            {
                queue.Enqueue(m);
            }
        });

        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalSeconds < TimeoutSeconds)
        {
            if (queue.TryDequeue(out var msg))
            {
                Context.Set("SubHolonRegisterMessage", msg);
                // try to capture agent id/role from payload
                try
                {
                    var json = msg.InteractionElements?.Count > 0
                        ? JsonSerializer.Serialize(msg.InteractionElements)
                        : string.Empty;
                    Context.Set("LastSubHolonRegisterPayload", json);
                }
                catch { }

                Logger.LogInformation("WaitForSubHolonRegister: received sub-holon registration from {Sender}", msg.Frame?.Sender?.Identification?.Id);
                return NodeStatus.Success;
            }
            await Task.Delay(100);
        }

        Logger.LogWarning("WaitForSubHolonRegister: timeout after {Timeout}s", TimeoutSeconds);
        return NodeStatus.Failure;
    }
}

public class SpawnSubHolonsNode : BTNode
{
    public SpawnSubHolonsNode() : base("SpawnSubHolons") {}

    public override Task<NodeStatus> Execute()
    {
        var planningCmd = Context.Get<string>("config.SubHolons.Planning.Command");
        var executionCmd = Context.Get<string>("config.SubHolons.Execution.Command");

        StartIfPresent(planningCmd, "Planning");
        StartIfPresent(executionCmd, "Execution");

        return Task.FromResult(NodeStatus.Success);
    }

    private void StartIfPresent(string? cmd, string label)
    {
        if (string.IsNullOrWhiteSpace(cmd))
        {
            Logger.LogInformation("SpawnSubHolons: no {Label} command configured", label);
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                ArgumentList = { "-c", cmd },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(psi);
            Logger.LogInformation("SpawnSubHolons: started {Label} sub-holon with command: {Cmd}", label, cmd);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SpawnSubHolons: failed to start {Label} sub-holon", label);
        }
    }
}
