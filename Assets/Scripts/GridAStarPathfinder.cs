using System.Collections.Generic;
using UnityEngine;

public static class GridAStarPathfinder
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    // Static caches — safe because Unity Update() is single-threaded.
    private static readonly List<(int f, Vector2Int cell)> s_heap = new();
    private static readonly HashSet<Vector2Int> s_closed = new();
    private static readonly Dictionary<Vector2Int, Vector2Int> s_cameFrom = new();
    private static readonly Dictionary<Vector2Int, int> s_gScore = new();

    public static bool TryFindPath(MapLoader mapLoader, Vector2Int start, Vector2Int goal, List<Vector2Int> path)
    {
        return TryFindPath(mapLoader, null, start, goal, path, null, 0f, false);
    }

    public static bool TryFindPath(MapLoader mapLoader, GridNavMask navMask, Vector2Int start, Vector2Int goal, List<Vector2Int> path)
    {
        return TryFindPath(mapLoader, navMask, start, goal, path, null, 0f, false);
    }

    public static bool TryFindPath(
        MapLoader mapLoader,
        GridNavMask navMask,
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> path,
        HashSet<Vector2Int> blockedCells)
    {
        return TryFindPath(mapLoader, navMask, start, goal, path, blockedCells, 0f, false);
    }

    public static bool TryFindPath(
        MapLoader mapLoader,
        GridNavMask navMask,
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> path,
        float clearanceRadius)
    {
        return TryFindPath(mapLoader, navMask, start, goal, path, null, clearanceRadius, false);
    }

    public static bool TryFindPath(
        MapLoader mapLoader,
        GridNavMask navMask,
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> path,
        HashSet<Vector2Int> blockedCells,
        float clearanceRadius)
    {
        return TryFindPath(mapLoader, navMask, start, goal, path, blockedCells, clearanceRadius, false);
    }

    public static bool TryFindPath(
        MapLoader mapLoader,
        GridNavMask navMask,
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> path,
        HashSet<Vector2Int> blockedCells,
        float clearanceRadius,
        bool enableSmoothing)
    {
        path.Clear();
        if (mapLoader == null) return false;

        if (!TryResolveEndpoint(mapLoader, navMask, blockedCells, start, out start)
            || !TryResolveEndpoint(mapLoader, navMask, blockedCells, goal, out goal))
        {
            return false;
        }

        s_heap.Clear();
        s_closed.Clear();
        s_cameFrom.Clear();
        s_gScore.Clear();

        s_gScore[start] = 0;
        HeapPush(start, Heuristic(start, goal));

        while (s_heap.Count > 0)
        {
            var (_, current) = HeapPop();

            // Lazy deletion: skip stale heap entries.
            if (s_closed.Contains(current)) continue;
            s_closed.Add(current);

            if (current == goal)
            {
                BuildPath(s_cameFrom, current, path);
                if (enableSmoothing && path.Count > 2)
                    SmoothPath(mapLoader, navMask, blockedCells, path, clearanceRadius);
                return true;
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int neighbor = current + Directions[i];
                if (s_closed.Contains(neighbor)) continue;
                if (!IsWalkable(mapLoader, navMask, blockedCells, neighbor)) continue;

                int stepCost = navMask != null ? navMask.GetCellCost(neighbor) : 0;
                if (stepCost == int.MaxValue) continue;

                int tentativeG = s_gScore[current] + 1 + stepCost;
                if (!s_gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                {
                    s_cameFrom[neighbor] = current;
                    s_gScore[neighbor] = tentativeG;
                    HeapPush(neighbor, tentativeG + Heuristic(neighbor, goal));
                }
            }
        }

        return false;
    }

    // ── Binary min-heap ───────────────────────────────────────────────────────

    private static void HeapPush(Vector2Int cell, int f)
    {
        s_heap.Add((f, cell));
        int i = s_heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (s_heap[parent].f <= s_heap[i].f) break;
            (s_heap[parent], s_heap[i]) = (s_heap[i], s_heap[parent]);
            i = parent;
        }
    }

    private static (int f, Vector2Int cell) HeapPop()
    {
        var result = s_heap[0];
        int last = s_heap.Count - 1;
        s_heap[0] = s_heap[last];
        s_heap.RemoveAt(last);
        int n = s_heap.Count;
        int i = 0;
        while (true)
        {
            int l = (i << 1) | 1, r = l + 1, m = i;
            if (l < n && s_heap[l].f < s_heap[m].f) m = l;
            if (r < n && s_heap[r].f < s_heap[m].f) m = r;
            if (m == i) break;
            (s_heap[m], s_heap[i]) = (s_heap[i], s_heap[m]);
            i = m;
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryResolveEndpoint(
        MapLoader mapLoader,
        GridNavMask navMask,
        HashSet<Vector2Int> blockedCells,
        Vector2Int cell,
        out Vector2Int resolvedCell)
    {
        if (IsWalkable(mapLoader, navMask, blockedCells, cell))
        {
            resolvedCell = cell;
            return true;
        }

        if (blockedCells != null && blockedCells.Count > 0)
            return TryFindWalkableNear(mapLoader, navMask, blockedCells, cell, out resolvedCell);

        if (navMask != null)
            return navMask.TryFindAgentWalkableNear(cell, out resolvedCell);

        return mapLoader.TryFindWalkableNear(cell, out resolvedCell);
    }

    private static bool TryFindWalkableNear(
        MapLoader mapLoader,
        GridNavMask navMask,
        HashSet<Vector2Int> blockedCells,
        Vector2Int preferredCell,
        out Vector2Int result)
    {
        if (IsWalkable(mapLoader, navMask, blockedCells, preferredCell))
        {
            result = preferredCell;
            return true;
        }

        int maxRadius = Mathf.Max(mapLoader.Width, mapLoader.Height);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int y = preferredCell.y - radius; y <= preferredCell.y + radius; y++)
            {
                for (int x = preferredCell.x - radius; x <= preferredCell.x + radius; x++)
                {
                    Vector2Int candidate = new Vector2Int(x, y);
                    if (IsWalkable(mapLoader, navMask, blockedCells, candidate))
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

    private static bool IsWalkable(MapLoader mapLoader, GridNavMask navMask, HashSet<Vector2Int> blockedCells, Vector2Int cell)
    {
        if (blockedCells != null && blockedCells.Contains(cell)) return false;
        // Destructible cells (thùng gỗ, rào chắn) luôn coi là passable để A* tìm đường qua
        if (mapLoader.IsDestructibleBlocked(cell)) return true;
        return navMask != null ? navMask.IsAgentWalkable(cell) : mapLoader.IsWalkable(cell);
    }

    private static int Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static void BuildPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current, List<Vector2Int> path)
    {
        path.Add(current);
        while (cameFrom.TryGetValue(current, out Vector2Int previous))
        {
            current = previous;
            path.Add(current);
        }
        path.Reverse();
    }

    // V4: smoothing disabled by default. Enable via TryFindPath enableSmoothing flag for ablation study.
    // Path raw 4-neighbor cell-to-cell is preferred for deterministic follow + PIBT compatibility.
    private static void SmoothPath(MapLoader mapLoader, GridNavMask navMask, HashSet<Vector2Int> blockedCells, List<Vector2Int> path, float clearanceRadius)
    {
        if (path.Count <= 2) return;

        bool useSweptCheck = navMask != null && clearanceRadius > 0f;

        List<Vector2Int> smoothedPath = new List<Vector2Int> { path[0] };
        int anchorIndex = 0;
        while (anchorIndex < path.Count - 1)
        {
            int furthestVisibleIndex = anchorIndex + 1;
            for (int candidateIndex = anchorIndex + 2; candidateIndex < path.Count; candidateIndex++)
            {
                bool clear;
                if (useSweptCheck)
                {
                    Vector2 startWorld = mapLoader.CellToWorld(path[anchorIndex]);
                    Vector2 endWorld = mapLoader.CellToWorld(path[candidateIndex]);
                    clear = navMask.HasClearance(startWorld, endWorld, clearanceRadius)
                        && HasLineOfSight(mapLoader, navMask, blockedCells, path[anchorIndex], path[candidateIndex]);
                }
                else
                {
                    clear = HasLineOfSight(mapLoader, navMask, blockedCells, path[anchorIndex], path[candidateIndex]);
                }

                if (!clear) break;
                furthestVisibleIndex = candidateIndex;
            }

            smoothedPath.Add(path[furthestVisibleIndex]);
            anchorIndex = furthestVisibleIndex;
        }

        path.Clear();
        path.AddRange(smoothedPath);
    }

    private static bool HasLineOfSight(
        MapLoader mapLoader,
        GridNavMask navMask,
        HashSet<Vector2Int> blockedCells,
        Vector2Int start,
        Vector2Int end)
    {
        int x = start.x;
        int y = start.y;
        int dx = end.x - start.x;
        int dy = end.y - start.y;
        int nx = Mathf.Abs(dx);
        int ny = Mathf.Abs(dy);
        int signX = dx == 0 ? 0 : dx > 0 ? 1 : -1;
        int signY = dy == 0 ? 0 : dy > 0 ? 1 : -1;
        int stepX = 0, stepY = 0;

        if (!IsWalkable(mapLoader, navMask, blockedCells, start)) return false;

        while (stepX < nx || stepY < ny)
        {
            int decision = (1 + 2 * stepX) * ny - (1 + 2 * stepY) * nx;
            if (decision == 0) { x += signX; y += signY; stepX++; stepY++; }
            else if (decision < 0) { x += signX; stepX++; }
            else { y += signY; stepY++; }

            if (!IsWalkable(mapLoader, navMask, blockedCells, new Vector2Int(x, y))) return false;
        }

        return true;
    }
}
