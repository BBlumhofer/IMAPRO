using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using BaSyx.Models.AdminShell;
using AasSharpClient.Models.Messages;
using AasSharpClient.Models;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// SendLogMessage - Sendet Log-Nachrichten via I4.0 Messaging
/// </summary>
public class SendLogMessageNode : BTNode
{
    public string LogLevel { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty; // Optional, default wird verwendet

    public SendLogMessageNode() : base("SendLogMessage")
    {
    }

    public override async Task<NodeStatus> Execute()
    {
        Logger.LogInformation("SendLogMessage: Sending log message '{Message}' (Level: {LogLevel})", Message, LogLevel);
        
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null)
        {
            Logger.LogWarning("SendLogMessage: MessagingClient not found in context, skipping MQTT publish");
            // Gebe trotzdem Success zurück, damit der Tree weiterläuft
            return NodeStatus.Success;
        }
        
        try
        {
            // Verwende ModuleId aus Parameter oder Context
            var moduleId = ModuleId;
            if (string.IsNullOrEmpty(moduleId))
            {
                moduleId = Context.Get<string>("ModuleId") ?? "UnknownModule";
            }

            // Erstelle I4.0 Log Message
            var messageBuilder = new I40MessageBuilder()
                .From($"{moduleId}_Execution_Agent", "ExecutionAgent")
                .To("Broadcast", "System")
                .WithType(I40MessageTypes.INFORM);
            
            // Bereite Rohwerte als Strings vor und logge sie (vor Erzeugung der AAS-Properties)
            var rawLogLevel = string.IsNullOrWhiteSpace(LogLevel) ? "INFO" : LogLevel;
            var rawMessage = Message ?? string.Empty;
            var rawAgentRole = "ExecutionAgent";
            var rawAgentState = moduleId ?? string.Empty;

            Logger.LogInformation("SendLogMessage: Raw values before AAS creation -> LogLevel='{LogLevel}', Message='{Message}', AgentRole='{AgentRole}', AgentState='{AgentState}'",
                rawLogLevel, rawMessage, rawAgentRole, rawAgentState);

            // Erstelle die AAS-konforme SubmodelElementCollection (wie im AAS-Sharp-Client)
            var logCollection = new LogMessage(rawLogLevel, rawMessage, rawAgentRole, rawAgentState);
            messageBuilder.AddElement(logCollection);

            var logMessage = messageBuilder.Build();

            // DEBUG: Logge die Property-Values und die serialisierte Nachricht vor dem Senden
            try
            {
                // Einzelne Property-Werte prüfen
                Logger.LogInformation("SendLogMessage: Prepared properties:");
                var collection = logMessage.InteractionElements.OfType<SubmodelElementCollection>().FirstOrDefault(e => e.IdShort == "Log");
                if (collection != null)
                {
                    foreach (var elem in collection.Value.Value.OfType<IProperty>())
                    {
                        var val = elem.Value?.Value?.ToObject<string>();
                        Logger.LogInformation("  - {IdShort}: {Value}", elem.IdShort, val);
                    }
                }
                else
                {
                    Logger.LogWarning("SendLogMessage: Log collection not found after message build");
                }

                // Serialisierte Nachricht (JSON) ausgeben
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    };
                    options.Converters.Add(new BaSyx.Models.Extensions.FullSubmodelElementConverter(new BaSyx.Models.Extensions.ConverterOptions()));
                    options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

                    var json = System.Text.Json.JsonSerializer.Serialize(logMessage, options);
                    Logger.LogInformation("SendLogMessage: Serialized message:\n{Json}", json);
                }
                catch (Exception se)
                {
                    Logger.LogWarning(se, "SendLogMessage: Failed to serialize message for debug output");
                }
            }
            catch { /* ensure logging never breaks sending */ }

            // Sende über angegebenes Topic oder Default-Topic
            var topic = !string.IsNullOrEmpty(Topic) ? Topic : $"/Modules/{moduleId}/Logs/";
            await client.PublishAsync(logMessage, topic);

            Logger.LogInformation("SendLogMessage: Sent log message to MQTT topic '{Topic}'", topic);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendLogMessage: Failed to send log message");
            return NodeStatus.Failure;
        }
    }
    
    // Message construction is done via AAS-Sharp-Client message factories.
    
    private static string GetLogLevelName(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        return logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Trace => "TRACE",
            Microsoft.Extensions.Logging.LogLevel.Debug => "DEBUG",
            Microsoft.Extensions.Logging.LogLevel.Information => "INFO",
            Microsoft.Extensions.Logging.LogLevel.Warning => "WARNING",
            Microsoft.Extensions.Logging.LogLevel.Error => "ERROR",
            Microsoft.Extensions.Logging.LogLevel.Critical => "CRITICAL",
            _ => "NONE"
        };
    }
}
