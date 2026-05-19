---
name: Phase plan dispatch template
description: Pattern for adding sub-agent assignment sections to multi-phase plans (v2 → v3 upgrade)
type: project
---

When the user asks to upgrade a multi-phase plan with sub-agent assignment, use this template structure:

1. **Activation matrix at the top** — table with columns: Phase | Owner | Verify | Viz | Metrics | Docs
2. **Per-phase "## Sub-agent assignment" section** with:
   - **Owner (implementation)**: agent + lý do + scope (specific files/methods)
   - **Verification**: typically `unity-mcp-operator` (compile + scene smoke)
   - **Visualization**: `mapf-ui-visualizer` if Gizmos/overlay/HUD needed
   - **Metrics**: `mapf-metrics-architect` if numbers to log/export
   - **Docs**: always `datn-docs-curator` — list which docs to update
   - **Không nên dùng**: explicit list of agents NOT to use, with reason
3. **Cross-phase coordination at the end**:
   - Dependency DAG (which phase blocks which)
   - Conflict file table (which files multiple phases touch — must serialize)
   - When to call `unity-mcp-operator` (gates between phases)
   - When to batch metrics/docs

**Why:** A plan without explicit ownership leaks into ad-hoc dispatch where the same file gets edited twice. Explicit per-phase assignment + conflict file table prevents merge collisions.
**How to apply:** Preserve all v2 prose intact (no rewriting analysis). Append new sections only. Keep prose Vietnamese, code/identifiers English. The activation matrix at top + cross-phase coordination at bottom bookend the per-phase additions.

**Common conflict files in this codebase:**
- `GridEnemyAgent.cs` — controller logic, often touched by multiple phases (rotate, scuff, snap)
- `GridNavMask.cs` — pathfinding mask, touched whenever cost/clearance API changes
- `MapScenarioBootstrap.cs` — Inspector field hub, every phase adds at least one field
- `MapF_TankTest.unity` — single source-of-truth scene, never parallel-edit

**Common straightforward phases:** geometric primitives (HasClearance), pure-AI tweaks (rotate threshold cycling), API consumers (waypoint snap reading existing cost).

**Common complex phases:** anything requiring layer/tag changes (needs `unity-scene-prefab-wiring` co-owner), anything bridging player + AI input (TankMover changes affect both — prefer AI-side cycling instead).
