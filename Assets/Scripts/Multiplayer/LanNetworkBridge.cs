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

    // Tank variant (color) chosen by this player in the lobby.
    // Server writes it (set by host directly, or via ServerRpc from client).
    public NetworkVariable<int> VariantIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Lobby "ready" flag for this player. Server-authoritative; client toggles via RPC.
    // The owner can only start the game once every connected player's Ready is true.
    public NetworkVariable<bool> Ready = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Server-side references ────────────────────────────────────────────────
    private TankController _serverTank;

    // ── Owner-side: sample input every frame ─────────────────────────────────
    private Camera _ownerCamera;

    // Input is sampled every frame for local prediction but only SENT to the server
    // at this fixed rate. Sending every render frame (up to 144 fps) floods the
    // server's transport receive queue over WebSocket and breaks the connection.
    private const float InputSendInterval = 1f / 30f;
    private float _inputSendTimer;
    private bool  _pendingShoot;   // latches a shoot press between throttled sends

    // Server-authoritative global pause. The owner is tracked so disconnecting the
    // player who paused cannot leave a dedicated server frozen forever.
    private static bool  _serverGamePaused;
    private static ulong _serverPauseOwner = ulong.MaxValue;

    public override void OnNetworkSpawn()
    {
        enabled = true;
        if (IsServer)
        {
            // Set host variant BEFORE registering so TryLink reads the correct value.
            if (IsOwner) VariantIndex.Value = LanSessionManager.LocalVariantIndex;
            LanGameCoordinator.Instance?.OnBridgeSpawned(this);
        }

        // Non-host client: send chosen variant to server via RPC.
        if (IsOwner && !IsServer)
            SendVariantServerRpc(LanSessionManager.LocalVariantIndex);

        // Re-apply sprite whenever variant resolves (client RPC may arrive after TryLink).
        VariantIndex.OnValueChanged += OnVariantIndexChanged;

        // Cả host lẫn client đều cần camera để convert mouse → world position.
        if (IsOwner)
            _ownerCamera = Camera.main;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && _serverGamePaused && _serverPauseOwner == OwnerClientId)
            ApplyServerGamePause(false, ulong.MaxValue);

        // Prevent Update/LateUpdate from running after NGO despawn — accessing IsOwner,
        // IsServer etc. would dereference the destroyed NetworkManager and throw
        // MissingReferenceException every frame until the GO is garbage-collected.
        enabled = false;
        VariantIndex.OnValueChanged -= OnVariantIndexChanged;
    }

    private void OnVariantIndexChanged(int prev, int next)
    {
        if (IsServer)
        {
            if (_serverTank != null && _serverTank.gameObject != null)
                MapTankTestBootstrap.ApplyVariantToTank(_serverTank.gameObject, next);
            // Re-broadcast init so all clients receive the updated variant.
            LanGameCoordinator.Instance?.ResendInit();
        }
        // On ALL machines: update the corresponding ghost sprite via LanClientView.
        int slot = Slot.Value;
        if (slot >= 0)
            LanClientView.Instance?.UpdateGhostVariant(slot, next);
    }

    [ServerRpc]
    private void SendVariantServerRpc(int variantIndex)
    {
        VariantIndex.Value = variantIndex;
    }

    // ── Lobby: ready toggle + owner-driven start ───────────────────────────────

    // The room owner is the connected client with the smallest clientId. Computed on
    // demand (no extra synced state) — used both to gate the START action and to label
    // the owner in the lobby UI.
    public static ulong ResolveOwnerClientId()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return ulong.MaxValue;
        ulong owner = ulong.MaxValue;
        if (nm.IsServer)
        {
            foreach (ulong id in nm.ConnectedClientsIds)
                if (id < owner) owner = id;
        }
        else
        {
            // On a pure client, derive from the bridges it can see.
            foreach (var b in FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
                if (b != null && b.IsSpawned && b.OwnerClientId < owner) owner = b.OwnerClientId;
        }
        return owner;
    }

    public bool IsRoomOwner => OwnerClientId == ResolveOwnerClientId();

    [ServerRpc]
    public void SetReadyServerRpc(bool ready)
    {
        Ready.Value = ready;
    }

    // Called by the lobby UI on the LOCAL (owned) bridge to toggle ready / change tank color.
    public void SubmitReady(bool ready)
    {
        if (IsServer) Ready.Value = ready;
        else SetReadyServerRpc(ready);
    }

    public void SubmitVariant(int variantIndex)
    {
        if (IsServer) VariantIndex.Value = variantIndex;
        else SendVariantServerRpc(variantIndex);
    }

    // The local client's own bridge (the one it owns), or null if not spawned yet.
    public static LanNetworkBridge Local
    {
        get
        {
            foreach (var b in FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
                if (b != null && b.IsSpawned && b.IsOwner) return b;
            return null;
        }
    }

    public void SubmitGamePause(bool paused)
    {
        if (!IsOwner) return;
        if (IsServer)
            ApplyServerGamePause(paused, OwnerClientId);
        else
            SetGamePausedServerRpc(paused);
    }

    [ServerRpc]
    private void SetGamePausedServerRpc(bool paused, ServerRpcParams rpcParams = default)
    {
        ApplyServerGamePause(paused, rpcParams.Receive.SenderClientId);
    }

    private void ApplyServerGamePause(bool paused, ulong requester)
    {
        _serverGamePaused = paused;
        _serverPauseOwner = paused ? requester : ulong.MaxValue;
        Time.timeScale = paused ? 0f : 1f;
        SyncGamePauseClientRpc(paused);
    }

    [ClientRpc]
    private void SyncGamePauseClientRpc(bool paused)
    {
        if (LanSessionManager.IsDedicatedServer) return;
        PauseMenuController.Ensure().ApplyNetworkPause(paused);
    }

    // Owner asks the server to start the game. RequireOwnership=false because this bridge
    // is owned by the calling client (its own player object), and we validate the room-owner
    // identity by clientId rather than NetworkObject ownership.
    [ServerRpc(RequireOwnership = false)]
    public void RequestStartServerRpc(ServerRpcParams p = default)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Only the room owner (smallest connected clientId) may start.
        if (p.Receive.SenderClientId != ResolveOwnerClientId()) return;

        // Need at least the owner connected, and every connected player's bridge must be Ready.
        if (nm.ConnectedClients.Count < 1) return;
        foreach (var b in FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
            if (b != null && b.IsSpawned && !b.Ready.Value) return;

        // Dedicated server: load through the bootstrap's guarded path (sets PlayerCount,
        // dedupes against the grace timer). Host mode uses LanLobbyController.DoStartGame.
        DedicatedServerBootstrap.Instance?.BeginGameplayScene();
    }

    private void Update()
    {
        // Chỉ owner của bridge mới xử lý input — tránh mọi bridge khác can thiệp.
        if (!IsOwner) return;

        if (PauseMenuController.IsLocalPauseActive)
        {
            _pendingShoot = false;
            if (IsServer)
            {
                LanGameCoordinator.Instance?.StopHostMovement();
                return;
            }

            // Keep sending a neutral packet while the local pause menu is open so
            // the authoritative server cannot retain the last movement command.
            _inputSendTimer += Time.unscaledDeltaTime;
            if (_inputSendTimer >= InputSendInterval)
            {
                _inputSendTimer = 0f;
                SendInputServerRpc(new LanInputPacket
                {
                    move = Vector2.zero,
                    turretAngle = GetCurrentTurretAngle(),
                    shoot = false
                });
            }
            return;
        }

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
            // Host input is applied locally (no network), so no throttling is needed.
            LanGameCoordinator.Instance?.ApplyHostInput(move, mouseWorld, shoot);
            return;
        }

        // Body prediction runs every frame for responsive local movement (no network cost).
        // Turret prediction is deferred to LateUpdate so it always wins over any
        // body-rotation side-effects that happen later in this same Update phase.
        LanClientView.Instance?.PredictOwnMovement(move);

        // Latch the shoot press so a tap landing between throttled sends is not lost.
        _pendingShoot |= shoot;

        // Throttle the input RPC to ~30 Hz. See InputSendInterval — sending every frame
        // overflows the server's receive queue over WebSocket and drops the connection.
        _inputSendTimer += Time.deltaTime;
        if (_inputSendTimer < InputSendInterval) return;
        _inputSendTimer = 0f;

        // Client: compute turret angle from OwnGhost's turret position (not raw world pos).
        // This decouples the angle from camera position — two machines with similar camera
        // views would otherwise compute the same world-pos and therefore the same angle.
        float turretAngle = ComputeTurretAngle(mouseWorld);

        // Client từ xa: gửi input qua RPC (đã throttle).
        SendInputServerRpc(new LanInputPacket { move = move, turretAngle = turretAngle, shoot = _pendingShoot });
        _pendingShoot = false;
    }

    private void LateUpdate()
    {
        // Only the owning client's bridge applies turret prediction.
        if (!IsOwner || IsServer || PauseMenuController.IsLocalPauseActive) return;
        if (_ownerCamera == null) _ownerCamera = Camera.main;
        Vector2 mouseWorld = _ownerCamera != null
            ? (Vector2)_ownerCamera.ScreenToWorldPoint(Input.mousePosition)
            : Vector2.zero;
        // Runs after ALL Update() calls — guarantees turret is correct before render.
        LanClientView.Instance?.PredictTurretAim(mouseWorld);
    }

    private static float GetCurrentTurretAngle()
    {
        Transform ghost = LanClientView.Instance?.OwnGhost;
        AimTurret aim = ghost != null ? ghost.GetComponentInChildren<AimTurret>(true) : null;
        return aim != null ? aim.transform.eulerAngles.z : 0f;
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
