using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using BaSyx.Models.AdminShell;

namespace MAS_BT.Nodes.Dispatching;

public class DispatchingModuleInfo
{
    public string ModuleId { get; set; } = string.Empty;
    public string? AasId { get; set; }
    public List<string> Capabilities { get; set; } = new();
    public List<string> Neighbors { get; set; } = new();
    public DateTime LastRegistrationUtc { get; set; } = DateTime.UtcNow;
}

public class DispatchingState
{
    private readonly Dictionary<string, DispatchingModuleInfo> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _capabilityIndex = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<DispatchingModuleInfo> Modules => _modules.Values;

    public void Upsert(DispatchingModuleInfo module)
    {
        if (string.IsNullOrWhiteSpace(module.ModuleId))
        {
            return;
        }

        if (_modules.TryGetValue(module.ModuleId, out var existing))
        {
            RemoveFromIndex(existing);
        }

        _modules[module.ModuleId] = module;
        AddToIndex(module);
    }

    public IReadOnlyCollection<string> FindModulesForCapability(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
        {
            return _modules.Keys.ToList();
        }

        if (_capabilityIndex.TryGetValue(capability, out var set))
        {
            return set.ToList();
        }

        return Array.Empty<string>();
    }

    public IReadOnlyCollection<string> AllModuleIds() => _modules.Keys.ToList();

    private void AddToIndex(DispatchingModuleInfo module)
    {
        foreach (var cap in module.Capabilities.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (!_capabilityIndex.TryGetValue(cap, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _capabilityIndex[cap] = set;
            }
            set.Add(module.ModuleId);
        }
    }

    private void RemoveFromIndex(DispatchingModuleInfo module)
    {
        foreach (var cap in module.Capabilities.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (_capabilityIndex.TryGetValue(cap, out var set))
            {
                set.Remove(module.ModuleId);
                if (set.Count == 0)
                {
                    _capabilityIndex.Remove(cap);
                }
            }
        }
    }
}

/// <summary>
/// Ensures a dispatching state is available in the BT context and pre-populated from config.
/// </summary>
public class InitializeDispatchingStateNode : BTNode
{
    public InitializeDispatchingStateNode() : base("InitializeDispatchingState") { }

    public override Task<NodeStatus> Execute()
    {
        var state = Context.Get<DispatchingState>("DispatchingState") ?? new DispatchingState();
        Context.Set("DispatchingState", state);

        try
        {
            var modulesElement = Context.Get<JsonElement>("config.DispatchingAgent.Modules");
            if (modulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var moduleElem in modulesElement.EnumerateArray())
                {
                    var module = ParseModule(moduleElem);
                    if (module != null)
                    {
                        state.Upsert(module);
                    }
                }
            }

            Logger.LogInformation("InitializeDispatchingState: loaded {Count} modules from config", state.Modules.Count);
            return Task.FromResult(NodeStatus.Success);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "InitializeDispatchingState: failed to load modules from config");
            return Task.FromResult(NodeStatus.Failure);
        }
    }

    private DispatchingModuleInfo? ParseModule(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var moduleId = element.TryGetProperty("ModuleId", out var idElem) && idElem.ValueKind == JsonValueKind.String
            ? idElem.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(moduleId))
            return null;

        var info = new DispatchingModuleInfo
        {
            ModuleId = moduleId!,
            AasId = element.TryGetProperty("AasId", out var aasElem) && aasElem.ValueKind == JsonValueKind.String
                ? aasElem.GetString()
                : null,
            LastRegistrationUtc = DateTime.UtcNow
        };

        if (element.TryGetProperty("Capabilities", out var capsElem) && capsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var cap in capsElem.EnumerateArray())
            {
                if (cap.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cap.GetString()))
                {
                    info.Capabilities.Add(cap.GetString()!);
                }
            }
        }

        if (element.TryGetProperty("Neighbors", out var neighElem) && neighElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var neighbor in neighElem.EnumerateArray())
            {
                if (neighbor.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(neighbor.GetString()))
                {
                    info.Neighbors.Add(neighbor.GetString()!);
                }
            }
        }

        return info;
    }
}

/// <summary>
/// Subscribes to the relevant MQTT topics for dispatching services.
/// </summary>
public class SubscribeDispatchingTopicsNode : BTNode
{
    public SubscribeDispatchingTopicsNode() : base("SubscribeDispatchingTopics") { }

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("SubscribeDispatchingTopics: MessagingClient unavailable or disconnected");
            return NodeStatus.Failure;
        }

        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"/{ns}/request/ProcessChain",
            $"/{ns}/request/ManufacturingSequence",
            $"/{ns}/request/BookStep",
            $"/{ns}/request/TransportPlan",
            $"/DispatchingAgent/{ns}/RequestProcessChain/",
            $"/DispatchingAgent/{ns}/RequestManufacturingSequence/",
            $"/DispatchingAgent/{ns}/BookStep/",
            $"/DispatchingAgent/{ns}/RequestTransportPlan/",
            $"/DispatchingAgent/{ns}/ModuleRegistration/"
        };

        var successCount = 0;
        foreach (var topic in topics)
        {
            try
            {
                await client.SubscribeAsync(topic);
                successCount++;
                Logger.LogInformation("SubscribeDispatchingTopics: subscribed to {Topic}", topic);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "SubscribeDispatchingTopics: failed to subscribe {Topic}", topic);
            }
        }

        return successCount > 0 ? NodeStatus.Success : NodeStatus.Failure;
    }
}

/// <summary>
/// Handles module registration messages and updates the in-memory registry.
/// </summary>
public class HandleModuleRegistrationNode : BTNode
{
    public HandleModuleRegistrationNode() : base("HandleModuleRegistration") { }

    public override Task<NodeStatus> Execute()
    {
        var state = Context.Get<DispatchingState>("DispatchingState") ?? new DispatchingState();
        Context.Set("DispatchingState", state);

        var message = Context.Get<I40Sharp.Messaging.Models.I40Message>("LastReceivedMessage");
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

/// <summary>
/// Responds to ProcessChain requests with a simple candidate list derived from the registry.
/// </summary>
public class HandleProcessChainRequestNode : BTNode
{
    public HandleProcessChainRequestNode() : base("HandleProcessChainRequest") { }

    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("HandleProcessChainRequest: MessagingClient unavailable");
            return NodeStatus.Failure;
        }

        var state = Context.Get<DispatchingState>("DispatchingState") ?? new DispatchingState();
        Context.Set("DispatchingState", state);

        var incoming = Context.Get<I40Sharp.Messaging.Models.I40Message>("LastReceivedMessage");
        if (incoming == null)
        {
            Logger.LogWarning("HandleProcessChainRequest: no incoming message");
            return NodeStatus.Failure;
        }

        var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
        var conversationId = incoming.Frame?.ConversationId ?? Guid.NewGuid().ToString();
        var requesterId = incoming.Frame?.Sender?.Identification?.Id ?? "Unknown";
        Context.Set("ConversationId", conversationId);

        var requestedCaps = ExtractRequestedCapabilities(incoming).ToList();
        if (requestedCaps.Count == 0)
        {
            requestedCaps = state.Modules.SelectMany(m => m.Capabilities).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        if (requestedCaps.Count == 0)
        {
            requestedCaps.Add("GenericCapability");
        }

        var steps = new List<ProcessChainStepDto>();
        var hasCandidates = true;

        foreach (var cap in requestedCaps)
        {
            var candidates = state.FindModulesForCapability(cap).ToList();
            if (candidates.Count == 0)
            {
                hasCandidates = false;
            }
            steps.Add(new ProcessChainStepDto
            {
                Capability = cap,
                CandidateModules = candidates
            });
        }

        var responseDto = new ProcessChainProposalDto
        {
            ProcessChainId = conversationId,
            Steps = steps
        };

        var messageType = hasCandidates ? I40MessageTypes.PROPOSAL : I40MessageTypes.REFUSE_PROPOSAL;
        var responseTopic = $"/{ns}/response/ProcessChain";

        try
        {
            var builder = new I40MessageBuilder()
                .From(Context.AgentId, string.IsNullOrWhiteSpace(Context.AgentRole) ? "DispatchingAgent" : Context.AgentRole)
                .To(requesterId, null)
                .WithType(messageType)
                .WithConversationId(conversationId);

            var serialized = JsonSerializer.Serialize(responseDto);
            var payload = new Property<string>("ProcessChain")
            {
                Value = new PropertyValue<string>(serialized)
            };
            builder.AddElement(payload);

            var response = builder.Build();
            await client.PublishAsync(response, responseTopic);
            Logger.LogInformation("HandleProcessChainRequest: sent {Type} with {StepCount} steps to {Topic}", messageType, steps.Count, responseTopic);
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "HandleProcessChainRequest: failed to publish response");
            return NodeStatus.Failure;
        }
    }

    private IEnumerable<string> ExtractRequestedCapabilities(I40Sharp.Messaging.Models.I40Message message)
    {
        if (message?.InteractionElements == null)
            yield break;

        foreach (var element in message.InteractionElements)
        {
            if (element is Property<string> strProp && !string.IsNullOrWhiteSpace(strProp.Value?.Value))
            {
                yield return strProp.Value.Value!;
            }
            else if (element is SubmodelElementCollection coll)
            {
                foreach (var child in coll.Values)
                {
                    if (child is Property<string> childProp && !string.IsNullOrWhiteSpace(childProp.Value?.Value))
                    {
                        yield return childProp.Value.Value!;
                    }
                }
            }
        }
    }

    private class ProcessChainProposalDto
    {
        public string ProcessChainId { get; set; } = string.Empty;
        public List<ProcessChainStepDto> Steps { get; set; } = new();
    }

    private class ProcessChainStepDto
    {
        public string Capability { get; set; } = string.Empty;
        public List<string> CandidateModules { get; set; } = new();
    }
}

/// <summary>
/// Stub implementation: respond to manufacturing sequence requests with a refusal until scheduling is implemented.
/// </summary>
public class HandleManufacturingSequenceRequestNode : BTNode
{
    public HandleManufacturingSequenceRequestNode() : base("HandleManufacturingSequenceRequest") { }

    public override async Task<NodeStatus> Execute()
    {
        return await SendSimpleRefusal("/response/ManufacturingSequence", "ManufacturingSequence not implemented yet");
    }

    private async Task<NodeStatus> SendSimpleRefusal(string relativeTopic, string reason)
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        var incoming = Context.Get<I40Sharp.Messaging.Models.I40Message>("LastReceivedMessage");
        if (client == null || incoming == null)
        {
            Logger.LogWarning("HandleManufacturingSequenceRequest: missing client or message");
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
            Logger.LogInformation("HandleManufacturingSequenceRequest: sent refusal to {Topic}", topic);
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "HandleManufacturingSequenceRequest: failed to send refusal");
            return NodeStatus.Failure;
        }
    }
}

/// <summary>
/// Stub: reply to BookStep with refusal until booking workflow is available.
/// </summary>
public class HandleBookStepRequestNode : BTNode
{
    public HandleBookStepRequestNode() : base("HandleBookStepRequest") { }

    public override async Task<NodeStatus> Execute()
    {
        return await SendSimpleRefusal("/response/BookStep", "BookStep not implemented yet");
    }

    private async Task<NodeStatus> SendSimpleRefusal(string relativeTopic, string reason)
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        var incoming = Context.Get<I40Sharp.Messaging.Models.I40Message>("LastReceivedMessage");
        if (client == null || incoming == null)
        {
            Logger.LogWarning("HandleBookStepRequest: missing client or message");
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
            Logger.LogInformation("HandleBookStepRequest: sent refusal to {Topic}", topic);
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "HandleBookStepRequest: failed to send refusal");
            return NodeStatus.Failure;
        }
    }
}

/// <summary>
/// Stub: reply to transport plan requests with refusal until routing is provided.
/// </summary>
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
        var incoming = Context.Get<I40Sharp.Messaging.Models.I40Message>("LastReceivedMessage");
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
