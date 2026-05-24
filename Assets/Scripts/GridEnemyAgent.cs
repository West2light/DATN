using System.Collections.Generic;
using UnityEngine;

public class GridEnemyAgent : MonoBehaviour
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
        Reverse,
        ExpandedMask
    }

    public MapLoader mapLoader;
    public GridNavMask navMask;
    public float tankClearanceRadius = 0.4f;
    [SerializeField] public bool enableSmoothing = false;
    public Transform eagleTarget;
    public Transform playerTarget;
    public TankController tankController;
    public float replanInterval = 0.75f;
    public float waypointReachDistance = 0.25f;
    public float waypointReachDistanceStraight = 0.3f;
    public float waypointReachDistanceTurning = 0.12f;
    public float eagleShootingRange = 5f;
    public float playerShootingRange = 7f;
    public LayerMask lineOfSightMask;
    public bool drawPath = true;
    [Range(-1f, 1f)] public float forwardAlignmentThreshold = 0.97f;
    [Range(-1f, 1f)] public float turningDriveAlignmentThreshold = 0.85f;
    [Range(-1f, 1f)] public float partialDriveAlignmentThreshold = 0.5f;
    [Range(0f, 1f)] public float mostlyAlignedTurnScale = 0.3f;
    [Min(0.01f)] public float partialDrivePeriod = 0.1f;
    [Range(0f, 1f)] public float partialDriveDutyCycle = 0.65f;
    public float progressEpsilon = 0.05f;
    public float stuckTimeout = 1.5f;
    public LayerMask obstacleContactMask;
    public float scuffTimeout = 0.4f;
    public float scuffVelocityThreshold = 0.1f;
    public float reverseRecoveryDuration = 0.8f;
    public float spatialStuckWindow = 3f;
    public float spatialStuckMinTravelDistance = 1.5f;
    public float spatialStuckMaxDisplacement = 0.75f;
    public float spatialStuckGoalProgressEpsilon = 0.5f;
    public int spatialRecentCellHistorySize = 8;
    public int spatialRecoveryBlockedCellCount = 4;

    private readonly List<Vector2Int> currentPath = new List<Vector2Int>();
    private readonly Queue<SpatialSample> spatialSamples = new Queue<SpatialSample>();
    private readonly List<Vector2Int> recentVisitedCells = new List<Vector2Int>();
    private int pathIndex;
    private float nextReplanTime;
    private GridNavMask recoveryNavMask;
    private RecoveryLevel recoveryLevel;
    private Vector3 lastProgressPosition;
    private float lastProgressTime;
    private bool hasProgressSample;
    private float reverseRecoveryEndTime;
    private int lastTrackedPathIndex = -1;
    private bool hasRecordedCell;
    private Vector2Int lastRecordedCell;
    private float lastSpatialRecoveryTime = float.NegativeInfinity;
    private HashSet<Vector2Int> pendingBlockedCells;
    private float scuffStartTime = -1f;
    private float lastScuffRecoveryTime = float.NegativeInfinity;
    private bool wasTouchingLastFrame;
    private float partialDriveAccumulator;
    private int lastSteeringDirection = 1;
    private int reverseRecoveryTurnDirection = 1;

    private void Awake()
    {
        if (tankController == null)
        {
            tankController = GetComponentInChildren<TankController>();
        }
    }

    private void Update()
    {
        if (mapLoader == null || tankController == null || eagleTarget == null)
        {
            return;
        }

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
        {
            ReplanPath();
        }

        FollowPath();
        UpdateProgressTracking();
    }

    private Transform GetShootingTarget()
    {
        if (CanShootTarget(playerTarget, playerShootingRange))
        {
            return playerTarget;
        }

        if (CanShootTarget(eagleTarget, eagleShootingRange))
        {
            return eagleTarget;
        }

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
        if (target == null)
        {
            return false;
        }

        Vector2 origin = tankController.aimTurret.transform.position;
        Vector2 targetPosition = target.position;
        if (Vector2.Distance(origin, targetPosition) > range)
        {
            return false;
        }

        RaycastHit2D hit = Physics2D.Raycast(origin, targetPosition - origin, range, lineOfSightMask);
        if (hit.collider == null)
        {
            return false;
        }

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
        Vector2 targetPosition = target.position;
        Vector2 direction = targetPosition - origin;
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

    private void ReplanPath()
    {
        nextReplanTime = Time.time + replanInterval;
        Vector2Int startCell = mapLoader.WorldToCell(GetAgentPosition());
        Vector2Int goalCell = mapLoader.WorldToCell(eagleTarget.position);
        HashSet<Vector2Int> blockedCells = MergeBlockedCells(pendingBlockedCells, BuildDynamicBlockedCells(startCell, goalCell));
        pendingBlockedCells = null;
        if (TryFindPathWithFallback(startCell, goalCell, blockedCells))
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

    private bool TryFindPathWithFallback(Vector2Int startCell, Vector2Int goalCell, HashSet<Vector2Int> blockedCells)
    {
        GridNavMask activeMask = GetActiveNavMask();
        if (GridAStarPathfinder.TryFindPath(mapLoader, activeMask, startCell, goalCell, currentPath, blockedCells, tankClearanceRadius, enableSmoothing))
        {
            return true;
        }

        int startingRadius = activeMask != null ? activeMask.InflateRadius - 1 : -1;
        for (int radius = startingRadius; radius >= 0; radius--)
        {
            if (navMask != null && radius == navMask.InflateRadius)
            {
                continue;
            }

            GridNavMask fallbackMask = radius > 0 ? new GridNavMask(mapLoader, radius) : null;
            if (GridAStarPathfinder.TryFindPath(mapLoader, fallbackMask, startCell, goalCell, currentPath, blockedCells, tankClearanceRadius, enableSmoothing))
            {
                return true;
            }
        }

        return false;
    }

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
            // Khi không chạm tường: arc nhỏ giúp thoát góc tự nhiên hơn xoay tại chỗ.
            // Khi đang chạm tường (hành lang hẹp): xoay tại chỗ, để scuff recovery xử lý.
            float fwd = IsScuffing() ? 0f : 0.1f;
            tankController.HandleMoveBody(new Vector2(rotation, fwd));
        }
    }

    private bool StepPartialDriveCycle()
    {
        partialDriveAccumulator += Time.deltaTime / Mathf.Max(0.01f, partialDrivePeriod);
        partialDriveAccumulator -= Mathf.Floor(partialDriveAccumulator);
        return partialDriveAccumulator < partialDriveDutyCycle;
    }

    private void ResetPartialDrive()
    {
        partialDriveAccumulator = 0f;
    }

    private float GetWaypointReachDistance()
    {
        if (pathIndex <= 0 || pathIndex + 1 >= currentPath.Count)
        {
            return waypointReachDistanceStraight;
        }

        Vector2Int incomingDirection = currentPath[pathIndex] - currentPath[pathIndex - 1];
        Vector2Int outgoingDirection = currentPath[pathIndex + 1] - currentPath[pathIndex];
        bool isTurnAhead = incomingDirection != outgoingDirection;
        return isTurnAhead ? waypointReachDistanceTurning : waypointReachDistanceStraight;
    }

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
            if (recoveryNavMask != null)
            {
                recoveryNavMask = null;
            }

            ResetScuffTracking();
            RegisterProgress();
            return;
        }

        Vector3 agentPosition = GetAgentPosition();
        UpdateSpatialHistory(agentPosition);
        if (!hasProgressSample)
        {
            lastProgressPosition = agentPosition;
            lastProgressTime = Time.time;
            hasProgressSample = true;
            return;
        }

        if (IsSpatiallyStuck())
        {
            TriggerSpatialRecovery();
            return;
        }

        if (TryTriggerScuffRecovery())
        {
            return;
        }

        if (Vector3.Distance(agentPosition, lastProgressPosition) > progressEpsilon)
        {
            RegisterProgress();
            return;
        }

        if (Time.time - lastProgressTime > stuckTimeout)
        {
            TriggerRecovery();
        }
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

        if (Time.time - scuffStartTime < scuffTimeout)
        {
            return false;
        }

        Debug.Log($"[GridEnemyAgent] {name} scuff recovery triggered after {Time.time - scuffStartTime:F2}s.");
        lastScuffRecoveryTime = Time.time;
        ResetScuffTracking();
        // Scuff during ExpandedMask should NOT reset to ForcedReplan; let TriggerRecovery
        // advance monotonically. Resetting tore down hard-won inflate during corner spawn loops.
        TriggerRecovery();
        return true;
    }

    private bool IsScuffing()
    {
        if (obstacleContactMask.value == 0
            || tankController == null
            || tankController.tankMover == null
            || tankController.tankMover.rb2d == null)
        {
            return false;
        }

        return tankController.tankMover.rb2d.IsTouchingLayers(obstacleContactMask);
    }

    private float GetAgentVelocityMagnitude()
    {
        if (tankController == null
            || tankController.tankMover == null
            || tankController.tankMover.rb2d == null)
        {
            return 0f;
        }

        return tankController.tankMover.rb2d.linearVelocity.magnitude;
    }

    private void UpdateSpatialHistory(Vector3 agentPosition)
    {
        RecordVisitedCell(mapLoader.WorldToCell(agentPosition));

        SpatialSample sample = new SpatialSample
        {
            Position = agentPosition,
            Time = Time.time,
            GoalDistance = GetNavigationGoalDistance(agentPosition)
        };
        spatialSamples.Enqueue(sample);

        while (spatialSamples.Count > 0 && Time.time - spatialSamples.Peek().Time > spatialStuckWindow)
        {
            spatialSamples.Dequeue();
        }
    }

    private void RecordVisitedCell(Vector2Int cell)
    {
        if (hasRecordedCell && cell == lastRecordedCell)
        {
            return;
        }

        lastRecordedCell = cell;
        hasRecordedCell = true;
        recentVisitedCells.Add(cell);
        if (recentVisitedCells.Count > Mathf.Max(1, spatialRecentCellHistorySize))
        {
            recentVisitedCells.RemoveAt(0);
        }
    }

    private bool IsSpatiallyStuck()
    {
        if (currentPath.Count == 0 || pathIndex >= currentPath.Count || spatialSamples.Count < 2)
        {
            return false;
        }

        if (Time.time - lastSpatialRecoveryTime < spatialStuckWindow * 0.5f)
        {
            return false;
        }

        SpatialSample[] samples = spatialSamples.ToArray();
        SpatialSample firstSample = samples[0];
        SpatialSample lastSample = samples[samples.Length - 1];
        float totalTravelDistance = 0f;
        for (int i = 1; i < samples.Length; i++)
        {
            totalTravelDistance += Vector3.Distance(samples[i - 1].Position, samples[i].Position);
        }

        if (totalTravelDistance < spatialStuckMinTravelDistance)
        {
            return false;
        }

        float displacement = Vector3.Distance(firstSample.Position, lastSample.Position);
        float goalProgress = firstSample.GoalDistance - lastSample.GoalDistance;
        return displacement <= spatialStuckMaxDisplacement && goalProgress <= spatialStuckGoalProgressEpsilon;
    }

    private void TriggerSpatialRecovery()
    {
        HashSet<Vector2Int> blockedCells = BuildSpatialRecoveryBlockedCells();
        if (blockedCells != null && blockedCells.Count > 0)
        {
            pendingBlockedCells = blockedCells;
            currentPath.Clear();
            pathIndex = 0;
            nextReplanTime = 0f;
            ReplanPath();
            if (currentPath.Count > 0)
            {
                lastSpatialRecoveryTime = Time.time;
                ResetSpatialHistory();
                RegisterProgress();
                return;
            }
        }

        lastSpatialRecoveryTime = Time.time;
        ResetSpatialHistory();
        TriggerRecovery();
    }

    private HashSet<Vector2Int> BuildSpatialRecoveryBlockedCells()
    {
        if (recentVisitedCells.Count <= 1)
        {
            return null;
        }

        Vector2Int startCell = mapLoader.WorldToCell(GetAgentPosition());
        Vector2Int goalCell = mapLoader.WorldToCell(eagleTarget.position);
        HashSet<Vector2Int> blockedCells = new HashSet<Vector2Int>();
        for (int i = recentVisitedCells.Count - 2; i >= 0 && blockedCells.Count < spatialRecoveryBlockedCellCount; i--)
        {
            Vector2Int cell = recentVisitedCells[i];
            if (cell == startCell || cell == goalCell)
            {
                continue;
            }

            blockedCells.Add(cell);
        }

        return blockedCells.Count > 0 ? blockedCells : null;
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
                recoveryLevel = RecoveryLevel.ExpandedMask;
                recoveryNavMask = new GridNavMask(mapLoader, GetBaseInflateRadius() + 1);
                currentPath.Clear();
                pathIndex = 0;
                nextReplanTime = 0f;
                ReplanPath();
                Debug.LogWarning($"[GridEnemyAgent] {name} escalated stuck recovery to inflate radius {recoveryNavMask.InflateRadius}.");
                break;
            case RecoveryLevel.ExpandedMask:
                currentPath.Clear();
                pathIndex = 0;
                nextReplanTime = 0f;
                ReplanPath();
                break;
        }

        lastProgressPosition = GetAgentPosition();
        lastProgressTime = Time.time;
        hasProgressSample = true;
        lastTrackedPathIndex = pathIndex;
    }

    private void ContinueReverseRecovery()
    {
        tankController.HandleMoveBody(new Vector2(reverseRecoveryTurnDirection, -1f));
    }

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
        {
            recoveryLevel = RecoveryLevel.None;
        }
    }

    private void ResetProgressTracking()
    {
        hasProgressSample = false;
        lastTrackedPathIndex = pathIndex;
        recoveryLevel = RecoveryLevel.None;
        recoveryNavMask = null;
        pendingBlockedCells = null;
        lastScuffRecoveryTime = float.NegativeInfinity;
        ResetScuffTracking();
        ResetSpatialHistory();
    }

    private void ResetScuffTracking()
    {
        scuffStartTime = -1f;
        wasTouchingLastFrame = false;
    }

    private int GetBaseInflateRadius()
    {
        // Physical-mode navMask reports InflateRadius=-1; clamp to 0 so escalation
        // (GetBaseInflateRadius()+1) yields a meaningful Chebyshev inflate during recovery.
        return navMask != null ? Mathf.Max(0, navMask.InflateRadius) : 0;
    }

    private GridNavMask GetActiveNavMask()
    {
        return recoveryNavMask ?? navMask;
    }

    private HashSet<Vector2Int> BuildDynamicBlockedCells(Vector2Int startCell, Vector2Int goalCell)
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

    private Vector3 GetAgentPosition()
    {
        return tankController != null && tankController.tankMover != null
            ? tankController.tankMover.transform.position
            : transform.position;
    }

    private float GetNavigationGoalDistance(Vector3 agentPosition)
    {
        if (currentPath.Count > 0 && pathIndex < currentPath.Count)
        {
            return Vector3.Distance(agentPosition, mapLoader.CellToWorld(currentPath[pathIndex]));
        }

        return eagleTarget != null ? Vector3.Distance(agentPosition, eagleTarget.position) : 0f;
    }

    private void ResetSpatialHistory()
    {
        spatialSamples.Clear();
        recentVisitedCells.Clear();
        hasRecordedCell = false;
    }

    private void OnDrawGizmos()
    {
        if (!drawPath || mapLoader == null || currentPath.Count < 2)
        {
            return;
        }

        Gizmos.color = Color.red;
        for (int i = 1; i < currentPath.Count; i++)
        {
            Gizmos.DrawLine(mapLoader.CellToWorld(currentPath[i - 1]), mapLoader.CellToWorld(currentPath[i]));
        }
    }
}
