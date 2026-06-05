using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CLIENT-ONLY singleton.  Receives server state via CustomMessagingManager and
/// drives display-only ghost copies of every tank and enemy.
///
/// World state arrives as raw bytes (MsgWorldState / MsgInitWorld) written by
/// LanGameCoordinator.BroadcastWorldState() / SendInitWorldMsg().
/// Bullet/explosion/win events still arrive via [ClientRpc] on LanNetworkBridge.
/// </summary>
public class LanClientView : MonoBehaviour
{
    public static LanClientView Instance { get; private set; }
    public Transform OwnGhost { get; private set; }

    private readonly List<Transform>    _playerGhosts = new List<Transform>();
    private readonly List<GameObject>   _bulletGhosts = new List<GameObject>();  // tracked so explosions can stop them
    private readonly List<Transform> _enemyGhosts  = new List<Transform>();
    private int _ownSlot = -1;
    private Transform _eagleGhost;

    // Interpolation targets for player ghosts (set each RPC, consumed in Update)
    private Vector3[]    _playerTargetPos;
    private Quaternion[] _playerTargetRot;
    // Authoritative turret world-rotation for non-own ghosts (re-applied every Update
    // after body-rotation lerp, which would otherwise drag the turret child along).
    private float[]      _playerTargetTurretRot;
    // Own-ghost server correction target
    private Vector3    _ownTargetPos;
    private Quaternion _ownTargetRot;
    private bool       _ownTargetSet;

    // Movement data for client-side prediction (loaded once from Resources)
    private TankMovementData _moveData;
    private bool             _moveDataLoaded;

    private Slider _ownHpSlider;
    private Slider _eagleHpSlider;
    private Text   _enemyCountText;
    private const int OwnMaxHp   = 20;
    private const int EagleMaxHp = 500;

    // Spectator mode — set true the first time own HP reaches 0.
    public  bool IsSpectating { get; private set; }
    private bool _ownDied;          // latched so we only react to the first death
    private int  _ownPrevHp = -1;   // tracks previous HP to detect the death transition

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        RegisterMessagingHandlers();
    }

    private void OnDestroy()
    {
        var mgr = NetworkManager.Singleton?.CustomMessagingManager;
        if (mgr != null)
        {
            mgr.UnregisterNamedMessageHandler(LanGameCoordinator.MsgWorldState);
            mgr.UnregisterNamedMessageHandler(LanGameCoordinator.MsgInitWorld);
        }
        if (Instance == this) Instance = null;
    }

    // Register CustomMessaging handlers as early as possible (Awake).
    private void RegisterMessagingHandlers()
    {
        var mgr = NetworkManager.Singleton?.CustomMessagingManager;
        if (mgr == null) return;  // NetworkManager not ready yet — Start() will retry.
        mgr.RegisterNamedMessageHandler(LanGameCoordinator.MsgInitWorld,  OnReceiveInitWorld);
        mgr.RegisterNamedMessageHandler(LanGameCoordinator.MsgWorldState, OnReceiveWorldState);
    }

    private void Start()
    {
        // Fallback: if NetworkManager wasn't ready in Awake, register now.
        var mgr = NetworkManager.Singleton?.CustomMessagingManager;
        if (mgr == null) return;
        // Unregister first to avoid duplicate handlers, then re-register.
        mgr.UnregisterNamedMessageHandler(LanGameCoordinator.MsgInitWorld);
        mgr.UnregisterNamedMessageHandler(LanGameCoordinator.MsgWorldState);
        mgr.RegisterNamedMessageHandler(LanGameCoordinator.MsgInitWorld,  OnReceiveInitWorld);
        mgr.RegisterNamedMessageHandler(LanGameCoordinator.MsgWorldState, OnReceiveWorldState);
    }

    // ── Custom message handlers ───────────────────────────────────────────────

    // MsgInitWorld packet:
    //   int playerCount
    //   int enemyCount
    //   ulong ownerClientId[0..playerCount-1]  (which client owns each slot)
    private void OnReceiveInitWorld(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int playerCount);
        reader.ReadValueSafe(out int enemyCount);

        // Determine own slot from the ownership map embedded in the message.
        ulong myClientId = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClientId
            : ulong.MaxValue;
        int ownSlot = Mathf.Min(1, playerCount - 1); // safe default (slot 1 for 2-player)
        for (int i = 0; i < playerCount; i++)
        {
            reader.ReadValueSafe(out ulong ownerClientId);
            if (ownerClientId == myClientId) ownSlot = i;
        }

        Debug.Log($"[LanClientView] OnReceiveInitWorld pc={playerCount} ec={enemyCount} ownSlot={ownSlot}");

        // Skip if ghost counts and own-slot are already correct — avoids
        // an unnecessary ghost-destroy/recreate cycle when the server
        // re-sends init after a late-spawning client bridge is linked.
        if (_playerGhosts.Count == playerCount
            && _enemyGhosts.Count == enemyCount
            && _ownSlot == ownSlot)
            return;

        InitGhosts(playerCount, enemyCount, ownSlot);
    }

    // WsPlayer / WsEnemy: temporary structs to buffer all parsed data before applying.
    private struct WsPlayer { public float px, py, bodyRot, turretRot; public int hp; }
    private struct WsEnemy  { public int idx; public float px, py, bodyRot, turretRot; public int hp; }

    // MsgWorldState packet:
    //   int playerCount
    //   per player: float px,py,bodyRot,turretRot; int hp
    //   int enemyCount
    //   per enemy:  int idx; float px,py,bodyRot,turretRot; int hp
    //   int eagleHp; float eaglePx, eaglePy
    private void OnReceiveWorldState(ulong senderId, FastBufferReader reader)
    {
        // ── 1. Read ALL data from the buffer first ──────────────────────────
        reader.ReadValueSafe(out int pc);
        var players = new WsPlayer[pc];
        for (int i = 0; i < pc; i++)
        {
            reader.ReadValueSafe(out players[i].px);
            reader.ReadValueSafe(out players[i].py);
            reader.ReadValueSafe(out players[i].bodyRot);
            reader.ReadValueSafe(out players[i].turretRot);
            reader.ReadValueSafe(out players[i].hp);
        }

        reader.ReadValueSafe(out int ec);
        var enemies = new WsEnemy[ec];
        for (int i = 0; i < ec; i++)
        {
            reader.ReadValueSafe(out enemies[i].idx);
            reader.ReadValueSafe(out enemies[i].px);
            reader.ReadValueSafe(out enemies[i].py);
            reader.ReadValueSafe(out enemies[i].bodyRot);
            reader.ReadValueSafe(out enemies[i].turretRot);
            reader.ReadValueSafe(out enemies[i].hp);
        }

        reader.ReadValueSafe(out int eagleHp);
        reader.ReadValueSafe(out float eaglePx);
        reader.ReadValueSafe(out float eaglePy);
        reader.ReadValueSafe(out float eagleW);
        reader.ReadValueSafe(out float eagleH);

        // ── 2. Init ghosts if counts don't match yet ────────────────────────
        if (pc > 0 && (_playerGhosts.Count != pc || _enemyGhosts.Count != ec))
            InitGhosts(pc, ec);  // fallback: let InitGhosts look up slot from bridge

        // ── 3. Apply player states ──────────────────────────────────────────
        for (int i = 0; i < pc; i++)
        {
            var p = players[i];
            if (i == _ownSlot)
            {
                // Own ghost: set server-correction target (blended in Update).
                _ownTargetPos = new Vector3(p.px, p.py, 0f);
                _ownTargetRot = Quaternion.Euler(0f, 0f, p.bodyRot);
                _ownTargetSet = true;
                // Do NOT override OwnGhost turret from server state — prediction in
                // LanNetworkBridge.Update (PredictTurretAim) already handles this every
                // frame. Applying server state at 30 Hz would cause visible snapping.

                // HP bar: normalize raw int HP against known max.
                if (_ownHpSlider != null)
                    _ownHpSlider.value = (float)p.hp / OwnMaxHp;

                // Death detection — trigger spectator mode on the first hp=0 transition.
                if (!_ownDied && _ownPrevHp > 0 && p.hp <= 0)
                {
                    _ownDied     = true;
                    IsSpectating = true;
                    ShowDeadOverlay();
                }
                _ownPrevHp = p.hp;
            }
            else if (i < _playerGhosts.Count && _playerGhosts[i] != null)
            {
                if (_playerTargetPos != null && i < _playerTargetPos.Length)
                {
                    _playerTargetPos[i] = new Vector3(p.px, p.py, 0f);
                    _playerTargetRot[i] = Quaternion.Euler(0f, 0f, p.bodyRot);
                }
                else
                {
                    _playerGhosts[i].position = new Vector3(p.px, p.py, 0f);
                    _playerGhosts[i].rotation = Quaternion.Euler(0f, 0f, p.bodyRot);
                }
                // Store authoritative turret rotation; Update() re-applies it every frame
                // to counteract body-rotation interpolation dragging the turret child.
                if (_playerTargetTurretRot != null && i < _playerTargetTurretRot.Length)
                    _playerTargetTurretRot[i] = p.turretRot;
                SetTurretRot(_playerGhosts[i], p.turretRot);
            }
        }

        // ── 4. Apply enemy states ───────────────────────────────────────────
        int alive = 0;
        for (int i = 0; i < ec; i++)
        {
            var e = enemies[i];
            if (e.idx < 0 || e.idx >= _enemyGhosts.Count) continue;
            Transform g = _enemyGhosts[e.idx];
            if (g == null) continue;

            if (e.hp <= 0)
            {
                if (g.gameObject.activeSelf) g.gameObject.SetActive(false);
            }
            else
            {
                alive++;
                if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);
                g.position = new Vector3(e.px, e.py, 0f);
                g.rotation = Quaternion.Euler(0f, 0f, e.bodyRot);
                SetTurretRot(g, e.turretRot);
            }
        }

        // ── 5. Eagle ────────────────────────────────────────────────────────
        var eaglePos = new Vector2(eaglePx, eaglePy);
        if (eagleW <= 0f) eagleW = 1.3f;
        if (eagleH <= 0f) eagleH = 1.3f;
        if (_eagleGhost == null && eagleHp > 0 && eaglePos != Vector2.zero)
        {
            _eagleGhost = SpawnEagleGhost(eaglePos, eagleW, eagleH);
        }
        else if (_eagleGhost != null)
        {
            // Sync position in case eagle moved (normally static, but stays correct).
            _eagleGhost.position    = new Vector3(eaglePx, eaglePy, 0f);
            // Apply scale if it differs (handles first-packet size correction).
            var want = new Vector3(eagleW, eagleH, 1f);
            if (_eagleGhost.localScale != want) _eagleGhost.localScale = want;
        }
        if (_eagleHpSlider  != null) _eagleHpSlider.value   = (float)eagleHp / EagleMaxHp;
        if (_enemyCountText != null) _enemyCountText.text    = $"ENEMY: {alive}";
    }

    // ── Update: interpolate ghosts toward server-authoritative positions ──────

    private void Update()
    {
        const float Speed = 25f;
        float t = Mathf.Min(1f, Speed * Time.deltaTime);

        // Own ghost: gentle server correction that doesn't fight client prediction.
        if (_ownTargetSet && OwnGhost != null)
        {
            float sqDist = Vector3.SqrMagnitude(OwnGhost.position - _ownTargetPos);
            if (sqDist > 16f)
            {
                // Large error (teleport / reconnect) — hard snap
                OwnGhost.position = _ownTargetPos;
                OwnGhost.rotation = _ownTargetRot;
            }
            else if (sqDist > 0.09f)
            {
                // Gentle correction at 5 Hz so prediction still feels responsive
                OwnGhost.position = Vector3.Lerp(OwnGhost.position, _ownTargetPos, 5f * Time.deltaTime);
            }
            // Within 0.3 units: trust local prediction — no correction needed
        }

        // Other player ghosts: smooth interpolation toward received target
        if (_playerTargetPos != null)
        {
            for (int i = 0; i < _playerGhosts.Count && i < _playerTargetPos.Length; i++)
            {
                if (i == _ownSlot || _playerGhosts[i] == null) continue;
                float sq = Vector3.SqrMagnitude(_playerGhosts[i].position - _playerTargetPos[i]);
                if (sq > 16f)
                    _playerGhosts[i].position = _playerTargetPos[i];  // hard snap on large gap
                else
                    _playerGhosts[i].position = Vector3.Lerp(_playerGhosts[i].position, _playerTargetPos[i], t);
                _playerGhosts[i].rotation = Quaternion.Lerp(_playerGhosts[i].rotation, _playerTargetRot[i], t);
                // Re-apply turret world rotation AFTER body lerp — body rotation drags
                // the turret child, making it appear to follow the wrong player's aim.
                if (_playerTargetTurretRot != null && i < _playerTargetTurretRot.Length)
                    SetTurretRot(_playerGhosts[i], _playerTargetTurretRot[i]);
            }
        }

        // Enemy positions are set directly in OnReceiveWorldState at 30 Hz —
        // the per-tick delta is tiny, so no additional lerp is needed.
    }

    // ── Ghost initialisation ──────────────────────────────────────────────────

    /// <summary>
    /// Creates or recreates all display-only ghosts.
    /// <paramref name="forcedOwnSlot"/> = the slot this client owns, as determined
    /// from the MsgInitWorld ownership map.  Pass -1 to fall back to NetworkVariable lookup.
    /// </summary>
    public void InitGhosts(int playerCount, int enemyCount, int forcedOwnSlot = -1)
    {
        foreach (var g in _playerGhosts) if (g != null) Destroy(g.gameObject);
        foreach (var g in _enemyGhosts)  if (g != null) Destroy(g.gameObject);
        foreach (var b in _bulletGhosts) if (b != null) Destroy(b);
        _playerGhosts.Clear();
        _enemyGhosts.Clear();
        _bulletGhosts.Clear();
        _ownSlot      = -1;
        OwnGhost      = null;
        _ownTargetSet = false;
        IsSpectating  = false;
        _ownDied      = false;
        _ownPrevHp    = -1;

        // Determine own slot ──────────────────────────────────────────────────
        if (forcedOwnSlot >= 0)
        {
            // Preferred path: slot was determined from the ownership map in MsgInitWorld.
            _ownSlot = forcedOwnSlot;
        }
        else
        {
            // Fallback: read the Slot NetworkVariable from the bridge that this client owns.
            // This can race with NetworkVariable replication, so it may return -1 initially.
            var bridges = FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None);
            foreach (var b in bridges)
                if (b.IsOwner && b.Slot.Value >= 0) { _ownSlot = b.Slot.Value; break; }
            if (_ownSlot < 0) _ownSlot = Mathf.Min(1, playerCount - 1);
        }

        // Spawn ghosts ─────────────────────────────────────────────────────────
        Color[] slotColors =
        {
            Color.cyan, Color.yellow, Color.magenta, Color.green,
            Color.white, Color.red, new Color(1f, 0.5f, 0f), Color.blue
        };

        GameObject tankPrefab  = Resources.Load<GameObject>("Prefabs/Tank");
        GameObject enemyPrefab = Resources.Load<GameObject>("Prefabs/StaticEnemy");

        for (int i = 0; i < playerCount; i++)
        {
            Color     col = i < slotColors.Length ? slotColors[i] : Color.white;
            Transform g   = SpawnDisplay(tankPrefab, $"GhostPlayer_{i}", 1.3f, col);
            _playerGhosts.Add(g);
            if (i == _ownSlot) OwnGhost = g;
        }

        for (int i = 0; i < enemyCount; i++)
            _enemyGhosts.Add(SpawnDisplay(enemyPrefab, $"GhostEnemy_{i}", 0.72f, Color.white));

        _playerTargetPos       = new Vector3[playerCount];
        _playerTargetRot       = new Quaternion[playerCount];
        _playerTargetTurretRot = new float[playerCount];
        for (int i = 0; i < playerCount; i++) _playerTargetRot[i] = Quaternion.identity;

        BuildHud();
        Debug.Log($"[LanClientView] InitGhosts: {playerCount} players / {enemyCount} enemies / ownSlot={_ownSlot} / OwnGhost={(OwnGhost != null ? "set" : "NULL")}");
    }

    private static Transform SpawnDisplay(GameObject prefab, string goName, float scale, Color fallback)
    {
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab);
            go.name = goName;
            go.transform.localScale = Vector3.one * scale;
            StripToDisplayOnly(go);
            return go.transform;
        }
        return MakeSquareGhost(goName, fallback, scale);
    }

    /// <summary>
    /// Removes all physics and disables every MonoBehaviour so nothing fights our position sync.
    /// SpriteRenderer inherits from Renderer (not MonoBehaviour) and is unaffected.
    /// </summary>
    private static void StripToDisplayOnly(GameObject go)
    {
        foreach (var col in go.GetComponentsInChildren<Collider2D>(true))
        { col.enabled = false; Object.Destroy(col); }

        foreach (var rb in go.GetComponentsInChildren<Rigidbody2D>(true))
        { rb.bodyType = RigidbodyType2D.Kinematic; rb.simulated = false; Object.Destroy(rb); }

        foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
            src.enabled = false;

        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) mb.enabled = false;
    }

    private static Transform MakeSquareGhost(string name, Color color, float size)
    {
        var go  = new GameObject(name);
        var sr  = go.AddComponent<SpriteRenderer>();
        sr.sprite = MakeSquareSprite();
        sr.color  = color;
        sr.sortingLayerName = "Player";
        go.transform.localScale = Vector3.one * size;
        return go.transform;
    }

    // ── Player state (from targeted ReceiveTankStateClientRpc) ───────────────

    public void ApplyOwnTankState(LanTankState state)
    {
        if (OwnGhost == null) return;
        _ownTargetPos = new Vector3(state.pos.x, state.pos.y, 0f);
        _ownTargetRot = Quaternion.Euler(0f, 0f, state.bodyRot);
        _ownTargetSet = true;
        SetTurretRot(OwnGhost, state.turretRot);
        if (_ownHpSlider != null)
            _ownHpSlider.value = (float)state.hp / OwnMaxHp;
    }

    // ── Client-side prediction ────────────────────────────────────────────────

    /// <summary>
    /// Called every frame from LanNetworkBridge (owner-client only) to move OwnGhost
    /// immediately using local input, matching the world-direction movement the server uses.
    /// Server correction from OnReceiveWorldState provides gentle drift correction.
    /// </summary>
    public void PredictOwnMovement(Vector2 move)
    {
        if (OwnGhost == null) return;
        if (move.sqrMagnitude < 0.01f) return;

        if (!_moveDataLoaded)
        {
            _moveDataLoaded = true;
            _moveData = Resources.Load<TankMovementData>("Data/PlayerTankMovementData");
        }
        if (_moveData == null) return;

        // Mirror TankMover.MoveWorldDirection physics:
        //   rb2d.linearVelocity = worldVector * maxSpeed * fixedDeltaTime
        //   displacement per render frame = velocity * deltaTime
        float effectiveSpeed = _moveData.maxSpeed * Time.fixedDeltaTime;
        Vector2 dir = move.magnitude > 1f ? move.normalized : move;
        OwnGhost.position += new Vector3(dir.x, dir.y, 0f) * effectiveSpeed * Time.deltaTime;

        // Rotate body toward movement direction (same logic as MoveWorldDirection)
        float targetAngle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = OwnGhost.eulerAngles.z;
        float newAngle     = Mathf.MoveTowardsAngle(currentAngle, targetAngle,
            _moveData.rotationSpeed * Time.deltaTime);
        OwnGhost.rotation = Quaternion.Euler(0f, 0f, newAngle);
    }

    /// <summary>
    /// Client-side prediction cho turret — xoay ghost ngay lập tức,
    /// tránh delay 30 Hz round-trip qua server.
    /// </summary>
    public void PredictTurretAim(Vector2 mouseWorldPos)
    {
        if (OwnGhost == null) return;
        AimTurret aim = OwnGhost.GetComponentInChildren<AimTurret>(true);
        if (aim == null) return;
        Vector2 dir = mouseWorldPos - (Vector2)aim.transform.position;
        if (dir.sqrMagnitude < 0.001f) return;
        // Phải khớp với AimTurret.Aim() phía server: Atan2(y,x)*Rad2Deg (không trừ 90°).
        // Trừ 90° là quy ước cho thân tank (tank body mặc định nhìn lên +Y),
        // nhưng AimTurret dùng atan2 trực tiếp — nên prediction phải dùng giống vậy.
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        aim.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ── World state (legacy public API kept for compatibility) ────────────────

    public void ApplyWorldState(LanTankState[] playerStates, LanEnemyState[] enemyStates, int eagleHp, Vector2 eaglePos)
    {
        // This method is no longer called (world state arrives via CustomMessaging now).
        // Kept as a stub so any stale call sites don't cause compile errors.
    }

    // ── Bullet (event-based, autonomous) ─────────────────────────────────────

    public void SpawnMovingBullet(Vector2 pos, Vector2 dir, float speed, float maxDist)
    {
        GameObject bulletPrefab = Resources.Load<GameObject>("Prefabs/Bullet");
        GameObject go;
        if (bulletPrefab != null)
        {
            go      = Object.Instantiate(bulletPrefab);
            go.name = "BulletGhost";
            StripToDisplayOnly(go);
        }
        else
        {
            go = new GameObject("BulletGhost");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite           = MakeSquareSprite();
            sr.color            = new Color(1f, 0.85f, 0.1f);
            sr.sortingLayerName = "Bullets";
            sr.sortingOrder     = 5;
            go.transform.localScale = new Vector3(0.12f, 0.3f, 1f);
        }
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.transform.up       = new Vector3(dir.x, dir.y, 0f);

        // Register before starting so StopNearestBulletGhost can find it.
        _bulletGhosts.Add(go);
        StartCoroutine(MoveBulletRoutine(go, dir.normalized, speed, maxDist));
    }

    // Non-static so it can remove the bullet from _bulletGhosts when it expires naturally.
    private IEnumerator MoveBulletRoutine(GameObject bullet, Vector2 dir, float speed, float maxDist)
    {
        float traveled = 0f;
        while (bullet != null && traveled < maxDist)
        {
            float step = speed * Time.deltaTime;
            bullet.transform.position += new Vector3(dir.x * step, dir.y * step, 0f);
            traveled += step;
            yield return null;
        }
        if (bullet != null)
        {
            _bulletGhosts.Remove(bullet);
            Destroy(bullet);
        }
    }

    // ── Explosion effect (event-based, immediate) ─────────────────────────────

    public void SpawnExplosion(Vector2 pos)
    {
        // Stop the bullet ghost that caused this explosion.
        // The ghost should be very close to the hit point (same speed, very low LAN latency).
        StopNearestBulletGhost(pos);

        GameObject explosionPrefab = Resources.Load<GameObject>("Prefabs/Explosion");
        if (explosionPrefab != null)
        {
            GameObject go = Object.Instantiate(explosionPrefab);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            foreach (var mb in go.GetComponentsInChildren<DestroyIfDisabled>(true))
                mb.enabled = false;
        }
    }

    // Find and destroy the bullet ghost nearest to the explosion position.
    // Uses a generous search radius (3 units) to account for LAN latency drift.
    private void StopNearestBulletGhost(Vector2 explosionPos)
    {
        const float SearchRadius = 3f;
        _bulletGhosts.RemoveAll(b => b == null);  // purge any already-destroyed ghosts

        GameObject nearest  = null;
        float      nearestD = SearchRadius;

        foreach (var b in _bulletGhosts)
        {
            float d = Vector2.Distance(b.transform.position, explosionPos);
            if (d < nearestD) { nearestD = d; nearest = b; }
        }

        if (nearest != null)
        {
            _bulletGhosts.Remove(nearest);
            Destroy(nearest);
        }
    }

    // ── Eagle ghost ───────────────────────────────────────────────────────────

    // sizeW/sizeH = server eagle's SpriteRenderer.bounds.size (world-space).
    // MakeSquareSprite() is 1×1 world unit at scale (1,1,1), so setting
    // localScale = (sizeW, sizeH) makes the ghost exactly match the real eagle.
    private static Transform SpawnEagleGhost(Vector2 pos, float sizeW = 1.3f, float sizeH = 1.3f)
    {
        var go = new GameObject("GhostEagle");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite           = MakeSquareSprite();
        sr.color            = new Color(1f, 0.85f, 0.15f);
        sr.sortingLayerName = "Eagle";
        sr.sortingOrder     = 10;
        go.transform.localScale = new Vector3(sizeW, sizeH, 1f);
        go.transform.position   = new Vector3(pos.x, pos.y, 0f);
        return go.transform;
    }

    // ── Win / GameOver ────────────────────────────────────────────────────────

    public void ApplyGameEvent(bool isWin)
    {
        if (isWin) MapWinController.Ensure().BeginWin();
        else       MapGameOverController.Ensure().BeginGameOver();
    }

    // ── Death overlay (shown when own tank's HP first reaches 0) ─────────────

    private void ShowDeadOverlay()
    {
        int L = LayerMask.NameToLayer("UI");

        var root = new GameObject("YouDiedOverlay"); root.layer = L;
        var cv   = root.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 80;
        var sc = root.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        root.AddComponent<GraphicRaycaster>();

        // Semi-transparent dark strip centred on screen.
        var bg = new GameObject("Bg"); bg.layer = L;
        bg.transform.SetParent(root.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.38f); bgRt.anchorMax = new Vector2(1f, 0.62f);
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        // "BẠN ĐÃ CHẾT" title.
        AddOverlayText(bg, "DeadTitle", "BẠN ĐÃ CHẾT",
            34, FontStyle.Bold, new Color(1f, 0.25f, 0.25f),
            new Vector2(0.5f, 0.7f));

        // Hint text.
        AddOverlayText(bg, "Hint", "Dùng WASD để kéo camera xem tiếp",
            16, FontStyle.Italic, new Color(0.85f, 0.85f, 0.85f),
            new Vector2(0.5f, 0.3f));

        // Auto-destroy after 4 seconds.
        Destroy(root, 4f);
    }

    private static void AddOverlayText(GameObject parent, string goName,
        string content, int size, FontStyle style, Color color, Vector2 anchorCenter)
    {
        int L = parent.layer;
        var go = new GameObject(goName); go.layer = L;
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchorCenter;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(700f, 50f);
        var t = go.AddComponent<Text>();
        t.text      = content;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = size;
        t.fontStyle = style;
        t.color     = color;
        t.alignment = TextAnchor.MiddleCenter;
    }

    // ── Turret rotation helper ────────────────────────────────────────────────

    private static void SetTurretRot(Transform root, float rot)
    {
        AimTurret aim = root.GetComponentInChildren<AimTurret>(true);
        if (aim != null) aim.transform.rotation = Quaternion.Euler(0f, 0f, rot);
    }

    // ── HUD builder ──────────────────────────────────────────────────────────

    private void BuildHud()
    {
        _ownHpSlider   = FindOrBuildBar("PlayerHealthHud", "HealthBar", "HP",   new Vector2(76f, -24f), Color.red);
        _eagleHpSlider = FindOrBuildBar("EagleHealthHud",  "HealthBar", "BASE", new Vector2(76f, -48f), Color.yellow);
        _enemyCountText = FindOrBuildText("EnemyCountHud", "EnemyCountText", new Vector2(24f, -72f));
    }

    private static Slider FindOrBuildBar(string canvasName, string barName,
        string label, Vector2 barPos, Color fillColor)
    {
        var existing = GameObject.Find(canvasName);
        if (existing != null)
        {
            var bar = existing.transform.Find(barName);
            if (bar != null && bar.TryGetComponent<Slider>(out var sl)) return sl;
        }

        var root = BuildOverlayCanvas(canvasName);
        AddLabel(root.transform, label, new Vector2(24f, barPos.y));

        var barGo = new GameObject(barName); barGo.layer = root.layer;
        barGo.transform.SetParent(root.transform, false);
        var barRect = barGo.AddComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = new Vector2(0f, 1f);
        barRect.pivot = new Vector2(0f, 1f);
        barRect.anchoredPosition = barPos;
        barRect.sizeDelta = new Vector2(120f, 18f);
        var bg = barGo.AddComponent<Image>(); bg.color = Color.black;

        var fillGo = new GameObject("Fill"); fillGo.layer = root.layer;
        fillGo.transform.SetParent(barGo.transform, false);
        var fRect = fillGo.AddComponent<RectTransform>();
        fRect.anchorMin = Vector2.zero; fRect.anchorMax = Vector2.one;
        fRect.offsetMin = fRect.offsetMax = Vector2.zero;
        fillGo.AddComponent<Image>().color = fillColor;

        var slider = barGo.AddComponent<Slider>();
        slider.interactable = false; slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f;
        slider.targetGraphic = bg; slider.fillRect = fRect.GetComponent<RectTransform>();
        return slider;
    }

    private static Text FindOrBuildText(string canvasName, string textName, Vector2 pos)
    {
        var existing = GameObject.Find(canvasName);
        if (existing != null)
        {
            var t = existing.transform.Find(textName);
            if (t != null && t.TryGetComponent<Text>(out var tx)) return tx;
        }

        var root = BuildOverlayCanvas(canvasName);
        var tGo  = new GameObject(textName); tGo.layer = root.layer;
        tGo.transform.SetParent(root.transform, false);
        var tRect = tGo.AddComponent<RectTransform>();
        tRect.anchorMin = tRect.anchorMax = new Vector2(0f, 1f);
        tRect.pivot = new Vector2(0f, 1f);
        tRect.anchoredPosition = pos; tRect.sizeDelta = new Vector2(172f, 18f);
        var text = tGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14; text.color = Color.white; text.alignment = TextAnchor.MiddleLeft;
        return text;
    }

    private static GameObject BuildOverlayCanvas(string goName)
    {
        var go = new GameObject(goName); go.layer = LayerMask.NameToLayer("UI");
        var cv = go.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 20;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f); sc.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        go.AddComponent<CanvasGroup>().alpha = 0.9f;
        return go;
    }

    private static void AddLabel(Transform parent, string labelText, Vector2 pos)
    {
        var go = new GameObject("Label"); go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = pos; r.sizeDelta = new Vector2(46f, 18f);
        var t = go.AddComponent<Text>();
        t.text = labelText; t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 14; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
    }

    // ── Sprite helpers ────────────────────────────────────────────────────────

    private static Sprite MakeSquareSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white); tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
    }
}
