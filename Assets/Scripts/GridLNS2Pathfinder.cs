using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static pathfinder dùng LNS2 (flow-aware A* + Frank-Wolfe).
/// Drop-in thay thế GridAStarPathfinder — trả về List&lt;Vector2Int&gt; path,
/// nhưng tối ưu traffic flow chung qua LNS2Planner.
///
/// Mỗi caller phải có agentId từ LNS2Planner.Register().
/// Shared flow grid trong LNS2Planner phối hợp tất cả các agent.
/// </summary>
public static class GridLNS2Pathfinder
{
    /// <summary>
    /// Tìm đường từ start → goal dùng LNS2.
    /// </summary>
    /// <param name="agentId">ID từ LNS2Planner.Register()</param>
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

        if (!LNS2Planner.IsReady) LNS2Planner.Init(mapLoader);
        if (!LNS2Planner.IsReady) return false;

        // Resolve endpoints nếu chưa walkable
        if (!mapLoader.IsWalkable(start) && !mapLoader.TryFindWalkableNear(start, out start)) return false;
        if (!mapLoader.IsWalkable(goal)  && !mapLoader.TryFindWalkableNear(goal,  out goal))  return false;

        int startFlat = LNS2Planner.ToFlat(start);
        int goalFlat  = LNS2Planner.ToFlat(goal);

        LNS2Planner.SetCurrentPos(agentId, startFlat);
        LNS2Planner.FrankWolfe(agentId, startFlat, goalFlat, frankWolfeMs);

        var traj = LNS2Planner.GetTraj(agentId);
        if (traj == null || traj.Count == 0) return false;

        foreach (int flat in traj)
            path.Add(LNS2Planner.FromFlat(flat));

        return path.Count > 0;
    }
}
