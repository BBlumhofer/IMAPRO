# MAS-BT Configuration Nodes

## ✅ Implementierte Behavior Tree Nodes

Alle **Configuration Nodes** aus der `specs.json` sind implementiert und integrieren den **I4.0 Sharp Messaging Client**:

### 1. ConnectToMessagingBrokerNode ⭐

**Zweck:** Stellt Verbindung zum MQTT Broker her und erstellt den MessagingClient.

**Inputs:**
- `Endpoint` (string): MQTT Broker Adresse (z.B. "localhost")
- `Port` (int): MQTT Broker Port (Standard: 1883)
- `Protocol` (string): "MQTT" (einzig unterstütztes Protokoll)
- `ClientId` (string, optional): Client ID (falls leer, wird AgentId verwendet)
- `DefaultTopic` (string, optional): Standard Topic für diesen Agent
- `Username` (string, optional): MQTT Authentifizierung
- `Password` (string, optional): MQTT Authentifizierung

**Outputs:**
- `Connected` (bool): True wenn Verbindung erfolgreich
- Context: `MessagingClient`, `MqttBroker`, `MqttPort`, `DefaultTopic`

**Beispiel:**
```csharp
var node = new ConnectToMessagingBrokerNode
{
    Endpoint = "localhost",
    Port = 1883,
    DefaultTopic = "factory/resources/rh2"
};
node.Initialize(context, logger);
var result = await node.Execute();
// result == NodeStatus.Success
// MessagingClient ist jetzt im Context verfügbar
```

### 2. ReadShellNode

**Zweck:** Lädt das komplette AAS Shell eines Agents.

**Inputs:**
- `AgentId` (string): Agent dessen Shell geladen wird (falls leer: Context.AgentId)
- `AasEndpoint` (string, optional): AAS Server URL

**Outputs:**
- `Shell` (object): Geladenes Shell Objekt
- Context: `Shell_{AgentId}`

### 3. ReadCapabilityDescriptionNode

**Zweck:** Lädt CapabilityDescription Submodel.

**Outputs:**
- `CapabilitySM` (object): Capability Beschreibung
- Context: `CapabilityDescription_{AgentId}`

### 4. ReadNameplateNode

**Zweck:** Lädt Nameplate Submodel (Maschinenmetadaten).

**Outputs:**
- `Nameplate` (object): Nameplate Daten
- Context: `Nameplate_{AgentId}`

### 5. ReadSkillsNode

**Zweck:** Lädt Skills Submodel.

**Outputs:**
- `Skills` (object): Verfügbare Skills
- Context: `Skills_{AgentId}`

### 6. ReadMachineScheduleNode

**Zweck:** Lädt MachineSchedule Submodel (InitialSchedule + ActualSchedule).

**Outputs:**
- `Schedule` (object): Schedule Daten
- Context: `MachineSchedule_{AgentId}`

### 7. ConnectToModuleNode

**Zweck:** Verbindet zu OPC UA Modul.

**Inputs:**
- `Endpoint` (string): OPC UA Endpunkt

**Outputs:**
- `Connected` (bool)
- Context: `OpcUaEndpoint`

*Hinweis: OPC UA Integration noch nicht implementiert (Placeholder)*

### 8. CoupleModuleNode ⭐

**Zweck:** Registriert Nachbarmodul zur Laufzeit und sendet Coupling-Nachricht über MQTT.

**Inputs:**
- `ModuleId` (string): ID des zu koppelnden Moduls

**Outputs:**
- `Coupled` (bool)
- Context: `CoupledModules` (Liste)

**Besonderheit:** Sendet I4.0 Message über MessagingClient!

```csharp
var message = new I40MessageBuilder()
    .From(Context.AgentId)
    .To("system")
    .WithType(I40MessageTypes.INFORM)
    .AddElement(new Property
    {
        IdShort = "CoupledModule",
        Value = ModuleId,
        ValueType = "xs:string"
    })
    .Build();

await messagingClient.PublishAsync(message, "factory/system/coupling");
```

### 9. EnsurePortsCoupledNode

**Zweck:** Prüft nach dem Lock, ob alle Ports eines Moduls über einen CoupleSkill verfügen und versetzt diese bei Bedarf in den Zustand `Running`, bevor der Startup-Skill ausgeführt wird.

**Inputs:**
- `ModuleName` (string): Modul, dessen Ports geprüft werden sollen
- `TimeoutSeconds` (int, optional): Zeitlimit pro Port für den Wechsel auf `Running`

**Outputs:**
- `portsCoupled` (bool): `true`, wenn alle Couple-Ports laufen oder keine existieren

**Besonderheiten:**
- Nutzt die port-spezifischen `RemotePort.CoupleAsync`-Methoden aus dem SkillSharp-Client.
- Bricht mit `Failure` ab, falls mindestens ein Couple-Skill nicht auf `Running` gebracht werden konnte.

## 🏗️ Architektur

### BTNode Basisklasse

```csharp
public abstract class BTNode
{
    public string Name { get; set; }
    public BTContext Context { get; set; }
    protected ILogger Logger { get; private set; }
    
    public abstract Task<NodeStatus> Execute();
    public virtual Task OnAbort();
    public virtual Task OnReset();
}

public enum NodeStatus
{
    Success,    // Node erfolgreich ausgeführt
    Failure,    // Node fehlgeschlagen
    Running     // Node läuft noch (async)
}
```

### BTContext

Shared State zwischen allen Behavior Tree Nodes:

```csharp
public class BTContext
{
    public string AgentId { get; set; }
    public string AgentRole { get; set; }
    
    public void Set<T>(string key, T value);
    public T? Get<T>(string key);
    public bool Has(string key);
    public void Remove(string key);
    public void Clear();
}
```

## 🚀 Verwendung

### Beispiel: Resource Holon Initialisierung

```csharp
// 1. Context erstellen
var context = new BTContext
{
    AgentId = "ResourceHolon_RH2",
    AgentRole = "ResourceHolon"
};

// 2. MQTT Verbindung herstellen
var connectNode = new ConnectToMessagingBrokerNode
{
    Endpoint = "localhost",
    Port = 1883,
    DefaultTopic = "factory/resources/rh2"
};
connectNode.Initialize(context, logger);
await connectNode.Execute();

// 3. AAS Daten laden
var readShellNode = new ReadShellNode();
readShellNode.Initialize(context, logger);
await readShellNode.Execute();

// 4. Capabilities laden
var readCapabilityNode = new ReadCapabilityDescriptionNode();
readCapabilityNode.Initialize(context, logger);
await readCapabilityNode.Execute();

// 5. Skills laden
var readSkillsNode = new ReadSkillsNode();
readSkillsNode.Initialize(context, logger);
await readSkillsNode.Execute();

// 6. Schedule laden
var readScheduleNode = new ReadMachineScheduleNode();
readScheduleNode.Initialize(context, logger);
await readScheduleNode.Execute();

// 7. Module koppeln
var coupleNode = new CoupleModuleNode { ModuleId = "RH3" };
coupleNode.Initialize(context, logger);
await coupleNode.Execute();

// MessagingClient ist jetzt verfügbar:
var client = context.Get<MessagingClient>("MessagingClient");
```

### Vollständiges Beispiel ausführen

```bash
cd /home/benjamin/AgentDevelopment/MAS-BT
dotnet run --project Examples/ResourceHolonInitialization.cs
```

**Voraussetzung:** MQTT Broker läuft auf localhost:1883

```bash
cd ../playground-v3
docker-compose up -d mosquitto
```

## 🔗 Integration

### Mit I4.0 Sharp Messaging

Der `ConnectToMessagingBrokerNode` erstellt einen vollständigen `MessagingClient`:

```csharp
var transport = new MqttTransport(Endpoint, Port, clientId);
var messagingClient = new MessagingClient(transport, defaultTopic);
await messagingClient.ConnectAsync();
Context.Set("MessagingClient", messagingClient);
```

Alle anderen Nodes können darauf zugreifen:

```csharp
var client = Context.Get<MessagingClient>("MessagingClient");
await client.PublishAsync(message, topic);
```

### Mit AAS Sharp Client

Die AAS Nodes (ReadShell, ReadCapability, etc.) sind vorbereitet für Integration mit dem AAS-Sharp-Client:

```csharp
// TODO: Integration beispiel
var aasClient = new AasSharpClient(endpoint);
var shell = await aasClient.GetShellAsync(agentId);
var capability = await aasClient.GetSubmodelAsync(agentId, "CapabilityDescription");
```

Aktuell: Placeholder-Implementierungen die Test-Daten zurückgeben.

## 📊 Context State nach Initialisierung

Nach erfolgreicher Ausführung aller Configuration Nodes:

```
Context State:
├── MessagingClient ✓
├── MqttBroker: "localhost"
├── MqttPort: 1883
├── DefaultTopic: "factory/resources/rh2"
├── Shell_ResourceHolon_RH2 ✓
├── CapabilityDescription_ResourceHolon_RH2 ✓
├── Skills_ResourceHolon_RH2 ✓
├── MachineSchedule_ResourceHolon_RH2 ✓
├── Nameplate_ResourceHolon_RH2 ✓
└── CoupledModules: ["ResourceHolon_RH3"]
```

## 🎯 Nächste Schritte

1. ✅ **Configuration Nodes** - Implementiert
2. ⏳ **Messaging Nodes** - Nächster Schritt
   - SendMessage
   - WaitForMessage
   - ReceiveOfferMessage
3. ⏳ **Product Nodes** - Für ProductHolon
   - AskForStepExecution
   - LoadRequiredCapabilities
4. ⏳ **Planning Nodes** - Für Bidding
   - ExecuteCapabilityMatchmaking
   - CalculateOffer
5. ⏳ **Monitoring Nodes** - Für Zustandsüberwachung
6. ⏳ **Skill Nodes** - Für OPC UA Skill Execution

## 🧪 Tests

Tests für Configuration Nodes erstellen:

```bash
cd /home/benjamin/AgentDevelopment/MAS-BT
dotnet test --filter "ConfigurationNodesTests"
```

## 💡 Design Entscheidungen

### Warum BTContext?

- **Shared State:** Alle Nodes teilen sich Daten (MessagingClient, AAS Daten)
- **Thread-Safe:** Lock-basierter Zugriff
- **Typisiert:** Get<T> mit generischen Typen
- **Flexibel:** Key-Value Store für beliebige Daten

### Warum async/await?

- MQTT Verbindungen sind asynchron
- AAS API Calls sind asynchron
- OPC UA Calls sind asynchron
- Behavior Trees müssen non-blocking sein

### Warum NodeStatus Enum?

- **Success:** Node erfolgreich → nächster Node
- **Failure:** Node fehlgeschlagen → Fehlerbehandlung
- **Running:** Node läuft noch → auf Completion warten

Ermöglicht komplexe Composite Nodes (Sequence, Selector, Parallel).

## 📚 Weiterführende Dokumentation

- [I4.0 Sharp Messaging README](../I4.0-Sharp-Messaging/README.md)
- [AAS Sharp Client README](../AAS-Sharp-Client/README.md)
- [MAS-BT Architecture README](./README.md)
- [specs.json](./specs.json)
