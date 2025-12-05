using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// SendMessage - Sends a generic message to another agent or broadcast
/// </summary>
public class SendMessageNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    public object? Payload { get; set; }
    public string MessageType { get; set; } = "StatusUpdate";
    
    public SendMessageNode() : base("SendMessage")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("SendMessage: Sending {MessageType} to {AgentId}", MessageType, AgentId);
        
        try
        {
            // TODO: Integration mit I4.0-Sharp-Messaging
            // var messagingClient = Context.Get<MessagingClient>("MessagingClient");
            // await messagingClient.PublishAsync(topic, payload);
            
            // Mockup
            var message = new
            {
                From = Context.AgentId,
                To = AgentId,
                Type = MessageType,
                Payload = Payload,
                Timestamp = DateTime.UtcNow
            };
            
            Context.Set("sent", true);
            Context.Set("lastMessage", message);
            
            Logger.LogInformation("SendMessage: Sent {MessageType} to {AgentId}", MessageType, AgentId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendMessage: Error sending message to {AgentId}", AgentId);
            return NodeStatus.Failure;
        }
    }
}
