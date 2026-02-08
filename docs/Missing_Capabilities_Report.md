# Missing Capabilities / Properties Report

Datum: 2026-01-07

Ziel: Übersichtlich und ausführlich dokumentieren, welche Neo4j-Objekte / Eigenschaften / Beziehungen aktuell fehlen oder unvollständig sind, damit die durchgesetzte Regel — "Transportwege müssen über ein Modul mit `Transport`-Capability gehen und die Handover‑Capabilities müssen vorhanden sein" — korrekt funktioniert.

---

## 1) Umgebung / Kontext

- Neo4j-Verbindung (aus `configs/specific_configs/NamespaceHolon/NamespaceHolon.json`):
  - URI: `neo4j://192.168.178.30:7687`
  - Username: `neo4j`
  - Password: `testtest`
  - Database: `neo4j`
- Relevant im Code: [MAS-BT/Services/Transport/Neo4jTransportGraphQuery.cs](MAS-BT/Services/Transport/Neo4jTransportGraphQuery.cs) — diese Komponente sucht jetzt strikt nach Pfaden, die über ein `Transport`-Capability-Modul laufen und verlangt vorhandene Handover‑Capabilities an `from`, `via` und `to`.
- Tests angepasst / diagnostisch: Veränderungen und geloggte Pfade finden sich in den Tests unter [MAS-BT/tests](MAS-BT/tests).

---

## 2) Zusammenfassung der aktuell fehlenden oder unvollständigen Elemente (hochdetailliert)

Die Aufzählung basiert auf den letzten Abfragen und dem Audit des Live‑Graphs während der Änderungen. Jeder Punkt enthält: betroffene Knoten/Beziehungen, beobachteter Ist‑Zustand, gewünschter Soll‑Zustand, und ggf. Hinweise zur Priorität.

### 2.1 Module, die `Transport` bereitstellen (Validierung)

- Beobachtung: Module, die aktuell `Transport` bereitstellen: `P200`, `P201`, `P400`, `P401`.
- Status: Vorhanden.
- Aktion: keine Änderung nötig, aber verwenden, um Pfade zu erzwingen.

### 2.2 Handover‑Capabilities (DockStore/DockRetrieve) an Quelle und Ziel

- Beobachtung: Module mit DockStore/DockRetrieve laut Abfragen: `P100`, `P103`, `P18`, `P200`, `P201`, `P24`, `P300`, `P303`, `P400`, `P401`.
- Problem: Für einige dieser Capability‑Knoten existieren zwar `Capability`-Knoten, aber die zugehörigen `Property`-Nodes (z. B. `transferRole`, `topologyType`, `dockRole`) haben `value = NULL` oder fehlen ganz.
- Konkrete Fälle (Beispiele):
  - `P200` → `DockStoreHandoverCapability`: Constraint-/Property‑Knoten existieren, aber deren `value`-Felder sind NULL oder leer.
  - `P103` hat zwar Handover‑Capabilities (ersichtlich), aber einige erwartete Properties oder komponierte Unterfähigkeiten können fehlen (siehe 2.3).
- Soll: Für alle Handover‑Capabilities an `from` und `to` müssen die folgenden Property‑Keys vollständig gesetzt sein: `transferRole`, `topologyType`, `dockRole`. Werte sollten semantisch korrekt sein (z. B. `transferRole` = `initiator`/`acceptor` o. ä.).
- Priorität: Hoch (Tests und Pfadvalidierung schlagen sonst fehl).

### 2.3 Fehlende `CapabilityComposedOf` / zusammengesetzte Unter‑Capabilities

- Beobachtung: Einige Handover‑Capabilities werden erwartet, aus `Docking`, `Store`, `Retrieve` etc. zusammengesetzt zu sein. Bei mindestens `P200`/`P103` fehlen aber Verknüpfungen `(:Capability)-[:COMPOSED_OF]->(:Capability)` oder diese Unterknoten fehlen.
- Soll: Für jede Handover‑Capability müssen die erwarteten Unterfähigkeiten als `COMPOSED_OF` bzw. entsprechendem Relationship‑Typ vorhanden sein, damit Code und Tests die Existenz von `Docking`, `Store` etc. prüfen können.
- Priorität: Mittel‑hoch.

### 2.4 ASSET_DISTANCE‑Beziehungen / Distanzkanten

- Beobachtung: Direktverbindung P200→P103 wurde inzwischen angelegt (MERGE durchgeführt). Es muss überprüft werden, ob weitere erwartete Kanten existieren.
- Fehlende oder zu prüfende Kanten (empfohlen zu verifizieren):
  - `P100` ↔ `P200` (sollte vorhanden sein; prüfen ob `distance` gesetzt ist)
  - `P200` → `P103` (wurde erstellt; prüfen `distance`-Wert)
  - Falls Tests andere Topologien erwarten (z. B. P100→P102), prüfen, ob dort Handover/Transport‑Anforderungen erfüllt sind.
- Soll: Alle ASSET_DISTANCE‑Rela­tionships, die von den Testfällen oder vom Planner erwartet werden, müssen existieren und die `distance`-Eigenschaft besitzen.
- Priorität: Hoch für Pfadfindungstests.

### 2.5 Neo4j‑Treiber/Query‑Einschränkung (technischer Hinweis)

- Beobachtung: Der Bolt‑Treiber verweigert parametrisierten variable‑length‑Pattern (z. B. `[:ASSET_DISTANCE*..$hopLimit]`) — deshalb wurde im Code literal Hoplimits (z. B. `..12` oder `..8`) eingesetzt.
- Empfehlung: Bedenken beim weiteren Parametrisieren beachten; Tests / Queries sollten literale Hoplimits oder sicher geprüfte String‑Interpolation verwenden.
- Priorität: Niedrig (nur Implementierungsdetail), aber dokumentieren.

### 2.6 Sonstige fehlende Properties / zusammengesetzte Child‑Nodes

- Beobachtung: Für einige Capability‑Constraints sind „Property“ oder „Constraint“-Knoten vorhanden, deren `value`-Felder NULL sind oder die erwarteten zusammengesetzten Child‑Ressourcen (z. B. `CapabilityChild` oder `ConstraintChild`) fehlen.
- Folgen: Assertions in Tests, die z. B. `transferRole` prüfen, schlagen fehl oder führen zu `No path found`‑Fehlern, weil Handover‑Qualifikation nicht vollständig erkannt wird.
- Soll: Vollständige Setzung aller erwarteten Property‑Knoten (nicht‑NULL) und Anlage fehlender zusammengesetzter Knoten.
- Priorität: Hoch (veraltet/teilweise Test‑Failurs verursachend).

---

## 3) Detaillierte, modulweise Checkliste (empfohlenes Prüfprotokoll)

Für jede relevante `Module`-Entität (mindestens `P100`, `P200`, `P102`, `P103`) führe die folgenden Prüfungen durch und dokumentiere die IST‑Werte:

- [ ] Existiert `Module`-Knoten mit `shell_id: '<ID>'`?
- [ ] Existiert `(:Module)-[:PROVIDES_CAPABILITY]->(:Capability {idShort:'DockStoreHandoverCapability'})`?
- [ ] Existiert `(:Capability)-[:HAS_PROPERTY]->(:Property {idShort:'transferRole'})` und ist `Property.value` nicht NULL?
- [ ] Existiert `(:Capability)-[:HAS_PROPERTY]->(:Property {idShort:'topologyType'})` und ist `Property.value` nicht NULL?
- [ ] Existiert `(:Capability)-[:HAS_PROPERTY]->(:Property {idShort:'dockRole'})` und ist `Property.value` nicht NULL?
- [ ] Sind erwartete `COMPOSED_OF` Beziehungen (z. B. zu `Docking`,`Store`,`Retrieve`) vorhanden?
- [ ] Existieren `ASSET_DISTANCE`-Rela­tionships zu erwarteten Nachbarn und besitzen sie `distance`?

---

## 4) Konkrete Beispiel‑Cypher‑Statements (Vorschläge, nicht ausgeführt)

Hinweis: Diese Beispiele sind Vorschläge, die der DB‑Admin oder Sie manuell ausführen können. Ich habe sie hier dokumentiert — wie gewünscht ohne Änderungen vorzunehmen.

Beispiel: TransferRole für `P200` setzen

```cypher
MATCH (m:Module {shell_id:'P200'})-[:PROVIDES_CAPABILITY]->(c:Capability {idShort:'DockStoreHandoverCapability'})
MERGE (c)-[:HAS_PROPERTY]->(p:Property {idShort:'transferRole'})
SET p.value = 'initiator';
```

Beispiel: topologyType und dockRole setzen (P103 als Empfänger)

```cypher
MATCH (m:Module {shell_id:'P103'})-[:PROVIDES_CAPABILITY]->(c:Capability {idShort:'DockStoreHandoverCapability'})
MERGE (c)-[:HAS_PROPERTY]->(p1:Property {idShort:'topologyType'})
SET p1.value = 'Dock';
MERGE (c)-[:HAS_PROPERTY]->(p2:Property {idShort:'dockRole'})
SET p2.value = 'Host';
```

Beispiel: `COMPOSED_OF`-Relation anlegen

```cypher
MATCH (parent:Capability {idShort:'DockStoreHandoverCapability', /* optional: filter by module */})
MATCH (child1:Capability {idShort:'Docking'})
MATCH (child2:Capability {idShort:'Store'})
MERGE (parent)-[:COMPOSED_OF]->(child1)
MERGE (parent)-[:COMPOSED_OF]->(child2);
```

Beispiel: ASSET_DISTANCE prüfen/erstellen

```cypher
MATCH (a:Module {shell_id:'P100'}), (b:Module {shell_id:'P200'})
MERGE (a)-[r:ASSET_DISTANCE]->(b)
SET r.distance = coalesce(r.distance, 1.0);
```

Wichtig: Wenn Sie viele Updates automatisiert ausführen, testen Sie zuerst einzelne MERGE/SET Befehle und sichern Sie ggf. DB‑Snapshots.

---

## 5) Hinweise zu erwarteten Testauswirkungen

- Sobald die `transferRole`/`topologyType`/`dockRole`-Properties nicht mehr NULL sind und `COMPOSED_OF`-Beziehungen existieren, sollten Tests, die jetzt fehlschlagen (z. B. `No path found` zwischen `P100` und `P103` bei transport‑konformen Pfaden), Erfolg haben.
- Prüfen Sie nach Änderungen die folgenden Testbereiche:
  - Transport‑Planer (Pfadrekonstruktion)
  - Integrationstests, die MQTT‑Messages erwarten (sie warten auf gültige Offers/Responses)
  - Unit‑/Integrationstests, die Capability‑Properties explizit prüfen

---

## 6) Nächste empfohlene Schritte (operativ)

1. Führen Sie ein Modul‑by‑Modul Audit gemäss Abschnitt 3 durch (P100, P200, P103 zuerst).
2. Setzen Sie fehlende Property‑Werte für Handover‑Capabilities (siehe 4.1/4.2 Beispiele).
3. Legen Sie fehlende `COMPOSED_OF` Beziehungen an.
4. Verifizieren Sie die ASSET_DISTANCE‑Kanten und `distance`‑Werte.
5. Starten Sie gezielt die Transport‑Tests (nur subset) und verifizieren Sie Fehlermeldungen; iterieren Sie.

---

## 7) Referenzen im Code (zur Verifikation)

- Pfadfindungs‑/Query‑Logik: [MAS-BT/Services/Transport/Neo4jTransportGraphQuery.cs](MAS-BT/Services/Transport/Neo4jTransportGraphQuery.cs)
- Tests mit Diagnostik/Erwartungen: [MAS-BT/tests](MAS-BT/tests)
- Konfiguration mit Neo4j Credentials: [MAS-BT/configs/specific_configs/NamespaceHolon/NamespaceHolon.json](MAS-BT/configs/specific_configs/NamespaceHolon/NamespaceHolon.json)

---

## 8) Anhang: Kurz‑Audit (letzte bekannte Fakten / Beobachtungen)

- `Transport`-Provider: P200, P201, P400, P401.
- Handover‑Capabilities vorhanden an: P100, P103, P18, P200, P201, P24, P300, P303, P400, P401.
- Direktverbindung P200→P103: wurde zuletzt per MERGE angelegt (Rückmeldung: Created 1 relationships, Set 1 properties).
- Einige `Property.value` Felder waren bei Inspektion NULL — diese müssen gesetzt werden.

---

Wenn Sie wollen, führe ich nach Ihrer Freigabe die dokumentierten Cypher‑Updates aus (oder erstelle ein kleines Skript mit den nötigen MERGE‑Statements), und danach führe ich die gezielten Transport‑Tests lokal aus und melde die Resultate.

---

## Modul‑by‑Modul Audit (ausführliche Live‑Ergebnisse)

Die nächsten Abschnitte enthalten die exakten Resultate der ausgeführten Cypher‑Abfragen für `P100`, `P200`, `P102` und `P103` (Capabilities, relevante Properties, `COMPOSED_OF` Kinder, `ASSET_DISTANCE` Nachbarn). Diese Daten wurden live vom Neo4j‑Server abgerufen.

### P100 (bereits geprüft)
- Capabilities (geordnet): DockRetrieveHandoverCapability, DockStoreHandoverCapability, Docking, Retrieve, Screw, Store
- Relevante Properties (insb. Handover):
  - `DockStoreHandoverCapability`: Property‑Eintrag vorhanden, `value = NULL`
  - `DockRetrieveHandoverCapability`: Property‑Eintrag vorhanden, `value = NULL`
- `COMPOSED_OF` Kinder: keine (alle Capability → [])
- `ASSET_DISTANCE` Nachbarn (out / in):
  - out: P18 (distance=43.649169522454834)
  - out: P200 (distance=51.041649659861115)
  - out: P104 (distance=5.220153254455275)
  - out: P106 (distance=23.43608329051593)
  - out: P24 (distance=46.42467016576423)
  - out: P201 (distance=53.90037105623671)
  - out: P102 (distance=30.41792234851026)
  - in: none

**Kurzbefund P100:** Handover‑Capabilities sind vorhanden, aber die zugehörigen Property‑Werte fehlen (`NULL`). Zusammengesetzte Unter‑Capabilities sind nicht verknüpft.

### P200
- Capabilities (geordnet): DockRetrieveHandoverCapability, DockStoreHandoverCapability, Docking, Retrieve, Store, Transport
- Relevante Properties:
  - `Transport`: `transferRole = "initiator"` (wurde gesetzt)
  - `DockStoreHandoverCapability`: Property‑Eintrag vorhanden, `value = NULL`
  - `DockRetrieveHandoverCapability`: Property‑Eintrag vorhanden, `value = NULL`
- `COMPOSED_OF` Kinder: keine (alle Capability → [])
- `ASSET_DISTANCE` Nachbarn (out / in):
  - out: P103 (distance=40.0)
  - out: P201 (distance=5.0)
  - out: P24 (distance=10.0)
  - in: P100 (distance=51.041649659861115)
  - in: P104 (distance=54.589376255824725)
  - in: P18 (distance=20.0)
  - in: P102 (distance=30.0)
  - in: P106 (distance=37.73592452822641)

**Kurzbefund P200:** `Transport` vorhanden und `transferRole` gesetzt; Handover‑Capabilities vorhanden, aber deren Property‑Werte sind NULL. Keine `COMPOSED_OF` Verknüpfungen.

### P102
- Capabilities (geordnet): Assemble
- Relevante Properties: keine Handover/Transport‑Capabilities vorhanden
- `COMPOSED_OF` Kinder: `Assemble` → []
- `ASSET_DISTANCE` Nachbarn (out / in):
  - out: P104 (31.622776601683793)
  - out: P106 (33.52610922848042)
  - out: P201 (30.4138126514911)
  - out: P24 (31.622776601683793)
  - out: P200 (30.0)
  - out: P18 (36.05551275463989)
  - in: P100 (30.41792234851026)

**Kurzbefund P102:** Bietet keine Handover/Transport‑Capabilities; ist nur `Assemble`. Daher nicht als Transport‑Modul geeignet.

### P103
- Capabilities (geordnet): DockRetrieveHandoverCapability, DockStoreHandoverCapability, Docking, Retrieve, Screw, Store
- Relevante Properties:
  - `DockStoreHandoverCapability`: Property‑Eintrag vorhanden, `value = NULL`
  - `DockRetrieveHandoverCapability`: Property‑Eintrag vorhanden, `value = NULL`
- `COMPOSED_OF` Kinder: keine (alle Capability → [])
- `ASSET_DISTANCE` Nachbarn (out / in):
  - out: P303 (distance=0.0)
  - in: P200 (distance=40.0)

**Kurzbefund P103:** Handover‑Capabilities vorhanden, aber Property‑Werte fehlen (`NULL`). `ASSET_DISTANCE` zu P200 ist vorhanden (inbound 40.0). Keine `COMPOSED_OF` Kinder.

---

Wenn Sie möchten, führe ich jetzt die dokumentierten, gezielten Cypher‑Updates aus (SET der Property‑Werte und MERGE der `COMPOSED_OF`-Rela­tionen) für `P100`, `P200` und `P103` und verifiziere anschließend die relevanten Transport‑Tests.

