---
name: Sub-agent roster
description: Seven specialist sub-agents available in .claude/agents/ — owner-to-file mapping for dispatch decisions
type: project
---

The DATN MAPF project has 7 specialist sub-agents. Use this when decomposing cross-cutting tasks.

| Agent | Domain | Owns (typical files) | Does NOT do |
|---|---|---|---|
| `datn-mapf-orchestrator` | Project lead, dispatch | (this agent) | Write code |
| `mapf-gameplay-engineer` | Algorithm + controller | `GridAStarPathfinder.cs`, `GridNavMask.cs`, `GridEnemyAgent.cs`, `MapLoader.cs`, `MapScenarioBootstrap.cs`, `TankController/TankMover` integration | Scene/prefab wiring, UI |
| `unity-mcp-operator` | MCP tool router | Reads editor state, manipulates scene/GameObject/component/prefab via MCP, runs tests, screenshots, console | Sửa C# logic |
| `unity-scene-prefab-wiring` | Scene/prefab/layers | Inspector references, prefab variants, layer/tag/sorting, collider setup, scene boot objects, `ProjectSettings/TagManager.asset` | Algorithm |
| `mapf-ui-visualizer` | HUD + debug overlay + Gizmos | Path lines, waypoint markers, grid overlay, blocked/reserved cell tints, heatmap cost viz, agent state labels, metrics HUD, algorithm selector menu | Algorithm logic |
| `mapf-metrics-architect` | Metrics schema + export | What to measure, how to measure fairly, CSV/JSON export, batch experiment runner, comparison report | Implement gameplay logic |
| `datn-docs-curator` | Vietnamese docs | `Assets/Docs/*.md`, technical decisions with WHY, demo checklist | Code |

**Why:** Knowing scope boundaries lets the orchestrator avoid double-assignment and prevents agents from drifting outside their lane.
**How to apply:** When decomposing a task, map each deliverable to exactly one owner from this table. Verify (compile/scene), Visualize (Gizmos), Metrics (schema), Docs are usually separate sub-dispatches.
