// filepath: /home/benjamin/AgentDevelopment/MAS-BT/Nodes/Configuration/ReadConfigNode.cs
using Microsoft.Extensions.Logging;
using MAS_BT.Core;
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
            
            Logger.LogInformation("ReadConfig: Successfully loaded configuration from {ConfigPath}", ConfigPath);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadConfig: Error loading configuration from {ConfigPath}", ConfigPath);
            return NodeStatus.Failure;
        }
    }
}
