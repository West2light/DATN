using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AIBehaviour điều khiển tank theo thuật toán LNS2.
/// Dùng LNS2Planner (Assets/Scripts/LNS2Planner.cs) cho pathfinding.
/// Gắn vào patrolBehaviour của DefaultEnemyAI.
/// </summary>
public class AIPatrolLNS2PathBehaviour : AIBehaviour
{
    [Header("References")]
    public MapLoader mapLoader;
    public Transform navigationTarget;

    [Header("Timing")]
    [Min(0.1f)] public float replanInterval = 0.5f;
    [Min(1f)]   public float frankWolfeMs   = 15f;

    [Header("Steering")]
    [Range(-1f, 1f)] public float forwardThreshold   = 0.97f;
    [Range(-1f, 1f)] public float turnDriveThreshold = 0.85f;
    [Min(0.01f)]     public float waypointReachDist  = 0.25f;

    public bool drawPath = true;

    private int agentId = -1;
    private readonly List<Vector2Int> path = new List<Vector2Int>();
    private int   pathIdx;
    private float nextReplanTime;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (mapLoader == null) mapLoader = FindFirstObjectByType<MapLoader>();
        TryInit();
    }

    private void OnDisable()
    {
        if (agentId >= 0) { LNS2Planner.Unregister(agentId); agentId = -1; }
    }

    public override void PerformAction(TankController tank, AIDetector detector)
    {
        if (mapLoader == null || navigationTarget == null) return;
        TryInit();
        if (agentId < 0) return;

        if (Time.time >= nextReplanTime || path.Count == 0)
        {
            Replan(tank);
            nextReplanTime = Time.time + replanInterval;
        }
        FollowPath(tank);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void TryInit()
    {
        if (!LNS2Planner.IsReady && mapLoader != null) LNS2Planner.Init(mapLoader);
        if (LNS2Planner.IsReady && agentId < 0) agentId = LNS2Planner.Register();
    }

    private void Replan(TankController tank)
    {
        Vector2Int sc = mapLoader.WorldToCell(tank.transform.position);
        Vector2Int gc = mapLoader.WorldToCell(navigationTarget.position);
        if (!mapLoader.IsWalkable(sc)) mapLoader.TryFindWalkableNear(sc, out sc);
        if (!mapLoader.IsWalkable(gc)) mapLoader.TryFindWalkableNear(gc, out gc);

        LNS2Planner.SetCurrentPos(agentId, LNS2Planner.ToFlat(sc));
        LNS2Planner.FrankWolfe(agentId, LNS2Planner.ToFlat(sc), LNS2Planner.ToFlat(gc), frankWolfeMs);

        path.Clear();
        var traj = LNS2Planner.GetTraj(agentId);
        if (traj != null) foreach (int f in traj) path.Add(LNS2Planner.FromFlat(f));
        pathIdx = path.Count > 1 ? 1 : 0;
    }

    private void FollowPath(TankController tank)
    {
        if (path.Count == 0 || pathIdx >= path.Count) { tank.HandleMoveBody(Vector2.zero); return; }

        Vector3 wp  = mapLoader.CellToWorld(path[pathIdx]);
        Vector2 dir = wp - tank.tankMover.transform.position;
        if (dir.magnitude <= waypointReachDist) { pathIdx++; return; }

        Vector2 fwd   = tank.tankMover.transform.up;
        float   dot   = Vector2.Dot(fwd, dir.normalized);
        float   cross = Vector3.Cross(fwd, dir.normalized).z;
        int     rot   = cross >= 0f ? -1 : 1;

        if      (dot >= forwardThreshold)   tank.HandleMoveBody(Vector2.up);
        else if (dot >= turnDriveThreshold) tank.HandleMoveBody(new Vector2(rot * 0.3f, 1f));
        else                                tank.HandleMoveBody(new Vector2(rot, dot > 0f ? 0.5f : 0f));
    }

    private void OnDrawGizmos()
    {
        if (!drawPath || mapLoader == null || path.Count < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = 1; i < path.Count; i++)
            Gizmos.DrawLine(mapLoader.CellToWorld(path[i - 1]), mapLoader.CellToWorld(path[i]));
    }
}
