using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models;
using AasSharpClient.Tools;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.ProcessChain;
using BaSyx.Models.AdminShell;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

/// <summary>
/// Aktualisiert AssetLocation für nächstes Requirement basierend auf aktuellem Offer
/// </summary>
public class UpdateAssetLocationNode : BTNode
{
    public UpdateAssetLocationNode() : base("UpdateAssetLocation") { }
    
    public override Task<NodeStatus> Execute()
    {
        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        var currentRequirement = Context.Get<CapabilityRequirement>("CurrentRequirement");

        if (ctx == null || currentRequirement == null)
        {
            Logger.LogWarning("UpdateAssetLocation: context or requirement missing");
            return Task.FromResult(NodeStatus.Success); // Nicht kritisch
        }

        var requestType = Context.Get<string>("ProcessChain.RequestType");
        if (!string.Equals(requestType, "ManufacturingSequence", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogDebug("UpdateAssetLocation: skipping for request type {Type}", requestType ?? "<unknown>");
            return Task.FromResult(NodeStatus.Success);
        }

        var selectedOffer = Context.Get<OfferedCapability>("ProcessChain.SelectedOffer")
                            ?? SelectPreferredOffer(currentRequirement);
        if (selectedOffer == null)
        {
            Logger.LogDebug("UpdateAssetLocation: no offer available for requirement {RequirementId}",
                currentRequirement.RequirementId);
            return Task.FromResult(NodeStatus.Success);
        }

        // Use the AssetLocation provided together with the CfP and stored in the negotiation context.
        SubmodelElementCollection? assetLocation = ctx.AssetLocation;
        if (assetLocation == null)
        {
            Logger.LogDebug("UpdateAssetLocation: no AssetLocation present in negotiation context for requirement {RequirementId}", currentRequirement.RequirementId);
            return Task.FromResult(NodeStatus.Success);
        }

        ctx.AssetLocation = assetLocation;
        // Parse the SubmodelElementCollection into the strongly-typed AssetLocationData
        try
        {
            var parsed = ParseAssetLocationData(assetLocation);
            if (parsed != null)
            {
                var stationVal = string.Empty;
                try { stationVal = AasValueUnwrap.UnwrapToString(selectedOffer?.Station?.Value) ?? string.Empty; } catch { }
                parsed = parsed with { Parent = stationVal };
                Context.Set("ProcessChain.AssetLocationData", parsed);

                // Also create a typed Submodel wrapper for convenience. Use JsonLoader to normalize/round-trip
                try
                {
                    // Serialize the raw SMC via JsonLoader and deserialize to ensure compatibility
                    var json = JsonLoader.SerializeElement(assetLocation, indented: false);
                    var des = JsonLoader.DeserializeElement(json);
                    var smc = des as SubmodelElementCollection ?? assetLocation;
                    var sm = AssetLocation.FromCollection(smc);
                    // write both typed and submodel/backing representations to the blackboard
                    Context.Set("ProcessChain.AssetLocationSubmodel", sm);
                    Context.Set("ProcessChain.AssetLocation", sm);
                    // update negotiation raw SMC as well so downstream nodes see the modified values
                    ctx.AssetLocation = smc;

                    // set Parent on typed AssetLocation
                    try
                    {
                        if (!string.IsNullOrEmpty(stationVal))
                        {
                            sm.Parent.Value = new BaSyx.Models.AdminShell.PropertyValue<string>(stationVal);
                        }
                    }
                    catch { }

                    // set Parent on raw smc (find existing Property or create one)
                    try
                    {
                        var parentProp = smc.Values?.OfType<Property>()
                            .FirstOrDefault(p => string.Equals(p.IdShort, "Parent", StringComparison.OrdinalIgnoreCase));
                        if (parentProp != null)
                        {
                            parentProp.Value = new BaSyx.Models.AdminShell.PropertyValue<string>(stationVal);
                        }
                        else if (!string.IsNullOrEmpty(stationVal))
                        {
                            var p = AasSharpClient.Models.SubmodelElementFactory.CreateStringProperty("Parent", stationVal);
                            smc.Add(p);
                        }
                    }
                    catch { }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "UpdateAssetLocation: failed to parse AssetLocation SMC for requirement {RequirementId}", currentRequirement.RequirementId);
        }

        Logger.LogInformation("UpdateAssetLocation: updated AssetLocation for requirement {RequirementId}", currentRequirement.RequirementId);
        return Task.FromResult(NodeStatus.Success);
    }

    private static AssetLocationData? ParseAssetLocationData(SubmodelElementCollection? smc)
    {
        if (smc == null) return null;

        string? address = null;
        string? parent = null;
        double? x = null;
        double? y = null;
        double? theta = null;

            foreach (var el in smc)
            {
                if (el == null) continue;

                if (el is Property p)
                {
                    if (string.Equals(p.IdShort, "Address", StringComparison.OrdinalIgnoreCase))
                    {
                        address = AasValueUnwrap.UnwrapToString(p.Value);
                    }
                    else if (string.Equals(p.IdShort, "Parent", StringComparison.OrdinalIgnoreCase))
                    {
                        parent = AasValueUnwrap.UnwrapToString(p.Value);
                    }
                }
            else if (el is SubmodelElementCollection coll && string.Equals(coll.IdShort, "Position", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var posEl in coll)
                {
                    if (posEl is Property pp)
                    {
                        var sval = pp.Value?.ToString();
                        if (string.Equals(pp.IdShort, "X", StringComparison.OrdinalIgnoreCase))
                        {
                            if (double.TryParse(sval, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv)) x = dv;
                        }
                        else if (string.Equals(pp.IdShort, "Y", StringComparison.OrdinalIgnoreCase))
                        {
                            if (double.TryParse(sval, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv)) y = dv;
                        }
                        else if (string.Equals(pp.IdShort, "Theta", StringComparison.OrdinalIgnoreCase))
                        {
                            if (double.TryParse(sval, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv)) theta = dv;
                        }
                    }
                }
            }
        }

        if (address == null && parent == null && x == null && y == null && theta == null) return null;

        // Provide defaults for missing numeric values
        var xv = x ?? 0.0;
        var yv = y ?? 0.0;
        var thv = theta ?? 0.0;

        return new AssetLocationData(address ?? string.Empty, parent ?? string.Empty, xv, yv, thv);
    }

    private static OfferedCapability? SelectPreferredOffer(CapabilityRequirement requirement)
    {
        if (requirement == null)
        {
            return null;
        }

        return EnumerateRequirementOffers(requirement)
            .OrderBy(GetOfferCost)
            .FirstOrDefault();
    }

    private static IEnumerable<OfferedCapability> EnumerateRequirementOffers(CapabilityRequirement requirement)
    {
        if (requirement == null)
        {
            yield break;
        }

        foreach (var offer in requirement.CapabilityOffers)
        {
            if (offer != null)
            {
                yield return offer;
            }
        }

        foreach (var sequence in requirement.OfferedCapabilitySequences)
        {
            if (sequence == null)
            {
                continue;
            }

            foreach (var capability in sequence.GetCapabilities())
            {
                if (capability != null)
                {
                    yield return capability;
                }
            }
        }
    }

    private static double GetOfferCost(OfferedCapability? offer)
    {
        if (offer == null)
        {
            return double.MaxValue;
        }

        try
        {
            return AasValueUnwrap.UnwrapToDouble(offer.Cost?.Value) ?? double.MaxValue;
        }
        catch
        {
            return double.MaxValue;
        }
    }

    private static SubmodelElementCollection? ExtractAssetLocationFromOffer(OfferedCapability offer)
    {
        if (offer?.Values == null)
        {
            return null;
        }

        foreach (var element in offer.Values)
        {
            if (element is SubmodelElementCollection smc
                && string.Equals(smc.IdShort, "AssetLocation", StringComparison.OrdinalIgnoreCase))
            {
                return smc;
            }
        }

        return null;
    }
}
