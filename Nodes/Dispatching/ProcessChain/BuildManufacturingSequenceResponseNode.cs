using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models.ManufacturingSequence;
using BaSyx.Models.AdminShell;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

public class BuildManufacturingSequenceResponseNode : BTNode
{
    public BuildManufacturingSequenceResponseNode() : base("BuildManufacturingSequenceResponse") { }

    public override Task<NodeStatus> Execute()
    {
        var negotiation = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        if (negotiation == null)
        {
            Logger.LogError("BuildManufacturingSequenceResponse: negotiation context missing");
            return Task.FromResult(NodeStatus.Failure);
        }

        Logger.LogInformation("Valid Offers received start building ManufacturingSequence Offer Response");

        var sequence = BuildManufacturingSequenceModel(negotiation);
        var success = negotiation.HasCompleteProcessChain;

        // Keep ProcessChain keys for backward compatibility and set manufacturing-specific keys
        Context.Set("ProcessChain.Result", sequence);
        Context.Set("ProcessChain.Success", success);
        Context.Set("ManufacturingSequence.Result", sequence);
        Context.Set("ManufacturingSequence.Success", success);

        Logger.LogInformation("BuildManufacturingSequenceResponse: built ManufacturingSequence with {Count} requirements (success={Success})",
            negotiation.Requirements.Count,
            success);

        return Task.FromResult(NodeStatus.Success);
    }

    private ManufacturingSequence BuildManufacturingSequenceModel(ProcessChainNegotiationContext negotiation)
    {
        var sequence = new ManufacturingSequence();
        var requirementIndex = 0;
        foreach (var requirement in negotiation.Requirements)
        {
            var requiredCapability = new ManufacturingRequiredCapability($"RequiredCapability_{++requirementIndex}");
            requiredCapability.SetInstanceIdentifier(requirement.RequirementId);
            var requestedReference = CloneReference(requirement.RequestedCapabilityReference);
            if (requestedReference != null)
            {
                requiredCapability.SetRequiredCapabilityReference(requestedReference);
            }

            if (requirement.OfferedCapabilitySequences.Count > 0)
            {
                foreach (var offeredSequence in requirement.OfferedCapabilitySequences)
                {
                    requiredCapability.AddSequence(offeredSequence);
                }
            }
            else if (requirement.CapabilityOffers.Count > 0)
            {
                var fallback = new ManufacturingOfferedCapabilitySequence();
                foreach (var offer in requirement.CapabilityOffers)
                {
                    fallback.AddCapability(offer);
                }
                requiredCapability.AddSequence(fallback);
            }

            sequence.AddRequiredCapability(requiredCapability);
        }

        return sequence;
    }

    private static Reference? CloneReference(Reference? source)
    {
        if (source == null)
        {
            return null;
        }

        var keys = source.Keys?
            .Select(k => (IKey)new Key(k.Type, k.Value))
            .ToList();

        if (keys == null || keys.Count == 0)
        {
            return null;
        }

        return new Reference(keys)
        {
            Type = source.Type
        };
    }
}
