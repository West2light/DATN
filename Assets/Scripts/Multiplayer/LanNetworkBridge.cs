using Unity.Netcode;
using UnityEngine;

// ── Serializable packets ──────────────────────────────────────────────────────

public struct LanInputPacket : INetworkSerializable
{
    public Vector2 move;
    public Vector2 turretWorldPos;
    public bool    shoot;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref move);
        s.SerializeValue(ref turretWorldPos);
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

        if (IsOwner && !IsServer)
            _ownerCamera = Camera.main;
    }

    private void Update()
    {
        if (!IsOwner || IsServer) return;

        // Camera is only valid in the game scene (not lobby); assign lazily.
        if (_ownerCamera == null)
            _ownerCamera = Camera.main;

        Vector2 move   = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool    shoot  = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
        Vector2 turret = _ownerCamera != null
            ? (Vector2)_ownerCamera.ScreenToWorldPoint(Input.mousePosition)
            : Vector2.zero;

        SendInputServerRpc(new LanInputPacket { move = move, turretWorldPos = turret, shoot = shoot });

        // Client-side prediction: move OwnGhost immediately so input feels responsive.
        LanClientView.Instance?.PredictOwnMovement(move);
    }

    // ── Server receives owner input ───────────────────────────────────────────

    [ServerRpc]
    public void SendInputServerRpc(LanInputPacket pkt)
    {
        if (_serverTank == null) return;
        _serverTank.HandleMoveWorldDirection(pkt.move);
        _serverTank.HandleTurretMovement(pkt.turretWorldPos);
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
