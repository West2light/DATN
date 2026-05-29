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
    public int    idx;
    public Vector2 pos;
    public float  rot;
    public int    hp;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref idx);
        s.SerializeValue(ref pos);
        s.SerializeValue(ref rot);
        s.SerializeValue(ref hp);
    }
}

// ── NetworkBehaviour: one per connected player (spawned as PlayerPrefab) ─────

/// <summary>
/// RPC hub for one player.
/// Owner sends input via ServerRpc; server pushes tank state back via targeted ClientRpc.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LanNetworkBridge : NetworkBehaviour
{
    // Slot index assigned by LanGameCoordinator (0 = host, 1+= clients)
    public NetworkVariable<int> Slot = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Server-side references ────────────────────────────────────────────────
    private TankController _serverTank;

    // ── Owner-side: sample input every frame ─────────────────────────────────
    private Camera _ownerCamera;
    private bool   _prevShoot;

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

        Vector2 move   = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool    shoot  = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
        Vector2 turret = _ownerCamera != null
            ? (Vector2)_ownerCamera.ScreenToWorldPoint(Input.mousePosition)
            : Vector2.zero;

        // Send every frame (server is authoritative, let it throttle)
        SendInputServerRpc(new LanInputPacket { move = move, turretWorldPos = turret, shoot = shoot });
    }

    // ── Server receives owner input ───────────────────────────────────────────

    [ServerRpc]
    public void SendInputServerRpc(LanInputPacket pkt)
    {
        if (_serverTank == null) return;
        _serverTank.HandleMoveBody(pkt.move);
        _serverTank.HandleTurretMovement(pkt.turretWorldPos);
        if (pkt.shoot) _serverTank.HandleShoot();
    }

    // ── Server pushes state to owner ─────────────────────────────────────────

    [ClientRpc]
    public void ReceiveTankStateClientRpc(LanTankState state, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        LanClientView.Instance?.ApplyOwnTankState(state);
    }

    // ── Server → All clients: enemy + other player states ────────────────────

    [ClientRpc]
    public void ReceiveWorldStateClientRpc(LanTankState[] playerStates, LanEnemyState[] enemyStates, int eagleHp)
    {
        if (IsServer) return;
        LanClientView.Instance?.ApplyWorldState(playerStates, enemyStates, eagleHp);
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
        _serverTank  = tank;
        Slot.Value   = slot;
    }

    public TankController ServerTank       => _serverTank;
    public ulong          ClientId         => OwnerClientId;
}
