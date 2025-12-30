using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.Messages;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Models;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

/// <summary>
/// Sendet CfP für EIN Requirement (CurrentRequirement) an passende Module
/// </summary>
public class DispatchOneCapabilityRequestNode : BTNode
{
    public DispatchOneCapabilityRequestNode() : base("DispatchOneCapabilityRequest") { }
    
    public override async Task<NodeStatus> Execute()
    {
        var client = Context.Get<MessagingClient>("MessagingClient");
        if (client == null || !client.IsConnected)
        {
            Logger.LogError("DispatchOneCapabilityRequest: MessagingClient unavailable");
            return NodeStatus.Failure;
        }
        
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        if (ctx == null)
        {
            Logger.LogError("DispatchOneCapabilityRequest: negotiation context missing");
            return NodeStatus.Failure;
        }
        
        var requirement = Context.Get<CapabilityRequirement>("CurrentRequirement");
        if (requirement == null)
        {
            Logger.LogError("DispatchOneCapabilityRequest: CurrentRequirement missing");
            return NodeStatus.Failure;
        }
        
        var ns = Context.Get<string>("config.Namespace");
        if (string.IsNullOrWhiteSpace(ns))
        {
            Logger.LogError("DispatchOneCapabilityRequest: missing config.Namespace");
            return NodeStatus.Failure;
        }
        
        var state = Context.Get<DispatchingState>("DispatchingState");
        if (state == null)
        {
            Logger.LogWarning("DispatchOneCapabilityRequest: DispatchingState not found");
            Context.Set("CurrentRequirement.ExpectedResponders", new List<string>());
            return NodeStatus.Success;
        }
        
        // DEBUG: Log state contents
        Logger.LogInformation("DispatchOneCapabilityRequest: State has {ModuleCount} modules: {Modules}", 
            state.Modules.Count, 
            string.Join(", ", state.Modules.Select(m => $"{m.ModuleId}:[{string.Join(",", m.Capabilities ?? new List<string>())}]")));
        
        // Query Neo4j for actual capabilities instead of relying on registration race
        var neo4jClient = Context.Get<IDriver>("Neo4jDriver");
        var moduleCapabilities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        
        if (neo4jClient != null)
        {
            try
            {
                await using var session = neo4jClient.AsyncSession();
                var result = await session.RunAsync(@"
                    MATCH (c:Capability)<-[:PROVIDES_CAPABILITY]-(a:Asset)
                    RETURN a.shell_id as moduleId, collect(c.idShort) as capabilities
                ").ConfigureAwait(false);
                
                await result.ForEachAsync(record =>
                {
                    var moduleId = record["moduleId"].As<string>();
                    var caps = record["capabilities"].As<List<string>>();
                    if (!string.IsNullOrWhiteSpace(moduleId))
                    {
                        moduleCapabilities[moduleId] = caps;
                    }
                }).ConfigureAwait(false);
                
                Logger.LogInformation("DispatchOneCapabilityRequest: Neo4j returned capabilities for {Count} modules", moduleCapabilities.Count);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "DispatchOneCapabilityRequest: Failed to query Neo4j, falling back to state");
            }
        }
        
        var similarityAgentId = Context.Get<string>("config.DispatchingAgent.SimilarityAgentId") ?? "SimilarityAnalysisAgent";
        
        // Finde Module mit passender Capability
        var allModules = state.Modules
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.ModuleId))
            .Where(m => !string.Equals(m.ModuleId, similarityAgentId, StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.Equals(m.ModuleId, Context.AgentId, StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.Equals(m.ModuleId, ns, StringComparison.OrdinalIgnoreCase))
            .Where(m => !m.ModuleId.StartsWith("_", StringComparison.OrdinalIgnoreCase) || m.ModuleId.Length < 2)
            .Where(m => !m.ModuleId.Contains("_Planning", StringComparison.OrdinalIgnoreCase))
            .Where(m => !m.ModuleId.Contains("_Execution", StringComparison.OrdinalIgnoreCase))
            .Where(m => !m.ModuleId.Contains("PlanningHolon", StringComparison.OrdinalIgnoreCase))
            .Where(m => !m.ModuleId.Contains("ExecutionHolon", StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        // Exact match: Module die die Capability haben
        // Prefer Neo4j data over state if available
        var candidateModules = allModules
            .Where(m =>
            {
                // Try Neo4j first
                if (moduleCapabilities.TryGetValue(m.ModuleId, out var neo4jCaps))
                {
                    return neo4jCaps.Contains(requirement.Capability, StringComparer.OrdinalIgnoreCase);
                }
                // Fallback to state
                return m.Capabilities != null && m.Capabilities.Contains(requirement.Capability, StringComparer.OrdinalIgnoreCase);
            })
            .Select(m => m.ModuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        // Retry logic: Wait for modules to register their capabilities
        const int maxRetries = 3;
        const int retryDelayMs = 500;
        int retryCount = 0;
        
        while (candidateModules.Count == 0 && retryCount < maxRetries)
        {
            retryCount++;
            Logger.LogInformation("DispatchOneCapabilityRequest: no modules with capabilities for {Capability}, waiting {Delay}ms (attempt {Attempt}/{Max})",
                requirement.Capability, retryDelayMs, retryCount, maxRetries);
            
            await Task.Delay(retryDelayMs).ConfigureAwait(false);
            
            // Re-fetch state
            state = Context.Get<DispatchingState>("DispatchingState");
            if (state == null) break;
            
            allModules = state.Modules
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.ModuleId))
                .Where(m => !string.Equals(m.ModuleId, similarityAgentId, StringComparison.OrdinalIgnoreCase))
                .Where(m => !string.Equals(m.ModuleId, Context.AgentId, StringComparison.OrdinalIgnoreCase))
                .Where(m => !string.Equals(m.ModuleId, ns, StringComparison.OrdinalIgnoreCase))
                .Where(m => !m.ModuleId.StartsWith("_", StringComparison.OrdinalIgnoreCase) || m.ModuleId.Length < 2)
                .Where(m => !m.ModuleId.Contains("_Planning", StringComparison.OrdinalIgnoreCase))
                .Where(m => !m.ModuleId.Contains("_Execution", StringComparison.OrdinalIgnoreCase))
                .Where(m => !m.ModuleId.Contains("PlanningHolon", StringComparison.OrdinalIgnoreCase))
                .Where(m => !m.ModuleId.Contains("ExecutionHolon", StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            candidateModules = allModules
                .Where(m =>
                {
                    // Try Neo4j first
                    if (moduleCapabilities.TryGetValue(m.ModuleId, out var neo4jCaps))
                    {
                        return neo4jCaps.Contains(requirement.Capability, StringComparer.OrdinalIgnoreCase);
                    }
                    // Fallback to state
                    return m.Capabilities != null && m.Capabilities.Contains(requirement.Capability, StringComparer.OrdinalIgnoreCase);
                })
                .Select(m => m.ModuleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        
        if (candidateModules.Count == 0)
        {
            Logger.LogWarning("DispatchOneCapabilityRequest: no modules with capability {Capability}", requirement.Capability);
            // Trotzdem Success - Collect wird keine Offers bekommen
            Context.Set("CurrentRequirement.ExpectedResponders", new List<string>());
            return NodeStatus.Success;
        }
        
        // Topic
        var topic = $"/{ns}/ModuleHolon/broadcast/OfferedCapability/Request";
        
        // Subtype
        var requestMode = Context.Get<string>("ProcessChain.RequestType") ?? "ManufacturingSequence";
        var subtype = string.Equals(requestMode, "ManufacturingSequence", StringComparison.OrdinalIgnoreCase)
            ? I40MessageTypeSubtypes.ManufacturingSequence
            : I40MessageTypeSubtypes.ProcessChain;
        
        // Capability Description (optional) - aktuell ohne
        string? descReq = null;
        
        // Setze product-basierte ConversationId: ProductId#RequirementId
        if (string.IsNullOrWhiteSpace(ctx.ProductId))
        {
            Logger.LogWarning("DispatchOneCapabilityRequest: ProductId missing in context; falling back to generated id");
        }
        ctx.ConversationId = $"{ctx.ProductId ?? Guid.NewGuid().ToString()}#{requirement.RequirementId}";
        Logger.LogInformation("DispatchOneCapabilityRequest: using ConversationId={Conv}", ctx.ConversationId);

        // Sende CfP an jeden Kandidaten
        foreach (var tgtModule in candidateModules)
        {
            var cfpMessage = new CapabilityCallForProposalMessage(
                Context.AgentId,
                Context.AgentRole,
                tgtModule,
                "ModuleHolon",
                ctx.ConversationId,
                subtype,
                requirement.Capability,
                requirement.RequirementId,
                ctx.ProductId,
                capabilityDescription: descReq,
                capabilityContainer: requirement.CapabilityContainer,
                assetLocation: ctx.AssetLocation);
            
            await cfpMessage.PublishAsync(client, topic).ConfigureAwait(false);
            Logger.LogInformation(
                "DispatchOneCapabilityRequest: CfP sent to {Module} for {Capability} (Requirement={RequirementId}) Topic={Topic}",
                tgtModule, requirement.Capability, requirement.RequirementId, topic);
        }
        
        // Speichere erwartete Responder für CollectNode
        var selfId = Context.AgentId;
        var filteredResponders = candidateModules
            .Where(id => !string.Equals(id, selfId, StringComparison.OrdinalIgnoreCase))
            .Where(id => !string.Equals(id, ns, StringComparison.OrdinalIgnoreCase))
            .Where(id => !string.Equals(id, similarityAgentId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        Context.Set("CurrentRequirement.ExpectedResponders", filteredResponders);
        
        Logger.LogInformation("DispatchOneCapabilityRequest: sent {Count} CfPs for requirement {RequirementId}, expecting responses from {ResponderCount} modules",
            candidateModules.Count, requirement.RequirementId, filteredResponders.Count);
        
        return NodeStatus.Success;
    }
}
