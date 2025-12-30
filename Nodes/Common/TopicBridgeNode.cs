using System;
using System.Threading.Tasks;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using MAS_BT.Nodes.Common;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Common
{
    /// <summary>
    /// Minimaler Topic-Bridge Node.
    /// Konfigurationskeys (im Context/config):
    /// - config.TopicBridge.FromTopic
    /// - config.TopicBridge.ToTopic
    /// - config.TopicBridge.AllowedSender (optional)
    ///
    /// Verhalten: abonniert FromTopic, prüft optional Sender-Id und published eingehende Messages unverändert auf ToTopic.
    /// Registrierte Callbacks und Subscription werden beim Reset entfernt.
    /// </summary>
    public class TopicBridgeNode : BTNode
    {
        private string? _fromTopic;
        private string? _toTopic;
        private string? _allowedSender;
        private bool _subscribed;
        private Action<I40Message, string>? _topicCallback;

        public TopicBridgeNode() : base("TopicBridge") { }

        public override async Task<NodeStatus> Execute()
        {
            var client = Context.Get<MessagingClient>("MessagingClient");
            if (client == null || !client.IsConnected)
            {
                Logger.LogWarning("TopicBridge: MessagingClient not available or not connected");
                return NodeStatus.Failure;
            }

            // Read configuration from context
            _fromTopic ??= Context.Get<string>("config.TopicBridge.FromTopic") ?? Context.Get<string>("TopicBridge.FromTopic");
            _toTopic ??= Context.Get<string>("config.TopicBridge.ToTopic") ?? Context.Get<string>("TopicBridge.ToTopic");
            _allowedSender ??= Context.Get<string>("config.TopicBridge.AllowedSender") ?? Context.Get<string>("TopicBridge.AllowedSender");

            if (string.IsNullOrWhiteSpace(_fromTopic) || string.IsNullOrWhiteSpace(_toTopic))
            {
                Logger.LogError("TopicBridge: missing configuration keys FromTopic/ToTopic");
                return NodeStatus.Failure;
            }

            if (!_subscribed)
            {
                _topicCallback = async (msg, topic) =>
                {
                    try
                    {
                        var sender = msg.Frame?.Sender?.Identification?.Id ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(_allowedSender) && !string.Equals(sender, _allowedSender, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.LogDebug("TopicBridge: ignoring message from {Sender} on {Topic}", sender, topic);
                            return;
                        }

                        Logger.LogInformation("TopicBridge: forwarding message type={Type} from={Sender} topic={Topic} -> {ToTopic}", msg.Frame?.Type ?? "<none>", sender, topic, _toTopic);
                        await client.PublishAsync(msg, _toTopic).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "TopicBridge: failed to forward message");
                    }
                };

                try
                {
                    await client.SubscribeAsync(_fromTopic).ConfigureAwait(false);
                    client.OnTopic(_fromTopic, _topicCallback);
                    _subscribed = true;
                    Logger.LogInformation("TopicBridge: subscribed {From} -> {To}", _fromTopic, _toTopic);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "TopicBridge: subscribe failed for {Topic}", _fromTopic);
                    return NodeStatus.Failure;
                }
            }

            return NodeStatus.Running;
        }

        public override Task OnReset()
        {
            try
            {
                var client = Context.Get<MessagingClient>("MessagingClient");
                if (client != null && _subscribed && !string.IsNullOrWhiteSpace(_fromTopic) && _topicCallback != null)
                {
                    try { client.OffTopic(_fromTopic, _topicCallback); } catch { }
                    try { client.UnsubscribeAsync(_fromTopic).GetAwaiter().GetResult(); } catch { }
                }
            }
            catch { }

            _subscribed = false;
            _topicCallback = null;
            _fromTopic = null;
            _toTopic = null;
            _allowedSender = null;
            return Task.CompletedTask;
        }
    }
}
