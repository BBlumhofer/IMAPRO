using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using System.Text.Json;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// SendConfigAsLog - Sendet die geladene Config via I4.0 Messaging als Log
/// </summary>
public class SendConfigAsLogNode : BTNode
{
    public SendConfigAsLogNode() : base("SendConfigAsLog")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        try
        {
            var client = Context.Get<MessagingClient>("MessagingClient");
            if (client == null || !client.IsConnected)
            {
                Logger.LogWarning("SendConfigAsLog: MessagingClient not available, skipping");
                return NodeStatus.Success; // Nicht kritisch
            }

            // Sammle alle Config-Werte aus dem Context
            var configElements = new List<SubmodelElement>();

            // Lese Config-Werte aus Context
            var endpoint = Context.Get<string>("config.OPCUA.Endpoint");
            var username = Context.Get<string>("config.OPCUA.Username");
            var moduleId = Context.Get<string>("config.Agent.ModuleId");

            if (!string.IsNullOrEmpty(endpoint))
            {
                configElements.Add(new Property
                {
                    IdShort = "OPCUA_Endpoint",
                    Value = endpoint,
                    ValueType = "xs:string"
                });
            }

            if (!string.IsNullOrEmpty(username))
            {
                configElements.Add(new Property
                {
                    IdShort = "OPCUA_Username",
                    Value = username,
                    ValueType = "xs:string"
                });
            }

            if (!string.IsNullOrEmpty(moduleId))
            {
                configElements.Add(new Property
                {
                    IdShort = "ModuleId",
                    Value = moduleId,
                    ValueType = "xs:string"
                });
            }

            // Füge Agent-Informationen hinzu
            configElements.Add(new Property
            {
                IdShort = "AgentId",
                Value = Context.AgentId,
                ValueType = "xs:string"
            });

            configElements.Add(new Property
            {
                IdShort = "AgentRole",
                Value = Context.AgentRole,
                ValueType = "xs:string"
            });

            // Erstelle Config-Collection
            var configCollection = new SubmodelElementCollection
            {
                IdShort = "AgentConfiguration",
                Value = configElements
            };

            // Erstelle I4.0 Message
            var message = new I40MessageBuilder()
                .From(Context.AgentId)
                .To("broadcast")
                .WithType("inform")
                .AddElement(new Property
                {
                    IdShort = "MessageType",
                    Value = "ConfigurationReport",
                    ValueType = "xs:string"
                })
                .AddElement(configCollection)
                .Build();

            // Sende Config
            var topic = $"{Context.AgentId}/config";
            await client.PublishAsync(message, topic);

            Logger.LogInformation("SendConfigAsLog: Sent configuration to MQTT topic {Topic}", topic);
            Logger.LogDebug("SendConfigAsLog: Config elements sent: {Count}", configElements.Count);

            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendConfigAsLog: Failed to send configuration");
            return NodeStatus.Failure;
        }
    }
}
