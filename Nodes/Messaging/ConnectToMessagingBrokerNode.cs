using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Transport;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// ConnectToMessagingBroker - Verbindet mit MQTT Broker via I4.0 Messaging
/// Verwendet I40Sharp.Messaging.MessagingClient
/// </summary>
public class ConnectToMessagingBrokerNode : BTNode
{
    public string BrokerHost { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public string DefaultTopic { get; set; } = "factory/agents/messages";
    public int TimeoutMs { get; set; } = 10000;

    public ConnectToMessagingBrokerNode() : base("ConnectToMessagingBroker")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("ConnectToMessagingBroker: Connecting to {Host}:{Port}", BrokerHost, BrokerPort);

        try
        {
            // Prüfe ob bereits verbunden
            var existingClient = Context.Get<MessagingClient>("MessagingClient");
            if (existingClient != null && existingClient.IsConnected)
            {
                Logger.LogDebug("ConnectToMessagingBroker: Already connected, reusing existing client");
                Set("messagingConnected", true);
                return NodeStatus.Success;
            }

            // Erstelle Transport und Client
            var clientId = $"{Context.AgentId}_{Context.AgentRole}";
            var transport = new MqttTransport(BrokerHost, BrokerPort, clientId);
            var client = new MessagingClient(transport, DefaultTopic);

            // Event-Handler registrieren
            client.Connected += (s, e) =>
            {
                Logger.LogInformation("MessagingClient: Connected to MQTT broker");
            };

            client.Disconnected += (s, e) =>
            {
                Logger.LogWarning("MessagingClient: Disconnected from MQTT broker");
            };

            // Global Callback für alle Nachrichten (für Debugging)
            client.OnMessage(msg =>
            {
                Logger.LogDebug("MessagingClient: Received message type {Type} from {Sender}",
                    msg.Frame.Type, msg.Frame.Sender.Identification.Id);
            });

            // Verbinde mit Timeout
            var connectTask = client.ConnectAsync();
            var timeoutTask = Task.Delay(TimeoutMs);
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Logger.LogError("ConnectToMessagingBroker: Connection timeout after {Timeout}ms", TimeoutMs);
                Set("messagingConnected", false);
                return NodeStatus.Failure;
            }

            await connectTask; // Await to catch exceptions

            // Warte kurz auf erfolgreiche Verbindung
            await Task.Delay(500);

            if (!client.IsConnected)
            {
                Logger.LogError("ConnectToMessagingBroker: Client not connected after connect call");
                Set("messagingConnected", false);
                return NodeStatus.Failure;
            }

            // Speichere Client im Context
            Context.Set("MessagingClient", client);
            Set("messagingConnected", true);
            Set("messagingBroker", $"{BrokerHost}:{BrokerPort}");

            Logger.LogInformation("ConnectToMessagingBroker: Successfully connected to {Host}:{Port}",
                BrokerHost, BrokerPort);

            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ConnectToMessagingBroker: Failed to connect to broker");
            Set("messagingConnected", false);
            return NodeStatus.Failure;
        }
    }
}
