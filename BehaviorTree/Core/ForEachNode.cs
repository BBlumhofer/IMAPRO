using Microsoft.Extensions.Logging;

namespace MAS_BT.Core;

/// <summary>
/// ForEach Node - Iteriert über ein Array und führt Child für jedes Element aus
/// Setzt CurrentItem im Context für Zugriff durch Child Nodes
/// </summary>
public class ForEachNode : DecoratorNode
{
    /// <summary>
    /// Blackboard Key für das Array über das iteriert werden soll
    /// z.B. "ProcessChain.Requirements"
    /// </summary>
    public string ArrayKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Blackboard Key unter dem das aktuelle Element gesetzt wird
    /// z.B. "CurrentRequirement"
    /// </summary>
    public string ItemKey { get; set; } = "CurrentItem";
    
    /// <summary>
    /// Blackboard Key für den aktuellen Index (optional)
    /// </summary>
    public string IndexKey { get; set; } = "CurrentIndex";
    
    private int _currentIndex = 0;
    private object[]? _array;
    private bool _arrayBound = false;
    
    public ForEachNode() : base("ForEach")
    {
    }
    
    public ForEachNode(string name) : base(name)
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        // Beim ersten Tick: Array aus Context holen
        if (!_arrayBound)
        {
            var arrayValue = Context.Get<object>(ArrayKey);
            if (arrayValue == null)
            {
                Logger.LogError("ForEach '{Name}': ArrayKey '{ArrayKey}' not found or null in context", Name, ArrayKey);
                return NodeStatus.Failure;
            }
            
            // Konvertiere zu Array (unterstützt List, Array, IEnumerable)
            if (arrayValue is System.Collections.IEnumerable enumerable)
            {
                var items = new List<object>();
                foreach (var item in enumerable)
                {
                    items.Add(item);
                }
                _array = items.ToArray();
            }
            else
            {
                Logger.LogError("ForEach '{Name}': ArrayKey '{ArrayKey}' is not enumerable", Name, ArrayKey);
                return NodeStatus.Failure;
            }
            
            _arrayBound = true;
            _currentIndex = 0;
            
            Logger.LogInformation("ForEach '{Name}': iterating over {Count} items from {ArrayKey}", Name, _array.Length, ArrayKey);
        }
        
        // Falls Array leer: Success (nichts zu tun)
        if (_array == null || _array.Length == 0)
        {
            Logger.LogDebug("ForEach '{Name}': empty array, returning Success", Name);
            await ResetState();
            return NodeStatus.Success;
        }
        
        // Falls alle Elemente abgearbeitet: Success
        if (_currentIndex >= _array.Length)
        {
            Logger.LogInformation("ForEach '{Name}': completed all {Count} items", Name, _array.Length);
            await ResetState();
            return NodeStatus.Success;
        }
        
        // Setze aktuelles Element im Context
        var currentItem = _array[_currentIndex];
        Context.Set(ItemKey, currentItem);
        Context.Set(IndexKey, _currentIndex);
        
        Logger.LogDebug("ForEach '{Name}': processing item {Index}/{Total}", Name, _currentIndex + 1, _array.Length);
        
        // Führe Child für aktuelles Element aus
        var result = await Child.Execute();
        
        if (result == NodeStatus.Running)
        {
            // Child noch nicht fertig, Running zurückgeben
            return NodeStatus.Running;
        }
        
        if (result == NodeStatus.Failure)
        {
            // Child gescheitert - je nach Konfiguration abbrechen oder fortfahren
            // Aktuell: abbrechen bei Failure
            Logger.LogWarning("ForEach '{Name}': child failed at item {Index}, stopping iteration", Name, _currentIndex);
            await ResetState();
            return NodeStatus.Failure;
        }
        
        // Child erfolgreich: Child resetten und zum nächsten Element
        await Child.OnReset();
        _currentIndex++;
        
        // Running zurückgeben damit nächster Tick nächstes Element verarbeitet
        return NodeStatus.Running;
    }
    
    private async Task ResetState()
    {
        _currentIndex = 0;
        _arrayBound = false;
        _array = null;
        await Child.OnReset();
    }
    
    public override async Task OnAbort()
    {
        await ResetState();
        await base.OnAbort();
    }
    
    public override async Task OnReset()
    {
        await ResetState();
        await base.OnReset();
    }
}
