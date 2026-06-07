using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-ONLY singleton.
/// World state (player/enemy/eagle positions + HP) is broadcast to all clients
/// via CustomMessagingManager at 30 Hz.
/// Bullet / explosion / win events continue to use [ClientRpc] on LanNetworkBridge
/// because they are one-shot events that must not be dropped.
/// </summary>
public class LanGameCoordinator : MonoBehaviour
{
    public static LanGameCoordinator Instance { get; private set; }

    // Custom-message channel names — LanClientView registers handlers for these.
    public const string MsgInitWorld  = "lan_init";
    public const string MsgWorldState = "lan_ws";

    private readonly List<LanNetworkBridge> _bridges       = new List<LanNetworkBridge>();
    private readonly List<TankController>   _serverTanks   = new List<TankController>();
    private readonly List<GameObject>       _serverEnemies = new List<GameObject>();

    // Cached per-enemy components so we don't call GetComponentInChildren every tick.
    private Damagable[]  _enemyDamagables;
    private AimTurret[]  _enemyAimTurrets;
    private TankMover[]  _enemyMovers;       // used to read the physics-body position

    private Coroutine _syncCoroutine;
    private int       _linkedBridgeCount;
    private int       _alivePlayerCount;  // decremented when a player tank dies
    private GameObject _eagleCache;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Bullet.OnAnyBulletFired += OnBulletFired;
        Bullet.OnAnyBulletHit   += OnBulletHit;
    }

    private void OnDestroy()
    {
        Bullet.OnAnyBulletFired -= OnBulletFired;
        Bullet.OnAnyBulletHit   -= OnBulletHit;
        if (Instance == this) Instance = null;
    }

    // ── Bullet / explosion events (reliable [ClientRpc] — must not be dropped) ──

    private void OnBulletFired(Vector2 pos, Vector2 dir, float speed, float maxDist)
        => Relay()?.SpawnBulletEffectClientRpc(pos, dir, speed, maxDist);

    private void OnBulletHit(Vector2 pos)
        => Relay()?.SpawnExplosionClientRpc(pos);

    // ── Registration ──────────────────────────────────────────────────────────

    public void RegisterServerTanks(List<TankController> tanks, List<GameObject> enemies)
    {
        _serverTanks.Clear();   _serverTanks.AddRange(tanks);
        _serverEnemies.Clear(); _serverEnemies.AddRange(enemies);

        // Pre-cache per-enemy components to avoid GetComponentInChildren every tick.
        int ec = enemies.Count;
        _enemyDamagables = new Damagable[ec];
        _enemyAimTurrets = new AimTurret[ec];
        _enemyMovers     = new TankMover[ec];
        for (int i = 0; i < ec; i++)
        {
            if (enemies[i] == null) continue;
            _enemyDamagables[i] = enemies[i].GetComponentInChildren<Damagable>();
            _enemyAimTurrets[i] = enemies[i].GetComponentInChildren<AimTurret>();
            _enemyMovers[i]     = enemies[i].GetComponentInChildren<TankMover>();
        }

        _alivePlayerCount = tanks.Count;

        ScanBridges();
        Debug.Log($"[Coordinator] {tanks.Count} tanks, {ec} enemies, {_bridges.Count} bridges");
        TryLink();
    }

    public void OnBridgeSpawned(LanNetworkBridge bridge)
    {
        if (!_bridges.Contains(bridge)) _bridges.Add(bridge);
        TryLink();
    }

    // ── Linking ───────────────────────────────────────────────────────────────

    private void TryLink()
    {
        ScanBridges();
        if (_serverTanks.Count == 0) return;

        // Link as many bridges as possible. Sort by clientId for consistent slot order.
        _bridges.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        int n = Mathf.Min(_bridges.Count, _serverTanks.Count);
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            _bridges[i].LinkTank(_serverTanks[i], i);
            // Apply the tank body sprite chosen by this player in the lobby.
            if (_serverTanks[i] != null && _serverTanks[i].gameObject != null)
                MapTankTestBootstrap.ApplyVariantToTank(_serverTanks[i].gameObject, _bridges[i].VariantIndex.Value);
        }

        // Start sync loop as soon as we have at least one bridge.
        if (_syncCoroutine == null)
            _syncCoroutine = StartCoroutine(SyncLoop());

        // Re-send the init message every time the number of linked bridges grows
        // so late-spawning client bridges receive their correct slot assignment.
        if (n > _linkedBridgeCount)
        {
            _linkedBridgeCount = n;
            SendInitWorldMsg();
            Debug.Log($"[Coordinator] Linked {n}/{_serverTanks.Count} bridge(s). Sent init.");
        }
    }

    private void ScanBridges()
    {
        foreach (var b in FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
            if (b != null && !_bridges.Contains(b)) _bridges.Add(b);
    }

    // ── Sync loop at 30 Hz ────────────────────────────────────────────────────

    private IEnumerator SyncLoop()
    {
        var wait  = new WaitForSeconds(1f / 30f);
        int ticks = 0;
        while (true)
        {
            yield return wait;
            if (ticks++ % 30 == 0) ScanBridges();
            BroadcastWorldState();
        }
    }

    // ── CustomMessaging: init world (one-time, Reliable) ─────────────────────
    //
    // Packet layout:
    //   int  playerCount
    //   int  enemyCount
    //   per slot: ulong ownerClientId, int variantIndex
    //
    // Always writes exactly playerCount entries so the client can read
    // a well-formed packet regardless of how many bridges are registered.

    public void ResendInit() => SendInitWorldMsg();

    private void SendInitWorldMsg()
    {
        var mgr = NetworkManager.Singleton?.CustomMessagingManager;
        if (mgr == null)
        {
            Debug.LogWarning("[Coordinator] CustomMessagingManager not ready for SendInitWorldMsg.");
            return;
        }

        int pc = _serverTanks.Count;
        int ec = _serverEnemies.Count;
        // 4 (pc) + 4 (ec) + pc * (8 ownerClientId + 4 variantIndex) + 16 safety
        int bufSize = 8 + pc * 12 + 16;
        using var writer = new FastBufferWriter(bufSize, Allocator.Temp);
        writer.WriteValueSafe(pc);
        writer.WriteValueSafe(ec);
        for (int i = 0; i < pc; i++)
        {
            ulong ownerId   = (i < _bridges.Count) ? _bridges[i].ClientId          : ulong.MaxValue;
            int   variantIdx = (i < _bridges.Count) ? _bridges[i].VariantIndex.Value : 0;
            writer.WriteValueSafe(ownerId);
            writer.WriteValueSafe(variantIdx);
        }

        mgr.SendNamedMessageToAll(MsgInitWorld, writer, NetworkDelivery.Reliable);
        Debug.Log($"[Coordinator] SendInitWorldMsg pc={pc} ec={ec} bridges={_bridges.Count}");
    }

    // ── CustomMessaging: world state (30 Hz, Reliable) ───────────────────────
    //
    // Packet layout:
    //   int  playerCount
    //   per player: float px, py, bodyRot, turretRot; int hp
    //   int  enemyCount
    //   per enemy:  int idx; float px, py, bodyRot, turretRot; int hp
    //   int  eagleHp; float eaglePx, eaglePy

    private void BroadcastWorldState()
    {
        var mgr = NetworkManager.Singleton?.CustomMessagingManager;
        if (mgr == null) return;

        int pc = _serverTanks.Count;
        int ec = _serverEnemies.Count;

        // Buffer:  4 + pc*20 + 4 + ec*24 + 20 (eagle: hp+px+py+w+h) + 64 safety
        int bufSize = 88 + pc * 20 + ec * 24;
        using var writer = new FastBufferWriter(bufSize, Allocator.Temp);

        // ── Players ────────────────────────────────────────────────────────
        writer.WriteValueSafe(pc);
        for (int i = 0; i < pc; i++)
        {
            var t = _serverTanks[i];
            if (t == null)
            {
                writer.WriteValueSafe(0f); writer.WriteValueSafe(0f);
                writer.WriteValueSafe(0f); writer.WriteValueSafe(0f);
                writer.WriteValueSafe(0);
                continue;
            }
            Damagable d = t.GetComponentInChildren<Damagable>();
            // Use TankMover's rb2d.transform position so we track the physics body
            // even when the Rigidbody2D lives on a child rather than the tank root.
            TankMover mover = t.tankMover;
            Vector2 pos = (mover != null && mover.rb2d != null)
                ? (Vector2)mover.rb2d.transform.position
                : (Vector2)t.transform.position;
            float bodyRot = (mover != null && mover.rb2d != null)
                ? mover.rb2d.transform.eulerAngles.z
                : t.transform.eulerAngles.z;
            float turretRot = t.aimTurret != null ? t.aimTurret.transform.eulerAngles.z : 0f;
            writer.WriteValueSafe(pos.x);
            writer.WriteValueSafe(pos.y);
            writer.WriteValueSafe(bodyRot);
            writer.WriteValueSafe(turretRot);
            writer.WriteValueSafe(d != null ? Mathf.RoundToInt(d.Health) : 0);
        }

        // ── Enemies ────────────────────────────────────────────────────────
        writer.WriteValueSafe(ec);
        for (int i = 0; i < ec; i++)
        {
            var e = _serverEnemies[i];
            if (e == null)
            {
                writer.WriteValueSafe(i);
                writer.WriteValueSafe(0f); writer.WriteValueSafe(0f);
                writer.WriteValueSafe(0f); writer.WriteValueSafe(0f);
                writer.WriteValueSafe(0);
                continue;
            }

            // Use cached components; fall back to GetComponent only if cache is stale.
            Damagable d   = (i < _enemyDamagables?.Length) ? _enemyDamagables[i] : e.GetComponentInChildren<Damagable>();
            AimTurret aim = (i < _enemyAimTurrets?.Length) ? _enemyAimTurrets[i] : e.GetComponentInChildren<AimTurret>();
            TankMover mv  = (i < _enemyMovers?.Length)     ? _enemyMovers[i]     : e.GetComponentInChildren<TankMover>();

            // Read the position from the physics body (rb2d.transform), not the enemy root,
            // because the Rigidbody2D may live on a child of the enemy prefab.
            Vector2 pos;
            float bodyRot;
            if (mv != null && mv.rb2d != null)
            {
                pos     = mv.rb2d.transform.position;
                bodyRot = mv.rb2d.transform.eulerAngles.z;
            }
            else
            {
                pos     = e.transform.position;
                bodyRot = e.transform.eulerAngles.z;
            }
            float turretRot = aim != null ? aim.transform.eulerAngles.z : 0f;
            int   hp        = d   != null ? Mathf.RoundToInt(d.Health)  : 0;

            writer.WriteValueSafe(i);
            writer.WriteValueSafe(pos.x);
            writer.WriteValueSafe(pos.y);
            writer.WriteValueSafe(bodyRot);
            writer.WriteValueSafe(turretRot);
            writer.WriteValueSafe(hp);
        }

        // ── Eagle ──────────────────────────────────────────────────────────
        if (_eagleCache == null) _eagleCache = GameObject.Find("EagleBase");
        int eagleHp = 0; float eaglePx = 0f, eaglePy = 0f;
        // Send the eagle's actual world-space sprite bounds so the client ghost
        // can be sized to exactly match the server-side visual.
        float eagleW = 1.3f, eagleH = 1.3f;
        if (_eagleCache != null)
        {
            Damagable d = _eagleCache.GetComponent<Damagable>();
            if (d != null) eagleHp = Mathf.RoundToInt(d.Health);
            eaglePx = _eagleCache.transform.position.x;
            eaglePy = _eagleCache.transform.position.y;
            var sr = _eagleCache.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                eagleW = sr.bounds.size.x;
                eagleH = sr.bounds.size.y;
            }
        }
        writer.WriteValueSafe(eagleHp);
        writer.WriteValueSafe(eaglePx);
        writer.WriteValueSafe(eaglePy);
        writer.WriteValueSafe(eagleW);
        writer.WriteValueSafe(eagleH);

        // Use Reliable delivery — avoids any packet-loss or sequencing edge-cases
        // on the LAN.  At 30 Hz the overhead is negligible.
        mgr.SendNamedMessageToAll(MsgWorldState, writer, NetworkDelivery.Reliable);
    }

    // ── Host input (called every frame from LanNetworkBridge when IsServer) ─────
    //
    // Route qua đây thay vì dùng _serverTank trên bridge trực tiếp để đảm bảo
    // input của host CHỈ đến _serverTanks[0] (tank của host), không bao giờ nhầm sang slot khác.

    public void ApplyHostInput(Vector2 move, Vector2 mouseWorldPos, bool shoot)
    {
        if (_serverTanks.Count == 0 || _serverTanks[0] == null) return;
        var tank = _serverTanks[0];
        tank.HandleMoveWorldDirection(move);
        // Compute angle from the host's own tank turret position to mouse world pos.
        // Same approach as the client-side ComputeTurretAngle — decouples from camera.
        if (tank.aimTurret != null)
        {
            Vector2 dir = mouseWorldPos - (Vector2)tank.aimTurret.transform.position;
            if (dir.sqrMagnitude > 0.001f)
                tank.aimTurret.transform.rotation =
                    Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }
        if (shoot) tank.HandleShoot();
    }

    // ── Player-death tracking (called by MapTankTestBootstrap per player slot) ──
    //
    // Individual player deaths do NOT end the game.  Game over only triggers when
    // _alivePlayerCount reaches 0 (all tanks destroyed) or eagle is shot down.

    public void OnPlayerTankDead(int slot)
    {
        _alivePlayerCount = Mathf.Max(0, _alivePlayerCount - 1);
        Debug.Log($"[Coordinator] Player slot {slot} died. Alive: {_alivePlayerCount}");
        if (_alivePlayerCount == 0)
        {
            // Use BeginGameOver() instead of BroadcastGameOver() directly so the HOST
            // also sees the overlay. [ClientRpc] is only received by non-server clients;
            // the host would miss it if we only called BroadcastGameOver() here.
            MapGameOverController.Ensure().BeginGameOver();
        }
    }

    // ── Win / GameOver (reliable [ClientRpc]) ─────────────────────────────────

    public void BroadcastWin()      => Relay()?.BroadcastEventClientRpc(true);
    public void BroadcastGameOver() => Relay()?.BroadcastEventClientRpc(false);

    // ── Bridge helper ─────────────────────────────────────────────────────────

    private LanNetworkBridge Relay()
    {
        foreach (var b in _bridges)
            if (b != null && b.IsSpawned) return b;
        ScanBridges();
        foreach (var b in _bridges)
            if (b != null && b.IsSpawned) return b;
        return null;
    }
}
