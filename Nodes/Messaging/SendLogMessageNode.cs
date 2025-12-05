using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// SendLogMessage - Sendet Log-Nachrichten via I4.0 Messaging
/// </summary>
public class SendLogMessageNode : BTNode
{
    public string LogLevel { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty; // Optional, default wird verwendet

    public SendLogMessageNode() : base("SendLogMessage")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        try
        {
            var client = Context.Get<MessagingClient>("MessagingClient");
            if (client == null || !client.IsConnected)
            {
                Logger.LogWarning("SendLogMessage: MessagingClient not available, skipping log message");
                return NodeStatus.Success; // Nicht kritisch, also Success
            }

            // Erstelle I4.0 Message mit Log-Informationen
            var logMessage = new I40MessageBuilder()
                .From(Context.AgentId)
                .To("broadcast") // Broadcast an alle
                .WithType("inform") // I4.0 MessageType für Informationen
                .AddElement(new Property
                {
                    IdShort = "LogLevel",
                    Value = LogLevel,
                    ValueType = "xs:string"
                })
                .AddElement(new Property
                {
                    IdShort = "Message",
                    Value = Message,
                    ValueType = "xs:string"
                })
                .AddElement(new Property
                {
                    IdShort = "Timestamp",
                    Value = DateTime.UtcNow.ToString("o"),
                    ValueType = "xs:dateTime"
                })
                .AddElement(new Property
                {
                    IdShort = "AgentRole",
                    Value = Context.AgentRole,
                    ValueType = "xs:string"
                })
                .Build();

            // Sende über angegebenes Topic oder Default-Topic
            var topic = !string.IsNullOrEmpty(Topic) ? Topic : $"{Context.AgentId}/logs";
            await client.PublishAsync(logMessage, topic);

            Logger.LogDebug("SendLogMessage: Sent log message via MQTT to {Topic}", topic);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendLogMessage: Failed to send log message");
            return NodeStatus.Failure;
        }
    }
}
