using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MAS_BT.Tests;

public class Neo4jIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public Neo4jIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void Log(string format, params object[] args)
    {
        var message = string.Format(format, args);
        _output.WriteLine(message);
        Console.WriteLine(message);
    }

    private (string uri, string user, string password, string database) GetConfig()
    {
        // 1) Try environment variables
        var uri = Environment.GetEnvironmentVariable("NEO4J_URI");
        var user = Environment.GetEnvironmentVariable("NEO4J_USER");
        var password = Environment.GetEnvironmentVariable("NEO4J_PASSWORD");
        var database = Environment.GetEnvironmentVariable("NEO4J_DATABASE");

        // 2) If any missing, try to load from NamespaceHolon config file (repo-relative path)
        if (string.IsNullOrEmpty(uri) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(database))
        {
            try
            {
                var cfgPath = Path.Combine(Environment.CurrentDirectory, "MAS-BT", "configs", "specific_configs", "NamespaceHolon", "NamespaceHolon.json");
                if (File.Exists(cfgPath))
                {
                    using var stream = File.OpenRead(cfgPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(stream);
                    if (doc.RootElement.TryGetProperty("Neo4j", out var neo))
                    {
                        if (string.IsNullOrEmpty(uri) && neo.TryGetProperty("Uri", out var u)) uri = u.GetString();
                        if (string.IsNullOrEmpty(user) && neo.TryGetProperty("Username", out var un)) user = un.GetString();
                        if (string.IsNullOrEmpty(password) && neo.TryGetProperty("Password", out var pw)) password = pw.GetString();
                        if (string.IsNullOrEmpty(database) && neo.TryGetProperty("Database", out var db)) database = db.GetString();
                    }
                }
            }
            catch
            {
                // best-effort: fallback to defaults below
            }
        }

        // 3) final fallbacks
        uri ??= "neo4j://192.168.178.30:7687";
        user ??= "neo4j";
        password ??= "neo4j";
        database ??= "neo4j";

        return (uri, user, password, database);
    }

    [Fact]
    public async Task ComposedOf_FromModule_ReturnsChildren()
    {
        try
        {
            var (uri, user, password, database) = GetConfig();

            await using var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));

            // Query capabilities provided by module P100 and their composed children
            var cypher = @"
// label-agnostic lookup by shell_id to support Asset nodes without Module label
MATCH (m {shell_id:$module})-[:PROVIDES_CAPABILITY]->(c:Capability)
OPTIONAL MATCH (c)-[:CapabilityComposedOf*1..6]->(child:Capability)
RETURN c.idShort AS parent, collect(DISTINCT child.idShort) AS children
LIMIT 50
";

            var cursor = await session.RunAsync(cypher, new { module = "P100" });
            var records = await cursor.ToListAsync();

            Log("Found capability rows: {0}", records.Count);

            // Fail if no capability rows found
            Assert.NotEmpty(records);

            // at least one parent should have children (otherwise test fails)
            var hasChildren = false;
            foreach (var r in records)
            {
                var children = r["children"].As<List<object>>();
                if (children != null && children.Count > 0)
                {
                    hasChildren = true;
                    Log("Parent {0} has children: {1}", r["parent"].As<string>(), string.Join(",", children.Select(c => c.ToString())));
                }
            }

            Assert.True(hasChildren, "No composed children found for module P100 (expected existing CapabilityComposedOf relations)");
        }
        catch (Neo4j.Driver.AuthenticationException)
        {
            // Skip test when authentication fails (credentials not provided)
            return;
        }
        catch (Neo4j.Driver.ServiceUnavailableException)
        {
            // Skip if service is unavailable
            return;
        }
    }

    [Fact]
    public async Task TransportCapabilities_Exist_For_Module()
    {
        try
        {
            var (uri, user, password, database) = GetConfig();

            await using var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));

            var cypher = @"
// label-agnostic lookup by shell_id to support Asset nodes without Module label
MATCH (m {shell_id:$module})-[:PROVIDES_CAPABILITY]->(c:Capability)
RETURN collect(c.idShort) AS caps
LIMIT 1
";

            var cursor = await session.RunAsync(cypher, new { module = "P200" });
            var rec = await cursor.SingleOrDefaultAsync();
            Assert.NotNull(rec);

            var caps = rec["caps"].As<List<object>>();
            Log("Module P200 capabilities: {0}", caps == null ? "<none>" : string.Join(",", caps.Select(c => c.ToString())));

            Assert.NotEmpty(caps);
        }
        catch (Neo4j.Driver.AuthenticationException)
        {
            // Skip test when authentication fails (credentials not provided)
            return;
        }
        catch (Neo4j.Driver.ServiceUnavailableException)
        {
            // Skip if service is unavailable
            return;
        }
    }
}
