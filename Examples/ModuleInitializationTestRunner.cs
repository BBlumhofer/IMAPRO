using Microsoft.Extensions.Logging;
using MAS_BT.Core;
using MAS_BT.Serialization;
using System.Text.Json;
using UAClient.Client;

namespace MAS_BT.Examples;

public class ModuleInitializationTestRunner
{
    public static async Task Run(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║    MAS-BT: Module Initialization Test Debug Runner          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        
        var logger = loggerFactory.CreateLogger<ModuleInitializationTestRunner>();
        
        // Lade Config
        var config = LoadConfig();
        var opcuaEndpoint = GetConfigValue(config, "OPCUA.Endpoint", "opc.tcp://192.168.178.30:4849");
        var opcuaUsername = GetConfigValue(config, "OPCUA.Username", "orchestrator");
        var opcuaPassword = GetConfigValue(config, "OPCUA.Password", "orchestrator");
        var moduleId = GetConfigValue(config, "Agent.ModuleId", "Module_Assembly_01");
        
        Console.WriteLine($"📋 Configuration:");
        Console.WriteLine($"   OPC UA Endpoint: {opcuaEndpoint}");
        Console.WriteLine($"   OPC UA Username: {opcuaUsername}");
        Console.WriteLine($"   Module ID: {moduleId}");
        Console.WriteLine();
        
        var context = new BTContext(loggerFactory.CreateLogger<BTContext>())
        {
            AgentId = "TestAgent",
            AgentRole = "ResourceHolon"
        };
        
        context.Set("config.OPCUA.Endpoint", opcuaEndpoint);
        context.Set("config.OPCUA.Username", opcuaUsername);
        context.Set("config.OPCUA.Password", opcuaPassword);
        context.Set("config.Agent.ModuleId", moduleId);
        
        Console.WriteLine("🔧 Setup:");
        Console.WriteLine("   ✓ BTContext erstellt");
        Console.WriteLine("   ✓ Config-Werte gesetzt (inkl. Username/Password)");
        Console.WriteLine("   → ConnectToModule wird den UaClient mit Credentials erstellen");
        Console.WriteLine();
        
        try
        {
            // var btFilePath = "Trees/ModuleInitializationTest.bt.xml";
            var btFilePath = "Trees/Init_and_ExecuteSkill.bt.xml";
            
            if (!File.Exists(btFilePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ BT Datei nicht gefunden: {btFilePath}");
                Console.ResetColor();
                return;
            }
            
            Console.WriteLine($"�� Lade Behavior Tree: {btFilePath}");
            Console.WriteLine();
            
            var registry = new NodeRegistry(loggerFactory.CreateLogger<NodeRegistry>());
            var deserializer = new XmlTreeDeserializer(registry, loggerFactory.CreateLogger<XmlTreeDeserializer>());
            
            var rootNode = deserializer.Deserialize(btFilePath, context);
            
            Console.WriteLine($"✓ BT geladen: {rootNode.Name}");
            Console.WriteLine();
            Console.WriteLine("────────────────────────────────────────────────────────────────");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("🚀 STARTE BEHAVIOR TREE EXECUTION");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("────────────────────────────────────────────────────────────────");
            Console.WriteLine();
            
            var result = await rootNode.Execute();
            
            Console.WriteLine();
            Console.WriteLine("────────────────────────────────────────────────────────────────");
            Console.WriteLine();
            
            if (result == NodeStatus.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ BEHAVIOR TREE ERFOLGREICH ABGESCHLOSSEN");
            }
            else if (result == NodeStatus.Failure)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ BEHAVIOR TREE FEHLGESCHLAGEN");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️  BEHAVIOR TREE STATUS: " + result);
            }
            Console.ResetColor();
            
            Console.WriteLine();
            Console.WriteLine("────────────────────────────────────────────────────────────────");
            Console.WriteLine();
            
            PrintContextState(context);
            
            Console.WriteLine();
            Console.WriteLine("🧹 Cleanup...");
            
            var server = context.Get<RemoteServer>("RemoteServer");
            if (server != null)
            {
                try
                {
                    server.Dispose();
                    Console.WriteLine("✓ RemoteServer disconnected");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing RemoteServer");
                }
            }
            
            Console.WriteLine("✓ Cleanup abgeschlossen");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ KRITISCHER FEHLER:");
            Console.WriteLine($"   {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Stack Trace:");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
        
        Console.WriteLine();
        Console.WriteLine("👋 Test beendet");
    }
    
    private static Dictionary<string, object> LoadConfig()
    {
        var configPath = "config.json";
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine("⚠️  config.json nicht gefunden, verwende Defaults");
            return new Dictionary<string, object>();
        }
        
        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            
            var result = new Dictionary<string, object>();
            if (config != null)
            {
                foreach (var kvp in config)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Fehler beim Laden von config.json: {ex.Message}");
            return new Dictionary<string, object>();
        }
    }
    
    private static string GetConfigValue(Dictionary<string, object> config, string path, string defaultValue)
    {
        var parts = path.Split('.');
        object? current = config;
        
        foreach (var part in parts)
        {
            if (current is Dictionary<string, object> dict && dict.TryGetValue(part, out var value))
            {
                current = value;
            }
            else if (current is JsonElement element && element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty(part, out var prop))
                {
                    current = prop;
                }
                else
                {
                    return defaultValue;
                }
            }
            else
            {
                return defaultValue;
            }
        }
        
        if (current is JsonElement jsonElem)
        {
            return jsonElem.GetString() ?? defaultValue;
        }
        
        return current?.ToString() ?? defaultValue;
    }
    
    private static void PrintContextState(BTContext context)
    {
        Console.WriteLine("📊 CONTEXT STATE:");
        Console.WriteLine();
        
        var data = new Dictionary<string, object?>
        {
            ["connected"] = context.Get<bool>("connected"),
            ["locked"] = context.Get<bool>("locked"),
            ["coupled"] = context.Get<bool>("coupled"),
            ["started"] = context.Get<bool>("started"),
            ["sent"] = context.Get<bool>("sent"),
            ["moduleEndpoint"] = context.Get<string>("moduleEndpoint"),
            ["lastExecutedSkill"] = context.Get<string>("lastExecutedSkill"),
            ["CoupledModules"] = context.Get<List<string>>("CoupledModules")
        };
        
        foreach (var kvp in data)
        {
            if (kvp.Value != null)
            {
                Console.WriteLine($"   • {kvp.Key}: {FormatValue(kvp.Value)}");
            }
        }
        
        Console.WriteLine();
    }
    
    private static string FormatValue(object value)
    {
        if (value is bool b)
            return b ? "✓" : "✗";
        
        if (value is List<string> list)
            return $"[{string.Join(", ", list)}]";
        
        return value.ToString() ?? "(null)";
    }
}
