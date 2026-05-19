---
name: Soft cost Chebyshev semantics with inflate
description: When inflate>0 coexists with soft cost on the same nav mask, measure Chebyshev distance to the agent-walkable boundary (not raw @ tiles), or the inner soft ring is empty.
type: project
---

When `GridNavMask` has BOTH `InflateRadius > 0` AND a soft-cost layer (`SoftRadius > 0`, `SoftCostNear`, `SoftCostMid`), the Chebyshev distance for soft-cost classification must be measured against the **agent-walkable boundary** (out-of-bounds OR `@` OR cell removed by inflate), not the raw `@` cells.

**Why**: With `InflateRadius=1`, no agent-walkable cell can be at Chebyshev=1 from a raw `@` — that ring was already removed by inflate. If the soft-cost code measures distance to raw `@`, the `SoftCostNear` ring is empty for default params and the heatmap acceptance criterion ("đỏ đậm visible") fails. The user-facing intent ("tránh cọ tường") includes "tường" = the inflated forbidden zone, not just the raw obstacle.

**How to apply**: When reviewing or extending soft-cost logic in `GridNavMask.BuildProximityCost` / `ChebyshevDistanceToBlocked`, the inner loop should test `agentWalkable[neighbor]` (which already incorporates inflate), not `map.IsWalkable(neighbor)`. The plan v3 Phase A spec was literal ("Chebyshev tới `@`") but the implementation must follow intent. Verify with a histogram: with `InflateRadius=1, SoftRadius=2, SoftCostNear=8, SoftCostMid=2` on `random-32-32-10.map` you should see roughly 290/81/5 cells at cost 8/2/0.
