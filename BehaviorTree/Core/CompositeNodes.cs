using Microsoft.Extensions.Logging;

namespace MAS_BT.Core;

/// <summary>
/// Sequence Node - Führt Kinder sequenziell aus (stoppt bei erstem Failure)
/// </summary>
public class SequenceNode : CompositeNode
{
    public SequenceNode() : base("Sequence") {}
    public SequenceNode(string name) : base(name) {}
    
    public override async Task<NodeStatus> Execute()
    {
        foreach (var child in Children)
        {
            var result = await child.Execute();
            if (result != NodeStatus.Success)
                return result;
        }
        return NodeStatus.Success;
    }
}

/// <summary>
/// Selector/Fallback Node - Führt Kinder aus bis eines Success zurückgibt
/// </summary>
public class SelectorNode : CompositeNode
{
    public SelectorNode() : base("Selector") {}
    public SelectorNode(string name) : base(name) {}
    
    public override async Task<NodeStatus> Execute()
    {
        foreach (var child in Children)
        {
            var result = await child.Execute();
            if (result == NodeStatus.Success)
                return NodeStatus.Success;
            if (result == NodeStatus.Running)
                return NodeStatus.Running;
        }
        return NodeStatus.Failure;
    }
}

/// <summary>
/// Fallback ist ein Alias für Selector
/// </summary>
public class FallbackNode : SelectorNode
{
    public FallbackNode() : base("Fallback") {}
    public FallbackNode(string name) : base(name) {}
}

/// <summary>
/// Parallel Node - Führt alle Kinder parallel aus
/// </summary>
public class ParallelNode : CompositeNode
{
    public int SuccessThreshold { get; set; } = 1;
    public int FailureThreshold { get; set; } = 1;
    
    public ParallelNode() : base("Parallel") {}
    public ParallelNode(string name) : base(name) {}
    
    public override async Task<NodeStatus> Execute()
    {
        var tasks = Children.Select(c => c.Execute()).ToArray();
        var results = await Task.WhenAll(tasks);
        
        var successCount = results.Count(r => r == NodeStatus.Success);
        var failureCount = results.Count(r => r == NodeStatus.Failure);
        
        if (successCount >= SuccessThreshold)
            return NodeStatus.Success;
        if (failureCount >= FailureThreshold)
            return NodeStatus.Failure;
        
        return NodeStatus.Running;
    }
}
