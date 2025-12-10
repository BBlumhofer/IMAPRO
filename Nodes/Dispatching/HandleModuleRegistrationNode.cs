using System;
using AasSharpClient.Messages;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging.Models;

namespace MAS_BT.Nodes.Dispatching
{
    public class HandleModuleRegistrationNode : BTNode
    {
        public HandleModuleRegistrationNode() : base("HandleModuleRegistration") { }

        public override Task<NodeStatus> Execute()
        {
            var state = Context.Get<DispatchingState>("DispatchingState") ?? new DispatchingState();
            Context.Set("DispatchingState", state);

            var message = Context.Get<I40Message>("LastReceivedMessage");
            if (message == null)
            {
                Logger.LogWarning("HandleModuleRegistration: no message in context");
                return Task.FromResult(NodeStatus.Failure);
            }

            var moduleId = message.Frame?.Sender?.Identification?.Id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                moduleId = message.Frame?.Receiver?.Identification?.Id ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(moduleId))
            {
                Logger.LogWarning("HandleModuleRegistration: unable to extract ModuleId from message");
                return Task.FromResult(NodeStatus.Failure);
            }

            var info = new DispatchingModuleInfo
            {
                ModuleId = moduleId,
                LastRegistrationUtc = DateTime.UtcNow
            };

            state.Upsert(info);
            Context.Set("LastRegisteredModuleId", moduleId);
            Logger.LogInformation("HandleModuleRegistration: registered/updated module {ModuleId}", moduleId);
            return Task.FromResult(NodeStatus.Success);
        }
    }
}
