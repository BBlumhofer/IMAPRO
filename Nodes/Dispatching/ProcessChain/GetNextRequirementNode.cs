using System.Threading.Tasks;
using AasSharpClient.Models.ProcessChain;
using MAS_BT.Core;
using MAS_BT.Nodes.Common;
using Microsoft.Extensions.Logging;

namespace MAS_BT.Nodes.Dispatching.ProcessChain;

/// <summary>
/// Holt das nächste Requirement aus der Liste und setzt es als CurrentRequirement.
/// Gibt Success zurück wenn ein Requirement vorhanden ist, Failure wenn alle durch.
/// </summary>
public class GetNextRequirementNode : BTNode
{
    private int _currentIndex = 0;
    private bool _initialized = false;
    
    public GetNextRequirementNode() : base("GetNextRequirement") { }
    
    public override Task<NodeStatus> Execute()
    {
        // Restore iterator state from Context so state survives child resets by Repeat nodes
        if (!_initialized)
        {
            try
            {
                var saved = Context.Get<int?>("ProcessChain.GetNextIndex");
                if (saved != null)
                {
                    _currentIndex = saved.Value;
                    Logger.LogDebug("GetNextRequirement: restored iterator from context index={Idx}", _currentIndex);
                }
            }
            catch
            {
                // ignore
            }

            // If a requirement was removed by a previous FinalizeRequirement, adjust our index
            if (Context.Has("ProcessChain.RequirementRemovedIndex"))
            {
                try
                {
                    var removedIndex = Context.Get<int>("ProcessChain.RequirementRemovedIndex");
                    if (removedIndex < _currentIndex && _currentIndex > 0)
                    {
                        _currentIndex--;
                        Logger.LogDebug("GetNextRequirement: adjusted iterator due to removed requirement at index {Idx}, new index {NewIdx}", removedIndex, _currentIndex);
                    }
                }
                catch
                {
                    // ignore parsing errors
                }
                // consume the marker
                Context.Remove("ProcessChain.RequirementRemovedIndex");
            }

            _initialized = true;
        }

        var ctx = Context.Get<ProcessChainNegotiationContext>("ProcessChain.Negotiation");
        if (ctx == null || ctx.Requirements == null)
        {
            Logger.LogError("GetNextRequirement: negotiation context or requirements missing");
            return Task.FromResult(NodeStatus.Failure);
        }
        
        // Initialisierung beim ersten Aufruf
        if (!_initialized)
        {
            _currentIndex = 0;
            _initialized = true;
            Logger.LogInformation("GetNextRequirement: starting iteration over {Count} requirements", ctx.Requirements.Count);
        }
        
        // Prüfe ob noch Requirements übrig sind
        if (_currentIndex >= ctx.Requirements.Count)
        {
            Logger.LogInformation("GetNextRequirement: all {Count} requirements processed", ctx.Requirements.Count);
            // Reset für nächsten Durchlauf
            _currentIndex = 0;
            _initialized = false;
            // clear persisted iterator
            Context.Remove("ProcessChain.GetNextIndex");
            return Task.FromResult(NodeStatus.Failure); // Signalisiert "fertig"
        }
        
        // Hole aktuelles Requirement
        var currentRequirement = ctx.Requirements[_currentIndex];
        
        // Setze im Context für nachfolgende Nodes
        Context.Set("CurrentRequirement", currentRequirement);
        Context.Set("CurrentRequirementIndex", _currentIndex);
        
        Logger.LogInformation("GetNextRequirement: processing requirement {Index}/{Total} - {RequirementId}", 
            _currentIndex + 1, ctx.Requirements.Count, currentRequirement.RequirementId);
        
        // Inkrementiere für nächsten Aufruf
        _currentIndex++;
        // persist iterator so it survives Repeat resets
        Context.Set("ProcessChain.GetNextIndex", _currentIndex);
        
        return Task.FromResult(NodeStatus.Success);
    }
    
    public override Task OnReset()
    {
        _currentIndex = 0;
        _initialized = false;
        return Task.CompletedTask;
    }
}
