using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// ConnectToMessagingBroker - Establishes connection with MQTT broker
/// </summary>
public class ConnectToMessagingBrokerNode : BTNode
{
    public string Endpoint { get; set; } = string.Empty;
    public string Protocol { get; set; } = "MQTT";
    
    public ConnectToMessagingBrokerNode() : base("ConnectToMessagingBroker")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("ConnectToMessagingBroker: Attempting connection to {Endpoint} via {Protocol}", 
            Endpoint, Protocol);
        
        try
        {
            // TODO: Integration mit I4.0-Sharp-Messaging
            
            // Mockup
            var isConnected = Context.Get<bool?>("MQTT_Connected") ?? false;
            
            if (!isConnected)
            {
                Logger.LogWarning("ConnectToMessagingBroker: Connection to {Endpoint} failed", Endpoint);
                return NodeStatus.Failure;
            }
            
            Context.Set("connected", true);
            Context.Set("messagingEndpoint", Endpoint);
            
            Logger.LogInformation("ConnectToMessagingBroker: Successfully connected to {Endpoint}", Endpoint);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ConnectToMessagingBroker: Error connecting to {Endpoint}", Endpoint);
            return NodeStatus.Failure;
        }
    }
}
