using Microsoft.Extensions.Logging;
using MAS_BT.Core;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using AasSharpClient.Models;

namespace MAS_BT.Nodes.Messaging;

/// <summary>
/// SendStateMessage - Sendet Modulzustände via MQTT
/// Topic: /Modules/{ModuleID}/State/
/// Verwendet ModuleState aus AAS-Sharp-Client
/// </summary>
public class SendStateMessageNode : BTNode
{
    public string ModuleId { get; set; } = "";
    public bool IncludeModuleLocked { get; set; } = true;
    public bool IncludeModuleReady { get; set; } = true;

    private bool _hasPublishedState;
    private (bool Locked, bool Ready, bool Error) _lastState;
    
    public SendStateMessageNode() : base("SendStateMessage")
    {
    }
    
    public SendStateMessageNode(string name) : base(name)
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("SendStateMessage: Evaluating state for module '{ModuleId}'", ModuleId);
        
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null)
        {
            Logger.LogError("SendStateMessage: MessagingClient not found in context");
            return NodeStatus.Failure;
        }
        
        try
        {
            var moduleName = Context.Get<string>("config.Agent.ModuleName") ?? "UnknownModule";
            
            // Hole State-Werte aus Context
            var isLocked = IncludeModuleLocked && Context.Get<bool>($"{moduleName}_Locked");
            var isReady = IncludeModuleReady && Context.Get<bool>($"{moduleName}_Ready");
            var hasError = Context.Get<bool>($"{moduleName}_HasError");

            var currentState = (Locked: isLocked, Ready: isReady, Error: hasError);
            var shouldPublish = !_hasPublishedState || !StatesEqual(_lastState, currentState);

            if (!shouldPublish)
            {
                Logger.LogDebug("SendStateMessage: State unchanged for module '{ModuleId}', skipping MQTT publish", ModuleId);
                return NodeStatus.Success;
            }
            
            // Erstelle ModuleState (AAS-Sharp-Client Klasse)
            var moduleState = new ModuleState(isLocked, isReady, hasError);
            
            // Erstelle I4.0 Message mit ModuleState
            var message = new I40MessageBuilder()
                .From($"{ModuleId}_Execution_Agent", "ExecutionAgent")
                .To("Broadcast", "System")
                .WithType(I40MessageTypes.INFORM)
                .AddElement(moduleState)
                .Build();
            
            var topic = $"/Modules/{ModuleId}/State/";
            await client.PublishAsync(message, topic);
            
            Logger.LogInformation("SendStateMessage: Published module state (Locked={Locked}, Ready={Ready}, Error={Error}) to topic '{Topic}'", 
                isLocked, isReady, hasError, topic);

            _lastState = currentState;
            _hasPublishedState = true;
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendStateMessage: Failed to send state message");
            return NodeStatus.Failure;
        }
    }
    
    public override Task OnAbort()
    {
        _hasPublishedState = false;
        return Task.CompletedTask;
    }
    
    public override Task OnReset()
    {
        _hasPublishedState = false;
        return Task.CompletedTask;
    }

    private static bool StatesEqual((bool Locked, bool Ready, bool Error) left, (bool Locked, bool Ready, bool Error) right)
        => left.Locked == right.Locked && left.Ready == right.Ready && left.Error == right.Error;
}
