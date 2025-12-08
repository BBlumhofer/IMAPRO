# AAS-Sharp-Client Library Documentation

## Table of Contents
1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Core Concepts](#core-concepts)
4. [Installation & Setup](#installation--setup)
5. [API Reference](#api-reference)
6. [Integration with MAS-BT](#integration-with-mas-bt)
7. [Usage Examples](#usage-examples)
8. [Common Patterns](#common-patterns)
9. [Submodel Specifications](#submodel-specifications)
10. [Error Handling](#error-handling)
11. [Best Practices](#best-practices)
12. [Troubleshooting](#troubleshooting)
13. [FAQ](#faq)

---

## Overview

### What is AAS-Sharp-Client?

**AAS-Sharp-Client** is a C#/.NET library that provides programmatic access to **Asset Administration Shells (AAS)** following the Industry 4.0 standard. It enables agents in the MAS-BT holonic control system to read and interact with semantic production data stored in AAS repositories.

### Purpose in MAS-BT

In the MAS-BT architecture, AAS serves as the **semantic layer** that bridges:
- **Real-time machine control** (OPC UA)
- **High-level production planning** (Behavior Trees)
- **Holonic communication** (MQTT/I4.0 messaging)

AAS-Sharp-Client allows behavior tree nodes to:
- ✅ Load machine capabilities and skills
- ✅ Read production schedules and plans
- ✅ Access machine metadata and nameplate information
- ✅ Retrieve process definitions and constraints
- ✅ Maintain semantic interoperability across heterogeneous systems

### Key Features

- **Standard-Compliant**: Implements Industry 4.0 AAS specification (IDTA standards)
- **Asynchronous API**: Non-blocking operations suitable for behavior tree execution
- **Type-Safe**: Strongly-typed access to AAS elements
- **Submodel Support**: Read shells, submodels, and properties
- **Production-Ready**: Designed for distributed manufacturing systems

---

## Architecture

### System Integration

```
┌─────────────────────────────────────────────────┐
│           MAS-BT Behavior Tree Agent            │
├─────────────────────────────────────────────────┤
│                                                 │
│  ┌───────────────┐  ┌─────────────────────┐   │
│  │  BT Nodes     │  │  AAS-Sharp-Client   │   │
│  │  (Config/     │──│  (Semantic Layer)   │   │
│  │   Monitoring) │  └──────────┬──────────┘   │
│  └───────────────┘             │               │
│                                │               │
│  ┌───────────────┐  ┌──────────▼──────────┐   │
│  │ SkillSharp    │  │   I40Sharp          │   │
│  │ Client        │  │   Messaging         │   │
│  │ (OPC UA)      │  │   (MQTT/I4.0)       │   │
│  └───────┬───────┘  └──────────┬──────────┘   │
└──────────┼──────────────────────┼──────────────┘
           │                      │
           ▼                      ▼
    ┌─────────────┐        ┌──────────────┐
    │  OPC UA     │        │  MQTT Broker │
    │  Server     │        │              │
    └─────────────┘        └──────────────┘
                                  │
                                  ▼
                          ┌──────────────┐
                          │  AAS Server  │
                          │  Repository  │
                          └──────────────┘
```

### Data Flow

1. **Configuration Phase**: BT nodes load AAS data via AAS-Sharp-Client
2. **Semantic Enrichment**: AAS provides context for OPC UA operations
3. **Runtime Execution**: Skills and constraints reference AAS metadata
4. **State Updates**: Changes propagate through MQTT using I4.0 messaging

### Layer Responsibilities

| Layer | Technology | Responsibility |
|-------|-----------|---------------|
| **Execution** | OPC UA + SkillSharp | Real-time machine control |
| **Semantic** | AAS + AAS-Sharp-Client | Process knowledge & metadata |
| **Control** | Behavior Trees | Decision-making logic |
| **Communication** | MQTT + I40Sharp | Holonic coordination |

---

## Core Concepts

### Asset Administration Shell (AAS)

An **AAS** is a digital twin representation of a physical or logical asset. It contains:
- **Shell**: The container identifying the asset
- **Submodels**: Semantic aspects of the asset (capabilities, nameplate, schedules)
- **Properties**: Data elements within submodels

### Shell Structure

```
Asset Administration Shell (AAS)
├── Id: "ResourceHolon_RH2"
├── IdShort: "AssemblyStation01"
└── Submodels
    ├── Nameplate
    ├── Skills
    ├── CapabilityDescription
    ├── MachineSchedule
    ├── ProcessChain
    └── ProductionPlan
```

### Submodel

A **submodel** represents a specific aspect or viewpoint of an asset:

```csharp
Submodel
├── IdShort: "Skills"
├── SemanticId: Reference to standard definition
└── SubmodelElements
    ├── Property: "StartupSkillDuration"
    ├── Property: "AssemblySkillName"
    └── Collection: "AvailableSkills"
```

### Property

A **property** is a named data element with value and type:

```csharp
Property
├── IdShort: "ManufacturerName"
├── Value: "ACME Industries"
├── ValueType: "xs:string"
└── SemanticId: Reference to ECLASS or IEC CDD
```

---

## Installation & Setup

### Prerequisites

- **.NET 10.0** or higher
- **AAS Server** (e.g., Eclipse BaSyx, FA³ST)
- **Network connectivity** to AAS repository

### Adding the Library

The library is referenced as a project dependency:

```xml
<ItemGroup>
  <ProjectReference Include="../AAS-Sharp-Client/AAS Sharp Client.csproj" />
</ItemGroup>
```

### Configuration

Configure AAS endpoint in `config.json`:

```json
{
  "AasEndpoint": "http://localhost:4001/aas",
  "AgentId": "ResourceHolon_RH2",
  "AgentRole": "ResourceHolon",
  "DefaultTimeout": 5000
}
```

### Initialization

Initialize AAS client in behavior tree context:

```csharp
// In ConnectToAasServer node or similar
var aasClient = new AasSharpClient(
    endpoint: config.AasEndpoint,
    timeout: TimeSpan.FromSeconds(5)
);

// Store in BT context for reuse
Context.Set("AASClient", aasClient);
```

---

## API Reference

### AasSharpClient Class

Main entry point for AAS operations.

#### Constructor

```csharp
public AasSharpClient(string endpoint, TimeSpan? timeout = null)
```

**Parameters:**
- `endpoint` (string): URL of the AAS server (e.g., "http://localhost:4001/aas")
- `timeout` (TimeSpan?): Optional request timeout (default: 5 seconds)

**Example:**
```csharp
var client = new AasSharpClient("http://aas-server:4001/aas");
```

---

### Core Methods

#### GetShellByIdAsync

Retrieves a complete Asset Administration Shell by its identifier.

```csharp
public async Task<AssetAdministrationShell> GetShellByIdAsync(string shellId)
```

**Parameters:**
- `shellId` (string): Unique identifier of the shell (e.g., "ResourceHolon_RH2")

**Returns:**
- `AssetAdministrationShell`: Complete shell object with metadata and submodel references

**Throws:**
- `AasNotFoundException`: Shell not found
- `AasConnectionException`: Connection to server failed
- `AasTimeoutException`: Request timed out

**Example:**
```csharp
var shell = await aasClient.GetShellByIdAsync("ResourceHolon_RH2");
Console.WriteLine($"Shell: {shell.IdShort}");
Console.WriteLine($"Submodels: {shell.Submodels.Count}");
```

---

#### GetSubmodelByIdAsync

Retrieves a specific submodel from a shell.

```csharp
public async Task<Submodel> GetSubmodelByIdAsync(
    string shellId, 
    string submodelIdShort
)
```

**Parameters:**
- `shellId` (string): ID of the shell containing the submodel
- `submodelIdShort` (string): Short ID of the submodel (e.g., "Skills", "Nameplate")

**Returns:**
- `Submodel`: Submodel object with all properties and elements

**Throws:**
- `AasNotFoundException`: Shell or submodel not found
- `AasConnectionException`: Connection failed

**Example:**
```csharp
var skills = await aasClient.GetSubmodelByIdAsync(
    "ResourceHolon_RH2", 
    "Skills"
);

// Access properties
foreach (var element in skills.SubmodelElements)
{
    Console.WriteLine($"{element.IdShort}: {element.Value}");
}
```

---

#### GetSubmodelAsync (Convenience)

Simplified method to get submodel using shell reference.

```csharp
public async Task<Submodel> GetSubmodelAsync(
    AssetAdministrationShell shell,
    string submodelIdShort
)
```

**Example:**
```csharp
var shell = await aasClient.GetShellByIdAsync("ResourceHolon_RH2");
var nameplate = await aasClient.GetSubmodelAsync(shell, "Nameplate");
```

---

#### GetPropertyValueAsync

Retrieves a single property value directly.

```csharp
public async Task<T> GetPropertyValueAsync<T>(
    string shellId,
    string submodelIdShort,
    string propertyIdShort
)
```

**Type Parameters:**
- `T`: Expected type of the property value

**Parameters:**
- `shellId` (string): Shell identifier
- `submodelIdShort` (string): Submodel short ID
- `propertyIdShort` (string): Property short ID

**Returns:**
- `T`: Typed property value

**Example:**
```csharp
// Get manufacturer name directly
var manufacturer = await aasClient.GetPropertyValueAsync<string>(
    "ResourceHolon_RH2",
    "Nameplate",
    "ManufacturerName"
);

// Get numeric property
var duration = await aasClient.GetPropertyValueAsync<int>(
    "ResourceHolon_RH2",
    "Skills",
    "StartupSkillDuration"
);
```

---

#### ListSubmodelsAsync

Lists all available submodels for a shell.

```csharp
public async Task<IEnumerable<SubmodelReference>> ListSubmodelsAsync(
    string shellId
)
```

**Returns:**
- `IEnumerable<SubmodelReference>`: Collection of submodel references

**Example:**
```csharp
var submodelRefs = await aasClient.ListSubmodelsAsync("ResourceHolon_RH2");
foreach (var submodel in submodelRefs)
{
    Console.WriteLine($"- {submodel.IdShort}");
}
```

---

### Data Models

#### AssetAdministrationShell

```csharp
public class AssetAdministrationShell
{
    public string Id { get; set; }
    public string IdShort { get; set; }
    public AssetInformation AssetInformation { get; set; }
    public List<SubmodelReference> Submodels { get; set; }
    public string Description { get; set; }
}
```

#### Submodel

```csharp
public class Submodel
{
    public string Id { get; set; }
    public string IdShort { get; set; }
    public Reference SemanticId { get; set; }
    public List<SubmodelElement> SubmodelElements { get; set; }
    public string Description { get; set; }
}
```

#### Property

```csharp
public class Property : SubmodelElement
{
    public string IdShort { get; set; }
    public string Value { get; set; }
    public string ValueType { get; set; }
    public Reference SemanticId { get; set; }
}
```

---

## Integration with MAS-BT

### Behavior Tree Node Integration

AAS-Sharp-Client is designed to be used within BT configuration nodes.

#### Standard Integration Pattern

```csharp
public class ReadCapabilityDescriptionSMNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public override async Task<NodeStatus> Execute()
    {
        try
        {
            // 1. Get AAS client from context
            var aasClient = Context.Get<AasSharpClient>("AASClient");
            
            // 2. Fetch submodel
            var capabilitySM = await aasClient.GetSubmodelByIdAsync(
                AgentId, 
                "CapabilityDescription"
            );
            
            // 3. Store in context for other nodes
            Context.Set("capabilitySM", capabilitySM);
            Context.Set($"CapabilityDescription_{AgentId}", capabilitySM);
            
            Logger.LogInformation(
                "Successfully loaded CapabilityDescription for {AgentId}", 
                AgentId
            );
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load CapabilityDescription");
            return NodeStatus.Failure;
        }
    }
}
```

### Context Key Conventions

Store AAS data in BTContext using these naming conventions:

```csharp
// Generic (for current agent)
Context.Set("shell", shell);
Context.Set("capabilitySM", capabilitySubmodel);
Context.Set("skills", skillsSubmodel);
Context.Set("nameplate", nameplateSubmodel);
Context.Set("schedule", scheduleSubmodel);

// Agent-specific (for multi-agent scenarios)
Context.Set($"Shell_{AgentId}", shell);
Context.Set($"CapabilityDescription_{AgentId}", capability);
Context.Set($"Skills_{AgentId}", skills);
Context.Set($"Nameplate_{AgentId}", nameplate);
Context.Set($"MachineSchedule_{AgentId}", schedule);
```

### Configuration Nodes Using AAS

The following nodes use AAS-Sharp-Client:

| Node | Purpose | Submodel |
|------|---------|----------|
| `ReadShellNode` | Load complete shell | - |
| `ReadCapabilityDescriptionSMNode` | Load capabilities | CapabilityDescription |
| `ReadSkillsSMNode` | Load available skills | Skills |
| `ReadNameplateSMNode` | Load machine metadata | Nameplate |
| `ReadMachineScheduleNode` | Load schedules | MachineSchedule |

---

## Usage Examples

### Example 1: Basic Shell Loading

```csharp
using AasSharpClient;

// Initialize client
var aasClient = new AasSharpClient("http://localhost:4001/aas");

// Load shell
var shell = await aasClient.GetShellByIdAsync("ResourceHolon_RH2");

Console.WriteLine($"Loaded shell: {shell.IdShort}");
Console.WriteLine($"Asset Type: {shell.AssetInformation.AssetType}");

// List available submodels
foreach (var submodelRef in shell.Submodels)
{
    Console.WriteLine($"- Submodel: {submodelRef.IdShort}");
}
```

**Output:**
```
Loaded shell: AssemblyStation01
Asset Type: Module
- Submodel: Nameplate
- Submodel: Skills
- Submodel: CapabilityDescription
- Submodel: MachineSchedule
```

---

### Example 2: Loading Skills Submodel

```csharp
var aasClient = new AasSharpClient("http://localhost:4001/aas");

// Get Skills submodel
var skillsSM = await aasClient.GetSubmodelByIdAsync(
    "ResourceHolon_RH2",
    "Skills"
);

// Extract skill information
var skillElements = skillsSM.SubmodelElements
    .OfType<Collection>()
    .FirstOrDefault(c => c.IdShort == "AvailableSkills");

if (skillElements != null)
{
    foreach (var skill in skillElements.Value)
    {
        var name = skill.GetProperty("Name")?.Value;
        var duration = skill.GetProperty("Duration")?.Value;
        
        Console.WriteLine($"Skill: {name}, Duration: {duration}ms");
    }
}
```

**Output:**
```
Skill: StartupSkill, Duration: 5000ms
Skill: AssemblySkill, Duration: 30000ms
Skill: ShutdownSkill, Duration: 3000ms
```

---

### Example 3: Reading Machine Schedule

```csharp
var aasClient = new AasSharpClient("http://localhost:4001/aas");

// Get MachineSchedule submodel
var scheduleSM = await aasClient.GetSubmodelByIdAsync(
    "ResourceHolon_RH2",
    "MachineSchedule"
);

// Access InitialSchedule
var initialSchedule = scheduleSM.GetSubmodelElement("InitialSchedule");
Console.WriteLine($"Initial Schedule: {initialSchedule}");

// Access ActualSchedule with drift detection
var actualSchedule = scheduleSM.GetSubmodelElement("ActualSchedule");
var lastUpdated = actualSchedule.GetProperty("LastTimeUpdated")?.Value;
var bookedSlots = actualSchedule.GetCollection("BookedSlots");

Console.WriteLine($"Last Updated: {lastUpdated}");
Console.WriteLine($"Booked Slots: {bookedSlots.Count}");

// Check schedule freshness
var lastUpdateTime = DateTime.Parse(lastUpdated);
var age = DateTime.UtcNow - lastUpdateTime;

if (age.TotalMinutes > 5)
{
    Console.WriteLine("⚠️ WARNING: Schedule may be stale");
}
```

---

### Example 4: Loading Capability Descriptions

```csharp
var aasClient = new AasSharpClient("http://localhost:4001/aas");

// Get CapabilityDescription submodel
var capabilitySM = await aasClient.GetSubmodelByIdAsync(
    "ResourceHolon_RH2",
    "CapabilityDescription"
);

// Extract capabilities
var capabilities = capabilitySM.GetCollection("Capabilities");

foreach (var capability in capabilities)
{
    var capName = capability.GetProperty("Name")?.Value;
    var capType = capability.GetProperty("Type")?.Value;
    var duration = capability.GetProperty("EstimatedDuration")?.Value;
    
    Console.WriteLine($"Capability: {capName}");
    Console.WriteLine($"  Type: {capType}");
    Console.WriteLine($"  Duration: {duration}ms");
    Console.WriteLine();
}
```

**Output:**
```
Capability: Assembly
  Type: Production
  Duration: 30000ms

Capability: QualityCheck
  Type: Inspection
  Duration: 15000ms
```

---

### Example 5: Reading Nameplate Information

```csharp
var aasClient = new AasSharpClient("http://localhost:4001/aas");

// Get Nameplate submodel
var nameplateSM = await aasClient.GetSubmodelByIdAsync(
    "ResourceHolon_RH2",
    "Nameplate"
);

// Extract nameplate properties
var manufacturer = nameplateSM.GetPropertyValue<string>("ManufacturerName");
var productDesignation = nameplateSM.GetPropertyValue<string>(
    "ManufacturerProductDesignation"
);
var serialNumber = nameplateSM.GetPropertyValue<string>("SerialNumber");
var yearOfConstruction = nameplateSM.GetPropertyValue<string>(
    "YearOfConstruction"
);

Console.WriteLine("Machine Information:");
Console.WriteLine($"  Manufacturer: {manufacturer}");
Console.WriteLine($"  Model: {productDesignation}");
Console.WriteLine($"  Serial: {serialNumber}");
Console.WriteLine($"  Year: {yearOfConstruction}");
```

**Output:**
```
Machine Information:
  Manufacturer: ACME Industries
  Model: Assembly Station Pro
  Serial: AS-2024-001
  Year: 2024
```

---

### Example 6: BT Context Integration

Complete example showing integration in behavior tree execution:

```csharp
using MAS_BT.Core;
using Microsoft.Extensions.Logging;

// Behavior Tree Context
var context = new BTContext
{
    AgentId = "ResourceHolon_RH2",
    AgentRole = "ResourceHolon"
};

// Initialize AAS client
var aasClient = new AasSharpClient("http://localhost:4001/aas");
context.Set("AASClient", aasClient);

// Configuration sequence
var nodes = new List<BTNode>
{
    new ReadShellNode { AgentId = context.AgentId },
    new ReadCapabilityDescriptionSMNode { AgentId = context.AgentId },
    new ReadSkillsSMNode { AgentId = context.AgentId },
    new ReadNameplateSMNode { AgentId = context.AgentId },
    new ReadMachineScheduleNode { AgentId = context.AgentId }
};

// Execute configuration
foreach (var node in nodes)
{
    node.Initialize(context, logger);
    var result = await node.Execute();
    
    if (result != NodeStatus.Success)
    {
        Console.WriteLine($"❌ Failed: {node.Name}");
        break;
    }
    
    Console.WriteLine($"✅ Success: {node.Name}");
}

// Verify context is populated
var shell = context.Get<object>("shell");
var capabilities = context.Get<object>("capabilitySM");
var skills = context.Get<object>("skills");
var nameplate = context.Get<object>("nameplate");
var schedule = context.Get<object>("schedule");

Console.WriteLine("\nContext State:");
Console.WriteLine($"  Shell: {shell != null}");
Console.WriteLine($"  Capabilities: {capabilities != null}");
Console.WriteLine($"  Skills: {skills != null}");
Console.WriteLine($"  Nameplate: {nameplate != null}");
Console.WriteLine($"  Schedule: {schedule != null}");
```

---

## Common Patterns

### Pattern 1: Lazy Loading

Load AAS data only when needed:

```csharp
public class AasLazyLoader
{
    private readonly BTContext _context;
    private readonly AasSharpClient _client;
    private readonly string _agentId;
    
    public async Task<Submodel> GetOrLoadSkillsAsync()
    {
        // Check if already loaded
        if (_context.Has("skills"))
        {
            return _context.Get<Submodel>("skills");
        }
        
        // Load on first access
        var skills = await _client.GetSubmodelByIdAsync(_agentId, "Skills");
        _context.Set("skills", skills);
        
        return skills;
    }
}
```

---

### Pattern 2: Caching with Expiration

Implement time-based cache invalidation:

```csharp
public class AasCachedAccess
{
    private readonly Dictionary<string, (object Data, DateTime Expiry)> _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    
    public async Task<Submodel> GetScheduleAsync()
    {
        var key = "MachineSchedule";
        
        // Check cache
        if (_cache.TryGetValue(key, out var cached))
        {
            if (DateTime.UtcNow < cached.Expiry)
            {
                return (Submodel)cached.Data;
            }
        }
        
        // Reload from AAS
        var schedule = await _client.GetSubmodelByIdAsync(_agentId, key);
        _cache[key] = (schedule, DateTime.UtcNow + _cacheDuration);
        
        return schedule;
    }
}
```

---

### Pattern 3: Bulk Loading

Load multiple submodels efficiently:

```csharp
public async Task LoadAllSubmodelsAsync(string agentId)
{
    var submodels = new[] 
    { 
        "Nameplate", 
        "Skills", 
        "CapabilityDescription", 
        "MachineSchedule" 
    };
    
    // Load in parallel
    var tasks = submodels.Select(async sm =>
    {
        try
        {
            var data = await _client.GetSubmodelByIdAsync(agentId, sm);
            return (sm, data, success: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Submodel}", sm);
            return (sm, null, success: false);
        }
    });
    
    var results = await Task.WhenAll(tasks);
    
    // Store successful loads
    foreach (var (name, data, success) in results)
    {
        if (success && data != null)
        {
            Context.Set(name.ToLower(), data);
        }
    }
}
```

---

### Pattern 4: Retry with Backoff

Handle transient failures:

```csharp
public async Task<Submodel> GetSubmodelWithRetryAsync(
    string shellId, 
    string submodelId,
    int maxRetries = 3
)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await _client.GetSubmodelByIdAsync(shellId, submodelId);
        }
        catch (AasConnectionException) when (i < maxRetries - 1)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, i));
            _logger.LogWarning(
                "AAS connection failed, retry {Attempt} after {Delay}s",
                i + 1, 
                delay.TotalSeconds
            );
            await Task.Delay(delay);
        }
    }
    
    throw new InvalidOperationException(
        $"Failed to load submodel after {maxRetries} attempts"
    );
}
```

---

### Pattern 5: Property Extraction Helper

Simplify property access:

```csharp
public static class SubmodelExtensions
{
    public static string GetPropertyValue(
        this Submodel submodel, 
        string propertyIdShort, 
        string defaultValue = ""
    )
    {
        var property = submodel.SubmodelElements
            .OfType<Property>()
            .FirstOrDefault(p => p.IdShort == propertyIdShort);
            
        return property?.Value ?? defaultValue;
    }
    
    public static T GetPropertyValue<T>(
        this Submodel submodel,
        string propertyIdShort,
        T defaultValue = default
    )
    {
        var value = GetPropertyValue(submodel, propertyIdShort);
        
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }
        
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}

// Usage
var duration = skillsSM.GetPropertyValue<int>("StartupSkillDuration", 5000);
var manufacturer = nameplateSM.GetPropertyValue("ManufacturerName", "Unknown");
```

---

## Submodel Specifications

### Nameplate Submodel

Machine identification and metadata.

**Standard Elements:**
```
Nameplate/
├── ManufacturerName (string)
├── ManufacturerProductDesignation (string)
├── SerialNumber (string)
├── YearOfConstruction (string)
├── ProductInstanceUri (anyURI, optional)
└── HardwareVersion (string, optional)
```

**Example Data:**
```json
{
  "idShort": "Nameplate",
  "submodelElements": [
    {"idShort": "ManufacturerName", "value": "ACME Industries"},
    {"idShort": "ManufacturerProductDesignation", "value": "Assembly Station Pro"},
    {"idShort": "SerialNumber", "value": "AS-2024-001"},
    {"idShort": "YearOfConstruction", "value": "2024"}
  ]
}
```

---

### Skills Submodel

Available machine skills and their parameters.

**Standard Elements:**
```
Skills/
└── AvailableSkills (Collection)
    └── Skill (SubmodelElementCollection)
        ├── Name (string)
        ├── Description (string)
        ├── Duration (int, milliseconds)
        ├── ParameterSet (Collection, optional)
        └── Constraints (Collection, optional)
```

**Example Data:**
```json
{
  "idShort": "Skills",
  "submodelElements": [
    {
      "idShort": "AvailableSkills",
      "value": [
        {
          "idShort": "StartupSkill",
          "value": [
            {"idShort": "Name", "value": "StartupSkill"},
            {"idShort": "Description", "value": "Initialize module"},
            {"idShort": "Duration", "value": "5000", "valueType": "xs:int"}
          ]
        },
        {
          "idShort": "AssemblySkill",
          "value": [
            {"idShort": "Name", "value": "AssemblySkill"},
            {"idShort": "Description", "value": "Assemble parts"},
            {"idShort": "Duration", "value": "30000", "valueType": "xs:int"}
          ]
        }
      ]
    }
  ]
}
```

---

### CapabilityDescription Submodel

Semantic description of what the machine can do.

**Standard Elements:**
```
CapabilityDescription/
└── Capabilities (Collection)
    └── Capability (SubmodelElementCollection)
        ├── Name (string)
        ├── Type (string) - e.g., "Production", "Inspection"
        ├── EstimatedDuration (int, milliseconds)
        ├── RequiredMaterials (Collection, optional)
        ├── RequiredTools (Collection, optional)
        └── ProcessParameters (Collection, optional)
```

**Example Data:**
```json
{
  "idShort": "CapabilityDescription",
  "submodelElements": [
    {
      "idShort": "Capabilities",
      "value": [
        {
          "idShort": "Assembly",
          "value": [
            {"idShort": "Name", "value": "Assembly"},
            {"idShort": "Type", "value": "Production"},
            {"idShort": "EstimatedDuration", "value": "30000"}
          ]
        },
        {
          "idShort": "QualityCheck",
          "value": [
            {"idShort": "Name", "value": "QualityCheck"},
            {"idShort": "Type", "value": "Inspection"},
            {"idShort": "EstimatedDuration", "value": "15000"}
          ]
        }
      ]
    }
  ]
}
```

---

### MachineSchedule Submodel

Production schedule with drift detection.

**Standard Elements:**
```
MachineSchedule/
├── InitialSchedule (SubmodelElementCollection)
│   └── Steps (Collection)
│       └── Step (SubmodelElementCollection)
│           ├── TaskId (string)
│           ├── ExpectedStartTime (dateTime)
│           ├── ExpectedEndTime (dateTime)
│           └── ProcessParameters (Collection)
└── ActualSchedule (SubmodelElementCollection)
    ├── BookedSlots (Collection)
    ├── TentativeSlots (Collection)
    └── LastTimeUpdated (dateTime)
```

**Example Data:**
```json
{
  "idShort": "MachineSchedule",
  "submodelElements": [
    {
      "idShort": "InitialSchedule",
      "value": [
        {"idShort": "Steps", "value": []}
      ]
    },
    {
      "idShort": "ActualSchedule",
      "value": [
        {"idShort": "BookedSlots", "value": []},
        {"idShort": "TentativeSlots", "value": []},
        {
          "idShort": "LastTimeUpdated",
          "value": "2024-12-08T16:00:00Z",
          "valueType": "xs:dateTime"
        }
      ]
    }
  ]
}
```

**Schedule Drift Detection:**
```csharp
var lastUpdated = scheduleSM.GetPropertyValue<DateTime>("LastTimeUpdated");
var age = DateTime.UtcNow - lastUpdated;

if (age.TotalMinutes > 5)
{
    // Schedule may be stale - trigger re-validation or cost increase
    Logger.LogWarning("Schedule drift detected: {Age} minutes old", age.TotalMinutes);
}
```

---

## Error Handling

### Exception Types

The library throws specific exceptions for different error conditions:

```csharp
// Base exception
public class AasException : Exception

// Specific exceptions
public class AasNotFoundException : AasException
public class AasConnectionException : AasException
public class AasTimeoutException : AasException
public class AasAuthenticationException : AasException
public class AasParsingException : AasException
```

### Handling Errors in BT Nodes

```csharp
public override async Task<NodeStatus> Execute()
{
    try
    {
        var aasClient = Context.Get<AasSharpClient>("AASClient");
        var submodel = await aasClient.GetSubmodelByIdAsync(AgentId, "Skills");
        
        Context.Set("skills", submodel);
        return NodeStatus.Success;
    }
    catch (AasNotFoundException ex)
    {
        // Shell or submodel not found
        Logger.LogError(ex, "AAS resource not found for {AgentId}", AgentId);
        return NodeStatus.Failure;
    }
    catch (AasConnectionException ex)
    {
        // Network/connection issue - might be transient
        Logger.LogWarning(ex, "AAS connection failed, may retry");
        return NodeStatus.Running; // Or Failure depending on retry strategy
    }
    catch (AasTimeoutException ex)
    {
        // Timeout - AAS server slow or unresponsive
        Logger.LogWarning(ex, "AAS request timed out");
        return NodeStatus.Failure;
    }
    catch (Exception ex)
    {
        // Unexpected error
        Logger.LogError(ex, "Unexpected error accessing AAS");
        return NodeStatus.Failure;
    }
}
```

### Retry Strategies

```csharp
// Option 1: Use RetryUntilSuccessDecorator in BT
<RetryUntilSuccess numCycles="3" delay="2000">
    <ReadSkillsSM AgentId="ResourceHolon_RH2" />
</RetryUntilSuccess>

// Option 2: Implement retry in node
public async Task<NodeStatus> ExecuteWithRetry(int maxRetries = 3)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            var result = await Execute();
            if (result == NodeStatus.Success)
            {
                return result;
            }
        }
        catch (AasConnectionException) when (attempt < maxRetries)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            Logger.LogInformation("Retry {Attempt}/{Max}", attempt, maxRetries);
        }
    }
    
    return NodeStatus.Failure;
}
```

---

## Best Practices

### 1. Initialize AAS Client Once

Don't create new clients for every operation:

```csharp
// ❌ BAD - Creates client every time
public override async Task<NodeStatus> Execute()
{
    var client = new AasSharpClient(endpoint); // Don't do this!
    var data = await client.GetSubmodelByIdAsync(agentId, "Skills");
}

// ✅ GOOD - Reuse client from context
public override async Task<NodeStatus> Execute()
{
    var client = Context.Get<AasSharpClient>("AASClient");
    var data = await client.GetSubmodelByIdAsync(agentId, "Skills");
}
```

---

### 2. Use Context Naming Conventions

Follow established naming patterns:

```csharp
// ✅ GOOD - Standard names
Context.Set("shell", shell);
Context.Set("skills", skills);
Context.Set($"CapabilityDescription_{AgentId}", capability);

// ❌ BAD - Inconsistent names
Context.Set("myShell", shell);
Context.Set("skillData", skills);
Context.Set("cap", capability);
```

---

### 3. Validate Data After Loading

Always validate that loaded data is complete:

```csharp
var skills = await aasClient.GetSubmodelByIdAsync(agentId, "Skills");

// Validate
if (skills == null || skills.SubmodelElements == null)
{
    Logger.LogError("Skills submodel is empty or invalid");
    return NodeStatus.Failure;
}

var skillList = skills.GetCollection("AvailableSkills");
if (skillList == null || !skillList.Any())
{
    Logger.LogWarning("No skills found in submodel");
}

Context.Set("skills", skills);
```

---

### 4. Handle Schedule Drift

Always check schedule freshness:

```csharp
var schedule = await aasClient.GetSubmodelByIdAsync(agentId, "MachineSchedule");
var lastUpdated = schedule.GetPropertyValue<DateTime>("LastTimeUpdated");
var age = DateTime.UtcNow - lastUpdated;

Context.Set("schedule", schedule);
Context.Set("schedule_age_minutes", age.TotalMinutes);

if (age.TotalMinutes > 5)
{
    Logger.LogWarning("Schedule is {Age} minutes old - may be stale", age.TotalMinutes);
    // Consider triggering re-validation
}
```

---

### 5. Log AAS Operations

Log all AAS interactions for debugging:

```csharp
Logger.LogDebug("Loading submodel {Submodel} for {AgentId}", submodelId, agentId);

var submodel = await aasClient.GetSubmodelByIdAsync(agentId, submodelId);

Logger.LogInformation(
    "Loaded {Submodel}: {ElementCount} elements",
    submodelId,
    submodel.SubmodelElements.Count
);
```

---

### 6. Use Timeout Configuration

Configure appropriate timeouts:

```csharp
// For production - reasonable timeout
var client = new AasSharpClient(endpoint, TimeSpan.FromSeconds(5));

// For development/debugging - longer timeout
var client = new AasSharpClient(endpoint, TimeSpan.FromSeconds(30));

// From configuration
var timeout = TimeSpan.FromSeconds(config.GetValue<int>("AasTimeoutSeconds"));
var client = new AasSharpClient(endpoint, timeout);
```

---

### 7. Separate Concerns

Keep AAS loading separate from business logic:

```csharp
// ✅ GOOD - Separate configuration and execution
// Configuration phase: Load AAS data
await new ReadSkillsSMNode().Execute();

// Execution phase: Use loaded data
var skills = Context.Get<Submodel>("skills");
var startupDuration = skills.GetPropertyValue<int>("StartupSkillDuration");

// ❌ BAD - Mixing concerns
public override async Task<NodeStatus> Execute()
{
    // Loading AAS
    var skills = await aasClient.GetSubmodelByIdAsync(...);
    
    // Business logic
    if (CheckSomething())
    {
        ExecuteSkill(...);
    }
    // Becomes hard to test and maintain
}
```

---

### 8. Cache Appropriately

Cache static data, refresh dynamic data:

```csharp
// Static data - cache indefinitely
if (!Context.Has("nameplate"))
{
    var nameplate = await aasClient.GetSubmodelByIdAsync(agentId, "Nameplate");
    Context.Set("nameplate", nameplate);
}

// Dynamic data - reload periodically
var lastScheduleLoad = Context.Get<DateTime>("schedule_loaded_at");
if (DateTime.UtcNow - lastScheduleLoad > TimeSpan.FromMinutes(1))
{
    var schedule = await aasClient.GetSubmodelByIdAsync(agentId, "MachineSchedule");
    Context.Set("schedule", schedule);
    Context.Set("schedule_loaded_at", DateTime.UtcNow);
}
```

---

## Troubleshooting

### Issue: "AAS connection failed"

**Symptoms:**
- `AasConnectionException` thrown
- Cannot reach AAS server

**Solutions:**
1. Verify AAS server is running:
   ```bash
   curl http://localhost:4001/aas/health
   ```

2. Check network connectivity:
   ```bash
   ping aas-server-hostname
   ```

3. Verify endpoint configuration in `config.json`:
   ```json
   {
     "AasEndpoint": "http://localhost:4001/aas"
   }
   ```

4. Check firewall rules allow outbound connections to AAS port

---

### Issue: "Shell not found"

**Symptoms:**
- `AasNotFoundException` for shell ID
- 404 errors in logs

**Solutions:**
1. Verify shell ID matches exactly:
   ```csharp
   // Case-sensitive!
   var shell = await client.GetShellByIdAsync("ResourceHolon_RH2");
   ```

2. List all shells to find correct ID:
   ```csharp
   var shells = await client.ListAllShellsAsync();
   foreach (var shell in shells)
   {
       Console.WriteLine($"Available: {shell.Id}");
   }
   ```

3. Ensure shell exists in AAS repository (check AAS admin UI)

---

### Issue: "Submodel not found"

**Symptoms:**
- `AasNotFoundException` for submodel
- Submodel ID seems correct

**Solutions:**
1. List available submodels:
   ```csharp
   var shell = await client.GetShellByIdAsync(agentId);
   foreach (var sm in shell.Submodels)
   {
       Console.WriteLine($"Available: {sm.IdShort}");
   }
   ```

2. Check submodel ID spelling (case-sensitive):
   - ✅ "Skills"
   - ❌ "skills"
   - ❌ "SkillsSM"

---

### Issue: "Request timeout"

**Symptoms:**
- `AasTimeoutException` after configured timeout
- Operations take too long

**Solutions:**
1. Increase timeout:
   ```csharp
   var client = new AasSharpClient(endpoint, TimeSpan.FromSeconds(30));
   ```

2. Check AAS server performance (CPU, memory, network)

3. Verify no network latency issues

4. Consider caching frequently accessed data

---

### Issue: "Property has null value"

**Symptoms:**
- Expected property value is null
- `GetPropertyValue` returns default

**Solutions:**
1. Verify property exists:
   ```csharp
   var property = submodel.SubmodelElements
       .OfType<Property>()
       .FirstOrDefault(p => p.IdShort == "ExpectedProperty");
   
   if (property == null)
   {
       Console.WriteLine("Property not found!");
   }
   ```

2. Check property spelling (case-sensitive)

3. Inspect submodel structure:
   ```csharp
   foreach (var element in submodel.SubmodelElements)
   {
       Console.WriteLine($"Element: {element.IdShort} = {element.Value}");
   }
   ```

---

### Issue: "Context doesn't have AAS client"

**Symptoms:**
- `Context.Get<AasSharpClient>("AASClient")` returns null
- Nodes fail to get client

**Solutions:**
1. Ensure client is initialized before nodes execute:
   ```csharp
   // In initialization sequence
   var aasClient = new AasSharpClient(endpoint);
   Context.Set("AASClient", aasClient);
   
   // Then run nodes that use it
   await new ReadSkillsSMNode().Execute();
   ```

2. Check initialization order in behavior tree

3. Verify context is shared across nodes

---

### Issue: "Schedule drift warnings"

**Symptoms:**
- Warnings about stale schedules
- Schedule age exceeds threshold

**Solutions:**
1. Increase acceptable schedule age:
   ```csharp
   var maxAgeMinutes = 10; // Instead of 5
   if (age.TotalMinutes > maxAgeMinutes)
   {
       // Trigger warning
   }
   ```

2. Implement periodic schedule refresh:
   ```csharp
   <Parallel>
       <Sequence>
           <!-- Main execution logic -->
       </Sequence>
       <Repeat>
           <Sequence>
               <Wait duration="60000" /> <!-- Wait 1 minute -->
               <ReadMachineSchedule AgentId="ResourceHolon_RH2" />
           </Sequence>
       </Repeat>
   </Parallel>
   ```

3. Check why schedule updates are slow (AAS server issue?)

---

## FAQ

### Q: What is the difference between AAS-Sharp-Client and I40Sharp.Messaging?

**A:** They serve different layers:
- **AAS-Sharp-Client**: Semantic layer - reads production metadata, capabilities, schedules from AAS repositories
- **I40Sharp.Messaging**: Communication layer - sends/receives Industry 4.0 messages via MQTT for holonic coordination

Both are used together in MAS-BT for complete holonic control.

---

### Q: Do I need to create a new AAS client for each agent?

**A:** No. Reuse a single client instance stored in BTContext:

```csharp
// Initialize once
var client = new AasSharpClient(endpoint);
Context.Set("AASClient", client);

// Reuse in all nodes
var client = Context.Get<AasSharpClient>("AASClient");
```

---

### Q: How often should I reload AAS data?

**A:** It depends on data type:
- **Static data** (Nameplate, Skills, Capabilities): Load once during initialization
- **Semi-static data** (InitialSchedule): Load once, may reload if explicitly updated
- **Dynamic data** (ActualSchedule): Reload periodically (every 1-5 minutes) or when schedule changes are detected

---

### Q: Can I write to AAS or only read?

**A:** Current implementation supports **read-only** operations. AAS updates typically happen through:
- OPC UA writes (for real-time state changes)
- MQTT messages (for coordination and scheduling)
- Direct AAS server API (for planning/configuration changes)

---

### Q: What happens if AAS server is down?

**A:** Behavior depends on implementation:
- **Fail-fast**: Return `NodeStatus.Failure` immediately
- **Retry**: Use retry decorators or retry logic in nodes
- **Fallback**: Use cached data if available (implement in node logic)

Recommended: Use retry with backoff for transient failures, fail for permanent issues.

---

### Q: How do I debug AAS data loading issues?

**A:** Enable detailed logging:

```csharp
// In configuration
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "AasSharpClient": "Debug",
      "MAS_BT.Nodes.Configuration": "Debug"
    }
  }
}
```

Then check logs for:
- AAS requests and responses
- Loaded submodel structure
- Property values
- Error details

---

### Q: Can I load multiple submodels in parallel?

**A:** Yes, use `Task.WhenAll`:

```csharp
var tasks = new[]
{
    client.GetSubmodelByIdAsync(agentId, "Skills"),
    client.GetSubmodelByIdAsync(agentId, "Nameplate"),
    client.GetSubmodelByIdAsync(agentId, "CapabilityDescription")
};

var results = await Task.WhenAll(tasks);

Context.Set("skills", results[0]);
Context.Set("nameplate", results[1]);
Context.Set("capabilitySM", results[2]);
```

This improves performance when loading multiple submodels.

---

### Q: How do I handle different AAS server versions?

**A:** AAS-Sharp-Client aims to be compatible with AAS v3.0 specification. For other versions:
1. Check library version compatibility
2. Update library if needed
3. Implement version-specific adapters if necessary

---

### Q: Should I validate submodel structure after loading?

**A:** Yes, especially for critical data:

```csharp
var skills = await client.GetSubmodelByIdAsync(agentId, "Skills");

// Validate required elements
if (skills.SubmodelElements == null || !skills.SubmodelElements.Any())
{
    throw new InvalidOperationException("Skills submodel is empty");
}

var availableSkills = skills.GetCollection("AvailableSkills");
if (availableSkills == null)
{
    throw new InvalidOperationException("AvailableSkills not found");
}
```

---

### Q: Can I extend AAS-Sharp-Client with custom methods?

**A:** Yes, use extension methods:

```csharp
public static class AasClientExtensions
{
    public static async Task<List<string>> GetAllSkillNamesAsync(
        this AasSharpClient client,
        string agentId
    )
    {
        var skills = await client.GetSubmodelByIdAsync(agentId, "Skills");
        var skillCollection = skills.GetCollection("AvailableSkills");
        
        return skillCollection
            .Select(s => s.GetProperty("Name")?.Value)
            .Where(n => n != null)
            .ToList();
    }
}

// Usage
var skillNames = await aasClient.GetAllSkillNamesAsync("ResourceHolon_RH2");
```

---

### Q: How do I handle missing optional properties?

**A:** Use safe access patterns:

```csharp
// Option 1: Null-conditional operator
var hardwareVersion = nameplate
    .SubmodelElements
    .OfType<Property>()
    .FirstOrDefault(p => p.IdShort == "HardwareVersion")
    ?.Value;

// Option 2: Extension method with default
var hardwareVersion = nameplate.GetPropertyValue("HardwareVersion", "Unknown");

// Option 3: Try-get pattern
bool hasHardwareVersion = nameplate.TryGetPropertyValue(
    "HardwareVersion", 
    out string version
);
```

---

### Q: What's the recommended timeout value?

**A:** Depends on environment:
- **Local network**: 5 seconds
- **Cloud/WAN**: 10-30 seconds
- **Development**: 30-60 seconds (for debugging)

Configure via `config.json`:
```json
{
  "AasTimeoutSeconds": 10
}
```

---

## Conclusion

AAS-Sharp-Client is a critical component of the MAS-BT holonic control architecture, providing semantic interoperability through Industry 4.0 Asset Administration Shells. By following this documentation, you can:

- ✅ Integrate AAS data loading into behavior tree nodes
- ✅ Access machine capabilities, skills, and schedules
- ✅ Implement robust error handling and retry logic
- ✅ Follow best practices for caching and validation
- ✅ Troubleshoot common issues effectively

For additional support:
- Check [CONFIGURATION_NODES.md](../CONFIGURATION_NODES.md) for node-specific documentation
- Review [README.md](../README.md) for overall MAS-BT architecture
- Inspect example trees in `Trees/` directory
- Examine implementation in `Nodes/Configuration/` directory

---

**Last Updated:** December 8, 2024  
**Version:** 1.0  
**Library:** AAS-Sharp-Client for MAS-BT
