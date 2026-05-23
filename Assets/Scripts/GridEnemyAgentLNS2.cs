using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bản sao GridEnemyAgent dùng LNS2 pathfinding thay A*.
///
/// Thay đổi so với GridEnemyAgent:
///  - Pathfinding: GridLNS2Pathfinder (flow-aware A* + Frank-Wolfe) thay GridAStarPathfinder
///  - Không dùng GridNavMask (LNS2 tự build neighbor list từ MapLoader.IsWalkable)
///  - agentId đăng ký với LNS2Planner để chia sẻ flow grid chung
///  - RecoveryLevel.ExpandedMask bị loại (LNS2 tự tránh tắc nghẽn qua flow)
///  - Spatial recovery không dùng blockedCells (force replan thay thế)
/// </summary>
public class GridEnemyAgentLNS2 : MonoBehaviour
{
    private struct SpatialSample
    {
        public Vector3 Position;
        public float Time;
        public float GoalDistance;
    }

    private enum RecoveryLevel
    {
        None,
        ForcedReplan,
        Reverse
    }

    // ── Inspector ──────────────────────────────────────────────────────────
    public MapLoader mapLoader;
    public Transform eagleTarget;
    public Transform playerTarget;
    public TankController tankController;

    [Header("LNS2")]
    [Min(1f)] public float frankWolfeMs = 15f;
    [Min(0)] public int obstacleInflateRadius = 1;

    [Header("Timing")]
    public float replanInterval = 0.75f;

    [Header("Steering")]
    public float waypointReachDistance    = 0.25f;
    public float waypointReachDistanceStraight = 0.3f;
    public float waypointReachDistanceTurning  = 0.12f;
    [Header("Obstacle corner safety")]
    public int obstacleProximityCellRadius = 1;
    public float obstacleWaypointReachDistance = 0.06f;
    [Range(-1f, 1f)] public float obstacleForwardAlignmentThreshold = 0.985f;
    [Range(-1f, 1f)] public float forwardAlignmentThreshold      = 0.97f;
    [Range(-1f, 1f)] public float turningDriveAlignmentThreshold = 0.85f;
    [Range(-1f, 1f)] public float partialDriveAlignmentThreshold = 0.5f;
    [Range(0f,  1f)] public float mostlyAlignedTurnScale         = 0.3f;
    [Min(0.01f)]     public float partialDrivePeriod             = 0.1f;
    [Range(0f,  1f)] public float partialDriveDutyCycle          = 0.65f;

    [Header("Shooting")]
    public float eagleShootingRange  = 5f;
    public float playerShootingRange = 7f;
    public LayerMask lineOfSightMask;

    [Header("Stuck detection")]
    public float progressEpsilon    = 0.05f;
    public float stuckTimeout       = 1.5f;
    public LayerMask obstacleContactMask;
    public float scuffTimeout       = 0.4f;
    public float scuffVelocityThreshold = 0.1f;
    public float reverseRecoveryDuration = 0.8f;
    public float spatialStuckWindow      = 3f;
    public float spatialStuckMinTravelDistance = 1.5f;
    public float spatialStuckMaxDisplacement   = 0.75f;
    public float spatialStuckGoalProgressEpsilon = 0.5f;
    public int   spatialRecentCellHistorySize  = 8;

    public bool drawPath = true;

    // ── Runtime state ──────────────────────────────────────────────────────
    private int agentId = -1;

    private readonly List<Vector2Int> currentPath    = new List<Vector2Int>();
    private readonly Queue<SpatialSample> spatialSamples = new Queue<SpatialSample>();
    private readonly List<Vector2Int> recentVisitedCells = new List<Vector2Int>();

    private int   pathIndex;
    private float nextReplanTime;
    private RecoveryLevel recoveryLevel;
    private Vector3 lastProgressPosition;
    private float   lastProgressTime;
    private bool    hasProgressSample;
    private float   reverseRecoveryEndTime;
    private int     lastTrackedPathIndex = -1;
    private bool    hasRecordedCell;
    private Vector2Int lastRecordedCell;
    private float   lastSpatialRecoveryTime = float.NegativeInfinity;
    private float   scuffStartTime = -1f;
    private float   lastScuffRecoveryTime = float.NegativeInfinity;
    private bool    wasTouchingLastFrame;
    private float   partialDriveAccumulator;
    private int     lastSteeringDirection = 1;
    private int     reverseRecoveryTurnDirection = 1;
    private HashSet<Vector2Int> pendingBlockedCells;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (tankController == null)
            tankController = GetComponentInChildren<TankController>();
    }

    private void OnDestroy()
    {
        if (agentId >= 0)
        {
            LNS2Planner.Unregister(agentId);
            agentId = -1;
        }
    }

    private void EnsureLNS2Ready()
    {
        if (mapLoader != null) LNS2Planner.Init(mapLoader, obstacleInflateRadius);
        if (LNS2Planner.IsReady && agentId < 0) agentId = LNS2Planner.Register();
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (mapLoader == null || tankController == null || eagleTarget == null) return;

        EnsureLNS2Ready();
        if (agentId < 0) return;

        bool blockedShotByFriendly = TryQueueFriendlyShotBlocker();
        Transform shootingTarget = blockedShotByFriendly ? null : GetShootingTarget();
        if (shootingTarget != null)
        {
            ResetProgressTracking();
            tankController.HandleMoveBody(Vector2.zero);
            tankController.HandleTurretMovement(shootingTarget.position);
            if (tankController.aimTurret != null && tankController.aimTurret.IsAlignedTo(shootingTarget.position))
            {
                tankController.HandleShoot();
            }
            return;
        }

        if (recoveryLevel == RecoveryLevel.Reverse && Time.time < reverseRecoveryEndTime)
        {
            ContinueReverseRecovery();
            return;
        }
        else if (recoveryLevel == RecoveryLevel.Reverse && reverseRecoveryEndTime > 0f)
        {
            FinishReverseRecovery();
        }

        if (Time.time >= nextReplanTime || currentPath.Count == 0)
            ReplanPath();

        FollowPath();
        UpdateProgressTracking();
    }

    // ── Shooting ───────────────────────────────────────────────────────────

    private Transform GetShootingTarget()
    {
        if (CanShootTarget(playerTarget, playerShootingRange)) return playerTarget;
        if (CanShootTarget(eagleTarget,  eagleShootingRange))  return eagleTarget;
        return null;
    }

    private bool TryQueueFriendlyShotBlocker()
    {
        if (TryQueueFriendlyShotBlocker(playerTarget, playerShootingRange))
        {
            return true;
        }

        return TryQueueFriendlyShotBlocker(eagleTarget, eagleShootingRange);
    }

    private bool TryQueueFriendlyShotBlocker(Transform target, float range)
    {
        if (!TryGetFriendlyShotBlocker(target, range, out Vector2Int blockedCell))
        {
            return false;
        }

        pendingBlockedCells ??= new HashSet<Vector2Int>();
        pendingBlockedCells.Add(blockedCell);
        currentPath.Clear();
        pathIndex = 0;
        nextReplanTime = 0f;
        return true;
    }

    private bool CanShootTarget(Transform target, float range)
    {
        if (target == null) return false;
        Vector2 origin = tankController.aimTurret.transform.position;
        Vector2 targetPos = target.position;
        if (Vector2.Distance(origin, targetPos) > range) return false;
        RaycastHit2D hit = Physics2D.Raycast(origin, targetPos - origin, range, lineOfSightMask);
        if (hit.collider == null) return false;
        return hit.collider.transform == target || hit.collider.transform.IsChildOf(target);
    }

    private bool TryGetFriendlyShotBlocker(Transform target, float range, out Vector2Int blockedCell)
    {
        blockedCell = default;
        if (target == null || tankController == null || tankController.aimTurret == null)
        {
            return false;
        }

        Vector2 origin = tankController.aimTurret.transform.position;
        Vector2 targetPos = target.position;
        Vector2 direction = targetPos - origin;
        if (direction.sqrMagnitude <= Mathf.Epsilon || direction.magnitude > range)
        {
            return false;
        }

        FactionMember selfFaction = GetComponent<FactionMember>();
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction.normalized, range, lineOfSightMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D collider = hits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (collider.transform == target || collider.transform.IsChildOf(target))
            {
                return false;
            }

            FactionMember hitFaction = FactionMember.FindForCollider(collider);
            if (FactionMember.AreFriendly(selfFaction, hitFaction))
            {
                blockedCell = mapLoader.WorldToCell(hitFaction.GetWorldPosition());
                return true;
            }

            return false;
        }

        return false;
    }

    // ── Pathfinding (LNS2) ─────────────────────────────────────────────────

    private void ReplanPath()
    {
        nextReplanTime = Time.time + replanInterval;
        Vector2Int startCell = mapLoader.WorldToCell(GetAgentPosition());
        HashSet<Vector2Int> blockedCells = MergeBlockedCells(pendingBlockedCells, BuildDynamicBlockedCells(startCell));
        pendingBlockedCells = null;
        Vector2Int goalCell  = ResolveGoalCell(mapLoader.WorldToCell(eagleTarget.position), blockedCells, startCell);

        if (GridLNS2Pathfinder.TryFindPath(mapLoader, agentId, startCell, goalCell, currentPath, frankWolfeMs, obstacleInflateRadius))
        {
            pathIndex = currentPath.Count > 1 ? 1 : 0;
            lastTrackedPathIndex = pathIndex;
        }
        else
        {
            currentPath.Clear();
            pathIndex = 0;
            lastTrackedPathIndex = -1;
        }
    }

    // ── Path following (giữ nguyên logic GridEnemyAgent) ──────────────────

    private void FollowPath()
    {
        if (currentPath.Count == 0 || pathIndex >= currentPath.Count)
        {
            ResetProgressTracking();
            ResetPartialDrive();
            tankController.HandleMoveBody(Vector2.zero);
            return;
        }

        Vector3 targetPosition = mapLoader.CellToWorld(currentPath[pathIndex]);
        if (IsCellOccupiedByFriendly(currentPath[pathIndex]))
        {
            currentPath.Clear();
            pathIndex = 0;
            nextReplanTime = 0f;
            tankController.HandleMoveBody(Vector2.zero);
            return;
        }

        Vector2 directionToTarget = targetPosition - tankController.tankMover.transform.position;
        float reachDistance = GetWaypointReachDistance();
        bool nearObstacle = IsNearObstacleCorner();
        if (nearObstacle)
        {
            reachDistance = Mathf.Min(reachDistance, obstacleWaypointReachDistance);
        }

        if (directionToTarget.magnitude <= reachDistance)
        {
            pathIndex++;
            ResetPartialDrive();
            return;
        }

        Vector2 forward = tankController.tankMover.transform.up;
        float dotProduct = Vector2.Dot(forward, directionToTarget.normalized);
        float cross = Vector3.Cross(forward, directionToTarget.normalized).z;
        int rotation = cross >= 0f ? -1 : 1;
        lastSteeringDirection = rotation;

        if (nearObstacle && dotProduct < obstacleForwardAlignmentThreshold)
        {
            ResetPartialDrive();
            tankController.HandleMoveBody(new Vector2(rotation, 0f));
            return;
        }

        if (dotProduct >= forwardAlignmentThreshold)
        {
            ResetPartialDrive();
            tankController.HandleMoveBody(Vector2.up);
        }
        else if (dotProduct >= turningDriveAlignmentThreshold)
        {
            ResetPartialDrive();
            tankController.HandleMoveBody(new Vector2(rotation * mostlyAlignedTurnScale, 1f));
        }
        else if (dotProduct >= partialDriveAlignmentThreshold)
        {
            bool driveThisFrame = StepPartialDriveCycle();
            tankController.HandleMoveBody(new Vector2(rotation, driveThisFrame ? 1f : 0f));
        }
        else
        {
            ResetPartialDrive();
            tankController.HandleMoveBody(new Vector2(rotation, 0f));
        }
    }

    private bool StepPartialDriveCycle()
    {
        partialDriveAccumulator += Time.deltaTime / Mathf.Max(0.01f, partialDrivePeriod);
        partialDriveAccumulator -= Mathf.Floor(partialDriveAccumulator);
        return partialDriveAccumulator < partialDriveDutyCycle;
    }

    private void ResetPartialDrive() => partialDriveAccumulator = 0f;

    private float GetWaypointReachDistance()
    {
        if (pathIndex <= 0 || pathIndex + 1 >= currentPath.Count) return waypointReachDistanceStraight;
        Vector2Int incoming = currentPath[pathIndex]     - currentPath[pathIndex - 1];
        Vector2Int outgoing = currentPath[pathIndex + 1] - currentPath[pathIndex];
        return incoming != outgoing ? waypointReachDistanceTurning : waypointReachDistanceStraight;
    }

    private bool IsNearObstacleCorner()
    {
        if (mapLoader == null || currentPath.Count == 0 || pathIndex >= currentPath.Count)
        {
            return false;
        }

        Vector2Int agentCell = mapLoader.WorldToCell(GetAgentPosition());
        return HasBlockedNeighborWithinRadius(agentCell, obstacleProximityCellRadius)
            || HasBlockedNeighborWithinRadius(currentPath[pathIndex], obstacleProximityCellRadius);
    }

    private bool HasBlockedNeighborWithinRadius(Vector2Int center, int radius)
    {
        int r = Mathf.Max(0, radius);
        for (int y = center.y - r; y <= center.y + r; y++)
        {
            for (int x = center.x - r; x <= center.x + r; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!mapLoader.IsInside(cell) || !mapLoader.IsWalkable(cell))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ── Progress tracking + Stuck detection (giữ nguyên) ──────────────────

    private void UpdateProgressTracking()
    {
        if (currentPath.Count == 0 || pathIndex >= currentPath.Count)
        {
            ResetProgressTracking();
            return;
        }

        if (pathIndex != lastTrackedPathIndex)
        {
            lastTrackedPathIndex = pathIndex;
            ResetScuffTracking();
            RegisterProgress();
            return;
        }

        Vector3 agentPos = GetAgentPosition();
        UpdateSpatialHistory(agentPos);

        if (!hasProgressSample)
        {
            lastProgressPosition = agentPos;
            lastProgressTime = Time.time;
            hasProgressSample = true;
            return;
        }

        if (IsSpatiallyStuck()) { TriggerSpatialRecovery(); return; }
        if (TryTriggerScuffRecovery()) return;

        if (Vector3.Distance(agentPos, lastProgressPosition) > progressEpsilon)
        {
            RegisterProgress();
            return;
        }

        if (Time.time - lastProgressTime > stuckTimeout)
            TriggerRecovery();
    }

    private bool TryTriggerScuffRecovery()
    {
        bool touching = IsScuffing();
        if (!touching || GetAgentVelocityMagnitude() > scuffVelocityThreshold)
        {
            ResetScuffTracking();
            return false;
        }

        if (!wasTouchingLastFrame || scuffStartTime < 0f)
        {
            scuffStartTime = Time.time;
            wasTouchingLastFrame = true;
            return false;
        }

        if (Time.time - scuffStartTime < scuffTimeout) return false;

        Debug.Log($"[GridEnemyAgentLNS2] {name} scuff recovery triggered.");
        lastScuffRecoveryTime = Time.time;
        ResetScuffTracking();
        TriggerRecovery();
        return true;
    }

    private bool IsScuffing()
    {
        if (obstacleContactMask.value == 0 || tankController?.tankMover?.rb2d == null) return false;
        return tankController.tankMover.rb2d.IsTouchingLayers(obstacleContactMask);
    }

    private float GetAgentVelocityMagnitude()
    {
        if (tankController?.tankMover?.rb2d == null) return 0f;
        return tankController.tankMover.rb2d.linearVelocity.magnitude;
    }

    private void UpdateSpatialHistory(Vector3 agentPos)
    {
        RecordVisitedCell(mapLoader.WorldToCell(agentPos));
        spatialSamples.Enqueue(new SpatialSample
        {
            Position = agentPos,
            Time = Time.time,
            GoalDistance = GetNavigationGoalDistance(agentPos)
        });
        while (spatialSamples.Count > 0 && Time.time - spatialSamples.Peek().Time > spatialStuckWindow)
            spatialSamples.Dequeue();
    }

    private void RecordVisitedCell(Vector2Int cell)
    {
        if (hasRecordedCell && cell == lastRecordedCell) return;
        lastRecordedCell = cell;
        hasRecordedCell = true;
        recentVisitedCells.Add(cell);
        if (recentVisitedCells.Count > Mathf.Max(1, spatialRecentCellHistorySize))
            recentVisitedCells.RemoveAt(0);
    }

    private bool IsSpatiallyStuck()
    {
        if (currentPath.Count == 0 || pathIndex >= currentPath.Count || spatialSamples.Count < 2) return false;
        if (Time.time - lastSpatialRecoveryTime < spatialStuckWindow * 0.5f) return false;

        SpatialSample[] samples = spatialSamples.ToArray();
        float totalTravel = 0f;
        for (int i = 1; i < samples.Length; i++)
            totalTravel += Vector3.Distance(samples[i - 1].Position, samples[i].Position);
        if (totalTravel < spatialStuckMinTravelDistance) return false;

        float displacement  = Vector3.Distance(samples[0].Position, samples[samples.Length - 1].Position);
        float goalProgress  = samples[0].GoalDistance - samples[samples.Length - 1].GoalDistance;
        return displacement <= spatialStuckMaxDisplacement && goalProgress <= spatialStuckGoalProgressEpsilon;
    }

    // Spatial recovery: LNS2 không dùng blockedCells → force replan thôi
    private void TriggerSpatialRecovery()
    {
        lastSpatialRecoveryTime = Time.time;
        ResetSpatialHistory();
        currentPath.Clear();
        pathIndex = 0;
        nextReplanTime = 0f;
        ReplanPath();
        if (currentPath.Count > 0)
        {
            RegisterProgress();
            return;
        }
        TriggerRecovery();
    }

    private void TriggerRecovery()
    {
        switch (recoveryLevel)
        {
            case RecoveryLevel.None:
                recoveryLevel = RecoveryLevel.ForcedReplan;
                currentPath.Clear();
                pathIndex = 0;
                nextReplanTime = 0f;
                ReplanPath();
                break;

            case RecoveryLevel.ForcedReplan:
                recoveryLevel = RecoveryLevel.Reverse;
                currentPath.Clear();
                pathIndex = 0;
                reverseRecoveryEndTime = Time.time + reverseRecoveryDuration;
                reverseRecoveryTurnDirection = lastSteeringDirection != 0 ? lastSteeringDirection : 1;
                tankController.HandleMoveBody(new Vector2(reverseRecoveryTurnDirection, -1f));
                break;

            case RecoveryLevel.Reverse:
                // Sau reverse → replan lại, reset về None (LNS2 sẽ tự tìm đường mới)
                recoveryLevel = RecoveryLevel.None;
                currentPath.Clear();
                pathIndex = 0;
                nextReplanTime = 0f;
                ReplanPath();
                Debug.LogWarning($"[GridEnemyAgentLNS2] {name} recovery: replan after reverse.");
                break;
        }

        lastProgressPosition = GetAgentPosition();
        lastProgressTime = Time.time;
        hasProgressSample = true;
        lastTrackedPathIndex = pathIndex;
    }

    private void ContinueReverseRecovery() =>
        tankController.HandleMoveBody(new Vector2(reverseRecoveryTurnDirection, -1f));

    private void FinishReverseRecovery()
    {
        reverseRecoveryEndTime = 0f;
        nextReplanTime = 0f;
        currentPath.Clear();
        ReplanPath();
        lastProgressPosition = GetAgentPosition();
        lastProgressTime = Time.time;
        hasProgressSample = true;
    }

    private void RegisterProgress()
    {
        lastProgressPosition = GetAgentPosition();
        lastProgressTime = Time.time;
        hasProgressSample = true;
        if (!IsScuffing() && Time.time - lastScuffRecoveryTime > stuckTimeout)
            recoveryLevel = RecoveryLevel.None;
    }

    private void ResetProgressTracking()
    {
        hasProgressSample = false;
        lastTrackedPathIndex = pathIndex;
        recoveryLevel = RecoveryLevel.None;
        lastScuffRecoveryTime = float.NegativeInfinity;
        ResetScuffTracking();
        ResetSpatialHistory();
    }

    private void ResetScuffTracking()
    {
        scuffStartTime = -1f;
        wasTouchingLastFrame = false;
    }

    private HashSet<Vector2Int> BuildDynamicBlockedCells(Vector2Int startCell)
    {
        FactionMember selfFaction = GetComponent<FactionMember>();
        FactionMember[] members = FindObjectsByType<FactionMember>(FindObjectsSortMode.None);
        HashSet<Vector2Int> blockedCells = null;
        for (int i = 0; i < members.Length; i++)
        {
            FactionMember member = members[i];
            if (member == null || member == selfFaction || !member.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!FactionMember.AreFriendly(selfFaction, member))
            {
                continue;
            }

            Vector2Int occupiedCell = mapLoader.WorldToCell(member.GetWorldPosition());
            if (occupiedCell == startCell)
            {
                continue;
            }

            blockedCells ??= new HashSet<Vector2Int>();
            blockedCells.Add(occupiedCell);
        }

        return blockedCells;
    }

    private Vector2Int ResolveGoalCell(Vector2Int preferredGoal, HashSet<Vector2Int> blockedCells, Vector2Int startCell)
    {
        if (!IsCellBlocked(preferredGoal, blockedCells) && LNS2Planner.IsAgentWalkable(preferredGoal))
        {
            return preferredGoal;
        }

        int maxRadius = Mathf.Max(mapLoader.Width, mapLoader.Height);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int y = preferredGoal.y - radius; y <= preferredGoal.y + radius; y++)
            {
                for (int x = preferredGoal.x - radius; x <= preferredGoal.x + radius; x++)
                {
                    Vector2Int candidate = new Vector2Int(x, y);
                    if (candidate == startCell || IsCellBlocked(candidate, blockedCells))
                    {
                        continue;
                    }

                    if (LNS2Planner.IsAgentWalkable(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return preferredGoal;
    }

    private bool IsCellOccupiedByFriendly(Vector2Int cell)
    {
        FactionMember selfFaction = GetComponent<FactionMember>();
        FactionMember[] members = FindObjectsByType<FactionMember>(FindObjectsSortMode.None);
        for (int i = 0; i < members.Length; i++)
        {
            FactionMember member = members[i];
            if (member == null || member == selfFaction || !member.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!FactionMember.AreFriendly(selfFaction, member))
            {
                continue;
            }

            if (mapLoader.WorldToCell(member.GetWorldPosition()) == cell)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCellBlocked(Vector2Int cell, HashSet<Vector2Int> blockedCells)
    {
        return blockedCells != null && blockedCells.Contains(cell);
    }

    private static HashSet<Vector2Int> MergeBlockedCells(HashSet<Vector2Int> first, HashSet<Vector2Int> second)
    {
        if (first == null || first.Count == 0)
        {
            return second;
        }

        if (second == null || second.Count == 0)
        {
            return first;
        }

        HashSet<Vector2Int> merged = new HashSet<Vector2Int>(first);
        merged.UnionWith(second);
        return merged;
    }

    private Vector3 GetAgentPosition() =>
        tankController?.tankMover != null ? tankController.tankMover.transform.position : transform.position;

    private float GetNavigationGoalDistance(Vector3 agentPos)
    {
        if (currentPath.Count > 0 && pathIndex < currentPath.Count)
            return Vector3.Distance(agentPos, mapLoader.CellToWorld(currentPath[pathIndex]));
        return eagleTarget != null ? Vector3.Distance(agentPos, eagleTarget.position) : 0f;
    }

    private void ResetSpatialHistory()
    {
        spatialSamples.Clear();
        recentVisitedCells.Clear();
        hasRecordedCell = false;
    }

    // ── Gizmos ─────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!drawPath || mapLoader == null || currentPath.Count < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = 1; i < currentPath.Count; i++)
            Gizmos.DrawLine(mapLoader.CellToWorld(currentPath[i - 1]), mapLoader.CellToWorld(currentPath[i]));
    }
}
