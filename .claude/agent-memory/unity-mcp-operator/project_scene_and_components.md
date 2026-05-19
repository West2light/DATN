---
name: Stable scene IDs and component field inventory for MapF_TankTest
description: Instance IDs, confirmed field names, and Phase A/B parameter values for MapRuntime GameObject in MapF_TankTest.unity
type: project
---

Last verified: 2026-05-10

## MapRuntime GameObject
- Instance ID: 34732 (stable across sessions in this project — but re-confirm if scene is re-saved)
- Components: Transform(34738), MapLoader(34736), MapTankTestBootstrap(34734), MapScenarioBootstrap(34740)

## MapScenarioBootstrap confirmed field values (Inspector, Edit Mode)
Last verified: 2026-05-11 (Phase v4.2)
- tankPhysicalRadius = 0.45 (v4.2 NEW — physical OverlapCircle inflate radius)
- obstacleLayerMask = 0 in Inspector (by design — runtime fallback to LayerMask.GetMask("ObstaclesMovement") when value==0; Reset() sets it on fresh component add only)
- useChebyshevInflateLegacy = false (v4.2 NEW — ablation toggle; true = legacy Chebyshev mode)
- agentInflateRadius = 1 (deprecated by v4.2, retained as Chebyshev fallback radius)
- agentSoftRadius = 2
- agentSoftCostNear = 8
- agentSoftCostMid = 2
- tankClearanceRadius = 0.4 (Phase B)
- disableLegacyEnemyAI = true
- enemyReplanInterval = 0.75
- enemyEagleShootingRange = 5.0
- enemyPlayerShootingRange = 7.0
- drawAgentNavMask = true
- drawNavMaskHeatmap = true
- eagleCell = (16, 16)
- eagleHealth = 500
- enemySpawnCells = [(30,1), (1,30), (28,28), (16,1)]  ← [2] changed from (30,30) per P3 fix 2026-05-11

## GridEnemyAgent private field names (confirmed via reflection, 2026-05-10)
navMask, tankClearanceRadius, eagleTarget, playerTarget, tankController, replanInterval,
waypointReachDistance, waypointReachDistanceStraight, waypointReachDistanceTurning,
eagleShootingRange, playerShootingRange, lineOfSightMask, drawPath,
forwardAlignmentThreshold, turningDriveAlignmentThreshold, progressEpsilon,
stuckTimeout, reverseRecoveryDuration, spatialStuckWindow, spatialStuckMinTravelDistance,
spatialStuckMaxDisplacement, spatialStuckGoalProgressEpsilon, spatialRecentCellHistorySize,
spatialRecoveryBlockedCellCount, currentPath, spatialSamples, recentVisitedCells,
pathIndex, nextReplanTime, recoveryNavMask, recoveryLevel, lastProgressPosition,
lastProgressTime, hasProgressSample, reverseRecoveryEndTime, lastTrackedPathIndex,
hasRecordedCell, lastRecordedCell, lastSpatialRecoveryTime, pendingBlockedCells

## GridNavMask v4.2 properties (plain C# class at Assets/Scripts/Pathfinding/GridNavMask.cs)
- InflateRadius property: returns -1 when in physical mode (sentinel), returns agentInflateRadius value in Chebyshev mode
- PhysicalRadius property: returns 0.45 in physical mode, returns -1 in Chebyshev mode (sentinel)
- 6-arg constructor: GridNavMask(MapLoader, float physicalRadius, LayerMask obstacleMask, int softRadius, int softCostNear, int softCostMid)
- 5-arg constructor (Chebyshev legacy): GridNavMask(MapLoader, int inflateRadius, int softRadius, int softCostNear, int softCostMid)

## v4.2 Histogram baselines (random-32-32-10.map, 32x32=1024 total cells)
- Physical mode (tankPhysicalRadius=0.45): hardBlock=102, walkable=922, soft8=546, soft2=290, free=86
- Chebyshev legacy (agentInflateRadius=1): hardBlock=648, walkable=376, soft8=290, soft2=81, free=5
- Physical mode has FEWER hardBlock cells (102 vs 648) because it only blocks cells where the 0.45-radius circle overlaps an actual collider, vs Chebyshev which inflates every obstacle cell by radius=1 in a grid pattern

## Key architecture notes
- GridAStarPathfinder is a STATIC class (not a MonoBehaviour/Component)
- GridNavMask is a plain C# class (not MonoBehaviour) — cannot use FindObjectsByType<GridNavMask>
- ContextMenu hooks: MapLoader.LoadAndBuild, MapScenarioBootstrap.SpawnScenario
  Invoke via reflection: type.GetMethod("MethodName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(component, null)
