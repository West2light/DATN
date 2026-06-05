using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static pathfinder dùng PIBT (flow-aware A* + Frank-Wolfe).
/// Drop-in thay thế GridAStarPathfinder — trả về List&lt;Vector2Int&gt; path,
/// nhưng tối ưu traffic flow chung qua PIBTPlanner.
///
/// Mỗi caller phải có agentId từ PIBTPlanner.Register().
/// Shared flow grid trong PIBTPlanner phối hợp tất cả các agent.
/// </summary>
public static class GridPIBTPathfinder
{
    /// <summary>
    /// Tìm đường từ start → goal dùng PIBT.
    /// </summary>
    /// <param name="agentId">ID từ PIBTPlanner.Register()</param>
    /// <param name="frankWolfeMs">Time budget (ms) cho Frank-Wolfe iterations</param>
    public static bool TryFindPath(
        MapLoader mapLoader,
        int agentId,
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> path,
        float frankWolfeMs = 15f)
    {
        path.Clear();
        if (mapLoader == null || agentId < 0) return false;

        if (!PIBTPlanner.IsReady) PIBTPlanner.Init(mapLoader);
        if (!PIBTPlanner.IsReady) return false;

        // Resolve endpoints nếu chưa walkable (destructible cell coi là passable)
        if (!mapLoader.IsWalkable(start) && !mapLoader.IsDestructibleBlocked(start))
            if (!mapLoader.TryFindWalkableNear(start, out start)) return false;
        if (!mapLoader.IsWalkable(goal) && !mapLoader.IsDestructibleBlocked(goal))
            if (!mapLoader.TryFindWalkableNear(goal, out goal)) return false;

        int startFlat = PIBTPlanner.ToFlat(start);
        int goalFlat = PIBTPlanner.ToFlat(goal);

        PIBTPlanner.SetCurrentPos(agentId, startFlat);
        PIBTPlanner.FrankWolfe(agentId, startFlat, goalFlat, frankWolfeMs);

        var traj = PIBTPlanner.GetTraj(agentId);
        if (traj == null || traj.Count == 0) return false;

        foreach (int flat in traj)
            path.Add(PIBTPlanner.FromFlat(flat));

        return path.Count > 0;
    }
}
