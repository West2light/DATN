using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy AI driven by the external C++ PIBT TCP server.
/// Does NOT do local pathfinding — receives next target cell from
/// MapScenarioBootstrapPIBT_TCP (the TCP coordinator) each server tick.
/// Between ticks it steers smoothly toward the last assigned target.
/// Shooting logic is handled locally (same as GridEnemyAgentPIBT).
/// </summary>
public class GridEnemyAgentPIBT_TCP : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    public MapLoader       mapLoader;
    public Transform       eagleTarget;
    public Transform       playerTarget;
    [HideInInspector] public Transform[] playerTargets;
    public TankController  tankController;

    [Header("Steering")]
    [Range(-1f, 1f)] public float forwardAlignmentThreshold     = 0.97f;
    [Range(-1f, 1f)] public float turningDriveAlignmentThreshold = 0.85f;
    [Range(-1f, 1f)] public float partialDriveAlignmentThreshold = 0.5f;
    [Range(0f, 1f)]  public float mostlyAlignedTurnScale         = 0.3f;
    public float waypointReachDistanceStraight = 0.3f;
    public float waypointReachDistanceTurning  = 0.12f;
    [Min(0.01f)] public float partialDrivePeriod    = 0.1f;
    [Range(0f, 1f)] public float partialDriveDutyCycle = 0.65f;

    [Header("Shooting")]
    public float eagleShootingRange  = 5f;
    public float playerShootingRange = 7f;
    public LayerMask lineOfSightMask;

    // ── Runtime state ──────────────────────────────────────────────────────
    private Vector2Int  _currentTarget;
    private bool        _hasTarget;
    private int         _lastSteeringDir = 1;
    private float       _partialDriveAcc;
    private FactionMember _selfFaction;

    // ── Backtest metrics ───────────────────────────────────────────────────
    [System.NonSerialized] public int   btShotCount;
    [System.NonSerialized] public int   btCellsVisited;
    [System.NonSerialized] public float btSpawnTime;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (tankController == null)
            tankController = GetComponentInChildren<TankController>();
        _selfFaction = GetComponent<FactionMember>();
    }

    private void Update()
    {
        if (mapLoader == null || tankController == null) return;

        Transform shootTarget = GetShootingTarget();
        if (shootTarget != null)
        {
            tankController.HandleMoveBody(Vector2.zero);
            tankController.HandleTurretMovement(shootTarget.position);
            if (tankController.aimTurret != null && tankController.aimTurret.IsAlignedTo(shootTarget.position))
            {
                tankController.HandleShoot();
                btShotCount++;
            }
            return;
        }

        if (!_hasTarget)
        {
            tankController.HandleMoveBody(Vector2.zero);
            return;
        }

        SteerTowardTarget();
    }

    // ── Public API (called by MapScenarioBootstrapPIBT_TCP) ───────────────

    /// <summary>Stop movement immediately (e.g. server connection lost).</summary>
    public void StopMovement() => _hasTarget = false;

    /// <summary>Set the next grid cell this agent should move to.</summary>
    public void SetNextTarget(Vector2Int cell)
    {
        if (cell != _currentTarget)
        {
            btCellsVisited++;
        }
        _currentTarget = cell;
        _hasTarget = true;
    }

    public Vector2Int CurrentCell =>
        mapLoader != null ? mapLoader.WorldToCell(AgentPosition()) : Vector2Int.zero;

    // ── Steering ───────────────────────────────────────────────────────────

    private void SteerTowardTarget()
    {
        Vector3 targetWorld = mapLoader.CellToWorld(_currentTarget);
        Vector2 dir         = targetWorld - tankController.tankMover.transform.position;
        float   dist        = dir.magnitude;

        // Already at target — wait for coordinator to set a new one
        if (dist <= waypointReachDistanceStraight)
        {
            _partialDriveAcc = 0f;
            tankController.HandleMoveBody(Vector2.zero);
            return;
        }

        Vector2 forward  = tankController.tankMover.transform.up;
        float   dot      = Vector2.Dot(forward, dir.normalized);
        float   cross    = Vector3.Cross(forward, dir.normalized).z;
        int     rotation = cross >= 0f ? -1 : 1;
        _lastSteeringDir = rotation;

        if (dot >= forwardAlignmentThreshold)
        {
            _partialDriveAcc = 0f;
            tankController.HandleMoveBody(Vector2.up);
        }
        else if (dot >= turningDriveAlignmentThreshold)
        {
            _partialDriveAcc = 0f;
            tankController.HandleMoveBody(new Vector2(rotation * mostlyAlignedTurnScale, 1f));
        }
        else if (dot >= partialDriveAlignmentThreshold)
        {
            bool drive = StepPartialDrive();
            tankController.HandleMoveBody(new Vector2(rotation, drive ? 1f : 0f));
        }
        else
        {
            _partialDriveAcc = 0f;
            tankController.HandleMoveBody(new Vector2(rotation, 0.1f));
        }
    }

    private bool StepPartialDrive()
    {
        _partialDriveAcc += Time.deltaTime / Mathf.Max(0.01f, partialDrivePeriod);
        _partialDriveAcc -= Mathf.Floor(_partialDriveAcc);
        return _partialDriveAcc < partialDriveDutyCycle;
    }

    // ── Shooting ───────────────────────────────────────────────────────────

    private Transform GetShootingTarget()
    {
        Transform nearest = GetNearestPlayerInRange();
        if (nearest != null) return nearest;
        if (CanShootTarget(eagleTarget, eagleShootingRange)) return eagleTarget;
        return null;
    }

    private Transform GetNearestPlayerInRange()
    {
        Transform best = null;
        float bestSq = float.MaxValue;
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
        Vector2 tPos   = target.position;
        if (Vector2.Distance(origin, tPos) > range) return false;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, tPos - origin, range, lineOfSightMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.transform == target || hit.collider.transform.IsChildOf(target)) return true;
            FactionMember fm = FactionMember.FindForCollider(hit.collider);
            if (FactionMember.AreFriendly(_selfFaction, fm)) continue;
            return false;
        }
        return false;
    }

    private Vector3 AgentPosition() =>
        tankController?.tankMover != null ? tankController.tankMover.transform.position : transform.position;
}
