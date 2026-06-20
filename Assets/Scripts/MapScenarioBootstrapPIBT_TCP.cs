using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Bootstrap cho Mode 3: PIBT TCP.
/// Spawn enemies giống MapScenarioBootstrapPIBT, nhưng mọi pathfinding được
/// giao cho C++ PIBT server qua PIBTTcpClient.
///
/// Flow:
///   1. SpawnScenario() được gọi sau khi map đã load.
///   2. Kết nối server, gửi "hello" với toàn bộ map.
///   3. Mỗi tcpTickInterval giây: gửi "plan_step" với vị trí + hướng + goal,
///      nhận action cho từng enemy, truyền target kế tiếp vào GridEnemyAgentPIBT_TCP.
/// </summary>
public class MapScenarioBootstrapPIBT_TCP : MonoBehaviour
{
    private const string WallLayerName                  = "Walls";
    private const string AgentBlockerLayerName          = "AgentBlocker";
    private const string LegacyMovementObstacleLayerName = "ObstaclesMovement";

    // ── Inspector ──────────────────────────────────────────────────────────
    public MapLoader mapLoader;

    [Header("TCP Server")]
    public string serverHost          = "110.172.28.110";
    public int    serverPort          = 7777;
    [Min(0.05f)]
    [Tooltip("Khoảng thời gian (giây) giữa các lần gọi server. 0.5s ≈ 2 tick/s.")]
    public float  tcpTickInterval     = 0.5f;

    [Header("Eagle Base")]
    public Vector2Int eagleCell                 = new Vector2Int(16, 16);
    public int        eagleHealth               = 500;
    public Sprite     eagleSprite;
    public Color      eagleColor                = Color.white;
    public Vector2    eagleColliderSize         = new Vector2(1.3f, 1.3f);
    public bool       spawnEagleNearPlayer      = true;
    [Min(1)] public int eagleMinPlayerDistanceCells = 3;
    [Min(1)] public int eagleMaxPlayerDistanceCells = 7;

    [Header("Enemies")]
    public GameObject      enemyPrefab;
    public string          enemyPrefabPath       = "Assets/Prefabs/StaticEnemy.prefab";
    public TankMovementData enemyMovementData;
    public string          enemyMovementDataPath = "Assets/Data/TankData/EnemyTankMovementData.asset";
    public List<Vector2Int> enemySpawnCells      = new List<Vector2Int>
    {
        new Vector2Int(30,  1),
        new Vector2Int( 1, 30),
        new Vector2Int(30, 30),
        new Vector2Int(16,  1)
    };
    public bool disableLegacyEnemyAI  = true;
    [Range(0.4f, 1.2f)] public float enemyScale = 0.72f;
    public float enemyReplanInterval            = 0.75f;
    public float enemyEagleShootingRange        = 5f;
    public float enemyPlayerShootingRange       = 7f;
    [Min(1)] public int enemyMaxHealth          = 20;

    [Header("Stuck recovery")]
    [SerializeField, Min(0f)] private float scuffTimeout = 0.4f;
    [SerializeField] private LayerMask obstacleContactMask;

    // ── Runtime ────────────────────────────────────────────────────────────
    private Transform       scenarioRoot;
    private GameObject      eagleBase;
    private int             enemiesAlive;
    private Text            enemyCountText;
    private bool            winTriggered;

    private PIBTTcpClient              _client;
    private readonly List<GridEnemyAgentPIBT_TCP> _agents = new();
    private readonly List<int> _agentOrientations = new();
    private string _sessionId;
    private int   _frame;
    private float _nextTickTime;
    private bool  _serverReady;

    public GameObject            EagleBase => eagleBase;
    public IReadOnlyList<GameObject> Enemies => _enemyGOs;
    private readonly List<GameObject> _enemyGOs = new();

    // ═══════════════════════════════════════════════════════════════════════
    // ENTRY POINT (called by MapTankTestBootstrap.SpawnScenario)
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("Spawn Scenario Now")]
    public void SpawnScenario()
    {
        ResolveReferences();
        Debug.Log($"[PIBT_TCP] SpawnScenario requested. backtest={BacktestMode.IsActive}, algorithm='{BacktestMode.Algorithm}', endpoint={serverHost}:{serverPort}");
        if (mapLoader == null || mapLoader.BuildWidth <= 0 || mapLoader.BuildHeight <= 0)
        {
            Debug.LogError("[PIBT_TCP] Map must be loaded before spawning.");
            return;
        }

        ClearScenario();
        scenarioRoot = new GameObject("ScenarioRuntime_PIBT_TCP").transform;
        scenarioRoot.SetParent(transform, false);

        eagleBase = SpawnEagleBase();
        SpawnEnemies();
        Debug.Log($"[PIBT_TCP] Spawned TCP scenario objects. eagle={eagleBase != null}, agents={_agents.Count}, enemies={_enemyGOs.Count}");

        ConnectAndHello();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TCP
    // ═══════════════════════════════════════════════════════════════════════

    private void ConnectAndHello()
    {
        _client = GetComponent<PIBTTcpClient>() ?? gameObject.AddComponent<PIBTTcpClient>();
        _client.host = serverHost;
        _client.port = serverPort;
        Debug.Log($"[PIBT_TCP] ConnectAndHello start. endpoint={_client.host}:{_client.port}, agents={_agents.Count}, map={mapLoader.BuildWidth}x{mapLoader.BuildHeight}");

        if (!_client.Connect())
        {
            Debug.LogError("[PIBT_TCP] Could not connect to PIBT server at " +
                           serverHost + ":" + serverPort +
                           $". Reason: {_client.LastError}");
            ShowToast($"Không thể kết nối PIBT server\n({serverHost}:{serverPort})\nHãy kiểm tra server/domain trước khi vào game.", 4f);
            return;
        }

        int height = mapLoader.BuildHeight;
        int width  = mapLoader.BuildWidth;
        string symbols = BuildMapSymbols(width, height);
        _sessionId = $"unity-{System.Guid.NewGuid():N}";
        ResetAgentOrientations();

        if (!_client.Hello(_sessionId, width, height, symbols, _agents.Count))
        {
            Debug.LogError($"[PIBT_TCP] Server hello failed. Reason: {_client.LastError}");
            _client.Disconnect();
            return;
        }

        _serverReady   = true;
        _nextTickTime  = Time.time + tcpTickInterval;
        Debug.Log($"[PIBT_TCP] Connected and initialized. {_agents.Count} agents, map {width}×{height}.");
    }

    private void Update()
    {
        if (!_serverReady || _client == null || !_client.IsConnected) return;
        if (Time.time < _nextTickTime) return;
        _nextTickTime = Time.time + tcpTickInterval;
        DoStep();
    }

    private void DoStep()
    {
        int rows    = mapLoader.BuildHeight;
        int cols    = mapLoader.BuildWidth;
        int goalFlat = EagleFlat(rows, cols);

        var data = new (int id, int loc, int orientation, int goalLoc)[_agents.Count];
        for (int i = 0; i < _agents.Count; i++)
            data[i] = (i, AgentFlat(i, rows, cols), AgentOrientation(i), goalFlat);

        string[] actions = _client.PlanStep(_frame, _frame, data);
        _frame++;
        if (actions == null)
        {
            string reason = _client != null && !string.IsNullOrEmpty(_client.LastError)
                ? _client.LastError
                : "Unknown plan_step failure.";
            Debug.LogWarning($"[PIBT_TCP] Server plan_step failed — stopping agents. {reason}");
            _serverReady = false;
            foreach (var a in _agents)
                if (a != null) a.StopMovement();
            ShowToast("Mất kết nối PIBT server.\nAgent đã dừng.", 3f);
            return;
        }

        for (int i = 0; i < _agents.Count && i < actions.Length; i++)
        {
            if (_agents[i] == null) continue;
            Vector2Int nextCell = ApplyAction(i, actions[i], rows, cols);
            _agents[i].SetNextTarget(nextCell);
        }
    }

    private string BuildMapSymbols(int cols, int rows)
    {
        var sb = new System.Text.StringBuilder(cols * rows);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Vector2Int cell = new Vector2Int(
                    mapLoader.BuildStartX + c,
                    mapLoader.BuildStartY + r);
                sb.Append(mapLoader.IsWalkable(cell) ? '.' : '@');
            }
        }
        return sb.ToString();
    }

    private void ResetAgentOrientations()
    {
        _agentOrientations.Clear();
        for (int i = 0; i < _agents.Count; i++)
            _agentOrientations.Add(ReadAgentOrientation(_agents[i]));
    }

    private int AgentOrientation(int agentIdx)
    {
        while (_agentOrientations.Count <= agentIdx) _agentOrientations.Add(0);
        return _agentOrientations[agentIdx];
    }

    private Vector2Int ApplyAction(int agentIdx, string action, int rows, int cols)
    {
        int orientation = AgentOrientation(agentIdx);
        Vector2Int cell = _agents[agentIdx].CurrentCell;

        switch (action)
        {
            case "FW":
                Vector2Int delta = OrientationToDelta(orientation);
                Vector2Int next = cell + delta;
                if (IsInsideBuild(next, rows, cols) && mapLoader.IsWalkable(next))
                    return next;
                return cell;
            case "CR":
                _agentOrientations[agentIdx] = (orientation + 1) & 3;
                return cell;
            case "CCR":
                _agentOrientations[agentIdx] = (orientation + 3) & 3;
                return cell;
            case "W":
                return cell;
            default:
                Debug.LogWarning($"[PIBT_TCP] Unknown action '{action}' for agent {agentIdx}; waiting.");
                return cell;
        }
    }

    private bool IsInsideBuild(Vector2Int cell, int rows, int cols)
    {
        int localR = cell.y - mapLoader.BuildStartY;
        int localC = cell.x - mapLoader.BuildStartX;
        return localR >= 0 && localR < rows && localC >= 0 && localC < cols;
    }

    private static Vector2Int OrientationToDelta(int orientation)
    {
        switch (orientation & 3)
        {
            case 0: return Vector2Int.right;
            case 1: return Vector2Int.up;
            case 2: return Vector2Int.left;
            default: return Vector2Int.down;
        }
    }

    private static int ReadAgentOrientation(GridEnemyAgentPIBT_TCP agent)
    {
        if (agent == null || agent.tankController == null || agent.tankController.tankMover == null)
            return 0;

        Vector2 forward = agent.tankController.tankMover.transform.up;
        if (Mathf.Abs(forward.x) >= Mathf.Abs(forward.y))
            return forward.x >= 0f ? 0 : 2;
        return forward.y >= 0f ? 3 : 1;
    }

    // ── Flat index helpers ─────────────────────────────────────────────────

    private int AgentFlat(int agentIdx, int rows, int cols)
    {
        if (_agents[agentIdx] == null) return 0;
        Vector2Int cell = _agents[agentIdx].CurrentCell;
        // cell is in world-grid coords; convert to build-local coords
        int localR = cell.y - mapLoader.BuildStartY;
        int localC = cell.x - mapLoader.BuildStartX;
        localR = Mathf.Clamp(localR, 0, rows - 1);
        localC = Mathf.Clamp(localC, 0, cols - 1);
        return localR * cols + localC;
    }

    private int EagleFlat(int rows, int cols)
    {
        if (eagleBase == null) return 0;
        Vector2Int cell  = mapLoader.WorldToCell(eagleBase.transform.position);
        int localR = Mathf.Clamp(cell.y - mapLoader.BuildStartY, 0, rows - 1);
        int localC = Mathf.Clamp(cell.x - mapLoader.BuildStartX, 0, cols - 1);
        return localR * cols + localC;
    }

    private Vector2Int FlatToCell(int flat, int cols)
    {
        int localR = flat / cols;
        int localC = flat % cols;
        return new Vector2Int(
            mapLoader.BuildStartX + localC,
            mapLoader.BuildStartY + localR);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EAGLE BASE (identical to MapScenarioBootstrapPIBT)
    // ═══════════════════════════════════════════════════════════════════════

    private GameObject SpawnEagleBase()
    {
        if (!TryResolveEagleSpawnCell(out Vector2Int spawnCell))
        {
            Debug.LogError("[PIBT_TCP] Could not find walkable Eagle spawn cell.");
            return null;
        }

        GameObject eagle = new GameObject("EagleBase");
        eagle.layer = LayerMask.NameToLayer("Hittable");
        eagle.transform.SetParent(scenarioRoot, false);
        eagle.transform.position = mapLoader.CellToWorld(spawnCell);
        FactionMember.Ensure(eagle, Faction.Base);

        SpriteRenderer sr = eagle.AddComponent<SpriteRenderer>();
        sr.sprite = eagleSprite != null ? eagleSprite : CreateWhiteSprite();
        sr.color  = eagleColor;
        sr.sortingLayerName = "Eagle";
        sr.sortingOrder = 10;

        Rigidbody2D rb = eagle.AddComponent<Rigidbody2D>();
        rb.bodyType     = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeAll;

        BoxCollider2D col = eagle.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
        col.size      = eagleColliderSize;

        Damagable dmg     = eagle.AddComponent<Damagable>();
        dmg.OnDead        = new UnityEvent();
        dmg.OnHealthChange = new UnityEvent<float>();
        dmg.OnHit         = new UnityEvent();
        dmg.OnHeal        = new UnityEvent();
        dmg.MaxHealth     = eagleHealth;
        dmg.Health        = eagleHealth;

        DestroyUtil du = eagle.AddComponent<DestroyUtil>();
        dmg.OnDead.AddListener(du.DestroyHelper);

        MapGameOverController goc = MapGameOverController.Ensure();
        dmg.OnDead.AddListener(goc.BeginGameOver);

        Slider healthBar = EnsureEagleHealthBar();
        if (healthBar != null)
        {
            healthBar.value = 1f;
            dmg.OnHealthChange.AddListener(healthBar.SetValueWithoutNotify);
        }

        return eagle;
    }

    private bool TryResolveEagleSpawnCell(out Vector2Int spawnCell)
    {
        if (BacktestMode.IsActive)
        {
            var center = new Vector2Int(
                mapLoader.BuildStartX + mapLoader.BuildWidth  / 2,
                mapLoader.BuildStartY + mapLoader.BuildHeight / 2);
            return mapLoader.TryFindWalkableNear(center, out spawnCell);
        }

        if (spawnEagleNearPlayer)
        {
            Transform player = GameObject.Find("Player")?.transform;
            if (player != null && TryFindRandomWalkableNearPlayer(player, out spawnCell))
                return true;
        }

        return mapLoader.TryFindWalkableNear(eagleCell, out spawnCell);
    }

    private bool TryFindRandomWalkableNearPlayer(Transform player, out Vector2Int spawnCell)
    {
        Vector2Int playerCell = mapLoader.WorldToCell(player.position);
        int minSqr = eagleMinPlayerDistanceCells * eagleMinPlayerDistanceCells;
        int maxSqr = Mathf.Max(eagleMinPlayerDistanceCells, eagleMaxPlayerDistanceCells);
        maxSqr *= maxSqr;
        var candidates = new List<Vector2Int>();
        for (int y = playerCell.y - eagleMaxPlayerDistanceCells; y <= playerCell.y + eagleMaxPlayerDistanceCells; y++)
            for (int x = playerCell.x - eagleMaxPlayerDistanceCells; x <= playerCell.x + eagleMaxPlayerDistanceCells; x++)
            {
                Vector2Int c = new Vector2Int(x, y);
                int d = (c - playerCell).sqrMagnitude;
                if (d < minSqr || d > maxSqr || !mapLoader.IsWalkable(c)) continue;
                candidates.Add(c);
            }
        if (candidates.Count == 0) { spawnCell = default; return false; }
        spawnCell = candidates[Random.Range(0, candidates.Count)];
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ENEMIES
    // ═══════════════════════════════════════════════════════════════════════

    private void SpawnEnemies()
    {
        GameObject prefab = ResolveEnemyPrefab();
        if (prefab == null) { Debug.LogError("[PIBT_TCP] Enemy prefab missing."); return; }

        enemiesAlive    = 0;
        enemyCountText  = EnsureEnemyCountText();

        for (int i = 0; i < enemySpawnCells.Count; i++)
        {
            if (!mapLoader.TryFindWalkableNear(enemySpawnCells[i], out Vector2Int spawnCell)) continue;

            GameObject enemy = Instantiate(prefab, mapLoader.CellToWorld(spawnCell),
                                           Quaternion.identity, scenarioRoot);
            enemy.name = $"EnemyPIBT_TCP_{i + 1}";
            ConfigureEnemy(enemy);
            _enemyGOs.Add(enemy);
            enemiesAlive++;
        }

        UpdateEnemyCountText();
    }

    private void ConfigureEnemy(GameObject enemy)
    {
        enemy.transform.localScale = Vector3.one * enemyScale;
        FactionMember faction = FactionMember.Ensure(enemy, Faction.Enemy);

        TankMover tankMover = enemy.GetComponentInChildren<TankMover>();
        if (tankMover != null && tankMover.movementData == null)
            tankMover.movementData = ResolveEnemyMovementData();

        ConfigureEnemyHealth(enemy);
        ConfigureEnemyHealthBar(enemy);
        TrackEnemyDeath(enemy);
        AddPlayerBlocker(enemy);
        IgnoreFriendlyCollisions(enemy, faction);
        AddGridEnemyAgentPIBT_TCP(enemy);

        if (disableLegacyEnemyAI)
            foreach (DefaultEnemyAI ai in enemy.GetComponentsInChildren<DefaultEnemyAI>(true))
                ai.enabled = false;

        AIDetector detector = enemy.GetComponentInChildren<AIDetector>(true);
        if (detector != null && eagleBase != null)
            detector.Target = eagleBase.transform;
    }

    private void AddGridEnemyAgentPIBT_TCP(GameObject enemy)
    {
        TankController tc = enemy.GetComponentInChildren<TankController>();
        if (tc == null || eagleBase == null) return;

        GridEnemyAgentPIBT_TCP agent = enemy.AddComponent<GridEnemyAgentPIBT_TCP>();
        agent.mapLoader             = mapLoader;
        agent.eagleTarget           = eagleBase.transform;
        agent.playerTarget          = GameObject.Find("Player")?.transform;
        agent.tankController        = tc;
        agent.eagleShootingRange    = enemyEagleShootingRange;
        agent.playerShootingRange   = enemyPlayerShootingRange;
        agent.lineOfSightMask       = LayerMask.GetMask("Agent", "Enemy", "Player", "Hittable",
                                          WallLayerName, LegacyMovementObstacleLayerName);
        agent.btSpawnTime           = Time.time;
        _agents.Add(agent);
    }

    private void IgnoreFriendlyCollisions(GameObject enemy, FactionMember faction)
    {
        Collider2D[] mine = enemy.GetComponentsInChildren<Collider2D>(true);
        foreach (var other in _enemyGOs)
        {
            if (other == null) continue;
            FactionMember of = other.GetComponent<FactionMember>();
            if (!FactionMember.AreFriendly(faction, of)) continue;
            Collider2D[] theirs = other.GetComponentsInChildren<Collider2D>(true);
            foreach (var a in mine)
                foreach (var b in theirs)
                    if (a != null && b != null)
                        Physics2D.IgnoreCollision(a, b, true);
        }
    }

    private void ConfigureEnemyHealth(GameObject enemy)
    {
        Damagable d = enemy.GetComponentInChildren<Damagable>();
        if (d == null) return;
        d.MaxHealth = enemyMaxHealth;
        d.Health    = enemyMaxHealth;
    }

    private void ConfigureEnemyHealthBar(GameObject enemy)
    {
        Transform hb = enemy.transform.Find("Canvas/HealthBar");
        if (hb != null && hb.TryGetComponent(out RectTransform rt))
        {
            rt.anchoredPosition = new Vector2(-0.42f, 0.34f);
            rt.sizeDelta        = new Vector2(0.84f, 0.18f);
        }
    }

    private void TrackEnemyDeath(GameObject enemy)
    {
        Damagable d = enemy.GetComponentInChildren<Damagable>();
        if (d == null) return;
        d.OnDead.RemoveListener(OnEnemyDead);
        d.OnDead.AddListener(OnEnemyDead);
    }

    private void OnEnemyDead()
    {
        RecountAliveEnemies();
        UpdateEnemyCountText();
        if (enemiesAlive == 0 && !winTriggered)
        {
            winTriggered = true;
            MapWinController.Ensure().BeginWin();
        }
    }

    private void RecountAliveEnemies()
    {
        int alive = 0;
        foreach (var e in _enemyGOs)
        {
            if (e == null) continue;
            Damagable d = e.GetComponentInChildren<Damagable>();
            if (d != null && d.Health > 0) alive++;
        }
        enemiesAlive = alive;
    }

    private void AddPlayerBlocker(GameObject enemy)
    {
        TankController tc = enemy.GetComponentInChildren<TankController>();
        if (tc == null) return;
        GameObject blocker = new GameObject("PlayerBlocker");
        blocker.layer = ResolveLayer(AgentBlockerLayerName, LegacyMovementObstacleLayerName);
        blocker.transform.SetParent(tc.transform, false);
        CapsuleCollider2D src = tc.GetComponent<CapsuleCollider2D>();
        CapsuleCollider2D bc  = blocker.AddComponent<CapsuleCollider2D>();
        if (src != null) { bc.size = src.size; bc.offset = src.offset; bc.direction = src.direction; }
    }

    // ── HUD helpers (same as MapScenarioBootstrapPIBT) ────────────────────

    private Slider EnsureEagleHealthBar()
    {
        GameObject existingCanvas = GameObject.Find("EagleHealthHud");
        Transform existing = existingCanvas != null ? existingCanvas.transform.Find("HealthBar") : null;
        if (existing != null && existing.TryGetComponent(out Slider s)) return s;

        GameObject canvas = new GameObject("EagleHealthHud");
        canvas.layer = LayerMask.NameToLayer("UI");
        Canvas c2 = canvas.AddComponent<Canvas>();
        c2.renderMode = RenderMode.ScreenSpaceOverlay;
        c2.sortingLayerName = "UI";
        c2.sortingOrder = 20;
        { var sc = canvas.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1280, 720); sc.matchWidthOrHeight = 0.5f; }
        canvas.AddComponent<GraphicRaycaster>();
        CanvasGroup cg = canvas.AddComponent<CanvasGroup>(); cg.alpha = 0.8f;

        GameObject labelGO = new GameObject("BaseLabel");
        labelGO.layer = LayerMask.NameToLayer("UI");
        labelGO.transform.SetParent(canvas.transform, false);
        RectTransform lr = labelGO.AddComponent<RectTransform>();
        lr.anchorMin = new Vector2(0, 1); lr.anchorMax = new Vector2(0, 1);
        lr.pivot = new Vector2(0, 1); lr.anchoredPosition = new Vector2(24, -48); lr.sizeDelta = new Vector2(46, 18);
        Text label = labelGO.AddComponent<Text>();
        label.text = "BASE"; label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 14; label.alignment = TextAnchor.MiddleLeft; label.color = Color.white;

        GameObject hbGO = new GameObject("HealthBar");
        hbGO.layer = LayerMask.NameToLayer("UI");
        hbGO.transform.SetParent(canvas.transform, false);
        RectTransform hbr = hbGO.AddComponent<RectTransform>();
        hbr.anchorMin = new Vector2(0, 1); hbr.anchorMax = new Vector2(0, 1);
        hbr.pivot = new Vector2(0, 1); hbr.anchoredPosition = new Vector2(76, -48); hbr.sizeDelta = new Vector2(120, 18);
        Image bg = hbGO.AddComponent<Image>(); bg.color = Color.black;
        Slider slider = hbGO.AddComponent<Slider>();
        slider.interactable = false; slider.minValue = 0; slider.maxValue = 1; slider.value = 1; slider.targetGraphic = bg;

        GameObject fill = new GameObject("HealthFill");
        fill.layer = LayerMask.NameToLayer("UI");
        fill.transform.SetParent(hbGO.transform, false);
        RectTransform fr = fill.AddComponent<RectTransform>();
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
        Image fi = fill.AddComponent<Image>(); fi.color = Color.yellow;
        slider.fillRect = fr;
        return slider;
    }

    private Text EnsureEnemyCountText()
    {
        GameObject existingCanvas = GameObject.Find("EnemyCountHud");
        Transform existing = existingCanvas != null ? existingCanvas.transform.Find("EnemyCountText") : null;
        if (existing != null && existing.TryGetComponent(out Text t)) return t;

        GameObject canvas = new GameObject("EnemyCountHud");
        canvas.layer = LayerMask.NameToLayer("UI");
        Canvas c2 = canvas.AddComponent<Canvas>();
        c2.renderMode = RenderMode.ScreenSpaceOverlay;
        c2.sortingLayerName = "UI"; c2.sortingOrder = 20;
        { var sc = canvas.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1280, 720); sc.matchWidthOrHeight = 0.5f; }
        canvas.AddComponent<GraphicRaycaster>();
        CanvasGroup cg = canvas.AddComponent<CanvasGroup>(); cg.alpha = 0.8f;

        GameObject textGO = new GameObject("EnemyCountText");
        textGO.layer = LayerMask.NameToLayer("UI");
        textGO.transform.SetParent(canvas.transform, false);
        RectTransform tr2 = textGO.AddComponent<RectTransform>();
        tr2.anchorMin = new Vector2(0, 1); tr2.anchorMax = new Vector2(0, 1);
        tr2.pivot = new Vector2(0, 1); tr2.anchoredPosition = new Vector2(24, -72); tr2.sizeDelta = new Vector2(172, 18);
        Text text = textGO.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14; text.alignment = TextAnchor.MiddleLeft; text.color = Color.white;
        return text;
    }

    private void UpdateEnemyCountText()
    {
        if (enemyCountText == null) enemyCountText = EnsureEnemyCountText();
        if (enemyCountText != null) enemyCountText.text = $"ENEMY: {enemiesAlive}";
    }

    // ── Misc helpers ───────────────────────────────────────────────────────

    private void ResolveReferences()
    {
        if (mapLoader == null) mapLoader = GetComponent<MapLoader>();
        if (mapLoader == null) mapLoader = FindFirstObjectByType<MapLoader>();
    }

    private int ResolveLayer(string name, string fallback)
    {
        int l = LayerMask.NameToLayer(name);
        return l >= 0 ? l : LayerMask.NameToLayer(fallback);
    }

    private void ClearScenario()
    {
        _agents.Clear();
        _enemyGOs.Clear();
        Transform existing = transform.Find("ScenarioRuntime_PIBT_TCP");
        if (existing == null) return;
        if (Application.isPlaying) Destroy(existing.gameObject);
        else DestroyImmediate(existing.gameObject);
    }

    private Sprite CreateWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
    }

    private GameObject ResolveEnemyPrefab()
    {
        if (enemyPrefab != null) return enemyPrefab;
#if UNITY_EDITOR
        var r = AssetDatabase.LoadAssetAtPath<GameObject>(enemyPrefabPath);
        if (r != null) return r;
#endif
        return Resources.Load<GameObject>("Prefabs/StaticEnemy");
    }

    private TankMovementData ResolveEnemyMovementData()
    {
        if (enemyMovementData != null) return enemyMovementData;
#if UNITY_EDITOR
        var r = AssetDatabase.LoadAssetAtPath<TankMovementData>(enemyMovementDataPath);
        if (r != null) return r;
#endif
        return Resources.Load<TankMovementData>("Data/EnemyTankMovementData");
    }

    // ── Toast notification ─────────────────────────────────────────────────

    private void ShowToast(string message, float duration)
    {
        StartCoroutine(ToastCoroutine(message, duration));
    }

    private IEnumerator ToastCoroutine(string message, float duration)
    {
        // Canvas
        GameObject canvasGO = new GameObject("PIBTToast");
        canvasGO.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 99;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Background panel — centered, middle of screen
        GameObject panelGO = new GameObject("Panel");
        panelGO.layer = LayerMask.NameToLayer("UI");
        panelGO.transform.SetParent(canvasGO.transform, false);
        RectTransform pr = panelGO.AddComponent<RectTransform>();
        pr.anchorMin        = new Vector2(0.5f, 0.5f);
        pr.anchorMax        = new Vector2(0.5f, 0.5f);
        pr.pivot            = new Vector2(0.5f, 0.5f);
        pr.anchoredPosition = Vector2.zero;
        pr.sizeDelta        = new Vector2(420, 100);
        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.82f);

        // Text
        GameObject textGO = new GameObject("Text");
        textGO.layer = LayerMask.NameToLayer("UI");
        textGO.transform.SetParent(panelGO.transform, false);
        RectTransform tr = textGO.AddComponent<RectTransform>();
        tr.anchorMin  = Vector2.zero;
        tr.anchorMax  = Vector2.one;
        tr.offsetMin  = new Vector2(12, 8);
        tr.offsetMax  = new Vector2(-12, -8);
        Text txt = textGO.AddComponent<Text>();
        txt.text      = message;
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = 18;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color     = new Color(1f, 0.35f, 0.35f, 1f);

        // Fade out in the last 0.6 s
        float fadeStart = duration - 0.6f;
        float elapsed   = 0f;
        CanvasGroup cg  = canvasGO.AddComponent<CanvasGroup>();
        cg.alpha = 1f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (elapsed > fadeStart)
                cg.alpha = Mathf.Lerp(1f, 0f, (elapsed - fadeStart) / 0.6f);
            yield return null;
        }

        Destroy(canvasGO);
    }
}
