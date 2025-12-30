# Transport Manager Usage of Capabilities

This document describes how the transport manager uses CapabilityDescription data to plan, match, and execute transfers across modules (e.g., P100/P103/P200/P201). The concept integrates capability semantics with skill-level execution without duplicating information.

## 1) Goal

Enable the transport manager to:
- discover compatible handover partners,
- select feasible transfer chains (active + passive),
- trigger and supervise the handshake process using skills,
- react safely to faults and safety events.

## 2) Capability Inputs

The manager reads the CapabilityDescription submodel and uses only the minimal set of properties required for matching and execution.

| Capability | Required Properties | Purpose |
| --- | --- | --- |
| DockStoreHandoverCapability | `transferRole`, `topologyType`, `requiredPartnerRole`, `loadCarrierTypes` | Defines if module initiates or accepts and what partners/load carriers are allowed |
| DockRetrieveHandoverCapability | `transferRole`, `topologyType`, `requiredPartnerRole`, `loadCarrierTypes` | Same as above for outbound handover |
| Docking | `dockRole`, `capacity`, `loadCarrierTypes` | Defines docking behavior, limits, and compatibility |
| Store | `direction`, `allowedTopologyTypes`, `requiredPartnerRole`, `loadCarrierTypes` | Defines inbound storage capability |
| Retrieve | `direction`, `allowedTopologyTypes`, `requiredPartnerRole`, `loadCarrierTypes` | Defines outbound retrieval capability |
| Transport | `transferRole`, `allowedTopologyTypes`, `loadCarrierTypes`, `maxPayloadKg`, `maxSpeedMps` | Defines transport function (mobile system) |

## 3) Matching Rules

The transport manager constructs pairings based on the following constraints.

| Rule | Description |
| --- | --- |
| Role complement | `transferRole` must be `initiator` on one side and `acceptor` on the other; the passive side’s `requiredPartnerRole` must include the active role |
| Topology match | `topologyType` must match; for docking, both must allow `Dock` in `allowedTopologyTypes` |
| Load carrier match | Intersection of `loadCarrierTypes` must be non-empty |
| Capacity check | If docking is required, `capacity > 0` on the dock host side |
| Direction alignment | For a Store handover, the passive module must provide `Store` with `direction = Inbound`; for Retrieve, `direction = Outbound` |
| Transport constraints | When using a transport module, `maxPayloadKg` must cover the requested payload |

## 4) Graph-Based Planning (Capability Chain)

The transport manager uses the **graph** to build a capability chain such as:
`Retrieve via Dock → Transport → Store via Dock`.

Graph model (input):
- **Records**: graph extracted into records (see `records (3).json`), containing assets, positions, and parent relations.
- **Nodes**: modules with capability sets.
- **Edges**: feasible handovers between modules, derived via queries over the records plus capability matching rules.
- **Transport edges**: traverse the `ASSET_DISTANCE` relationship for routing between modules.

Chain construction:
1. **Path selection**: choose a module path from source to target (graph routing).
2. **Edge expansion**: for each edge, derive required handover capabilities:
   - Source module: `DockRetrieveHandoverCapability` (outbound, initiator/acceptor depending on role).
   - Target module: `DockStoreHandoverCapability`.
3. **Transport insertion**: if the path includes a transport module, add its `Transport` capability between handover legs.
4. **Validation**: check `PropertyConstraints` and `TransitionConstraints` per leg.

The output is an ordered list of **capability legs** that form the chain.

## 5) Capability Requests and Offers (Dispatcher-Style)

After the chain is constructed, the transport manager:
1. **Requests capabilities** from all involved modules (same flow as dispatcher).
2. **Collects proposals** for each required leg (see PlanningAgent flow).
3. **Builds a `CapabilitySequence`** from the accepted proposals.
4. **Returns the `CapabilitySequence`** to the requester as a `TransportPlanResponseMessage`.

The sequence is the transport manager’s contract: it encodes *what* will run, *where*, and *in which order*.
The response payload is a `TransportRequestMessage` that embeds the `CapabilitiesSequence`, consistent with the existing dispatcher implementation.

Sequence entry format (aligned to PlanningAgent):
- `OfferedCapabilityReference` (ReferenceElement)
- `InstanceIdentifier` (Property)
- `Station` (Property)
- `Actions` (SubmodelElementList with Action collections)
- `EarliestSchedulingInformation` (SubmodelElementCollection)
- `MatchingScore`, `Cost`, `SequencePlacement` (Properties)

Timeouts: reuse dispatcher defaults (e.g., `CollectTransportResponses` ~5s, `AwaitOfferDecision` ~2s) unless overridden in config.

## 6) CapabilitySequence Construction (Pseudo-Flow)

Input:
- Planned chain legs (e.g., `Retrieve via Dock`, `Transport`, `Store via Dock`)
- For each leg: proposals from modules (OfferedCapability)

Algorithm:
1. **Create leg requests** from the chain.
2. **Dispatch requests** to modules (dispatcher flow).
3. **Collect proposals** per leg with timeout.
4. **Pick winner per leg** (best cost/time/score).
5. **Assemble CapabilitySequence** in chain order.

Pseudo-code (conceptual):
```text
legs = planChain(start, goal, graph, capabilities)

for leg in legs:
  sendOfferedCapabilityRequest(leg.requiredCapability, leg.targetModule)

proposals = collectProposals(timeout)
sequence = new CapabilitySequence()

for leg in legs:
  candidates = proposals.filter(p => p.matches(leg))
  selected = selectBest(candidates)
  sequence.add(selected)

return TransportPlanResponseMessage(TransportRequestMessage with sequence)
```

Mapping to OfferedCapability:
- `OfferedCapabilityReference`: reference to the offered capability (as in dispatcher/proposal).
- `Station`: module id for the leg.
- `Actions`: action list (skill references and parameters).
- `Cost`: `distance * costPerMeter` for the leg.
- `SequencePlacement`: optional ordering label (e.g., `LEG_01`, `LEG_02`, ...).

## 6) Execution (Handshake Supervision)

Execution uses the skill-level handshake defined in the capability concept. For each handover leg, the manager starts the **external skill** on the initiator side, which coordinates the partner’s internal skill.

| Phase | External Skill (Initiator) | Internal Skill (Acceptor) | Condition |
| --- | --- | --- | --- |
| Ready | ready | ready | Preconditions satisfied |
| Start | running | ready | External skill started |
| Engage | running | running | Internal skill activated |
| Complete | running | completed | Internal skill finishes |
| Halt | halting | completed | External skill halts after success |
| Halted | halted | halted | Normal completion |

Safety behavior:
- Any safety signal or unexpected halt triggers simultaneous `halting` → `halted` on both sides.
- Recovery is manual to avoid unsafe asynchronous states.

## 7) Example Chain

Example chain: P100 → P200 → P103

- Edge 1 (P100 → P200): P100 uses `DockRetrieveHandoverCapability`, P200 uses `DockStoreHandoverCapability`
- Transport leg: P200 uses `Transport`
- Edge 2 (P200 → P103): P200 uses `DockRetrieveHandoverCapability`, P103 uses `DockStoreHandoverCapability`

The manager requests these capabilities, receives offers, builds a single `CapabilitySequence`, and returns it to the requester.

## 8) Cost Model

- Base cost per leg = `distance * costPerMeter`.
- `distance` comes from `ASSET_DISTANCE`.
- `costPerMeter` is configurable (transport manager config).
- Total path cost is the sum of leg costs; use it as tie-breaker or primary selection.

## 9) Implementation Notes

- Capability data is a **planning and coordination input**; execution is always via skills.
- Use `ComposedOf` to verify that each composite handover capability includes Docking + Store/Retrieve.
- The model supports synchronous and asynchronous transfers with the same role/topology rules.
- The manager should cache offers briefly and revalidate constraints before execution.
