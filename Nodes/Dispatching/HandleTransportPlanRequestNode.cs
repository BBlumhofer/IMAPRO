using System;
using System.Threading.Tasks;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using BaSyx.Models.AdminShell;

namespace MAS_BT.Nodes.Dispatching
{
    public class HandleTransportPlanRequestNode : BTNode
    {
        public HandleTransportPlanRequestNode() : base("HandleTransportPlanRequest") { }

        public override async Task<NodeStatus> Execute()
        {
            return await SendSimpleRefusal("/response/TransportPlan", "Transport planning not implemented yet");
        }

        private async Task<NodeStatus> SendSimpleRefusal(string relativeTopic, string reason)
        {
            var client = Context.Get<MessagingClient>("MessagingClient");
            var incoming = Context.Get<I40Message>("LastReceivedMessage");
            if (client == null || incoming == null)
            {
                Logger.LogWarning("HandleTransportPlanRequest: missing client or message");
                return NodeStatus.Failure;
            }

            var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
            var topic = relativeTopic.StartsWith("/") ? $"/{ns}{relativeTopic}" : $"/{ns}/{relativeTopic}";
            var conversationId = incoming.Frame?.ConversationId ?? Guid.NewGuid().ToString();
            var requesterId = incoming.Frame?.Sender?.Identification?.Id ?? "Unknown";

            try
            {
                var builder = new I40MessageBuilder()
                    .From(Context.AgentId, string.IsNullOrWhiteSpace(Context.AgentRole) ? "DispatchingAgent" : Context.AgentRole)
                    .To(requesterId, null)
                    .WithType(I40MessageTypes.REFUSAL)
                    .WithConversationId(conversationId);

                var payload = new Property<string>("Reason")
                {
                    Value = new PropertyValue<string>(reason)
                };
                builder.AddElement(payload);

                var response = builder.Build();
                await client.PublishAsync(response, topic);
                Logger.LogInformation("HandleTransportPlanRequest: sent refusal to {Topic}", topic);
                return NodeStatus.Success;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "HandleTransportPlanRequest: failed to send refusal");
                return NodeStatus.Failure;
            }
        }
    }
}
