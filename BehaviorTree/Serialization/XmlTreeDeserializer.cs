using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Serialization;

/// <summary>
/// XML Tree Deserializer - Lädt BT aus XML-Dateien
/// </summary>
public class XmlTreeDeserializer
{
    private readonly NodeRegistry _registry;
    private readonly ILogger _logger;
    
    public XmlTreeDeserializer(NodeRegistry registry, ILogger logger)
    {
        _registry = registry;
        _logger = logger;
    }
    
    /// <summary>
    /// Lädt einen Behavior Tree aus XML-Datei
    /// </summary>
    public BTNode Deserialize(string xmlPath, BTContext context)
    {
        _logger.LogInformation("Lade Behavior Tree aus: {Path}", xmlPath);
        
        if (!File.Exists(xmlPath))
        {
            throw new FileNotFoundException($"XML-Datei nicht gefunden: {xmlPath}");
        }
        
        var doc = XDocument.Load(xmlPath);
        var root = doc.Root;
        
        if (root == null || root.Name != "BehaviorTree")
        {
            throw new InvalidOperationException("XML muss mit <BehaviorTree> Root Element beginnen");
        }
        
        var treeName = root.Attribute("name")?.Value ?? "UnnamedTree";
        _logger.LogInformation("Lade Tree: {TreeName}", treeName);
        
        // Finde Root Node (erstes Kind-Element)
        var rootNodeElement = root.Elements().FirstOrDefault();
        if (rootNodeElement == null)
        {
            throw new InvalidOperationException("BehaviorTree muss mindestens einen Root Node enthalten");
        }
        
        var rootNode = DeserializeNode(rootNodeElement, context);
        
        _logger.LogInformation("✓ Behavior Tree '{TreeName}' erfolgreich geladen", treeName);
        return rootNode;
    }
    
    /// <summary>
    /// Deserialisiert einen einzelnen Node aus XML Element
    /// </summary>
    private BTNode DeserializeNode(XElement element, BTContext context)
    {
        var nodeName = element.Name.LocalName;
        
        _logger.LogDebug("Deserialisiere Node: {NodeName}", nodeName);
        
        // Spezialbehandlung: Wenn Element "Root" heißt, überspringe es und nimm erstes Kind
        if (nodeName == "Root")
        {
            _logger.LogDebug("Root Element gefunden, überspringe und nehme erstes Kind");
            var firstChild = element.Elements().FirstOrDefault();
            if (firstChild == null)
            {
                throw new InvalidOperationException("Root Element muss mindestens ein Kind-Element haben");
            }
            return DeserializeNode(firstChild, context);
        }
        
        // Erstelle Node-Instanz
        var node = _registry.CreateNode(nodeName);
        node.Initialize(context, _logger);
        
        // Setze Name aus Attribut (falls vorhanden)
        var nameAttr = element.Attribute("name")?.Value;
        if (!string.IsNullOrEmpty(nameAttr))
        {
            node.Name = nameAttr;
        }
        
        // Setze Properties aus XML Attributen
        SetPropertiesFromAttributes(node, element);
        
        // Handle Composite Nodes (mit Children)
        if (node is SequenceNode sequence)
        {
            sequence.Children = DeserializeChildren(element, context);
        }
        else if (node is SelectorNode selector)
        {
            selector.Children = DeserializeChildren(element, context);
        }
        else if (node is ParallelNode parallel)
        {
            parallel.Children = DeserializeChildren(element, context);
        }
        // Handle Decorator Nodes (mit einem Child)
        else if (node is RetryNode retry)
        {
            retry.Child = DeserializeSingleChild(element, context);
        }
        else if (node is RetryUntilSuccessNode retryUntilSuccess)
        {
            retryUntilSuccess.Child = DeserializeSingleChild(element, context);
        }
        else if (node is TimeoutNode timeout)
        {
            timeout.Child = DeserializeSingleChild(element, context);
        }
        else if (node is RepeatNode repeat)
        {
            repeat.Child = DeserializeSingleChild(element, context);
        }
        else if (node is InverterNode inverter)
        {
            inverter.Child = DeserializeSingleChild(element, context);
        }
        else if (node is SucceederNode succeeder)
        {
            succeeder.Child = DeserializeSingleChild(element, context);
        }
        else if (node is ConditionNode condition)
        {
            condition.Child = DeserializeSingleChild(element, context);
        }
        
        return node;
    }
    
    /// <summary>
    /// Deserialisiert alle Kinder eines Composite Nodes
    /// </summary>
    private List<BTNode> DeserializeChildren(XElement parentElement, BTContext context)
    {
        return parentElement.Elements()
            .Select(childElement => DeserializeNode(childElement, context))
            .ToList();
    }
    
    /// <summary>
    /// Deserialisiert das einzelne Kind eines Decorator Nodes
    /// </summary>
    private BTNode DeserializeSingleChild(XElement parentElement, BTContext context)
    {
        var childElement = parentElement.Elements().FirstOrDefault();
        if (childElement == null)
        {
            throw new InvalidOperationException($"Decorator Node '{parentElement.Name}' muss genau ein Kind-Element haben");
        }
        
        return DeserializeNode(childElement, context);
    }
    
    /// <summary>
    /// Setzt Properties aus XML Attributen (mit Config-Interpolation)
    /// </summary>
    private void SetPropertiesFromAttributes(BTNode node, XElement element)
    {
        var nodeType = node.GetType();
        
        foreach (var attr in element.Attributes())
        {
            if (attr.Name.LocalName == "name")
                continue; // Name wird separat behandelt
            
            var propName = ToPascalCase(attr.Name.LocalName);
            var prop = nodeType.GetProperty(propName);
            
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    var value = InterpolateConfigValues(attr.Value, node.Context);
                    var convertedValue = ConvertValue(value, prop.PropertyType);
                    prop.SetValue(node, convertedValue);
                    
                    _logger.LogDebug("Property gesetzt: {Node}.{Property} = {Value}", 
                        node.Name, propName, convertedValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fehler beim Setzen von Property {Property} auf Node {Node}", 
                        propName, node.Name);
                }
            }
        }
    }
    
    /// <summary>
    /// Interpoliert Config-Werte in Strings (z.B. {config.OPCUA.Endpoint})
    /// </summary>
    private string InterpolateConfigValues(string value, BTContext context)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("{"))
            return value;
        
        var result = value;
        var startIndex = 0;
        
        while (startIndex < result.Length)
        {
            var openBrace = result.IndexOf('{', startIndex);
            if (openBrace == -1)
                break;
            
            var closeBrace = result.IndexOf('}', openBrace);
            if (closeBrace == -1)
                break;
            
            var placeholder = result.Substring(openBrace + 1, closeBrace - openBrace - 1);
            var replacement = context.Get<string>(placeholder) ?? $"{{{placeholder}}}";
            
            result = result.Substring(0, openBrace) + replacement + result.Substring(closeBrace + 1);
            startIndex = openBrace + replacement.Length;
            
            _logger.LogDebug("Config-Interpolation: {{{Placeholder}}} → {Value}", placeholder, replacement);
        }
        
        return result;
    }
    
    /// <summary>
    /// Konvertiert String-Wert zum Zieltyp
    /// </summary>
    private object? ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string))
            return value;
        
        if (targetType == typeof(int))
            return int.Parse(value);
        
        if (targetType == typeof(double))
            return double.Parse(value);
        
        if (targetType == typeof(bool))
            return bool.Parse(value);
        
        if (targetType == typeof(long))
            return long.Parse(value);
        
        return value;
    }
    
    /// <summary>
    /// Konvertiert kebab-case zu PascalCase (z.B. "timeout-ms" → "TimeoutMs")
    /// </summary>
    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        
        // Behandle verschiedene Naming Conventions
        var parts = input.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 1)
        {
            // Einfacher Name: Ersten Buchstaben groß
            return char.ToUpper(parts[0][0]) + parts[0].Substring(1);
        }
        
        // Mehrere Teile: Jeden Teil mit Großbuchstaben beginnen
        return string.Join("", parts.Select(p => char.ToUpper(p[0]) + p.Substring(1)));
    }
}
