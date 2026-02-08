using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MAS_BT.Tests.TestHelpers;

internal sealed class NamespaceHolonTestConfig
{
    public string Neo4jUri { get; }
    public string Neo4jUser { get; }
    public string Neo4jPassword { get; }
    public string Neo4jDatabase { get; }
    public string MqttHost { get; }
    public int MqttPort { get; }
    public string? MqttUsername { get; }
    public string? MqttPassword { get; }

    private NamespaceHolonTestConfig(
        string neo4jUri,
        string neo4jUser,
        string neo4jPassword,
        string neo4jDatabase,
        string mqttHost,
        int mqttPort,
        string? mqttUsername,
        string? mqttPassword)
    {
        Neo4jUri = neo4jUri;
        Neo4jUser = neo4jUser;
        Neo4jPassword = neo4jPassword;
        Neo4jDatabase = neo4jDatabase;
        MqttHost = mqttHost;
        MqttPort = mqttPort;
        MqttUsername = mqttUsername;
        MqttPassword = mqttPassword;
    }

    public static NamespaceHolonTestConfig Load()
    {
        string? neo4jUri = Environment.GetEnvironmentVariable("NEO4J_URI");
        string? neo4jUser = Environment.GetEnvironmentVariable("NEO4J_USER");
        string? neo4jPassword = Environment.GetEnvironmentVariable("NEO4J_PASSWORD");
        string? neo4jDatabase = Environment.GetEnvironmentVariable("NEO4J_DATABASE");

        string? mqttHost = Environment.GetEnvironmentVariable("MASBT_TEST_MQTT_HOST");
        int? mqttPort = TryParseInt(Environment.GetEnvironmentVariable("MASBT_TEST_MQTT_PORT"));
        string? mqttUsername = Environment.GetEnvironmentVariable("MASBT_TEST_MQTT_USERNAME");
        string? mqttPassword = Environment.GetEnvironmentVariable("MASBT_TEST_MQTT_PASSWORD");

        foreach (var candidate in EnumerateCandidateConfigPaths())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                ApplyConfigFromFile(candidate, ref neo4jUri, ref neo4jUser, ref neo4jPassword, ref neo4jDatabase, ref mqttHost, ref mqttPort, ref mqttUsername, ref mqttPassword);
                break;
            }
            catch
            {
                // Ignore malformed config entries and continue with other candidates.
            }
        }

        neo4jUri ??= "neo4j://192.168.178.30:7687";
        neo4jUser ??= "neo4j";
        neo4jPassword ??= "testtest";
        neo4jDatabase ??= "neo4j";

        mqttHost ??= "192.168.178.30";
        mqttPort ??= 1883;

        return new NamespaceHolonTestConfig(
            neo4jUri,
            neo4jUser,
            neo4jPassword,
            neo4jDatabase,
            mqttHost,
            mqttPort.Value,
            mqttUsername,
            mqttPassword);
    }

    private static void ApplyConfigFromFile(
        string path,
        ref string? neo4jUri,
        ref string? neo4jUser,
        ref string? neo4jPassword,
        ref string? neo4jDatabase,
        ref string? mqttHost,
        ref int? mqttPort,
        ref string? mqttUsername,
        ref string? mqttPassword)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        if (root.TryGetProperty("Neo4j", out var neo))
        {
            neo4jUri ??= neo.TryGetProperty("Uri", out var uriProp) ? uriProp.GetString() : null;
            neo4jUser ??= neo.TryGetProperty("Username", out var userProp) ? userProp.GetString() : null;
            neo4jPassword ??= neo.TryGetProperty("Password", out var passwordProp) ? passwordProp.GetString() : null;
            neo4jDatabase ??= neo.TryGetProperty("Database", out var databaseProp) ? databaseProp.GetString() : null;
        }

        if (root.TryGetProperty("MQTT", out var mqtt))
        {
            mqttHost ??= mqtt.TryGetProperty("Broker", out var brokerProp) ? brokerProp.GetString() : null;
            mqttUsername ??= mqtt.TryGetProperty("Username", out var usernameProp) ? usernameProp.GetString() : null;
            mqttPassword ??= mqtt.TryGetProperty("Password", out var passwordProp) ? passwordProp.GetString() : null;
            if (mqttPort == null && mqtt.TryGetProperty("Port", out var portProp) && portProp.TryGetInt32(out var parsedPort))
            {
                mqttPort = parsedPort;
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateConfigPaths()
    {
        var suffix = Path.Combine("configs", "specific_configs", "NamespaceHolon", "NamespaceHolon.json");
        var roots = new List<string?>
        {
            Directory.GetCurrentDirectory(),
            SafeGetFullPath(AppContext.BaseDirectory, "../../../../../"),
            SafeGetFullPath(AppContext.BaseDirectory, "../../../../"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var withMasBt = Path.Combine(root, "MAS-BT", suffix);
            var direct = Path.Combine(root, suffix);
            if (seen.Add(withMasBt))
            {
                yield return withMasBt;
            }
            if (seen.Add(direct))
            {
                yield return direct;
            }
        }
    }

    private static string? SafeGetFullPath(string? basePath, string relative)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(basePath, relative));
        }
        catch
        {
            return null;
        }
    }

    private static int? TryParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
