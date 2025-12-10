using Microsoft.Extensions.Logging;
using MAS_BT.Core;
using System.Globalization;
using System.Text.Json;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// ReadConfig - Reads configuration from a JSON file
/// </summary>
public class ReadConfigNode : BTNode
{
    public string ConfigPath { get; set; } = "config.json";
    
    public ReadConfigNode() : base("ReadConfig")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("ReadConfig: Loading configuration from {ConfigPath}", ConfigPath);
        
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Logger.LogWarning("ReadConfig: Configuration file not found: {ConfigPath}", ConfigPath);
                return NodeStatus.Failure;
            }
            var jsonContent = await File.ReadAllTextAsync(ConfigPath);
            var config = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            
            Context.Set("config", config);
            if (config.ValueKind == JsonValueKind.Object)
            {
                FlattenObject(config, "config");
            }
            else
            {
                Logger.LogWarning("ReadConfig: Expected root object in configuration file {ConfigPath}", ConfigPath);
            }
            
            Logger.LogInformation("ReadConfig: Successfully loaded configuration from {ConfigPath}", ConfigPath);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadConfig: Error loading configuration from {ConfigPath}", ConfigPath);
            return NodeStatus.Failure;
        }
    }

    private void FlattenObject(JsonElement element, string prefix)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            Context.Set(prefix, element);
            foreach (var property in element.EnumerateObject())
            {
                var childPrefix = string.IsNullOrEmpty(prefix)
                    ? property.Name
                    : $"{prefix}.{property.Name}";
                FlattenObject(property.Value, childPrefix);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            Context.Set(prefix, element);
            return;
        }

        Context.Set(prefix, ConvertPrimitive(element));
    }

    private static object? ConvertPrimitive(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue.ToString(CultureInfo.InvariantCulture)
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue.ToString(CultureInfo.InvariantCulture)
                    : element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
