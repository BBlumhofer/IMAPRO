# Module Holon (Router um Planning- und Execution-Agent)

Der Module Holon kapselt PlanningAgent und ExecutionAgent und tritt nach außen als einziges Modul-Ende auf. Er übernimmt Registrierung, Routing und das Lifecycle-Management der Sub-Holone (Planning/Execution) in eigenen Threads.

## Ziele
- **Single Endpoint nach außen:** Dispatcher sieht nur den Module Holon; interne Agents bleiben verborgen.
- **Registrierung & Heartbeats:** Module Holon meldet Modul-Fähigkeiten/Topology beim Dispatching Agent an und hält die Registrierung aktuell.
- **Routing:** Alle Angebots-/Scheduling-/Booking- und Transport-Nachrichten werden über den Module Holon geleitet und an Planning/Execution weitergereicht.
- **Sub-Holon-Instanziierung:** Module Holon startet PlanningAgent und ExecutionAgent in eigenen Threads/Prozessen und registriert sie auf einem internen Register-Topic.

## Rollen & Topics
- **Extern (Dispatcher-facing):**
  - Registration: `/DispatchingAgent/{ns}/ModuleRegistration/`
  - Offers: `/DispatchingAgent/{ns}/ModuleOffers/{ModuleId}/Request|Response/`
  - Scheduling/Booking: `/Modules/{ModuleId}/ScheduleAction/`, `/Modules/{ModuleId}/BookingConfirmation/`
  - Transport: `/Modules/{ModuleId}/TransportPlan/`
- **Intern (Sub-Holon-facing):**
  - Planning Offer-Requests: `/ModuleHolon/{ModuleId}/Planning/OfferRequest`
  - Planning Schedule/Booking: `/ModuleHolon/{ModuleId}/Planning/ScheduleAction`
  - Execution SkillRequests (optional): `/ModuleHolon/{ModuleId}/Execution/SkillRequest`
  - Sub-Holons melden sich an über: `/ModuleHolon/{ModuleId}/register`

## Ablauf (vereinfacht)
1. **Init:** Module Holon lädt Config (AAS-Endpunkte, ModuleId, Namespace, Subholons), verbindet MQTT, liest Nameplate/CapabilityDescription/Neighbors.
2. **Registrierung:** Publiziert `ModuleRegistration` an den Dispatcher und subscribed auf dessen Service-Topics.
3. **Sub-Holon-Start:** Spawnt PlanningAgent und ExecutionAgent (eigene Threads/Tasks/Prozesse, konfigurierbar über Commands) und wartet auf deren Registrierung über `/ModuleHolon/{ModuleId}/register`.
4. **Routing-Loop:**
   - Offer-/Scheduling-/Booking-/Transport-Nachrichten vom Dispatcher → interne Topics → wartet auf Antwort → sendet zurück an Dispatcher (ConversationId bleibt erhalten).
   - Heartbeat/Registration-Refresh in Intervallen.
5. **Health/Recovery:** Falls ein Sub-Holon nicht reagiert, kann der Module Holon Refuse senden oder das Sub-Holon neu starten.

## Behavior-Tree-Skizze
```
Sequence ModuleHolon
  ReadConfig (module_holon.json)
  Bind AgentId/Role/Namespace
  ConnectToMessagingBroker
  ReadShell / ReadCapabilityDescription / ReadNeighbors
  RegisterModule (send to dispatcher)
  SpawnSubHolons (Planning, Execution) in Threads
  WaitForSubHolonRegister (Topic /ModuleHolon/{ModuleId}/register)
  SubscribeExternalTopics
  Parallel
    - OfferHandler Loop (dispatcher offer req -> planning -> dispatcher)
    - ScheduleHandler Loop (schedule/book -> planning -> booking confirmation)
    - TransportHandler Loop (transport req -> planning/transport)
    - Heartbeat/RegistrationRefresh Loop
```

## Inventory- und Neighbor-Snapshots
- `SubscribeModuleHolonTopics` abonniert `/ {Namespace}/{ModuleId}/Inventory` sowie `/Neighbors` und cached die eingehenden `InventoryMessage`- bzw. `neighborsUpdate`-Payloads.
- Die Snapshots werden im `ModuleHolon`-Context unter `ModuleInventory` und `Neighbors` abgelegt, damit der `ModuleHolonRegistration` bei jedem Heartbeat die aktuellen StorageUnits/Slots direkt mit registrieren kann.
- Das Inventory-Format entspricht exakt der `InventoryMessage` aus `AAS-Sharp-Client` (`StorageUnits -> Slots -> SlotContent`). Dadurch kann der Dispatcher sofort den realen Modulzustand sehen, ohne erneut beim Modul nachfragen zu müssen.
- Beim Start (InitialRegistration) wartet der Holon optional einige Millisekunden (`config.Agent.InitialSnapshotTimeoutMs`), damit erste Snapshots ankommen. Fällt während des Betriebs die Verbindung weg, wird weiterhin das zuletzt empfangene Cache-Abbild verwendet.

## Registrierung der Sub-Holone
- Jeder Sub-Holon veröffentlicht nach Start eine Nachricht auf `/ModuleHolon/{ModuleId}/register` mit seinen Identifiers (AgentId, Role, SupportedMessages).
- Module Holon speichert die Sub-Holon-Endpunkte und nutzt sie für das Routing.
- Startbefehle können im Config unter `SubHolons.Planning.Command` bzw. `SubHolons.Execution.Command` hinterlegt werden (z. B. `dotnet run -- Trees/PlanningAgent.bt.xml`).

## Offene Punkte
- Start der Sub-Holone: per ProcessStart vs. Thread/Task (aktuell Thread/Task vorgesehen).
- Fehlerfall: Backoff/Retry beim Spawn und beim internen Routing.
- Security/ACL: Topics ggf. einschränken, falls MQTT-ACLs aktiv sind.
