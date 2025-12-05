using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// WaitForMessage - Wartet auf eine Nachricht eines bestimmten Typs
/// </summary>
public class WaitForMessageNode : BTNode
{
    public string MessageType { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public int TimeoutMs { get; set; } = 30000;
    
    public WaitForMessageNode() : base("WaitForMessage")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("WaitForMessage: Waiting for {MessageType} from {AgentId}", MessageType, AgentId);
        
        try
        {
            // TODO: Integration mit I4.0-Sharp-Messaging
            // var messagingClient = Context.Get<MessagingClient>("MessagingClient");
            // await messagingClient.WaitForMessageAsync(MessageType, TimeoutMs);
            
            // Mockup: Simuliere Wartezeit
            await Task.Delay(100);
            
            Logger.LogInformation("WaitForMessage: Received {MessageType}", MessageType);
            
            Context.Set("messageReceived", true);
            Context.Set("lastReceivedMessage", new { Type = MessageType, From = AgentId });
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "WaitForMessage: Error waiting for message");
            Context.Set("messageReceived", false);
            return NodeStatus.Failure;
        }
    }
}
