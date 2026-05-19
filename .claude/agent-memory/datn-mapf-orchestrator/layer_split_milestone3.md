---
name: Layer split required before Milestone 3
description: Walls vs AgentBlocker split needed before multi-agent to prevent scuff false-positives from tank-tank contact
type: project
---

Before starting Milestone 3 (multi-enemy), the project must split layer `ObstaclesMovement` into:
- `Walls` — actual map obstacles (used by `MapLoader.AddObstacleColliders`)
- `AgentBlocker` — per-tank player-blocker collider (used by `MapScenarioBootstrap.AddPlayerBlocker`)

**Why:** Phase D (capsule-aware scuff detection) uses `Rigidbody2D.IsTouchingLayers(obstacleContactMask)`. If `obstacleContactMask` includes `ObstaclesMovement` and PlayerBlocker also lives on that layer, then a tank touching another tank triggers a false scuff event — which causes recovery escalation (`Reverse` → `ExpandedMask`) on legit close-contact between agents. Multi-agent will be unstable.

**How to apply:**
- Single-agent mode: workaround is OK — keep `obstacleContactMask = ObstaclesMovement`. Tag a TODO.
- Before Milestone 3 (or any multi-enemy test): dispatch `unity-scene-prefab-wiring` to:
  1. Add layers `Walls` and `AgentBlocker` in `ProjectSettings/TagManager.asset`
  2. Update `MapLoader.AddObstacleColliders` to set obstacle layer = `Walls`
  3. Update `MapScenarioBootstrap.AddPlayerBlocker` to set blocker layer = `AgentBlocker`
  4. Default `obstacleContactMask` on `GridEnemyAgent` = `Walls` only
  5. Update layer collision matrix so Agent layer collides with both Walls and AgentBlocker

This is the only Phase D dependency that crosses the gameplay-engineer / wiring-agent boundary — both must be dispatched, with wiring going first.
