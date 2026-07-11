# Backtest Metrics Note: Total Replans and Cells Traveled

Date: 2026-06-24

Scope: Unity backtest report for `AStar`, `PIBT`, and `PIBT_TCP`.

## Where The Summary Values Come From

Each backtest run produces one `BacktestRunRecord`.

For each run, `BacktestRunner.RecordResult()` collects every active enemy agent and copies these per-agent counters into `BacktestAgentRecord`:

- `replanCount` from `agent.btReplanCount`
- `cellsVisited` from `agent.btCellsVisited`

Then it computes run-level totals:

```text
run.totalReplans = sum(agent.replanCount for all agents in the run)
run.totalCells   = sum(agent.cellsVisited for all agents in the run)
```

The HTML summary table groups records by `(Map, Algorithm)` and displays the average across repetitions:

```text
summary Total replans = average(run.totalReplans for same map + algorithm)
summary Cells traveled = average(run.totalCells for same map + algorithm)
```

So the table is not summing all repetitions together. It is displaying the average of the per-run totals.

## AStar

Source component: `GridEnemyAgent`

### Total replans

`btReplanCount` increments whenever `ReplanPath()` or `ReplanToDestructible()` runs.

Main triggers:

- Periodic path refresh when `Time.time >= nextReplanTime`
- Empty path, which forces a new path request
- Dynamic/friendly blocker handling that clears the path and sets `nextReplanTime = 0`
- Recovery flow that clears the path and calls `ReplanPath()`
- Destructible obstacle handling via `ReplanToDestructible()`

Default cadence comes from `replanInterval`, wired by `MapScenarioBootstrap.enemyReplanInterval`.

### Cells traveled

`btCellsVisited` increments when the agent reaches the current path waypoint and advances `pathIndex`.

This is path-waypoint progress, not raw physics distance. If a tank wiggles inside the same grid cell, it does not increase this value.

## PIBT C#

Source component: `GridEnemyAgentPIBT`

### Total replans

`btReplanCount` increments whenever `ReplanPath()` or `ReplanToDestructible()` runs.

Main triggers:

- Periodic PIBT path refresh when `Time.time >= nextReplanTime`
- Empty path, which forces a new PIBT path request
- Friendly shot blocker handling that clears the path and sets `nextReplanTime = 0`
- Spatial/scuff/reverse recovery paths that call `ReplanPath()`
- Destructible obstacle handling via `ReplanToDestructible()`

Default cadence comes from `replanInterval`, wired by `MapScenarioBootstrapPIBT.enemyReplanInterval`.

Important interpretation: this metric is "number of local C# planner calls", not only emergency replans. A long run with many agents naturally produces many replans even when movement is healthy.

### Cells traveled

`btCellsVisited` increments when the tank reaches the current path waypoint and advances `pathIndex`.

This counts completed grid-waypoint transitions along the planned path. It does not count every physics frame or every small movement input.

## PIBT_TCP

Source component: `GridEnemyAgentPIBT_TCP`; coordinator: `MapScenarioBootstrapPIBT_TCP`.

### Total replans (updated 2026-06-24 — cadence-normalized)

`btReplanCount` now increments via **two sources** that are summed into the same field:

**Source 1 — cadence-normalized server ticks (primary, implemented 2026-06-24):**

`MapScenarioBootstrapPIBT_TCP.AccumulateReplanWindow()` is called after each successful `plan_step`. It accumulates `tcpTickInterval` (0.25s) and fires `btReplanCount++` for every active agent once per `enemyReplanInterval` (0.75s) window.

```
_replanWindowAccum += tcpTickInterval
if _replanWindowAccum >= enemyReplanInterval:
    foreach agent: btReplanCount++
    _replanWindowAccum -= enemyReplanInterval
```

This puts PIBT_TCP on the same cadence as A*/PIBT-C# (+1 per agent per 0.75s), making the column directly comparable.

**Source 2 — forced replan due to stuck (secondary, unchanged):**

`btReplanCount++` also fires in `TrackStuckAndRecover()` when the no-progress watchdog triggers. This is rare during healthy movement and does not significantly affect the total.

### Comparison validity

All three algorithms now share the same unit: **"number of times a planner was invoked for one agent, measured in 0.75s windows"**.

| Algorithm | Cadence | `btReplanCount` per agent per 30s |
|---|---|---|
| AStar | +1 per ReplanPath() call ≈ 0.75s | ~40 |
| PIBT C# | +1 per ReplanPath() call ≈ 0.75s | ~40 |
| PIBT_TCP | +1 per 0.75s window (from 0.25s ticks) | ~40 |

Note for thesis: 1 `plan_step` solves for all N agents simultaneously (centralized), while A* and PIBT C# replan per-agent independently. The cadence normalization makes the count comparable, but the architectural difference should be noted.

### Cells traveled

`btCellsVisited` increments when `CurrentCell` changes.

This is actual grid-cell progress based on the tank's world position converted through `MapLoader.WorldToCell(...)`. It is stricter than counting target assignments from the server: receiving a new target does not count unless the tank physically enters a new grid cell.

## Reading The Report Correctly

- `Total replans` is a run-level total across all agents, averaged across repetitions in the summary table.
- `Cells traveled` is a run-level total across all agents, averaged across repetitions in the summary table.
- All three algorithms count planner invocations on the same 0.75s cadence — columns are directly comparable.
- Old CSV/HTML files generated before the cadence-normalization fix (2026-06-24) have PIBT_TCP `Replans = 0` (only stuck-replans were counted) and should not be used for algorithm comparison.
