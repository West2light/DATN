using UnityEngine;
using PibtTcp;

/// <summary>
/// Enemy AI runtime cho mode PIBT_TCP.
/// Thay vì tự tính path, agent đọc action (FW/CR/CCR/W) từ PibtTcpSessionState
/// — được điền bởi PibtTcpClient gọi server C++ mỗi tick.
///
/// Pipeline một tick:
///   1. MapScenarioBootstrapPIBTTcp gửi plan_step chung cho toàn bộ enemy
///   2. Khi có plan_result -> SetActions vào SessionState
///   3. Mỗi agent đọc action theo agentId và drive TankControllerCopy
///
/// Shooting logic được giữ y hệt GridEnemyAgentPIBT (ưu tiên nhất).
/// Cell commit: chỉ cập nhật currentCell khi tank đủ gần center để tránh lệch với server.
/// </summary>
public class GridEnemyAgentPIBTTcpCopy : MonoBehaviour
{
    private enum RecoveryLevel { None, ForcedWait, Reverse }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("References")]
    public MapLoader         mapLoader;
    public Transform         eagleTarget;
    public Transform         playerTarget;
    [HideInInspector] public Transform[] playerTargets;
    public TankControllerCopy tankController;
    public PibtTcpSessionState sessionState;  // injected by MapScenarioBootstrapPIBTTcp

    [Header("Agent")]
    public int agentId = -1;   // set by bootstrap; must be unique per agent

    [Header("Timing")]
    [Min(0.05f)] public float stepInterval = 0.10f;  // seconds between plan_step requests

    [Header("Steering")]
    public float cellCenterThreshold          = 0.25f;  // world-units; must reach before committing next action
    [Range(-1f, 1f)] public float forwardAlignmentThreshold = 0.97f;
    [Range(-1f, 1f)] public float turningDriveAlignmentThreshold = 0.85f;
    [Range(-1f, 1f)] public float partialDriveAlignmentThreshold = 0.5f;
    [Range(0f, 1f)]  public float mostlyAlignedTurnScale = 0.3f;
    [Min(0.01f)]     public float partialDrivePeriod = 0.1f;
    [Range(0f, 1f)]  public float partialDriveDutyCycle = 0.65f;

    [Header("Shooting")]
    public float      eagleShootingRange  = 5f;
    public float      playerShootingRange = 7f;
    public bool       enablePlayerCombat  = false;
    public LayerMask  lineOfSightMask;

    [Header("Stuck recovery")]
    public float      scuffTimeout        = 0.4f;
    public float      scuffVelocityThreshold = 0.1f;
    public float      reverseRecoveryDuration = 0.8f;
    [Min(0.1f)] public float maxMoveStepDuration = 1.1f;
    public LayerMask  obstacleContactMask;

    [Header("Phase D Execution")]
    [Min(2)] public int diagnosticTurnStreakThreshold = 4;
    [Min(0.05f)] public float diagnosticReplanCooldown = 0.25f;
    public bool goalBiasCorrectBadFw = false;

    [Header("PIBT TCP Diagnostics")]
    [Min(0.1f)] public float diagnosticLongFwSeconds = 1.5f;
    [Min(0.1f)] public float diagnosticNoProgressSeconds = 0.75f;
    [Min(0.05f)] public float diagnosticLogCooldown = 0.5f;

    // ── Backtest metrics ─────────────────────────────────────────────────────
    [System.NonSerialized] public int   btShotCount;
    [System.NonSerialized] public int   btCellsVisited;
    [System.NonSerialized] public int   btTimeoutCount;
    [System.NonSerialized] public float btSpawnTime;
    [System.NonSerialized] public float btLatencyMsTotal;
    [System.NonSerialized] public int   btStepCount;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private Vector2Int   currentCell;
    private Vector2Int   committedFacing = Vector2Int.up;
    private Vector2Int   targetCell;    // nextLoc from last FW action
    [System.NonSerialized] public bool hasAssignedGoalCell;
    [System.NonSerialized] public Vector2Int assignedGoalCell;
    private bool         hasPendingMove;// true while executing a FW step
    private string       pendingAction; // "FW","CR","CCR","W" — current action being executed
    private int          lastAppliedResultVersion;
    private int          repeatedTurnActionCount;
    private string       lastServerAction;
    private int          lastLoggedRequestId = -1;
    private Vector2Int   lastTurnLoopCell;
    private bool         hasLastTurnLoopCell;
    private float        lastDiagnosticRecoveryTime = float.NegativeInfinity;
    private float        pendingMoveStartTime = -1f;
    private float        pendingMoveLastProgressTime = -1f;
    private float        pendingMoveBestDistance = float.PositiveInfinity;
    private float        nextPendingMoveDiagnosticTime;
    private float        nextTurnLoopDiagnosticTime;
    private float        nextScuffDiagnosticTime;
    private float        nextStateDiagnosticTime;
    private float        nextGoalDiagnosticTime;
    private float        nextSnapDiagnosticTime;
    private readonly ContactPoint2D[] debugContactBuffer = new ContactPoint2D[8];

    // Rotation (CR/CCR)
    private bool         isRotating;
    private float        rotateStartAngle;
    private float        rotateTargetAngle;
    private float        rotateProgress;
    private const float  RotateDuration = 0.18f;   // seconds for a 90° turn

    // Recovery
    private RecoveryLevel recoveryLevel;
    private float         reverseRecoveryEndTime;
    private int           reverseRecoveryTurnDirection = 1;
    private float         scuffStartTime  = -1f;
    private bool          wasTouchingLastFrame;
    private float         lastScuffRecoveryTime = float.NegativeInfinity;
    private int           lastSteeringDirection = 1;

    // Partial drive cycle
    private float partialDriveAccumulator;

    // Shooting
    private FactionMember selfFaction;

    public bool IsExecutingTcpAction =>
        hasPendingMove || isRotating || recoveryLevel == RecoveryLevel.Reverse;

    public void AssignGoalCell(Vector2Int goalCell)
    {
        assignedGoalCell = goalCell;
        hasAssignedGoalCell = true;
    }

    public void ClearAssignedGoalCell()
    {
        assignedGoalCell = new Vector2Int();
        hasAssignedGoalCell = false;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (tankController == null)
            tankController = GetComponentInChildren<TankControllerCopy>();
        selfFaction = GetComponent<FactionMember>();
    }

    private void Start()
    {
        currentCell = mapLoader != null
            ? mapLoader.WorldToCell(GetAgentPosition())
            : Vector2Int.zero;
        committedFacing = QuantizeFacingFromTransform();
        targetCell  = currentCell;
        btSpawnTime = Time.time;

        if (!IsCellValidForMovement(currentCell))
            TryRecoverToNearestValidCell("start_state_invalid", sessionState?.GetAction(agentId), GetAgentPosition(), currentCell);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (mapLoader == null || tankController == null || eagleTarget == null || sessionState == null)
            return;

        if (!hasPendingMove && !isRotating && !IsCellValidForMovement(currentCell))
        {
            Vector3 bodyPos = GetBodyPosition();
            Vector2Int observedCell = mapLoader.WorldToCell(bodyPos);
            TryRecoverToNearestValidCell("observed_cell_outside", sessionState.GetAction(agentId), bodyPos, observedCell);
            return;
        }

        // ── 1. Shooting (highest priority) ────────────────────────────────────
        Transform shootTarget = GetShootingTarget();
        if (shootTarget != null)
        {
            ClearExecutingActionForShooting();
            AimAndShoot(shootTarget);
            return;
        }

        // ── 2. Reverse recovery in progress ──────────────────────────────────
        if (recoveryLevel == RecoveryLevel.Reverse && Time.time < reverseRecoveryEndTime)
        {
            tankController.HandleMoveBody(new Vector2(reverseRecoveryTurnDirection, -1f));
            return;
        }
        if (recoveryLevel == RecoveryLevel.Reverse && reverseRecoveryEndTime > 0f)
            FinishReverseRecovery();

        // ── 3. Scuff detection ───────────────────────────────────────────────
        CheckScuffRecovery();

        // ── 4. Consume latest server action for this agent ───────────────────
        ApplyLatestActionIfNeeded();

        // ── 5. Execute current action ─────────────────────────────────────────
        if (isRotating)
            ContinueRotation();
        else if (hasPendingMove)
            ContinueFwMove();
        else
            StopMovement(); // W or waiting
    }

    public AgentStateDto BuildAgentStateDto()
    {
        EnsureRuntimeStateBeforeSend();

        Vector2Int goalCell;
        Vector2Int preferredGoalCell;
        if (!TryResolveGoalCell(out goalCell, out preferredGoalCell))
        {
            goalCell = currentCell;
            TraceDiagnosticWithCooldown(
                "goal_cell_invalid",
                sessionState?.GetAction(agentId),
                GetDistanceToTargetCell(),
                ref nextGoalDiagnosticTime,
                preferredGoalCell,
                sessionState?.GetAction(agentId)?.nextLoc ?? -1,
                GetBodyPosition(),
                mapLoader != null ? Vector2.Distance(GetBodyPosition(), mapLoader.CellToWorld(currentCell)) : -1f);
        }
        else if (goalCell != preferredGoalCell)
        {
            TraceDiagnosticWithCooldown(
                "goal_cell_invalid",
                sessionState?.GetAction(agentId),
                GetDistanceToTargetCell(),
                ref nextGoalDiagnosticTime,
                preferredGoalCell,
                sessionState?.GetAction(agentId)?.nextLoc ?? -1,
                GetBodyPosition(),
                mapLoader != null ? Vector2.Distance(GetBodyPosition(), mapLoader.CellToWorld(currentCell)) : -1f);
        }

        Vector2Int facing = committedFacing;
        return PibtTcpGridAdapter.BuildAgentState(
            agentId, currentCell, facing, goalCell, mapLoader.BuildWidth);
    }

    private void ApplyLatestActionIfNeeded()
    {
        if (hasPendingMove || isRotating || recoveryLevel == RecoveryLevel.Reverse)
            return;

        int version = sessionState.ResultVersion;
        if (version == lastAppliedResultVersion) return;

        lastAppliedResultVersion = version;
        btTimeoutCount = sessionState.TimeoutCount;
        btLatencyMsTotal += sessionState.LatencyMsLast;
        btStepCount++;
        ActionDto action = sessionState.GetAction(agentId);
        UpdateTurnStreak(action);
        if (TryDiagnosticTurnRecovery(action))
        {
            TraceAgentExecution("diagnostic_recovery", action);
            return;
        }
        TraceTurnLoopDiagnosticIfNeeded(action);
        ApplyAction(action);
        TraceAgentExecution("apply", action);
    }

    // ── Action execution ──────────────────────────────────────────────────────

    private void ApplyAction(ActionDto dto)
    {
        if (dto == null) { pendingAction = "W"; return; }

        pendingAction = dto.action;

        switch (dto.action)
        {
            case "FW":
                if (!TryDecodeServerForwardTarget(dto, out Vector2Int serverTarget, out string fwReason))
                {
                    if (goalBiasCorrectBadFw
                        && TryCorrectBadForwardTarget(dto != null ? dto.nextLoc : -1, serverTarget, out Vector2Int correctedCell))
                    {
                        Debug.LogWarning(
                            $"[PIBT_TCP_PHASE_D] agent={agentId} corrected FW current={currentCell} " +
                            $"serverTarget={serverTarget} corrected={correctedCell} goal={GetGoalCell()}");
                        targetCell = correctedCell;
                        BeginForwardMove();
                        break;
                    }

                    RejectForwardAction(fwReason, dto);
                    return;
                }

                targetCell = serverTarget;

                if (targetCell == currentCell)
                {
                    RejectForwardAction("server_fw_no_op", dto);
                    return;
                }

                BeginForwardMove();
                break;

            case "CR":   // clockwise = -90°
                if (TryCorrectBadTurnTarget("CR", out Vector2Int crCorrectedCell))
                {
                    StartCorrectedForwardMove(crCorrectedCell, "CR");
                    return;
                }
                StartRotation(-90f);
                break;

            case "CCR":  // counter-clockwise = +90°
                if (TryCorrectBadTurnTarget("CCR", out Vector2Int ccrCorrectedCell))
                {
                    StartCorrectedForwardMove(ccrCorrectedCell, "CCR");
                    return;
                }
                StartRotation(+90f);
                break;

            case "W":
            default:
                if (TryAutoAdvanceFromWait())
                    break;
                hasPendingMove = false;
                isRotating    = false;
                ClearForwardMoveTracking();
                StopMovement();
                break;
        }
    }

    // ── FW movement ───────────────────────────────────────────────────────────

    private void ContinueFwMove()
    {
        if (!IsCellValidForMovement(targetCell))
        {
            RejectForwardAction("server_fw_target_invalid_during_execute", sessionState?.GetAction(agentId));
            return;
        }

        Vector3 targetWorld = mapLoader.CellToWorld(targetCell);
        Vector2 dirToTarget = targetWorld - GetBodyPosition();
        float   dist        = dirToTarget.magnitude;

        if (HasForwardMoveTimedOut())
        {
            HandleForwardMoveTimeout(dist);
            return;
        }

        if (dist <= cellCenterThreshold)
        {
            // Arrived at cell center → commit and pick next action
            Vector2Int previousCell = currentCell;
            currentCell   = targetCell;
            committedFacing = QuantizeFacingFromCellDelta(previousCell, targetCell, committedFacing);
            hasPendingMove = false;
            hasLastTurnLoopCell = false;
            repeatedTurnActionCount = 0;
            btCellsVisited++;
            ClearForwardMoveTracking();
            SnapBodyToCurrentCellCenter();
            TraceAgentExecution("arrive", sessionState.GetAction(agentId));
            StopMovement();
            return;
        }

        UpdateForwardMoveDiagnostics(dist);
        ResetPartialDrive();
        SteerTowardTargetCell(dirToTarget);
    }

    private bool HasForwardMoveTimedOut()
    {
        return pendingMoveStartTime >= 0f
            && Time.time - pendingMoveStartTime > maxMoveStepDuration;
    }

    private void HandleForwardMoveTimeout(float dist)
    {
        ActionDto dto = sessionState?.GetAction(agentId);
        Vector3 bodyPos = GetBodyPosition();
        Vector2Int observedCell = mapLoader != null ? mapLoader.WorldToCell(bodyPos) : currentCell;

        TraceMovementDiagnostic("fw_step_timeout", dto, dist, GetGoalCell(), dto?.nextLoc ?? -1, bodyPos,
            mapLoader != null ? Vector2.Distance(bodyPos, mapLoader.CellToWorld(observedCell)) : -1f);

        pendingAction = "W";
        hasPendingMove = false;
        isRotating = false;
        ClearForwardMoveTracking();
        ResetPartialDrive();
        StopMovement();
        ZeroBodyVelocity();

        if (!IsCellValidForMovement(currentCell) || !IsCellValidForMovement(observedCell))
            TryRecoverToNearestValidCell("fw_step_timeout_recover", dto, bodyPos, observedCell);
        else
            RequestImmediatePlanStep();
    }

    private void SteerTowardTargetCell(Vector2 directionToTarget)
    {
        TankMoverCopy mover = GetTankMover();
        if (mover == null || directionToTarget.sqrMagnitude <= Mathf.Epsilon)
        {
            StopMovement();
            return;
        }

        Vector2 directionNormalized = directionToTarget.normalized;
        Vector2 forward = mover.transform.up;
        float cross = Vector3.Cross(forward, directionNormalized).z;
        int rotation = cross >= 0f ? -1 : 1;
        lastSteeringDirection = rotation;

        ResetPartialDrive();
        tankController.HandleMoveWorldDirection(directionNormalized);
    }

    // ── Rotation (CR / CCR) ───────────────────────────────────────────────────

    private void StartRotation(float deltaDeg)
    {
        StopMovement();
        ZeroBodyVelocity();
        TraceOffCenterRotateIfNeeded(deltaDeg);
        isRotating        = true;
        hasPendingMove    = false;
        ClearForwardMoveTracking();
        rotateProgress    = 0f;
        rotateStartAngle  = GetBodyRotationAngle();
        rotateTargetAngle = rotateStartAngle + deltaDeg;
    }

    private void ContinueRotation()
    {
        rotateProgress += Time.deltaTime / RotateDuration;
        float angle = Mathf.LerpAngle(rotateStartAngle, rotateTargetAngle, Mathf.Clamp01(rotateProgress));
        StopMovement();
        SetBodyRotationAngle(angle);
        ZeroBodyVelocity();

        if (rotateProgress >= 1f)
        {
            SnapCommittedFacingAfterRotation(pendingAction);
            isRotating = false;
            TraceAgentExecution("rotate_done", sessionState.GetAction(agentId));
            StopMovement();
        }
    }

    // ── Shooting ──────────────────────────────────────────────────────────────

    private Transform GetShootingTarget()
    {
        if (enablePlayerCombat)
        {
            Transform nearestPlayer = GetNearestPlayerInRange();
            if (nearestPlayer != null) return nearestPlayer;
        }
        if (CanShootTarget(eagleTarget, eagleShootingRange)) return eagleTarget;
        return null;
    }

    public bool CanShootEagleNow()
    {
        return CanShootTarget(eagleTarget, eagleShootingRange);
    }

    public bool IsReverseRecoveringDebug()
    {
        return recoveryLevel == RecoveryLevel.Reverse;
    }

    public bool IsScuffingDebug()
    {
        return IsScuffing();
    }

    public string GetMotionStateForDebug()
    {
        if (CanShootEagleNow())
            return "shoot_eagle";
        if (enablePlayerCombat && GetShootingTarget() == playerTarget)
            return "shoot_player";
        if (recoveryLevel == RecoveryLevel.Reverse)
            return "reverse_recovery";
        if (hasPendingMove)
            return "fw_pending";
        if (isRotating)
            return "rotating";
        if (IsScuffing())
            return "scuffing";
        return "idle";
    }

    public bool IsHoldingAssignedEagleSlot()
    {
        return hasAssignedGoalCell
            && currentCell == assignedGoalCell
            && CanShootEagleNow();
    }

    private Transform GetNearestPlayerInRange()
    {
        Transform best = null;
        float bestSq   = float.MaxValue;

        void Check(Transform t)
        {
            if (t == null || !CanShootTarget(t, playerShootingRange)) return;
            float d = ((Vector2)transform.position - (Vector2)t.position).sqrMagnitude;
            if (d < bestSq) { best = t; bestSq = d; }
        }

        Check(playerTarget);
        if (playerTargets != null) foreach (var t in playerTargets) Check(t);
        return best;
    }

    private bool CanShootTarget(Transform target, float range)
    {
        if (target == null || tankController?.aimTurret == null) return false;
        Vector2 origin = tankController.aimTurret.transform.position;
        Vector2 dest   = target.position;
        if (Vector2.Distance(origin, dest) > range) return false;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dest - origin, range, lineOfSightMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.transform == target || hit.collider.transform.IsChildOf(target)) return true;
            FactionMember fm = FactionMember.FindForCollider(hit.collider);
            if (FactionMember.AreFriendly(selfFaction, fm)) continue;
            return false;
        }
        return false;
    }

    private void AimAndShoot(Transform target)
    {
        tankController.HandleTurretMovement(target.position);
        if (tankController.aimTurret != null && tankController.aimTurret.IsAlignedTo(target.position))
        {
            tankController.HandleShoot();
            btShotCount++;
        }
    }

    // ── Scuff recovery ────────────────────────────────────────────────────────
    private void ClearExecutingActionForShooting()
    {
        bool wasExecuting = IsExecutingTcpAction;
        if (wasExecuting)
        {
            ActionDto dto = sessionState?.GetAction(agentId);
            Vector3 bodyPos = GetBodyPosition();
            TraceMovementDiagnostic(
                "shoot_clear_pending_action",
                dto,
                GetDistanceToTargetCell(),
                GetGoalCell(),
                dto?.nextLoc ?? -1,
                bodyPos,
                mapLoader != null ? Vector2.Distance(bodyPos, mapLoader.CellToWorld(currentCell)) : -1f);
        }

        pendingAction = "W";
        hasPendingMove = false;
        isRotating = false;
        recoveryLevel = RecoveryLevel.None;
        reverseRecoveryEndTime = 0f;
        ClearForwardMoveTracking();
        ResetPartialDrive();
        ResetScuffTracking();
        StopMovement();
        ZeroBodyVelocity();

        if (wasExecuting)
            RequestImmediatePlanStep();
    }


    private void CheckScuffRecovery()
    {
        bool touching = IsScuffing();
        if (!touching || GetAgentVelocityMagnitude() > scuffVelocityThreshold)
        {
            ResetScuffTracking();
            return;
        }

        if (!wasTouchingLastFrame || scuffStartTime < 0f)
        {
            scuffStartTime      = Time.time;
            wasTouchingLastFrame = true;
            return;
        }

        if (Time.time - scuffStartTime < scuffTimeout) return;

        // Trigger reverse recovery
        TraceMovementDiagnostic("scuff_recovery", sessionState?.GetAction(agentId), GetDistanceToTargetCell());
        lastScuffRecoveryTime      = Time.time;
        recoveryLevel              = RecoveryLevel.Reverse;
        reverseRecoveryEndTime     = Time.time + reverseRecoveryDuration;
        reverseRecoveryTurnDirection = lastSteeringDirection != 0 ? lastSteeringDirection : 1;
        hasPendingMove             = false;
        isRotating                 = false;
        ClearForwardMoveTracking();
        ResetScuffTracking();
    }

    private void FinishReverseRecovery()
    {
        recoveryLevel          = RecoveryLevel.None;
        reverseRecoveryEndTime = 0f;
        hasPendingMove         = false;
        isRotating             = false;
        committedFacing        = QuantizeFacingFromTransform();
        TraceMovementDiagnostic("recovery_done", sessionState?.GetAction(agentId), GetDistanceToTargetCell());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsScuffing()
    {
        Rigidbody2D rb = GetBodyRigidbody();
        if (obstacleContactMask.value == 0 || rb == null) return false;
        return rb.IsTouchingLayers(obstacleContactMask);
    }

    private float GetAgentVelocityMagnitude()
    {
        Rigidbody2D rb = GetBodyRigidbody();
        return rb != null ? rb.linearVelocity.magnitude : 0f;
    }

    private void StopMovement()
    {
        if (tankController != null)
            tankController.HandleMoveBody(Vector2.zero);
    }

    private void RejectForwardAction(string reason, ActionDto dto)
    {
        pendingAction = "W";
        hasPendingMove = false;
        isRotating = false;
        ClearForwardMoveTracking();
        ResetPartialDrive();
        StopMovement();
        ZeroBodyVelocity();

        Vector3 bodyPos = GetBodyPosition();
        Vector2Int observedCell = mapLoader != null ? mapLoader.WorldToCell(bodyPos) : currentCell;
        TraceDiagnosticWithCooldown(
            reason,
            dto,
            GetDistanceToTargetCell(),
            ref nextStateDiagnosticTime,
            null,
            dto?.nextLoc ?? -1,
            bodyPos,
            mapLoader != null ? Vector2.Distance(bodyPos, mapLoader.CellToWorld(observedCell)) : -1f);

        RequestImmediatePlanStep();
    }

    private TankMoverCopy GetTankMover() => tankController != null ? tankController.tankMover : null;

    private Rigidbody2D GetBodyRigidbody()
    {
        TankMoverCopy mover = GetTankMover();
        return mover != null ? mover.rb2d : null;
    }

    private Vector3 GetAgentPosition() => GetBodyPosition();

    private Vector3 GetBodyPosition()
    {
        Rigidbody2D rb = GetBodyRigidbody();
        if (rb != null)
            return rb.position;

        TankMoverCopy mover = GetTankMover();
        return mover != null ? mover.transform.position : transform.position;
    }

    private Vector3 GetTankMoverTransformPosition()
    {
        TankMoverCopy mover = GetTankMover();
        return mover != null ? mover.transform.position : transform.position;
    }

    private void SetBodyPosition(Vector3 center)
    {
        Rigidbody2D rb = GetBodyRigidbody();
        TankMoverCopy mover = GetTankMover();

        if (rb != null)
        {
            rb.position = center;
            if (mover != null && mover.transform == rb.transform)
                mover.transform.position = center;
            return;
        }

        if (mover != null)
            mover.transform.position = center;
        else
            transform.position = center;
    }

    private float GetBodyRotationAngle()
    {
        Rigidbody2D rb = GetBodyRigidbody();
        if (rb != null)
            return rb.rotation;

        TankMoverCopy mover = GetTankMover();
        return mover != null ? mover.transform.eulerAngles.z : transform.eulerAngles.z;
    }

    private bool IsCellInBuildBounds(Vector2Int cell)
    {
        if (mapLoader == null || mapLoader.BuildWidth <= 0 || mapLoader.BuildHeight <= 0)
            return false;

        return cell.x >= mapLoader.BuildStartX
            && cell.y >= mapLoader.BuildStartY
            && cell.x < mapLoader.BuildStartX + mapLoader.BuildWidth
            && cell.y < mapLoader.BuildStartY + mapLoader.BuildHeight;
    }

    private bool IsCellValidForMovement(Vector2Int cell)
    {
        return IsCellInBuildBounds(cell) && mapLoader != null && mapLoader.IsWalkable(cell);
    }

    private void EnsureRuntimeStateBeforeSend()
    {
        if (mapLoader == null)
            return;

        if (!IsCellValidForMovement(currentCell))
        {
            Vector3 bodyPos = GetBodyPosition();
            Vector2Int observedCell = mapLoader.WorldToCell(bodyPos);
            TryRecoverToNearestValidCell("agent_state_outside_before_send", sessionState?.GetAction(agentId), bodyPos, observedCell);
            return;
        }

        if (!hasPendingMove && !isRotating)
            TryCommitCurrentCellFromPosition();
    }

    private bool TryResolveGoalCell(out Vector2Int goalCell, out Vector2Int preferredGoalCell)
    {
        goalCell = currentCell;
        preferredGoalCell = currentCell;

        if (mapLoader == null)
            return false;

        if (hasAssignedGoalCell)
        {
            preferredGoalCell = assignedGoalCell;
            if (IsCellValidForMovement(preferredGoalCell))
            {
                goalCell = preferredGoalCell;
                return true;
            }

            if (mapLoader.TryFindWalkableNear(preferredGoalCell, out goalCell) && IsCellValidForMovement(goalCell))
                return true;

            goalCell = currentCell;
            return false;
        }

        if (eagleTarget == null)
            return IsCellValidForMovement(goalCell);

        preferredGoalCell = mapLoader.WorldToCell(eagleTarget.position);
        if (IsCellValidForMovement(preferredGoalCell))
        {
            goalCell = preferredGoalCell;
            return true;
        }

        if (mapLoader.TryFindWalkableNear(preferredGoalCell, out goalCell) && IsCellValidForMovement(goalCell))
            return true;

        goalCell = currentCell;
        return false;
    }

    private bool TryDecodeServerForwardTarget(ActionDto dto, out Vector2Int targetCell, out string reason)
    {
        targetCell = currentCell;
        reason = "server_fw_missing";

        if (dto == null)
            return false;
        if (mapLoader == null)
        {
            reason = "server_fw_no_map";
            return false;
        }

        int limit = mapLoader.BuildWidth * mapLoader.BuildHeight;
        if (dto.nextLoc < 0 || dto.nextLoc >= limit)
        {
            reason = "server_fw_invalid_bounds";
            return false;
        }

        targetCell = PibtTcpGridAdapter.LocToCell(dto.nextLoc, mapLoader.BuildWidth);
        if (!IsCellInBuildBounds(targetCell))
        {
            reason = "server_fw_invalid_bounds";
            return false;
        }

        if (!mapLoader.IsWalkable(targetCell))
        {
            reason = "server_fw_blocked_cell";
            return false;
        }

        if (!IsAdjacent(currentCell, targetCell))
        {
            reason = "server_fw_non_adjacent";
            return false;
        }

        reason = null;
        return true;
    }

    private bool TryRecoverToNearestValidCell(string reason, ActionDto dto, Vector3 observedPosition, Vector2Int observedCell)
    {
        if (mapLoader == null)
            return false;

        Vector2Int recoveredCell;
        bool hasRecovery = false;

        if (IsCellValidForMovement(currentCell))
        {
            recoveredCell = currentCell;
            hasRecovery = true;
        }
        else if (mapLoader.TryFindWalkableNear(observedCell, out recoveredCell) && IsCellValidForMovement(recoveredCell))
        {
            hasRecovery = true;
        }
        else if (mapLoader.TryFindWalkableNear(GetGoalCell(), out recoveredCell) && IsCellValidForMovement(recoveredCell))
        {
            hasRecovery = true;
        }

        if (!hasRecovery)
        {
            TraceDiagnosticWithCooldown(
                reason,
                dto,
                GetDistanceToTargetCell(),
                ref nextStateDiagnosticTime,
                null,
                dto?.nextLoc ?? -1,
                observedPosition,
                mapLoader != null ? Vector2.Distance(observedPosition, mapLoader.CellToWorld(observedCell)) : -1f);
            return false;
        }

        float distToCenter = Vector2.Distance(observedPosition, mapLoader.CellToWorld(recoveredCell));
        TraceDiagnosticWithCooldown(
            reason,
            dto,
            GetDistanceToTargetCell(),
            ref nextStateDiagnosticTime,
            null,
            dto?.nextLoc ?? -1,
            observedPosition,
            distToCenter);

        SetBodyPosition(mapLoader.CellToWorld(recoveredCell));
        currentCell = recoveredCell;
        targetCell = recoveredCell;
        committedFacing = QuantizeFacingFromTransform();
        SnapBodyRotationToCommittedFacing();

        pendingAction = "W";
        hasPendingMove = false;
        isRotating = false;
        recoveryLevel = RecoveryLevel.None;
        repeatedTurnActionCount = 0;
        hasLastTurnLoopCell = false;
        ClearForwardMoveTracking();
        ResetPartialDrive();
        ResetScuffTracking();
        StopMovement();
        ZeroBodyVelocity();

        RequestImmediatePlanStep();
        return true;
    }

    private void RequestImmediatePlanStep()
    {
        MapScenarioBootstrapPIBTTcp bootstrap = GetComponentInParent<MapScenarioBootstrapPIBTTcp>();
        if (bootstrap != null)
            bootstrap.RequestImmediatePlanStep();
    }

    private bool TraceDiagnosticWithCooldown(
        string reason,
        ActionDto dto,
        float distToTarget,
        ref float nextAllowedTime,
        Vector2Int? goalCellOverride = null,
        int? nextLocOverride = null,
        Vector3? bodyPosOverride = null,
        float? distCenterOverride = null)
    {
        if (!ShouldTraceDiagnostics() || Time.time < nextAllowedTime)
            return false;

        nextAllowedTime = Time.time + diagnosticLogCooldown;
        TraceMovementDiagnostic(reason, dto, distToTarget, goalCellOverride, nextLocOverride, bodyPosOverride, distCenterOverride);
        return true;
    }

    public Vector3 GetTankMoverPositionForDebug() => GetAgentPosition();

    private bool TryCommitCurrentCellFromPosition()
    {
        Vector3 position = GetAgentPosition();
        Vector2Int observedCell = mapLoader.WorldToCell(position);
        if (!IsCellValidForMovement(observedCell))
        {
            TryRecoverToNearestValidCell("observed_cell_outside", sessionState?.GetAction(agentId), position, observedCell);
            return false;
        }

        Vector3 center = mapLoader.CellToWorld(observedCell);
        float distance = Vector2.Distance(position, center);
        if (distance > cellCenterThreshold)
            return false;

        currentCell = observedCell;
        return true;
    }

    private Vector2Int QuantizeFacingFromTransform()
    {
        Vector2 up = Quaternion.Euler(0f, 0f, GetBodyRotationAngle()) * Vector2.up;

        float absX = Mathf.Abs(up.x);
        float absY = Mathf.Abs(up.y);
        if (absY >= absX) return up.y >= 0 ? Vector2Int.down : Vector2Int.up;
        return up.x >= 0 ? Vector2Int.right : Vector2Int.left;
    }

    private static Vector2Int QuantizeFacingFromCellDelta(Vector2Int fromCell, Vector2Int toCell, Vector2Int fallback)
    {
        Vector2Int delta = toCell - fromCell;
        if (delta == Vector2Int.right || delta == Vector2Int.left || delta == Vector2Int.up || delta == Vector2Int.down)
            return delta;
        return fallback;
    }

    private static bool IsAdjacent(Vector2Int a, Vector2Int b)
    {
        Vector2Int delta = b - a;
        return Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1;
    }

    private bool TryCorrectBadForwardTarget(int nextLoc, Vector2Int serverTarget, out Vector2Int correctedCell)
    {
        correctedCell = serverTarget;
        if (!goalBiasCorrectBadFw || mapLoader == null || eagleTarget == null)
            return false;

        bool invalidLoc = nextLoc < 0 || nextLoc >= mapLoader.BuildWidth * mapLoader.BuildHeight;
        bool invalidCell = !IsCellValidForMovement(serverTarget) || !IsAdjacent(currentCell, serverTarget);
        int currentDistance = Manhattan(currentCell, GetGoalCell());
        int serverDistance = invalidCell ? int.MaxValue : Manhattan(serverTarget, GetGoalCell());

        if (!invalidLoc && !invalidCell && serverDistance <= currentDistance)
            return false;

        return TryFindGreedyGoalStep(currentDistance, out correctedCell);
    }

    private bool TryCorrectBadTurnTarget(string action, out Vector2Int correctedCell)
    {
        correctedCell = currentCell;
        if (!goalBiasCorrectBadFw || mapLoader == null || eagleTarget == null)
            return false;

        int currentDistance = Manhattan(currentCell, GetGoalCell());
        if (!TryFindGreedyGoalStep(currentDistance, out correctedCell))
            return false;

        int orientation = PibtTcpGridAdapter.DirectionToOrientation(committedFacing);
        orientation = action == "CR"
            ? PibtTcpGridAdapter.RotateClockwise(orientation)
            : PibtTcpGridAdapter.RotateCounterClockwise(orientation);

        Vector2Int serverForward = currentCell + PibtTcpGridAdapter.OrientationToCell(orientation);
        bool serverForwardHelps = IsCellValidForMovement(serverForward)
            && Manhattan(serverForward, GetGoalCell()) < currentDistance;

        return !serverForwardHelps || repeatedTurnActionCount >= diagnosticTurnStreakThreshold;
    }

    private void StartCorrectedForwardMove(Vector2Int correctedCell, string serverAction)
    {
        Debug.LogWarning(
            $"[PIBT_TCP_PHASE_D] agent={agentId} corrected {serverAction} turn to FW " +
            $"current={currentCell} corrected={correctedCell} goal={GetGoalCell()} turnStreak={repeatedTurnActionCount}");
        targetCell = correctedCell;
        BeginForwardMove();
        pendingAction = "FW";
    }

    private bool TryAutoAdvanceFromWait()
    {
        MapScenarioBootstrapPIBTTcp bootstrap = GetComponentInParent<MapScenarioBootstrapPIBTTcp>();
        if (bootstrap == null || bootstrap.HasAnyEnemyHoldingEagleSlot())
            return false;

        int currentDistance = Manhattan(currentCell, GetGoalCell());
        if (!TryFindGreedyGoalStep(currentDistance, out Vector2Int correctedCell))
            return false;

        if (correctedCell == currentCell)
            return false;

        targetCell = correctedCell;
        BeginForwardMove();
        pendingAction = "FW";

        TraceMovementDiagnostic(
            "auto_advance_from_wait",
            sessionState?.GetAction(agentId),
            GetDistanceToTargetCell(),
            GetGoalCell(),
            sessionState?.GetAction(agentId)?.nextLoc ?? -1,
            GetAgentPosition(),
            mapLoader != null ? Vector2.Distance(GetAgentPosition(), mapLoader.CellToWorld(currentCell)) : -1f);

        return true;
    }

    private bool TryFindGreedyGoalStep(int currentDistance, out Vector2Int bestCell)
    {
        bestCell = currentCell;
        Vector2Int goal = GetGoalCell();
        Vector2Int[] dirs =
        {
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.up
        };

        int bestDistance = currentDistance;
        foreach (Vector2Int dir in dirs)
        {
            Vector2Int candidate = currentCell + dir;
            if (!IsCellValidForMovement(candidate))
                continue;

            int distance = Manhattan(candidate, goal);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestCell = candidate;
            }
        }

        return bestCell != currentCell;
    }

    private Vector2Int GetGoalCell()
    {
        Vector2Int preferredGoalCell;
        if (TryResolveGoalCell(out Vector2Int goalCell, out preferredGoalCell))
        {
            if (goalCell != preferredGoalCell)
            {
                // Fallback goal was recovered; caller can inspect diagnostics separately.
            }
            return goalCell;
        }

        return currentCell;
    }

    private bool IsCellInMap(Vector2Int cell)
    {
        return IsCellInBuildBounds(cell);
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private void SnapCommittedFacingAfterRotation(string action)
    {
        int orientation = PibtTcpGridAdapter.DirectionToOrientation(committedFacing);
        if (action == "CR")
            orientation = PibtTcpGridAdapter.RotateClockwise(orientation);
        else if (action == "CCR")
            orientation = PibtTcpGridAdapter.RotateCounterClockwise(orientation);
        else
            orientation = PibtTcpGridAdapter.DirectionToOrientation(QuantizeFacingFromTransform());

        committedFacing = PibtTcpGridAdapter.OrientationToCell(orientation);
        SnapBodyRotationToCommittedFacing();
    }

    private void SnapBodyRotationToCommittedFacing()
    {
        if (tankController?.tankMover == null)
            return;

        float angle = FacingToWorldAngle(committedFacing);
        SetBodyRotationAngle(angle);
        ZeroBodyVelocity();
    }

    private void SetBodyRotationAngle(float angle)
    {
        Rigidbody2D rb = GetBodyRigidbody();
        TankMoverCopy mover = GetTankMover();

        if (rb != null)
        {
            rb.rotation = angle;
            if (mover != null && mover.transform == rb.transform)
                mover.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            return;
        }

        if (mover != null)
            mover.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        else
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void ZeroBodyVelocity()
    {
        Rigidbody2D rb = GetBodyRigidbody();
        if (rb == null)
            return;

        rb.angularVelocity = 0f;
        rb.linearVelocity = Vector2.zero;
    }

    private void SnapBodyToCurrentCellCenter()
    {
        if (tankController?.tankMover == null || mapLoader == null)
            return;

        Vector3 center = mapLoader.CellToWorld(currentCell);
        Vector3 bodyPos = GetBodyPosition();
        float snapDistance = Vector2.Distance(bodyPos, center);
        if (snapDistance > 0.5f)
        {
            TraceDiagnosticWithCooldown(
                "snap_distance_large",
                sessionState?.GetAction(agentId),
                GetDistanceToTargetCell(),
                ref nextSnapDiagnosticTime,
                GetGoalCell(),
                sessionState?.GetAction(agentId)?.nextLoc ?? -1,
                bodyPos,
                snapDistance);
        }

        SetBodyPosition(center);

        ZeroBodyVelocity();
        SnapBodyRotationToCommittedFacing();
    }

    private void BeginForwardMove()
    {
        hasPendingMove = true;
        isRotating = false;

        float dist = GetDistanceToTargetCell();
        pendingMoveStartTime = Time.time;
        pendingMoveLastProgressTime = Time.time;
        pendingMoveBestDistance = dist;
        nextPendingMoveDiagnosticTime = Time.time + Mathf.Min(diagnosticNoProgressSeconds, diagnosticLongFwSeconds);
    }

    private void ClearForwardMoveTracking()
    {
        pendingMoveStartTime = -1f;
        pendingMoveLastProgressTime = -1f;
        pendingMoveBestDistance = float.PositiveInfinity;
        nextPendingMoveDiagnosticTime = 0f;
    }

    private void UpdateForwardMoveDiagnostics(float dist)
    {
        if (pendingMoveStartTime < 0f)
            BeginForwardMove();

        if (dist < pendingMoveBestDistance - 0.02f)
        {
            pendingMoveBestDistance = dist;
            pendingMoveLastProgressTime = Time.time;
            return;
        }

        if (!ShouldTraceDiagnostics())
            return;

        float moveAge = Time.time - pendingMoveStartTime;
        float staleAge = Time.time - pendingMoveLastProgressTime;
        if (Time.time < nextPendingMoveDiagnosticTime)
            return;

        bool longMove = moveAge >= diagnosticLongFwSeconds;
        bool noProgress = staleAge >= diagnosticNoProgressSeconds;
        if (!longMove && !noProgress)
            return;

        nextPendingMoveDiagnosticTime = Time.time + diagnosticLogCooldown;
        string reason = longMove && noProgress
            ? "fw_pending_no_progress"
            : (longMove ? "fw_pending_long" : "fw_no_progress");
        TraceMovementDiagnostic(reason, sessionState?.GetAction(agentId), dist);
    }

    private float GetDistanceToTargetCell()
    {
        if (mapLoader == null)
            return -1f;

        Vector3 targetWorld = mapLoader.CellToWorld(targetCell);
        return Vector2.Distance(GetBodyPosition(), targetWorld);
    }

    private void TraceOffCenterRotateIfNeeded(float deltaDeg)
    {
        if (mapLoader == null || !ShouldTraceDiagnostics())
            return;

        Vector3 center = mapLoader.CellToWorld(currentCell);
        float dist = Vector2.Distance(GetBodyPosition(), center);
        if (dist <= cellCenterThreshold || Time.time < nextTurnLoopDiagnosticTime)
            return;

        nextTurnLoopDiagnosticTime = Time.time + diagnosticLogCooldown;
        TraceMovementDiagnostic($"rotate_off_center delta={deltaDeg:F0}", sessionState?.GetAction(agentId), dist);
    }

    private static float FacingToWorldAngle(Vector2Int facing)
    {
        if (facing == Vector2Int.right) return -90f;
        if (facing == Vector2Int.up)    return 180f;
        if (facing == Vector2Int.left)  return 90f;
        return 0f;
    }

    private bool StepPartialDriveCycle()
    {
        partialDriveAccumulator += Time.deltaTime / Mathf.Max(0.01f, partialDrivePeriod);
        partialDriveAccumulator -= Mathf.Floor(partialDriveAccumulator);
        return partialDriveAccumulator < partialDriveDutyCycle;
    }

    private void ResetPartialDrive() => partialDriveAccumulator = 0f;

    private void ResetScuffTracking()
    {
        scuffStartTime       = -1f;
        wasTouchingLastFrame = false;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void UpdateTurnStreak(ActionDto dto)
    {
        string action = dto?.action ?? "W";
        bool isTurn = action == "CR" || action == "CCR";
        repeatedTurnActionCount = isTurn ? repeatedTurnActionCount + 1 : 0;

        lastServerAction = action;
    }

    private void TraceTurnLoopDiagnosticIfNeeded(ActionDto dto)
    {
        if (!ShouldTraceDiagnostics())
            return;

        string action = dto?.action ?? "W";
        if (action != "CR" && action != "CCR")
            return;

        if (!hasLastTurnLoopCell || lastTurnLoopCell != currentCell)
            return;

        if (repeatedTurnActionCount < 3 || Time.time < nextTurnLoopDiagnosticTime)
            return;

        nextTurnLoopDiagnosticTime = Time.time + diagnosticLogCooldown;
        TraceMovementDiagnostic("turn_loop_same_cell", dto, GetDistanceToTargetCell());
    }

    private bool TryDiagnosticTurnRecovery(ActionDto dto)
    {
        string action = dto?.action ?? "W";
        if (action != "CR" && action != "CCR")
        {
            hasLastTurnLoopCell = false;
            return false;
        }

        if (!hasLastTurnLoopCell || lastTurnLoopCell != currentCell)
        {
            hasLastTurnLoopCell = true;
            lastTurnLoopCell = currentCell;
            return false;
        }

        if (repeatedTurnActionCount < diagnosticTurnStreakThreshold)
            return false;

        if (Time.time - lastDiagnosticRecoveryTime < diagnosticReplanCooldown)
            return false;

        lastDiagnosticRecoveryTime = Time.time;
        committedFacing = QuantizeFacingFromTransform();
        SnapBodyRotationToCommittedFacing();
        pendingAction = "W";
        hasPendingMove = false;
        isRotating = false;
        StopMovement();

        MapScenarioBootstrapPIBTTcp bootstrap = GetComponentInParent<MapScenarioBootstrapPIBTTcp>();
        if (bootstrap != null)
            bootstrap.RequestImmediatePlanStep();

        Debug.LogWarning(
            $"[PIBT_TCP_PHASE_D] diagnostic recovery agent={agentId} cell={currentCell} " +
            $"action={action} turnStreak={repeatedTurnActionCount} facing={committedFacing}");
        repeatedTurnActionCount = 0;
        hasLastTurnLoopCell = false;
        return true;
    }

    private void TraceAgentExecution(string phase, ActionDto dto)
    {
        MapScenarioBootstrapPIBTTcp bootstrap = GetComponentInParent<MapScenarioBootstrapPIBTTcp>();
        if (bootstrap == null || !bootstrap.debugTcpTrace || sessionState == null || mapLoader == null || tankController?.tankMover == null)
            return;

        int requestId = sessionState.LastRequestId;
        bool shouldLog = requestId != lastLoggedRequestId
            || phase == "rotate_done"
            || phase == "arrive"
            || repeatedTurnActionCount >= 3;
        if (!shouldLog)
            return;

        lastLoggedRequestId = requestId;
        Vector2Int eagleCell = GetGoalCell();
        string assignedGoalText = hasAssignedGoalCell ? assignedGoalCell.ToString() : "-";
        Vector3 pos = GetAgentPosition();
        string action = dto?.action ?? "W";
        int nextLoc = dto?.nextLoc ?? -1;
        string targetCellText = hasPendingMove ? targetCell.ToString() : "-";
        float eagleDistance = eagleTarget != null ? Vector2.Distance(pos, eagleTarget.position) : -1f;
        bool canShootEagle = CanShootEagleNow();
        Transform shootingTarget = GetShootingTarget();
        string motionState = GetMotionStateForDebug();
        string shootingTargetText = shootingTarget == null
            ? "-"
            : shootingTarget == eagleTarget
                ? "Eagle"
                : shootingTarget == playerTarget
                    ? "Player"
                    : shootingTarget.name;

        Debug.Log(
            $"[PIBT_TCP_TRACE] agent={agentId} phase={phase} req={requestId} t={sessionState.LastTimestep} " +
            $"cell={currentCell} goal={eagleCell} assignedGoal={assignedGoalText} canShootEagle={canShootEagle} shootTarget={shootingTargetText} motionState={motionState} facing={committedFacing} physFacing={QuantizeFacingFromTransform()} pos=({pos.x:F2},{pos.y:F2}) " +
            $"action={action} nextLoc={nextLoc} targetCell={targetCellText} pendingMove={hasPendingMove} " +
            $"rotating={isRotating} turnStreak={repeatedTurnActionCount} distEagle={eagleDistance:F2}");
    }

    private bool ShouldTraceDiagnostics()
    {
        MapScenarioBootstrapPIBTTcp bootstrap = GetComponentInParent<MapScenarioBootstrapPIBTTcp>();
        return bootstrap != null && bootstrap.debugTcpTrace;
    }

    private void TraceMovementDiagnostic(
        string reason,
        ActionDto dto,
        float distToTarget,
        Vector2Int? goalCellOverride = null,
        int? nextLocOverride = null,
        Vector3? bodyPosOverride = null,
        float? distCenterOverride = null)
    {
        if (!ShouldTraceDiagnostics())
            return;

        if (reason == "scuff_recovery")
        {
            if (Time.time < nextScuffDiagnosticTime)
                return;
            nextScuffDiagnosticTime = Time.time + diagnosticLogCooldown;
        }

        Rigidbody2D rb = GetBodyRigidbody();
        Vector3 bodyPos = bodyPosOverride.HasValue ? bodyPosOverride.Value : GetBodyPosition();
        Vector3 rbPos = rb != null ? (Vector3)rb.position : Vector3.zero;
        Vector3 moverPos = GetTankMoverTransformPosition();
        Vector3 rootPos = transform.position;
        Vector2 velocity = rb != null ? rb.linearVelocity : Vector2.zero;
        string action = dto?.action ?? pendingAction ?? "W";
        string targetText = hasPendingMove ? targetCell.ToString() : "-";
        Vector2Int goalCell = goalCellOverride.HasValue ? goalCellOverride.Value : GetGoalCell();
        string assignedGoalText = hasAssignedGoalCell ? assignedGoalCell.ToString() : "-";
        int nextLoc = nextLocOverride.HasValue ? nextLocOverride.Value : (dto?.nextLoc ?? -1);
        float distCenter = distCenterOverride.HasValue
            ? distCenterOverride.Value
            : (mapLoader != null ? Vector2.Distance(bodyPos, mapLoader.CellToWorld(currentCell)) : -1f);
        bool canShootEagle = CanShootEagleNow();

        Debug.LogWarning(
            $"[PIBT_TCP_DIAG] agent={agentId} reason={reason} req={sessionState?.LastRequestId ?? -1} " +
            $"loc={currentCell} goalCell={goalCell} assignedGoal={assignedGoalText} canShootEagle={canShootEagle} target={targetText} action={action} nextLoc={nextLoc} pendingAction={pendingAction ?? "-"} " +
            $"bodyPos=({bodyPos.x:F2},{bodyPos.y:F2}) rbPos=({rbPos.x:F2},{rbPos.y:F2}) moverPos=({moverPos.x:F2},{moverPos.y:F2}) rootPos=({rootPos.x:F2},{rootPos.y:F2}) " +
            $"rbVel=({velocity.x:F3},{velocity.y:F3}) distCenter={distCenter:F3} distTarget={distToTarget:F3} touching={GetTouchingLayersForDebug()} " +
            $"pendingMove={hasPendingMove} rotating={isRotating} recovery={recoveryLevel} turnStreak={repeatedTurnActionCount}");
    }

    private string GetTouchingLayersForDebug()
    {
        Rigidbody2D rb = GetBodyRigidbody();
        if (rb == null)
            return "-";

        int count = rb.GetContacts(debugContactBuffer);
        if (count <= 0)
            return IsScuffing() ? $"mask:{obstacleContactMask.value}" : "-";

        string layers = "";
        for (int i = 0; i < count; i++)
        {
            Collider2D col = debugContactBuffer[i].collider;
            if (col == null)
                continue;

            string layerName = LayerMask.LayerToName(col.gameObject.layer);
            if (string.IsNullOrEmpty(layerName))
                layerName = col.gameObject.layer.ToString();

            if (layers.Contains(layerName))
                continue;

            layers = string.IsNullOrEmpty(layers) ? layerName : layers + "," + layerName;
        }

        return string.IsNullOrEmpty(layers) ? "-" : layers;
    }

    private void OnDrawGizmos()
    {
        if (mapLoader == null) return;
        if (hasPendingMove)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(GetAgentPosition(), mapLoader.CellToWorld(targetCell));
            Gizmos.DrawWireSphere(mapLoader.CellToWorld(targetCell), 0.2f);
        }
    }
}
