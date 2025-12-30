using System;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;

namespace MAS_BT.Nodes.ModuleHolon;

public class ReplyToDispatcherNode : BTNode
{
    public ReplyToDispatcherNode() : base("ReplyToDispatcher") {}

    // Timeout for waiting an internal response (seconds). If zero or negative, do not wait.
    public int TimeoutSeconds { get; set; } = 10;
    private readonly System.Collections.Generic.HashSet<string> _registeredConvHandlers = new(System.StringComparer.OrdinalIgnoreCase);
    private bool _subscribedProxy = false;

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null)
        {
            Logger.LogError("ReplyToDispatcher: missing MessagingClient");
            return NodeStatus.Failure;
        }

        // This node acts as a lightweight proxy: subscribe once to the module's response topic
        // and forward any proposals/refusals to the OriginalRequester stored in the blackboard.
        if (!_subscribedProxy)
        {
            var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
            var moduleId = Context.Get<string>("ModuleId") ?? Context.Get<string>("config.Agent.ModuleId") ?? Context.AgentId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                Logger.LogError("ReplyToDispatcher: ModuleId missing, cannot subscribe proxy topic");
                return NodeStatus.Failure;
            }

            var responseTopic = $"/{ns}/{moduleId}/OfferedCapability/Response";
            try
            {
                client.SubscribeAsync(responseTopic).Wait();
                client.OnMessage(async message =>
                {
                    try
                    {
                        if (message == null) return;
                        var t = message.Frame?.Type ?? string.Empty;
                        var baseType = t.Split('/')[0].ToLowerInvariant();
                        if (!baseType.StartsWith("propos") && !baseType.StartsWith("refus")) return;

                        var conv = message.Frame?.ConversationId;
                        if (string.IsNullOrWhiteSpace(conv)) return;

                        var origRequester = Context.Get<string>($"OriginalRequester_{conv}");
                        if (string.IsNullOrWhiteSpace(origRequester))
                        {
                            Logger.LogWarning("ReplyToDispatcherProxy: no OriginalRequester for conv {Conv}, skipping", conv);
                            return;
                        }

                        // Prevent replying to Broadcast
                        if (string.Equals(origRequester, "Broadcast", StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.LogWarning("ReplyToDispatcherProxy: OriginalRequester for conv {Conv} is Broadcast; skipping", conv);
                            return;
                        }

                        var nsLocal = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
                        var outTopic = $"/{nsLocal}/{origRequester}/OfferedCapability/Response";

                        // Ensure receiver fields are set
                        try
                        {
                            message.Frame.Receiver = message.Frame.Receiver ?? new I40Sharp.Messaging.Models.Participant();
                            message.Frame.Receiver.Identification = message.Frame.Receiver.Identification ?? new I40Sharp.Messaging.Models.Identification();
                            message.Frame.Receiver.Identification.Id = origRequester;
                        }
                        catch { }

                        await client.PublishAsync(message, outTopic).ConfigureAwait(false);
                        Logger.LogInformation("ReplyToDispatcherProxy: forwarded conv {Conv} to {Orig} on {Topic}", conv, origRequester, outTopic);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "ReplyToDispatcherProxy: failed to forward message");
                    }
                });
                _subscribedProxy = true;
                Logger.LogInformation("ReplyToDispatcher: proxy subscribed to {Topic}", responseTopic);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "ReplyToDispatcher: failed to subscribe to proxy topic {Topic}", responseTopic);
                return NodeStatus.Failure;
            }
        }

        // This node is a background proxy; no immediate forwarding action required on Execute
        return NodeStatus.Success;
    }
}
