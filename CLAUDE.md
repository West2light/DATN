# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

Unity 2D top-down tank game built as a graduation thesis (DATN) on Multi-Agent Pathfinding (MAPF). The end goal is to compare AI navigation tiers (A\* baseline → prioritized planning → LNS2) inside a multiplayer tank game where players defend an Eagle base. See `Assets/Docs/DATN.md` for the full thesis brief and `Assets/Docs/mapf_eagle_enemy_astar_plan.md` for the staged implementation plan (Milestone 1 → 5).

- Unity Editor: **6000.3.10f1** (Unity 6). Render pipeline: URP. 2D project.
- Source-of-truth scene for MAPF/AI work is `Assets/Scenes/MapF_TankTest.unity`. `Lvl1`/`Lvl2`/`Menu` are the older single-player tutorial scenes that the MAPF work is built on top of.
- Project uses MAPF benchmark `.map` files from `Assets/MapData/` (currently `random-32-32-10.map`) — keep this format as the canonical map representation; do not migrate to Unity NavMesh as the primary navigation system.
- Documentation in `Assets/Docs/` is in Vietnamese. Keep prose in Vietnamese when extending those docs; keep code/identifiers in English.

## Build, run, test

- No CLI build commands. Open the project with `Unity Hub` or by opening `projectY.sln`. Editing/iteration happens inside the Unity Editor.
- To run the MAPF scenario: open `Assets/Scenes/MapF_TankTest.unity` and press Play. `MapTankTestBootstrap` builds the map, spawns the player tank, and triggers `MapScenarioBootstrap.SpawnScenario()` which spawns the Eagle and enemies.
- To rebuild the map without entering Play Mode: right-click the `MapLoader` component → **Load Map Now** (ContextMenu). Same trick for `MapScenarioBootstrap` → **Spawn Scenario Now**.
- Tests: `com.unity.test-framework` is in `Packages/manifest.json` but no test assemblies have been authored yet. Run via **Window → General → Test Runner** if/when tests are added.
- The Unity MCP plugin (`com.coplaydev.unity-mcp`) is installed — agent-driven editor automation is available if the user has the MCP server running.

## High-level architecture

### Map → grid → world coordinates
`MapLoader` (`Assets/Scripts/MapLoader.cs`) is the foundation everything else builds on. It:
- Reads MAPF benchmark `.map` headers (`type`, `height`, `width`, `map`) from `Assets/MapData/<file>.map`.
- Builds a `char[][] grid` and instantiates one `GameObject` per cell using sprites from `Assets/Sprites/Kenny Topdown Tanks Redux/...`. Walkable cells use `.`; obstacles use `@` (also `T`/`W`/`S` if present).
- Adds `BoxCollider2D` to obstacles on layer **`ObstaclesMovement`**, plus a child trigger collider on layer **`Hittable`** so bullets register hits via `Bullet.OnTriggerEnter2D`.
- Wraps the build window in 4 boundary colliders so tanks cannot leave the map.
- Exposes `IsWalkable(cell)`, `CellToWorld(cell)`, `WorldToCell(pos)`, and `TryFindWalkableNear(cell, out result)` — every spawner and pathfinder must go through these. Cells use top-left origin with Y growing downward (XY plane, Z=0).

### Two AI systems coexist — keep them separate
1. **Legacy AI** (`Assets/Scripts/Ai/`): `DefaultEnemyAI` toggles between `shootBehaviour` and `patrolBehaviour` (`AIBehaviour` subclasses) based on `AIDetector.TargetVisible`. Used by `EnemyTank`/`PatrolingEnemy`/`StaticEnemy` prefabs in the older levels.
2. **MAPF AI** (`Assets/Scripts/GridEnemyAgent.cs` + `GridAStarPathfinder.cs`): grid-based A\* path follower that drives `TankController` directly. Replans every `replanInterval`. Switches to shoot mode when player or eagle is within range and visible.

`MapScenarioBootstrap.disableLegacyEnemyAI = true` (default) disables `DefaultEnemyAI` on spawned enemies so the two systems do not fight for control of `TankController`. **Do not run both on the same prefab simultaneously** — see `Assets/Docs/mapf_eagle_enemy_astar_plan.md` "Rủi ro cần tránh".

### Scenario boot flow (MapF_TankTest)
```
MapTankTestBootstrap.Start()
 ├─ mapLoader.LoadAndBuild()              // build tiles + bounds
 ├─ SpawnPlayer()                          // Tank.prefab @ playerSpawnCell
 ├─ SetupCamera()                          // orthographic follow in LateUpdate
 └─ MapScenarioBootstrap.SpawnScenario()
      ├─ SpawnEagleBase()                  // runtime GO with Damagable, layer Hittable
      └─ SpawnEnemies()                    // for each cell: prefab → ConfigureEnemy
           ├─ AddPlayerBlocker             // ObstaclesMovement collider so player can't pass
           ├─ AddGridEnemyAgent            // wires MapLoader, eagle target, layer mask
           └─ disable DefaultEnemyAI components
```

When adding new enemy types or scenario variants, extend `MapScenarioBootstrap` rather than `MapTankTestBootstrap`. The latter only handles map + player + camera.

### Tank control pipeline
Input → `PlayerInput` UnityEvents → `TankController.HandleMoveBody / HandleTurretMovement / HandleShoot` → `TankMover` (Rigidbody2D physics, accel/rotation from `TankMovementData` ScriptableObject) and `Turret`/`AimTurret`. AI agents call the same `TankController` methods, so any control logic added must work for both human and AI input.

`TankMover` uses continuous physics; A\* outputs discrete waypoints. `GridEnemyAgent.FollowPath()` uses a dot-product threshold (`0.96`) to decide rotate-then-drive, with `waypointReachDistance = 0.25f`. Tune those when changing `tileSize`.

### Damage / pooling
- Damage is one-way: `Bullet.OnTriggerEnter2D` → `Damagable.Hit(damage)`. `Damagable.OnDead` is a UnityEvent; the Eagle wires it to `DestroyUtil.DestroyHelper`. Bullets use `Linecast` from previous to current position to avoid tunneling at high speeds.
- `ObjectPool` is a generic queue-based pool. Pooled objects auto-attach `DestroyIfDisabled` and are returned by SetActive(false). Used by `Turret` for bullets.

### ScriptableObject data
`Assets/Data/` holds `TankMovementData`, `BulletData`, `TurretData` assets. The `MovementData` reference on `TankMover` and `BulletData` on `Bullet` are required — `MapTankTestBootstrap` and `MapScenarioBootstrap` auto-resolve them via `AssetDatabase.LoadAssetAtPath` (Editor-only) when the inline reference is null.

### Layers (referenced by name in code)
- `ObstaclesMovement` — physical blocking colliders (walls, tank player-blocker)
- `Hittable` — bullet trigger receivers (walls, eagle, tanks)
- `Player`, `Agent`, `Eagle` — sorting/raycast layers used by `GridEnemyAgent.lineOfSightMask`

If you add new layers, also update the layer-mask construction in `MapScenarioBootstrap.AddGridEnemyAgent`.

## Conventions specific to this codebase

- Runtime spawning over prefab instantiation for the MAPF scenario: Eagle and per-tile colliders are built procedurally so the same code works with any `.map` file. Don't bake scenario state into scenes.
- `AssetDatabase.LoadAssetAtPath` is wrapped in `#if UNITY_EDITOR` for fallback resolution. Anything that must survive in a built player must have its references serialized in the Inspector.
- Map cell origin is **top-left** with Y increasing downward (matches the `.map` file row order); world coordinates are centered on (0,0) in XY. When you compute new cell math, mirror `MapLoader.CellToWorld` rather than re-deriving.
- A\* is currently 4-neighbor with Manhattan heuristic. The plan calls for keeping it 4-neighbor before introducing reservation tables / prioritized planning (Milestone 4 in `mapf_eagle_enemy_astar_plan.md`). Don't switch to 8-neighbor without revisiting that plan.
- This is a thesis project — prefer readable, instrumentable code over clever optimizations. Metrics (path length, replan count, collisions) will be added in Milestone 3.
