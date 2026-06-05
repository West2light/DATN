using Unity.Netcode;
using UnityEngine;

// ── Serializable packets ──────────────────────────────────────────────────────

public struct LanInputPacket : INetworkSerializable
{
    public Vector2 move;
    // Turret rotation in degrees, pre-computed on the sender relative to their own
    // tank/ghost position.  Replaces world-pos to avoid camera-position coupling
    // (two machines at similar world positions would otherwise share the same angle).
    public float   turretAngle;
    public bool    shoot;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref move);
        s.SerializeValue(ref turretAngle);
        s.SerializeValue(ref shoot);
    }
}

public struct LanTankState : INetworkSerializable
{
    public Vector2 pos;
    public float   bodyRot;
    public float   turretRot;
    public int     hp;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref pos);
        s.SerializeValue(ref bodyRot);
        s.SerializeValue(ref turretRot);
        s.SerializeValue(ref hp);
    }
}

public struct LanEnemyState : INetworkSerializable
{
    public int     idx;
    public Vector2 pos;
    public float   rot;
    public int     hp;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref idx);
        s.SerializeValue(ref pos);
        s.SerializeValue(ref rot);
        s.SerializeValue(ref hp);
    }
}

public struct LanBulletState : INetworkSerializable
{
    public Vector2 pos;
    public float   rot;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref pos);
        s.SerializeValue(ref rot);
    }
}

// ── NetworkBehaviour: one per connected player (spawned as PlayerPrefab) ─────

/// <summary>
/// RPC hub for one player.
/// - Owner sends input via ServerRpc every frame.
/// - World state (positions, HP) is broadcast via CustomMessagingManager in
///   LanGameCoordinator — NOT via ClientRpc — to decouple the sync channel
///   from any individual NetworkObject's lifetime.
/// - Bullet/explosion/win events use ClientRpc because they are one-shot and
///   must not be dropped.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LanNetworkBridge : NetworkBehaviour
{
    // Slot index assigned by LanGameCoordinator (0 = host, 1+ = clients)
    public NetworkVariable<int> Slot = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Server-side references ────────────────────────────────────────────────
    private TankController _serverTank;

    // ── Owner-side: sample input every frame ─────────────────────────────────
    private Camera _ownerCamera;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            LanGameCoordinator.Instance?.OnBridgeSpawned(this);

        // Cả host lẫn client đều cần camera để convert mouse → world position.
        if (IsOwner)
            _ownerCamera = Camera.main;
    }

    private void Update()
    {
        // Chỉ owner của bridge mới xử lý input — tránh mọi bridge khác can thiệp.
        if (!IsOwner) return;

        if (_ownerCamera == null)
            _ownerCamera = Camera.main;

        Vector2 move      = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool    shoot     = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
        Vector2 mouseWorld = _ownerCamera != null
            ? (Vector2)_ownerCamera.ScreenToWorldPoint(Input.mousePosition)
            : Vector2.zero;

        if (IsServer)
        {
            // Host: route qua Coordinator để CHẮC CHẮN chỉ Tank[0] (slot host) nhận input.
            LanGameCoordinator.Instance?.ApplyHostInput(move, mouseWorld, shoot);
            return;
        }

        // Client: compute turret angle from OwnGhost's turret position (not raw world pos).
        // This decouples the angle from camera position — two machines with similar camera
        // views would otherwise compute the same world-pos and therefore the same angle.
        float turretAngle = ComputeTurretAngle(mouseWorld);

        // Client từ xa: gửi input qua RPC.
        SendInputServerRpc(new LanInputPacket { move = move, turretAngle = turretAngle, shoot = shoot });

        // Body prediction runs here (Update) for responsive movement.
        // Turret prediction is deferred to LateUpdate so it always wins over any
        // body-rotation side-effects that happen later in this same Update phase.
        LanClientView.Instance?.PredictOwnMovement(move);
    }

    private void LateUpdate()
    {
        // Only the owning client's bridge applies turret prediction.
        if (!IsOwner || IsServer) return;
        if (_ownerCamera == null) _ownerCamera = Camera.main;
        Vector2 mouseWorld = _ownerCamera != null
            ? (Vector2)_ownerCamera.ScreenToWorldPoint(Input.mousePosition)
            : Vector2.zero;
        // Runs after ALL Update() calls — guarantees turret is correct before render.
        LanClientView.Instance?.PredictTurretAim(mouseWorld);
    }

    // Compute the desired turret rotation (degrees) for this client.
    // Uses OwnGhost's turret world position as the "from" point so the angle is
    // independent of camera position and matches what the player sees locally.
    private float ComputeTurretAngle(Vector2 mouseWorld)
    {
        Transform ghost = LanClientView.Instance?.OwnGhost;
        if (ghost != null)
        {
            AimTurret aim = ghost.GetComponentInChildren<AimTurret>(true);
            if (aim != null)
            {
                Vector2 dir = mouseWorld - (Vector2)aim.transform.position;
                if (dir.sqrMagnitude > 0.001f)
                    return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            }
        }
        // Fallback: direction from camera centre to mouse (before ghost is ready).
        if (_ownerCamera != null)
        {
            Vector2 camPos = _ownerCamera.transform.position;
            Vector2 dir    = mouseWorld - camPos;
            if (dir.sqrMagnitude > 0.001f)
                return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }
        return 0f;
    }

    // ── Server receives owner input ───────────────────────────────────────────

    [ServerRpc]
    public void SendInputServerRpc(LanInputPacket pkt)
    {
        if (_serverTank == null) return;
        _serverTank.HandleMoveWorldDirection(pkt.move);
        // Apply pre-computed angle directly — no world-pos re-derivation needed.
        if (_serverTank.aimTurret != null)
            _serverTank.aimTurret.transform.rotation = Quaternion.Euler(0f, 0f, pkt.turretAngle);
        if (pkt.shoot) _serverTank.HandleShoot();
    }

    // ── Server → Owner: targeted tank-state correction (optional refinement) ─

    [ClientRpc]
    public void ReceiveTankStateClientRpc(LanTankState state, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        LanClientView.Instance?.ApplyOwnTankState(state);
    }

    // ── Server → All clients: bullet spawn (event-driven, immediate) ─────────

    [ClientRpc]
    public void SpawnBulletEffectClientRpc(Vector2 pos, Vector2 dir, float speed, float maxDist)
    {
        if (IsServer) return;
        LanClientView.Instance?.SpawnMovingBullet(pos, dir, speed, maxDist);
    }

    // ── Server → All clients: bullet hit explosion effect ────────────────────

    [ClientRpc]
    public void SpawnExplosionClientRpc(Vector2 pos)
    {
        if (IsServer) return;
        LanClientView.Instance?.SpawnExplosion(pos);
    }

    // ── Server → All clients: win / game over ────────────────────────────────

    [ClientRpc]
    public void BroadcastEventClientRpc(bool isWin)
    {
        if (IsServer) return;
        LanClientView.Instance?.ApplyGameEvent(isWin);
    }

    // ── Server-side API ───────────────────────────────────────────────────────

    public void LinkTank(TankController tank, int slot)
    {
        _serverTank = tank;
        Slot.Value  = slot;
    }

    public TankController ServerTank => _serverTank;
    public ulong          ClientId   => OwnerClientId;
}
