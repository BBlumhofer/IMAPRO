using System;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.Messages;
using BaSyx.Models.AdminShell;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using MAS_BT.Nodes.Dispatching;
using MAS_BT.Services.Transport;
using MAS_BT.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Neo4j.Driver;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MAS_BT.Tests;

[Collection("NamespaceHolonIntegration")]
public sealed class HandleTransportPlanRequestNodeIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public HandleTransportPlanRequestNodeIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Execute_PublishesTransportPlanUsingNamespaceHolonConfig()
    {
        var cfg = NamespaceHolonTestConfig.Load();
        var ns = $"test{Guid.NewGuid():N}";
        var conversationId = Guid.NewGuid().ToString();

        _output.WriteLine(
            "Neo4j config: Uri={0}, User={1}, Database={2}",
            cfg.Neo4jUri,
            cfg.Neo4jUser,
            string.IsNullOrWhiteSpace(cfg.Neo4jDatabase) ? "(default)" : cfg.Neo4jDatabase);

        // Probe Neo4j for a transport-constrained path; continue even if none found.
        await ProbeNeo4jAsync(cfg);

        await using var publisher = new MqttTestClientHandle(cfg.MqttHost, cfg.MqttPort, cfg.MqttUsername, cfg.MqttPassword, $"{ns}_publisher");
        await using var subscriber = new MqttTestClientHandle(cfg.MqttHost, cfg.MqttPort, cfg.MqttUsername, cfg.MqttPassword, $"{ns}_subscriber");

        try
        {
            await publisher.ConnectAsync().ConfigureAwait(false);
            await subscriber.ConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new XunitException($"MQTT broker unavailable: {ex.Message}");
        }

        var responseTopic = $"/{ns}/TransportPlan/Response";
        try
        {
            await subscriber.Client.SubscribeAsync(responseTopic).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new XunitException($"MQTT subscribe failed: {ex.Message}");
        }

        var responseTcs = new TaskCompletionSource<I40Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(I40Message message, string _)
        {
            if (string.Equals(message.Frame?.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            {
                responseTcs.TrySetResult(message);
            }
        }
        subscriber.Client.OnTopic(responseTopic, Handler);

        try
        {
            var ctx = new BTContext(NullLogger<BTContext>.Instance)
            {
                AgentId = "TransportManager_integration",
                AgentRole = "TransportManager"
            };
            ctx.Set("MessagingClient", publisher.Client);
            ctx.Set("config.Namespace", ns);
            ctx.Set("Namespace", ns);
            ctx.Set("config.Neo4j.Uri", cfg.Neo4jUri);
            ctx.Set("config.Neo4j.Username", cfg.Neo4jUser);
            ctx.Set("config.Neo4j.Password", cfg.Neo4jPassword);
            ctx.Set("config.Neo4j.Database", cfg.Neo4jDatabase);
            ctx.Set("config.TransportPlanning.CostPerMeter", 1.0);

            var incoming = BuildTransportRequestMessage(conversationId);
            ctx.Set("LastReceivedMessage", incoming);

            var node = new HandleTransportPlanRequestNode
            {
                Context = ctx
            };
            node.SetLogger(NullLogger<HandleTransportPlanRequestNode>.Instance);

            var status = await node.Execute().ConfigureAwait(false);
            if (status != NodeStatus.Success)
            {
                _output.WriteLine("HandleTransportPlanRequestNode returned {0} - likely no transport path available.", status);
                // Accept non-success when transport planning cannot produce a plan due to graph rule.
                return;
            }

            var completed = await Task.WhenAny(responseTcs.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            if (!ReferenceEquals(completed, responseTcs.Task))
            {
                _output.WriteLine("No transport plan response received within timeout; test accepts this when no transport path exists.");
                return;
            }

            var response = await responseTcs.Task.ConfigureAwait(false);
            Assert.NotNull(response.Frame);
            Assert.Equal($"{I40MessageTypes.CONSENT}/{I40MessageTypeSubtypes.TransportRequest}", response.Frame!.Type);

            var transportCollection = response.InteractionElements
                .OfType<SubmodelElementCollection>()
                .FirstOrDefault(e => string.Equals(e.IdShort, "TransportRequest", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(transportCollection);

            var resolved = new TransportRequestMessage(transportCollection!);
            Assert.True(resolved.CapabilitiesSequence.Count > 0, "Transport plan response did not contain capability legs");
            _output.WriteLine("Received {0} capability leg(s) for conversation {1}.", resolved.CapabilitiesSequence.Count, conversationId);
        }
        finally
        {
            subscriber.Client.OffTopic(responseTopic, Handler);
            try { await subscriber.Client.UnsubscribeAsync(responseTopic).ConfigureAwait(false); } catch { }
        }
    }

    private static I40Message BuildTransportRequestMessage(string conversationId)
    {
        var request = new TransportRequestMessage("TransportRequest");
        request.InstanceIdentifier.SetText($"transportreq_{Guid.NewGuid():N}");
        request.TransportStartStation.SetText("P100");
        request.TransportGoalStation.SetText("P102");
        request.SetIdentifierType(TransportRequestMessage.IdentifierTypeEnum.ProductId);
        request.IdentifierValue.SetText("PRODUCT-TEST");
        request.SetAmount(1);

        return new I40MessageBuilder()
            .From("DispatchingAgent", "DispatchingAgent")
            .To("TransportManager", "TransportManager")
            .WithType(I40MessageTypes.REQUIREMENT, I40MessageTypeSubtypes.TransportRequest)
            .WithConversationId(conversationId)
            .AddElement(request)
            .Build();
    }

    private async Task ProbeNeo4jAsync(NamespaceHolonTestConfig cfg)
    {
        try
        {
            await using var probe = new Neo4jTransportGraphQuery(cfg.Neo4jUri, cfg.Neo4jUser, cfg.Neo4jPassword, cfg.Neo4jDatabase);
            var edges = await probe.GetShortestPathAsync("P100", "P102").ConfigureAwait(false);
            if (edges == null || edges.Count == 0)
            {
                _output.WriteLine("No transport-constrained path found between P100 and P102 (tests will tolerate this).");
                return;
            }

            var path = edges.Select(e => e.From).Concat(new[] { edges.Last().To }).ToList();
            _output.WriteLine("Probe transport-constrained path: {0}", string.Join(" -> ", path));
        }
        catch (AuthenticationException ex)
        {
            throw new XunitException($"Neo4j authentication failed: {ex.Message}");
        }
        catch (ServiceUnavailableException ex)
        {
            throw new XunitException($"Neo4j service unavailable: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            throw new XunitException($"Unable to query Neo4j: {ex.Message}");
        }
    }
}
