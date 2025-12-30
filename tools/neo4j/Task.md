# Task: Neo4j Graph Query Script Update

## Goal
Adjust `MAS-BT/tools/neo4j/run_graph_queries.sh` to support capability planning analysis without dumping large embeddings or terminal output spam.

## Requirements
- Write all query results only to `/home/benjamin/AgentDevelopment/temp/graph_output.txt`.
- Create `/home/benjamin/AgentDevelopment/temp` if it does not exist.
- Keep a short terminal message showing the Neo4j config (uri/db/from/to) and output path.
- Do not print query results or full query text to the terminal.
- Filter out `embedding` fields from all query result projections.
- Use relationship type `CapabilityComposedOf` for composed-of traversal.

## Queries to Include/Adjust
- Transport capability summary, properties, and constraints (existing sections).
- Capability tree queries with `embedding: null` projection.
- Composed-of summary by module and capability using `CapabilityComposedOf`.

## Validation
Run:
```
bash MAS-BT/tools/neo4j/run_graph_queries.sh \
  MAS-BT/configs/specific_configs/NamespaceHolon/P100_Planning_agent.json \
  P100 P101
```
Confirm:
- Output is written only to `temp/graph_output.txt`.
- Terminal shows only config + output path.
- Composed-of section returns `parentCapability -> childCapabilities`.

