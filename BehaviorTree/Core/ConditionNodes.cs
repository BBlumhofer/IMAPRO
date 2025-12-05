using Microsoft.Extensions.Logging;

namespace MAS_BT.Core;

/// <summary>
/// Condition Node - Wrapper für Bedingungsprüfungen
/// </summary>
public class ConditionNode : BTNode
{
    public BTNode Child { get; set; } = null!;
    
    public ConditionNode() : base("Condition")
    {
    }
    
    public ConditionNode(string name) : base(name)
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        return await Child.Execute();
    }
    
    public override async Task OnAbort()
    {
        await Child.OnAbort();
    }
    
    public override async Task OnReset()
    {
        await Child.OnReset();
    }
}

/// <summary>
/// BlackboardCondition - Prüft Blackboard-Wert gegen erwarteten Wert
/// </summary>
public class BlackboardConditionNode : BTNode
{
    /// <summary>
    /// Blackboard-Key zum Prüfen
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// Erwarteter Wert
    /// </summary>
    public object? ExpectedValue { get; set; }
    
    public BlackboardConditionNode() : base("BlackboardCondition")
    {
    }
    
    public BlackboardConditionNode(string name) : base(name)
    {
    }
    
    public override Task<NodeStatus> Execute()
    {
        if (string.IsNullOrEmpty(Key))
        {
            Logger.LogError("BlackboardCondition '{Name}' has no key specified", Name);
            return Task.FromResult(NodeStatus.Failure);
        }
        
        var actualValue = Context.Get<object>(Key);
        
        if (actualValue == null && !Context.Has(Key))
        {
            Logger.LogDebug("BlackboardCondition '{Name}': Key '{Key}' not found in blackboard", 
                Name, Key);
            return Task.FromResult(NodeStatus.Failure);
        }
        
        // Null-Handling
        if (ExpectedValue == null && actualValue == null)
        {
            Logger.LogDebug("BlackboardCondition '{Name}': Both values are null - Success", Name);
            return Task.FromResult(NodeStatus.Success);
        }
        
        if (ExpectedValue == null || actualValue == null)
        {
            Logger.LogDebug("BlackboardCondition '{Name}': One value is null - Failure", Name);
            return Task.FromResult(NodeStatus.Failure);
        }
        
        // String-Vergleich (case-insensitive für bool-Strings)
        string expectedStr = ExpectedValue.ToString()?.ToLowerInvariant() ?? "";
        string actualStr = actualValue.ToString()?.ToLowerInvariant() ?? "";
        
        bool match = expectedStr == actualStr;
        
        Logger.LogDebug(
            "BlackboardCondition '{Name}': Key='{Key}', Expected='{Expected}', Actual='{Actual}' → {Result}",
            Name, Key, ExpectedValue, actualValue, match ? "Success" : "Failure");
        
        return Task.FromResult(match ? NodeStatus.Success : NodeStatus.Failure);
    }
    
    public override Task OnAbort()
    {
        return Task.CompletedTask;
    }
    
    public override Task OnReset()
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// CompareCondition - Vergleicht zwei Blackboard-Werte
/// </summary>
public class CompareConditionNode : BTNode
{
    public string KeyA { get; set; } = string.Empty;
    public string KeyB { get; set; } = string.Empty;
    public string Operator { get; set; } = "=="; // ==, !=, <, >, <=, >=
    
    public CompareConditionNode() : base("CompareCondition")
    {
    }
    
    public CompareConditionNode(string name) : base(name)
    {
    }
    
    public override Task<NodeStatus> Execute()
    {
        if (!Blackboard.ContainsKey(KeyA) || !Blackboard.ContainsKey(KeyB))
        {
            Logger.LogWarning("CompareCondition '{Name}': Missing keys", Name);
            return Task.FromResult(NodeStatus.Failure);
        }
        
        var valueA = Blackboard[KeyA];
        var valueB = Blackboard[KeyB];
        
        bool result = Operator switch
        {
            "==" => Equals(valueA, valueB),
            "!=" => !Equals(valueA, valueB),
            _ => false
        };
        
        Logger.LogDebug("CompareCondition '{Name}': {A} {Op} {B} = {Result}",
            Name, valueA, Operator, valueB, result);
        
        return Task.FromResult(result ? NodeStatus.Success : NodeStatus.Failure);
    }
    
    public override Task OnAbort()
    {
        return Task.CompletedTask;
    }
    
    public override Task OnReset()
    {
        return Task.CompletedTask;
    }
}
