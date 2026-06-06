using UnityEngine;

public class GridNavMask
{
    private readonly MapLoader map;
    private bool[,] agentWalkable;
    private int[,] proximityCost;
    private int width;
    private int height;

    // Phase v4.2: physical-mode fields. When physicalRadius >= 0, RebuildPhysical
    // is used and InflateRadius == -1 acts as a sentinel "physical mode".
    private float physicalRadius = -1f;
    private LayerMask obstacleLayerMask;

    public int InflateRadius { get; private set; }
    public int SoftRadius { get; private set; }
    public int SoftCostNear { get; private set; }
    public int SoftCostMid { get; private set; }
    public float PhysicalRadius => physicalRadius;
    public LayerMask ObstacleLayerMask => obstacleLayerMask;

    // Backward-compat constructor (no soft cost layer, Chebyshev mode).
    public GridNavMask(MapLoader map, int inflateRadius)
        : this(map, inflateRadius, 0, 0, 0)
    {
    }

    // Legacy Chebyshev mode (deprecated, retained for ablation study).
    public GridNavMask(MapLoader map, int inflateRadius, int softRadius, int softCostNear, int softCostMid)
    {
        this.map = map;
        this.physicalRadius = -1f;
        this.obstacleLayerMask = 0;
        Rebuild(inflateRadius, softRadius, softCostNear, softCostMid);
    }

    /// <summary>
    /// Phase v4.2 — Physical-based hard inflate. Cell hard-blocked when a circle
    /// of <paramref name="physicalRadius"/> at cell center overlaps any collider
    /// on <paramref name="obstacleLayerMask"/>. Soft cost layer (Phase A) preserved
    /// on top via Chebyshev distance to hard-blocked cells.
    /// </summary>
    public GridNavMask(MapLoader map, float physicalRadius, LayerMask obstacleLayerMask, int softRadius, int softCostNear, int softCostMid)
    {
        this.map = map;
        this.physicalRadius = Mathf.Max(0f, physicalRadius);
        this.obstacleLayerMask = obstacleLayerMask;

        // Sentinel: physical mode → InflateRadius = -1.
        this.InflateRadius = -1;
        this.SoftRadius = Mathf.Max(0, softRadius);
        this.SoftCostNear = Mathf.Max(0, softCostNear);
        this.SoftCostMid = Mathf.Max(0, softCostMid);

        width = map != null ? map.Width : 0;
        height = map != null ? map.Height : 0;

        if (map == null || width <= 0 || height <= 0)
        {
            agentWalkable = null;
            proximityCost = null;
            return;
        }

        agentWalkable = new bool[width, height];
        proximityCost = new int[width, height];
        RebuildPhysical();
    }

    public bool IsAgentWalkable(Vector2Int cell)
    {
        return map != null
            && map.IsInside(cell)
            && agentWalkable != null
            && agentWalkable[cell.x, cell.y];
    }

    /// <summary>
    /// Returns soft-cost penalty for stepping onto <paramref name="cell"/>.
    /// 0 when far from obstacles, SoftCostMid for Chebyshev=2, SoftCostNear for Chebyshev=1.
    /// Returns int.MaxValue when the cell is hard-blocked (not agent-walkable).
    /// </summary>
    public int GetCellCost(Vector2Int cell)
    {
        if (!IsAgentWalkable(cell))
        {
            return int.MaxValue;
        }

        if (proximityCost == null)
        {
            return 0;
        }

        return proximityCost[cell.x, cell.y];
    }

    public void Rebuild(int newInflateRadius)
    {
        Rebuild(newInflateRadius, SoftRadius, SoftCostNear, SoftCostMid);
    }

    /// <summary>
    /// Legacy Chebyshev rebuild (deprecated). Kept for ablation study via
    /// <c>useChebyshevInflateLegacy</c> toggle in <see cref="MapScenarioBootstrap"/>.
    /// </summary>
    public void Rebuild(int newInflateRadius, int newSoftRadius, int newSoftCostNear, int newSoftCostMid)
    {
        InflateRadius = Mathf.Max(0, newInflateRadius);
        SoftRadius = Mathf.Max(0, newSoftRadius);
        SoftCostNear = Mathf.Max(0, newSoftCostNear);
        SoftCostMid = Mathf.Max(0, newSoftCostMid);

        // Switching to Chebyshev mode: clear physical-mode sentinel.
        physicalRadius = -1f;
        obstacleLayerMask = 0;

        width = map != null ? map.Width : 0;
        height = map != null ? map.Height : 0;

        if (map == null || width <= 0 || height <= 0)
        {
            agentWalkable = null;
            proximityCost = null;
            return;
        }

        agentWalkable = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                agentWalkable[x, y] = map.IsWalkable(cell) && !HasBlockedNeighborWithinRadius(cell);
            }
        }

        BuildProximityCost();
    }

    /// <summary>
    /// Phase v4.2 — Hard-block cells via <see cref="Physics2D.OverlapCircle"/>.
    /// Cell is agent-walkable iff (a) raw <see cref="MapLoader.IsWalkable"/> AND
    /// (b) no collider on <see cref="obstacleLayerMask"/> overlaps a circle of
    /// <see cref="physicalRadius"/> at cell center. Soft cost layer rebuilt on top.
    /// Requires obstacle colliders to be already spawned by the time this is called
    /// (callers should invoke <c>Physics2D.SyncTransforms</c> first when running in
    /// Edit Mode immediately after collider creation).
    /// </summary>
    public void RebuildPhysical()
    {
        if (map == null || width <= 0 || height <= 0)
        {
            agentWalkable = null;
            proximityCost = null;
            return;
        }

        if (agentWalkable == null || agentWalkable.GetLength(0) != width || agentWalkable.GetLength(1) != height)
        {
            agentWalkable = new bool[width, height];
        }

        // Step 1: hard-block via Physics2D.OverlapCircle.
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!map.IsWalkable(cell))
                {
                    agentWalkable[x, y] = false;
                    continue;
                }

                Vector2 cellCenter = map.CellToWorld(cell);
                Collider2D hit = Physics2D.OverlapCircle(cellCenter, physicalRadius, obstacleLayerMask);
                agentWalkable[x, y] = (hit == null);
            }
        }

        // Step 2: soft cost layer (reuse Phase A logic, measured against the
        // physical-walkable mask).
        BuildProximityCost();
    }

    /// <summary>
    /// Returns true when a swept circle of <paramref name="radius"/> sliding from
    /// <paramref name="worldStart"/> to <paramref name="worldEnd"/> does not touch
    /// any obstacle cell (raw `@` tiles in <see cref="MapLoader"/>). Pure analytical
    /// segment-vs-expanded-tile check that runs in Edit mode without physics.
    /// Used by the path smoother to ensure shortcut segments keep enough clearance
    /// for a tank-sized agent.
    /// </summary>
    public bool HasClearance(Vector2 worldStart, Vector2 worldEnd, float radius)
    {
        if (map == null || width <= 0 || height <= 0)
        {
            return false;
        }

        float tile = map.tileSize;
        float expandedHalfExtent = tile * 0.5f + radius;
        float pad = expandedHalfExtent;

        // Bounding box in world coords, padded so any obstacle tile whose expanded
        // box could overlap the segment is considered.
        float minX = Mathf.Min(worldStart.x, worldEnd.x) - pad;
        float maxX = Mathf.Max(worldStart.x, worldEnd.x) + pad;
        float minY = Mathf.Min(worldStart.y, worldEnd.y) - pad;
        float maxY = Mathf.Max(worldStart.y, worldEnd.y) + pad;

        Vector2Int cellMin = map.WorldToCell(new Vector3(minX, maxY, 0f)); // top-left
        Vector2Int cellMax = map.WorldToCell(new Vector3(maxX, minY, 0f)); // bottom-right

        int xLo = Mathf.Clamp(Mathf.Min(cellMin.x, cellMax.x), 0, width - 1);
        int xHi = Mathf.Clamp(Mathf.Max(cellMin.x, cellMax.x), 0, width - 1);
        int yLo = Mathf.Clamp(Mathf.Min(cellMin.y, cellMax.y), 0, height - 1);
        int yHi = Mathf.Clamp(Mathf.Max(cellMin.y, cellMax.y), 0, height - 1);

        Vector2 expandedHalfExtents = Vector2.one * expandedHalfExtent;
        for (int y = yLo; y <= yHi; y++)
        {
            for (int x = xLo; x <= xHi; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                // We only care about *physical* obstacles (raw `@`), not inflate-soft cells.
                if (map.IsWalkable(cell))
                {
                    continue;
                }

                Vector2 cellCenter = map.CellToWorld(cell);
                if (SegmentIntersectsAabb(worldStart, worldEnd, cellCenter, expandedHalfExtents))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SegmentIntersectsAabb(Vector2 segStart, Vector2 segEnd, Vector2 boxCenter, Vector2 boxHalfExtents)
    {
        Vector2 boxMin = boxCenter - boxHalfExtents;
        Vector2 boxMax = boxCenter + boxHalfExtents;
        Vector2 delta = segEnd - segStart;
        float tMin = 0f;
        float tMax = 1f;

        return ClipSegmentAxis(segStart.x, delta.x, boxMin.x, boxMax.x, ref tMin, ref tMax)
            && ClipSegmentAxis(segStart.y, delta.y, boxMin.y, boxMax.y, ref tMin, ref tMax);
    }

    private static bool ClipSegmentAxis(float start, float delta, float min, float max, ref float tMin, ref float tMax)
    {
        if (Mathf.Abs(delta) <= Mathf.Epsilon)
        {
            return start >= min && start <= max;
        }

        float inverseDelta = 1f / delta;
        float t1 = (min - start) * inverseDelta;
        float t2 = (max - start) * inverseDelta;
        if (t1 > t2)
        {
            float temp = t1;
            t1 = t2;
            t2 = temp;
        }

        tMin = Mathf.Max(tMin, t1);
        tMax = Mathf.Min(tMax, t2);
        return tMin <= tMax;
    }

    /// <summary>
    /// Sau khi navMask được build, mở lại các cell destructible và vùng lân cận
    /// mà bị đánh dấu non-walkable do inflation — để A* có thể tìm đường qua thùng gỗ.
    /// Gọi ngay sau BuildNavMask() trong MapScenarioBootstrap.
    /// </summary>
    public void PatchDestructibleCells(MapLoader mapLoader)
    {
        if (agentWalkable == null || mapLoader == null) return;

        int patchRadius = physicalRadius >= 0
            ? Mathf.CeilToInt(physicalRadius / Mathf.Max(0.001f, mapLoader.tileSize)) + 1
            : Mathf.Max(1, InflateRadius + 1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mapLoader.IsDestructibleBlocked(new Vector2Int(x, y))) continue;

                // Bản thân cell destructible: coi là passable trong pathfinding
                agentWalkable[x, y] = true;

                // Các cell lân cận bị inflation block vì crate — mở lại nếu là grid-walkable
                for (int dy = -patchRadius; dy <= patchRadius; dy++)
                {
                    for (int dx = -patchRadius; dx <= patchRadius; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        var nb = new Vector2Int(nx, ny);
                        // Chỉ mở lại cell thuần walkable hoặc cũng là destructible, không phải static wall
                        if (!mapLoader.IsWalkable(nb) && !mapLoader.IsDestructibleBlocked(nb)) continue;
                        agentWalkable[nx, ny] = true;
                    }
                }
            }
        }
    }

    // Cập nhật walkability của một ô cụ thể — dùng bởi DynamicObstacleSpawner
    // để đồng bộ NavMask khi thùng sắt động xuất hiện / biến mất.
    public void SetCellAgentWalkable(Vector2Int cell, bool walkable)
    {
        if (agentWalkable != null && cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height)
            agentWalkable[cell.x, cell.y] = walkable;
    }

    public bool TryFindAgentWalkableNear(Vector2Int preferredCell, out Vector2Int result)
    {
        if (IsAgentWalkable(preferredCell))
        {
            result = preferredCell;
            return true;
        }

        int maxRadius = Mathf.Max(width, height);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int y = preferredCell.y - radius; y <= preferredCell.y + radius; y++)
            {
                for (int x = preferredCell.x - radius; x <= preferredCell.x + radius; x++)
                {
                    Vector2Int candidate = new Vector2Int(x, y);
                    if (IsAgentWalkable(candidate))
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
        }

        result = default;
        return false;
    }

    private bool HasBlockedNeighborWithinRadius(Vector2Int centerCell)
    {
        if (InflateRadius <= 0)
        {
            return false;
        }

        for (int y = centerCell.y - InflateRadius; y <= centerCell.y + InflateRadius; y++)
        {
            for (int x = centerCell.x - InflateRadius; x <= centerCell.x + InflateRadius; x++)
            {
                Vector2Int neighbor = new Vector2Int(x, y);
                if (!map.IsInside(neighbor) || !map.IsWalkable(neighbor))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void BuildProximityCost()
    {
        proximityCost = new int[width, height];
        if (SoftRadius <= 0 || (SoftCostNear <= 0 && SoftCostMid <= 0))
        {
            return;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!agentWalkable[x, y])
                {
                    continue;
                }

                int chebyshev = ChebyshevDistanceToBlocked(new Vector2Int(x, y));
                if (chebyshev <= 0)
                {
                    continue; // cell itself is blocked – guarded above.
                }

                int cost = 0;
                if (chebyshev == 1)
                {
                    cost = SoftCostNear;
                }
                else if (chebyshev == 2)
                {
                    cost = SoftCostMid;
                }
                // chebyshev >= 3 ⇒ cost stays 0.

                if (cost > proximityCost[x, y])
                {
                    proximityCost[x, y] = cost;
                }
            }
        }
    }

    /// <summary>
    /// Smallest Chebyshev distance from <paramref name="cell"/> to any cell that the
    /// agent cannot stand on — out-of-bounds, an `@` tile, OR a cell removed by
    /// <see cref="InflateRadius"/>. We measure against the agent-walkable mask rather
    /// than the raw map so that with InflateRadius=1 the "wall-adjacent" ring
    /// (cells next to the inflated forbidden zone) still gets <see cref="SoftCostNear"/>.
    /// Searches only within SoftRadius. Returns int.MaxValue if no blocked cell is
    /// found inside the window (i.e. cell is "deep interior").
    /// </summary>
    private int ChebyshevDistanceToBlocked(Vector2Int cell)
    {
        for (int radius = 1; radius <= SoftRadius; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    // Only inspect the perimeter of the current radius ring.
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                    {
                        continue;
                    }

                    Vector2Int neighbor = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (!map.IsInside(neighbor) || !agentWalkable[neighbor.x, neighbor.y])
                    {
                        return radius;
                    }
                }
            }
        }

        return int.MaxValue;
    }
}
