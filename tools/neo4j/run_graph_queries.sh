#!/usr/bin/env bash
set -euo pipefail

CONFIG_PATH="${1:-MAS-BT/configs/specific_configs/NamespaceHolon/P100_Planning_agent.json}"
FROM_MODULE="${2:-P100}"
TO_MODULE="${3:-P101}"

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 not found. Set NEO4J_URI/NEO4J_USER/NEO4J_PASSWORD/NEO4J_DATABASE manually."
  exit 1
fi

mapfile -t NEO4J_LINES < <(python3 - "$CONFIG_PATH" <<'PY'
import json
import sys

config_path = sys.argv[1] if len(sys.argv) > 1 else "MAS-BT/configs/specific_configs/NamespaceHolon/P100_Planning_agent.json"
with open(config_path, "r", encoding="utf-8") as f:
    data = json.load(f)

neo = data.get("Neo4j", {})
print(neo.get("Uri", ""))
print(neo.get("Username", ""))
print(neo.get("Password", ""))
print(neo.get("Database", ""))
PY

)

NEO4J_URI="${NEO4J_LINES[0]:-}"
NEO4J_USER="${NEO4J_LINES[1]:-}"
NEO4J_PASSWORD="${NEO4J_LINES[2]:-}"
NEO4J_DATABASE="${NEO4J_LINES[3]:-}"

if [[ -z "${NEO4J_URI}" || -z "${NEO4J_USER}" || -z "${NEO4J_PASSWORD}" || -z "${NEO4J_DATABASE}" ]]; then
  echo "Missing Neo4j config. Check ${CONFIG_PATH} or set env vars: NEO4J_URI NEO4J_USER NEO4J_PASSWORD NEO4J_DATABASE"
  exit 1
fi

QUERY=$(cat <<CYPHER
// 1) Inspect labels and relationship types
CALL db.labels();
CALL db.relationshipTypes();

// 2) Example asset nodes with shell_id
MATCH (a) WHERE a.shell_id IS NOT NULL
RETURN labels(a) AS labels, keys(a) AS keys, a.shell_id AS shell_id
LIMIT 5;

// 3) Capabilities attached to assets (sample) - exclude embeddings
MATCH (a)-[:PROVIDES_CAPABILITY]->(c)
RETURN a.shell_id AS shell_id,
       labels(c) AS capLabels,
       [k IN keys(c) WHERE k <> "embedding"] AS capKeys
LIMIT 5;

// 4) Neighbors by ASSET_DISTANCE from a module
MATCH (a {shell_id: "${FROM_MODULE}"})-[r:ASSET_DISTANCE]-(b)
RETURN a.shell_id AS from, b.shell_id AS to, r.distance AS distance
ORDER BY distance ASC
LIMIT 10;

// 5) Shortest path by distance (ASSET_DISTANCE is undirected)
MATCH (start {shell_id: "${FROM_MODULE}"}), (goal {shell_id: "${TO_MODULE}"})
MATCH p = (start)-[:ASSET_DISTANCE*..6]-(goal)
WITH p, reduce(dist = 0.0, rel IN relationships(p) | dist + coalesce(rel.distance, 0.0)) AS totalDist
RETURN [n IN nodes(p) | n.shell_id] AS path, totalDist
ORDER BY totalDist ASC
LIMIT 1;

// 6) Modules that provide Transport capability (name/idShort/capability heuristic)
MATCH (m)-[:PROVIDES_CAPABILITY]->(c)
WHERE toLower(coalesce(c.name, c.idShort, c.capability, "")) CONTAINS "transport"
RETURN DISTINCT m.shell_id AS module
ORDER BY module;

// 7) Path that includes a transport-capable module (optional)
MATCH (start {shell_id: "${FROM_MODULE}"}), (goal {shell_id: "${TO_MODULE}"})
MATCH p = (start)-[:ASSET_DISTANCE*..8]-(goal)
WHERE ANY(n IN nodes(p) WHERE EXISTS {
  MATCH (n)-[:PROVIDES_CAPABILITY]->(c)
  WHERE toLower(coalesce(c.name, c.idShort, c.capability, "")) CONTAINS "transport"
})
WITH p, reduce(dist = 0.0, rel IN relationships(p) | dist + coalesce(rel.distance, 0.0)) AS totalDist
RETURN [n IN nodes(p) | n.shell_id] AS path, totalDist
ORDER BY totalDist ASC
LIMIT 1;

// 8) Docking role hints (initiator/acceptor) - exclude embeddings
MATCH (m)-[:PROVIDES_CAPABILITY]->(c)
WHERE toLower(coalesce(c.name, c.idShort, c.capability, "")) CONTAINS "dock"
RETURN m.shell_id AS module, c{.*, embedding: null} AS capability
LIMIT 10;

// 9) Capability role properties (dockRole/transferRole/topologyType/requiredPartnerRole) via HAS_ELEMENT/HAS_PROPERTY
MATCH (m)-[:PROVIDES_CAPABILITY]->(cap)
WHERE toLower(coalesce(cap.name, cap.idShort, cap.capability, "")) CONTAINS "dock"
OPTIONAL MATCH (cap)-[:HAS_ELEMENT|HAS_PROPERTY|REFERS_TO*1..5]->(p:Property)
WHERE toLower(p.idShort) IN ["dockrole", "transferrole", "topologytype", "requiredpartnerrole"]
RETURN m.shell_id AS module,
       cap.idShort AS capabilityIdShort,
       collect(DISTINCT {idShort: p.idShort, value: p.value}) AS properties
LIMIT 10;

// 10) Transport capability summary for P200/P201 (always returns a row)
MATCH (a:Asset)-[:PROVIDES_CAPABILITY]->(cap:Capability)
WHERE a.shell_id IN ["P200", "P201"]
  AND cap.idShort IN ["Transport", "DockStoreHandoverCapability", "DockRetrieveHandoverCapability"]
RETURN "transport_caps_summary" AS section,
       a.shell_id AS module,
       collect(DISTINCT cap.idShort) AS capabilities
ORDER BY module;

// 11) Transport + handover capability properties for P200/P201 via HAS_PROPERTY
MATCH (a:Asset)-[:PROVIDES_CAPABILITY]->(cap:Capability)-[:HAS_PROPERTY]->(p:SubmodelElementCollection)
WHERE a.shell_id IN ["P200", "P201"]
  AND cap.idShort IN ["Transport", "DockStoreHandoverCapability", "DockRetrieveHandoverCapability"]
  AND toLower(p.idShort) IN ["transferrole", "topologytype", "requiredpartnerrole", "dockrole", "loadcarriertypes"]
RETURN "transport_props" AS section,
       a.shell_id AS module,
       cap.idShort AS capability,
       collect(DISTINCT {idShort: p.idShort, value: coalesce(p.value, p[p.idShort])}) AS properties
ORDER BY module, capability;

// 12) Transport constraints for P200/P201 via HAS_CONSTRAINT
MATCH (a:Asset)-[:PROVIDES_CAPABILITY]->(cap:Capability)
WHERE a.shell_id IN ["P200", "P201"]
  AND cap.idShort IN ["Transport", "DockStoreHandoverCapability", "DockRetrieveHandoverCapability"]
OPTIONAL MATCH (cap)-[:HAS_CONSTRAINT*1..3]->(c)
RETURN "transport_constraints" AS section,
       a.shell_id AS module,
       cap.idShort AS capability,
       collect(DISTINCT c{.*, embedding: null, reference: null}) AS constraints
ORDER BY module, capability;

// 13) Simplified capability/property structure (path without embeddings)
MATCH path=((cap:Capability)-[r]->(smc:SubmodelElementCollection)-[re:HAS_ELEMENT*1..2]->(p:Property))
RETURN [node IN nodes(path) | node{.*, embedding: null, reference: null}] AS nodes,
       [rel IN relationships(path) | rel{.*}] AS rels
LIMIT 100;

// 14) ComposedOf relationships (parent -> child capabilities) without embeddings
MATCH (a:Asset)-[:PROVIDES_CAPABILITY]->(parent:Capability)
OPTIONAL MATCH (parent)-[:CapabilityComposedOf*1..6]->(child:Capability)
RETURN "composed_of" AS section,
       a.shell_id AS module,
       parent.idShort AS parentCapability,
       collect(DISTINCT child.idShort) AS childCapabilities
ORDER BY module, parentCapability;
CYPHER
)

if ! command -v cypher-shell >/dev/null 2>&1; then
  echo "cypher-shell not found. Install Neo4j client or run queries in Neo4j Browser."
  exit 1
fi

OUTPUT_DIR="/home/benjamin/AgentDevelopment/temp"
mkdir -p "${OUTPUT_DIR}"
OUTPUT_FILE="${OUTPUT_DIR}/graph_output.txt"

echo "Using Neo4j: ${NEO4J_URI} db=${NEO4J_DATABASE} from=${FROM_MODULE} to=${TO_MODULE} -> ${OUTPUT_FILE}"

cypher-shell -a "${NEO4J_URI}" -u "${NEO4J_USER}" -p "${NEO4J_PASSWORD}" -d "${NEO4J_DATABASE}" <<CYPHER > "${OUTPUT_FILE}"
$QUERY
CYPHER
