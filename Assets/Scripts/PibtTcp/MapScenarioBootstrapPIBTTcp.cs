using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using PibtTcp;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Bootstrap cho scene MapF_TankTest_PIBT_TCP.
/// Clone có chọn lọc từ MapScenarioBootstrapPIBT; thay AddGridEnemyAgentPIBT
/// bằng TCP session + GridEnemyAgentPIBTTcp (sẽ implement ở Sprint 4).
///
/// Đảm bảo mỗi enemy chỉ chạy MỘT AI runtime:
///   - DefaultEnemyAI  → disabled
///   - GridEnemyAgent  → không add
///   - GridEnemyAgentPIBT → không add
///   - GridEnemyAgentPIBTTcp → được add bởi component này
///
/// Gắn component này cùng chỗ với MapLoader và MapTankTestBootstrap.
/// </summary>
public class MapScenarioBootstrapPIBTTcp : MonoBehaviour
{
    private const string WallLayerName                    = "Walls";
    private const string AgentBlockerLayerName            = "AgentBlocker";
    private const string LegacyMovementObstacleLayerName  = "ObstaclesMovement";

    // ─── References ──────────────────────────────────────────────────────────
    public MapLoader mapLoader;

    // ─── Eagle ────────────────────────────────────────────────────────────────
    [Header("Eagle Base")]
    public Vector2Int eagleCell                   = new Vector2Int(16, 16);
    public int        eagleHealth                 = 500;
    public Sprite     eagleSprite;
    public Color      eagleColor                  = Color.white;
    public Vector2    eagleColliderSize            = new Vector2(1.3f, 1.3f);
    public bool       spawnEagleNearPlayer         = false;
    [Min(1)] public int eagleMinPlayerDistanceCells = 3;
    [Min(1)] public int eagleMaxPlayerDistanceCells = 7;

    // ─── Enemies ──────────────────────────────────────────────────────────────
    [Header("Enemies")]
    public GameObject         enemyPrefab;
    public string             enemyPrefabPath          = "Assets/Prefabs/StaticEnemy.prefab";
    public TankMovementData   enemyMovementData;
    public string             enemyMovementDataPath     = "Assets/Data/TankData/EnemyTankMovementData.asset";
    [Header("Phase 1 Copy Movement Test")]
    public bool               useCopyMovementComponents = true;
    public TankMovementData   enemyMovementDataCopy;
    public string             enemyMovementDataCopyPath = "Assets/Data/TankData/EnemyTankMovementDataCopy.asset";
    public List<Vector2Int>   enemySpawnCells           = new List<Vector2Int>
    {
        new Vector2Int(30,  1),
        new Vector2Int( 1, 30),
        new Vector2Int(30, 30),
        new Vector2Int(16,  1)
    };
    public bool       disableLegacyEnemyAI      = true;
    [Range(0.4f, 1.2f)]
    public float      enemyScale                = 0.72f;
    public float      enemyReplanInterval       = 0.75f;
    public float      enemyEagleShootingRange   = 5f;
    public float      enemyPlayerShootingRange  = 7f;
    public bool       enablePlayerCombat       = false;
    [Min(1)]
    public int        enemyMaxHealth            = 20;

    // ─── TCP ──────────────────────────────────────────────────────────────────
    [Header("TCP Server")]
    public string tcpHost    = "127.0.0.1";
    public int    tcpPort    = 7777;
    public int    tcpTimeout = 1000;  // ms per request timeout

    [Header("Phase A Debug")]
    public bool debugTcpTrace = false;
    [Min(1)] public int debugTraceRequestLimit = 24;
    [Min(1)] public int debugTraceEveryNthRequest = 10;

    [Header("Phase B Validation")]
    public bool debugValidateOrientationOnSpawn = false;

    // ─── Stuck recovery ───────────────────────────────────────────────────────
    [Header("Stuck recovery")]
    [SerializeField, Min(0f)] private float     scuffTimeout        = 0.4f;
    [SerializeField]          private LayerMask obstacleContactMask;

    // ─── Runtime ──────────────────────────────────────────────────────────────
    private Transform        scenarioRoot;
    private GameObject       eagleBase;
    private int              enemiesAlive;
    private Text             enemyCountText;
    private bool             winTriggered;

    private readonly List<GameObject> enemies = new List<GameObject>();

    // Shared TCP session state (all agents read from this)
    private PibtTcpSessionState _sessionState;
    private PibtTcpClient       _tcpClient;
    private bool                _tcpConnected;
    private bool                _requestInFlight;
    private float               _nextPlanTime;
    private int                 _requestId;
    private int                 _debugTraceCount;

    public GameObject            EagleBase => eagleBase;
    public IReadOnlyList<GameObject> Enemies => enemies;
    public PibtTcpSessionState   SessionState => _sessionState;
    public PibtTcpClient         GetTcpClient() => _tcpClient;
    public void RequestImmediatePlanStep() => _nextPlanTime = 0f;

    public bool HasAnyEnemyAbleToShootEagle()
    {
        if (eagleBase == null || enemies.Count == 0)
            return false;

        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null)
                continue;

            Damagable d = enemy.GetComponentInChildren<Damagable>();
            if (d != null && d.Health <= 0)
                continue;

            GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
            if (copyAgent != null && copyAgent.CanShootEagleNow())
                return true;

            GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
            if (agent != null && agent.CanShootEagleNow())
                return true;
        }

        return false;
    }

    public bool HasAnyEnemyHoldingEagleSlot()
    {
        if (eagleBase == null || enemies.Count == 0)
            return false;

        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null)
                continue;

            Damagable d = enemy.GetComponentInChildren<Damagable>();
            if (d != null && d.Health <= 0)
                continue;

            GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
            if (copyAgent != null && copyAgent.IsHoldingAssignedEagleSlot())
                return true;

            GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
            if (agent != null && agent.IsHoldingAssignedEagleSlot())
                return true;
        }

        return false;
    }


    // ─── Entry point ──────────────────────────────────────────────────────────

    [ContextMenu("Spawn Scenario Now")]
    public void SpawnScenario()
    {
        ResolveReferences();
        if (mapLoader == null || mapLoader.BuildWidth <= 0 || mapLoader.BuildHeight <= 0)
        {
            Debug.LogError("[MapScenarioBootstrapPIBTTcp] Map must be loaded before spawning scenario.");
            return;
        }

        ClearScenario();
        scenarioRoot = new GameObject("ScenarioRuntime_PIBT_TCP").transform;
        scenarioRoot.SetParent(transform, false);

        _sessionState = new PibtTcpSessionState
        {
            SessionId = System.Guid.NewGuid().ToString("N")[..8],
            TeamSize  = enemySpawnCells.Count
        };

        _tcpClient = new PibtTcpClient(tcpHost, tcpPort, tcpTimeout);

        eagleBase = SpawnEagleBase();
        SpawnEnemies();
        if (debugValidateOrientationOnSpawn)
            ValidateOrientationConvention();

        // Bắt đầu kết nối TCP sau khi mọi enemy đã spawn
        StartCoroutine(ConnectAndStartSession());
    }

    // ─── TCP connection ────────────────────────────────────────────────────────

    private IEnumerator ConnectAndStartSession()
    {
        bool[,] walkable = BuildWalkableArray();
        var map = PibtTcpGridAdapter.BuildMapDto(walkable, mapLoader.BuildWidth, mapLoader.BuildHeight);

        var connectTask = _tcpClient.ConnectAndHelloAsync(
            _sessionState.SessionId,
            _sessionState.TeamSize,
            map);

        yield return new WaitUntil(() => connectTask.IsCompleted);

        if (!connectTask.Result)
        {
            Debug.LogError("[MapScenarioBootstrapPIBTTcp] Failed to connect to TCP server.");
            yield break;
        }

        Debug.Log("[MapScenarioBootstrapPIBTTcp] TCP session started: " + _sessionState.SessionId);
        _tcpConnected = true;
    }

    private void Update()
    {
        if (!_tcpConnected || _requestInFlight || _tcpClient == null || _sessionState == null) return;
        if (Time.time < _nextPlanTime) return;
        if (!AreAgentsReadyForPlanStep())
        {
            _nextPlanTime = Time.time + 0.05f;
            return;
        }

        SendTeamPlanStepAsync();
    }

    private bool AreAgentsReadyForPlanStep()
    {
        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null) continue;

            Damagable d = enemy.GetComponentInChildren<Damagable>();
            if (d != null && d.Health <= 0) continue;

            GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
            if (copyAgent != null && copyAgent.IsExecutingTcpAction)
                return false;

            GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
            if (agent != null && agent.IsExecutingTcpAction)
                return false;
        }

        return true;
    }

    private async void SendTeamPlanStepAsync()
    {
        AssignEnemyAttackSlots();

        List<AgentStateDto> agentStates = BuildAgentStates();
        if (agentStates.Count == 0) return;

        _requestInFlight = true;
        _nextPlanTime = Time.time + Mathf.Max(0.05f, enemyReplanInterval);
        int requestId = ++_requestId;
        int timestep = Mathf.RoundToInt(Time.time * 10);

        if (ShouldTraceRequest(requestId))
            Debug.Log(BuildRequestTrace(requestId, timestep, agentStates));

        try
        {
            PlanResult result = await _tcpClient.PlanStepAsync(requestId, timestep, agentStates);
            if (result != null)
            {
                _sessionState.SetActions(result.actions);
                _sessionState.LatencyMsLast = _tcpClient.LatencyMsLast;
                _sessionState.ComputeMsLast = _tcpClient.ComputeMsLast;
                _sessionState.TimeoutCount = _tcpClient.TimeoutCount;
                _sessionState.LastRequestId = requestId;
                _sessionState.LastTimestep = timestep;
                _sessionState.ResultReady = true;

                if (ShouldTraceRequest(requestId))
                {
                    _debugTraceCount++;
                    Debug.Log(BuildResultTrace(result));
                }
            }
        }
        finally
        {
            _requestInFlight = false;
        }
    }

    private List<AgentStateDto> BuildAgentStates()
    {
        var states = new List<AgentStateDto>(enemies.Count);
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] == null) continue;
            Damagable d = enemies[i].GetComponentInChildren<Damagable>();
            if (d != null && d.Health <= 0) continue;

            GridEnemyAgentPIBTTcpCopy copyAgent = enemies[i].GetComponent<GridEnemyAgentPIBTTcpCopy>();
            if (copyAgent != null)
            {
                states.Add(copyAgent.BuildAgentStateDto());
                continue;
            }

            GridEnemyAgentPIBTTcp agent = enemies[i].GetComponent<GridEnemyAgentPIBTTcp>();
            if (agent != null)
                states.Add(agent.BuildAgentStateDto());
        }
        return states;
    }

    private bool ShouldTraceRequest(int requestId)
    {
        if (!debugTcpTrace)
            return false;

        if (_debugTraceCount < debugTraceRequestLimit)
            return true;

        return debugTraceEveryNthRequest > 0 && requestId % debugTraceEveryNthRequest == 0;
    }

    private string BuildRequestTrace(int requestId, int timestep, List<AgentStateDto> agentStates)
    {
        string eagleCellText = eagleBase != null
            ? mapLoader.WorldToCell(eagleBase.transform.position).ToString()
            : "none";
        string motionSummary = BuildMotionSummary();
        return $"[PIBT_TCP_TRACE] send req={requestId} t={timestep} eagleCell={eagleCellText} summary={motionSummary} agents={FormatAgentStates(agentStates)}";
    }

    private string BuildResultTrace(PlanResult result)
    {
        return $"[PIBT_TCP_TRACE] recv req={result.requestId} t={result.timestep} compute={result.computeMs:F1} timeout={result.timeout} actions={FormatActions(result.actions)}";
    }

    private string BuildMotionSummary()
    {
        int shootingEagle = 0;
        int shootingPlayer = 0;
        int pendingMove = 0;
        int reverseRecovery = 0;
        int scuffing = 0;
        int idle = 0;

        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null)
                continue;

            Damagable d = enemy.GetComponentInChildren<Damagable>();
            if (d != null && d.Health <= 0)
                continue;

            GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
            if (copyAgent != null)
            {
                string state = copyAgent.GetMotionStateForDebug();
                if (state == "shoot_eagle") shootingEagle++;
                else if (state == "shoot_player") shootingPlayer++;
                else if (state == "fw_pending") pendingMove++;
                else if (state == "reverse_recovery") reverseRecovery++;
                else if (state == "scuffing") scuffing++;
                else idle++;
                continue;
            }

            GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
            if (agent != null)
                idle++;
        }

        return $"shootEagle={shootingEagle} shootPlayer={shootingPlayer} pendingMove={pendingMove} reverse={reverseRecovery} scuffing={scuffing} idle={idle}";
    }

    private static string FormatAgentStates(List<AgentStateDto> states)
    {
        if (states == null || states.Count == 0)
            return "[]";

        var parts = new List<string>(states.Count);
        foreach (AgentStateDto state in states)
        {
            parts.Add($"id={state.id},loc={state.loc},ori={state.orientation},goal={state.goalLoc}");
        }

        return "[" + string.Join(" | ", parts) + "]";
    }

    private static string FormatActions(List<ActionDto> actions)
    {
        if (actions == null || actions.Count == 0)
            return "[]";

        var parts = new List<string>(actions.Count);
        foreach (ActionDto action in actions)
        {
            parts.Add($"id={action.id},act={action.action},next={action.nextLoc}");
        }

        return "[" + string.Join(" | ", parts) + "]";
    }

    [ContextMenu("Phase B Validate Orientation")]
    public void ValidateOrientationConvention()
    {
        ResolveReferences();
        int width = mapLoader != null && mapLoader.BuildWidth > 0 ? mapLoader.BuildWidth : 32;
        string report = PibtTcpGridAdapter.BuildOrientationConventionReport(width);
        Debug.Log($"[PIBT_TCP_PHASE_B] orientation_report width={width} {report}");

        foreach (GameObject enemy in enemies)
        {
            if (enemy == null) continue;
            AgentStateDto state;
            Vector3 pos;
            GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
            if (copyAgent != null)
            {
                state = copyAgent.BuildAgentStateDto();
                pos = copyAgent.GetTankMoverPositionForDebug();
            }
            else
            {
                GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
                if (agent == null) continue;
                state = agent.BuildAgentStateDto();
                pos = agent.GetTankMoverPositionForDebug();
            }

            Vector2Int facing = PibtTcpGridAdapter.OrientationToCell(state.orientation);
            int fwDelta = PibtTcpGridAdapter.ForwardDeltaLoc(state.orientation, width);
            int cw = PibtTcpGridAdapter.RotateClockwise(state.orientation);
            int ccw = PibtTcpGridAdapter.RotateCounterClockwise(state.orientation);

            Debug.Log(
                $"[PIBT_TCP_PHASE_B] agent={state.id} pos=({pos.x:F2},{pos.y:F2}) cell={PibtTcpGridAdapter.LocToCell(state.loc, width)} " +
                $"ori={state.orientation} facing={facing} goal={PibtTcpGridAdapter.LocToCell(state.goalLoc, width)} " +
                $"CR->{cw} CCR->{ccw} FWdelta={fwDelta} nextCell={PibtTcpGridAdapter.LocToCell(state.loc + fwDelta, width)}");
        }
    }

    private bool[,] BuildWalkableArray()
    {
        int w = mapLoader.BuildWidth;
        int h = mapLoader.BuildHeight;
        bool[,] arr = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                arr[x, y] = mapLoader.IsWalkable(new Vector2Int(x, y));
        return arr;
    }

    // ─── Eagle ────────────────────────────────────────────────────────────────

    private GameObject SpawnEagleBase()
    {
        if (!TryResolveEagleSpawnCell(out Vector2Int spawnCell))
        {
            Debug.LogError("[MapScenarioBootstrapPIBTTcp] Could not find walkable Eagle spawn cell.");
            return null;
        }

        GameObject eagle = new GameObject("EagleBase");
        eagle.layer = LayerMask.NameToLayer("Hittable");
        eagle.transform.SetParent(scenarioRoot, false);
        eagle.transform.position = mapLoader.CellToWorld(spawnCell);
        FactionMember.Ensure(eagle, Faction.Base);

        SpriteRenderer sr = eagle.AddComponent<SpriteRenderer>();
        sr.sprite = eagleSprite != null ? eagleSprite : CreateWhiteSprite();
        sr.color = eagleColor;
        sr.sortingLayerName = "Eagle";
        sr.sortingOrder = 10;

        Rigidbody2D rb = eagle.AddComponent<Rigidbody2D>();
        rb.bodyType    = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeAll;

        BoxCollider2D col = eagle.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
        col.size      = eagleColliderSize;

        Damagable dmg = eagle.AddComponent<Damagable>();
        dmg.OnDead        = new UnityEvent();
        dmg.OnHealthChange = new UnityEvent<float>();
        dmg.OnHit         = new UnityEvent();
        dmg.OnHeal        = new UnityEvent();
        dmg.MaxHealth     = eagleHealth;
        dmg.Health        = eagleHealth;

        DestroyUtil du = eagle.AddComponent<DestroyUtil>();
        dmg.OnDead.AddListener(du.DestroyHelper);

        MapGameOverController gameOverController = MapGameOverController.Ensure();
        dmg.OnDead.AddListener(gameOverController.BeginGameOver);

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
        int minDist  = Mathf.Max(1, eagleMinPlayerDistanceCells);
        int maxDist  = Mathf.Max(minDist, eagleMaxPlayerDistanceCells);
        int minSqr   = minDist * minDist;
        int maxSqr   = maxDist * maxDist;

        var candidates = new List<Vector2Int>();
        for (int y = playerCell.y - maxDist; y <= playerCell.y + maxDist; y++)
        {
            for (int x = playerCell.x - maxDist; x <= playerCell.x + maxDist; x++)
            {
                var c = new Vector2Int(x, y);
                int sqr = (c - playerCell).sqrMagnitude;
                if (sqr < minSqr || sqr > maxSqr) continue;
                if (!mapLoader.IsWalkable(c)) continue;
                candidates.Add(c);
            }
        }

        if (candidates.Count == 0) { spawnCell = default; return false; }
        spawnCell = candidates[Random.Range(0, candidates.Count)];
        return true;
    }

    private Slider EnsureEagleHealthBar()
    {
        GameObject existingCanvas = GameObject.Find("EagleHealthHud");
        Transform existing = existingCanvas?.transform.Find("HealthBar");
        if (existing != null && existing.TryGetComponent(out Slider s)) return s;

        GameObject canvasObj = new GameObject("EagleHealthHud");
        canvasObj.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 20;
        var sc = canvasObj.AddComponent<CanvasScaler>();
        sc.uiScaleMode        = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new UnityEngine.Vector2(1280f, 720f);
        sc.matchWidthOrHeight  = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();
        var cg = canvasObj.AddComponent<CanvasGroup>();
        cg.alpha = 0.8f;

        // Label
        GameObject labelObj = new GameObject("BaseLabel");
        labelObj.layer = LayerMask.NameToLayer("UI");
        labelObj.transform.SetParent(canvasObj.transform, false);
        var lr = labelObj.AddComponent<RectTransform>();
        lr.anchorMin = new Vector2(0f, 1f); lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0f, 1f); lr.anchoredPosition = new Vector2(24f, -48f);
        lr.sizeDelta = new Vector2(46f, 18f);
        var lt = labelObj.AddComponent<Text>();
        lt.text = "BASE"; lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.fontSize = 14; lt.alignment = TextAnchor.MiddleLeft; lt.color = Color.white;

        // Health bar
        GameObject hbObj = new GameObject("HealthBar");
        hbObj.layer = LayerMask.NameToLayer("UI");
        hbObj.transform.SetParent(canvasObj.transform, false);
        var hbr = hbObj.AddComponent<RectTransform>();
        hbr.anchorMin = new Vector2(0f, 1f); hbr.anchorMax = new Vector2(0f, 1f);
        hbr.pivot = new Vector2(0f, 1f); hbr.anchoredPosition = new Vector2(76f, -48f);
        hbr.sizeDelta = new Vector2(120f, 18f);
        var bg = hbObj.AddComponent<Image>();
        bg.color = Color.black;
        var slider = hbObj.AddComponent<Slider>();
        slider.interactable = false; slider.minValue = 0f; slider.maxValue = 1f;
        slider.value = 1f; slider.targetGraphic = bg;

        // Fill
        GameObject fillObj = new GameObject("HealthFill");
        fillObj.layer = LayerMask.NameToLayer("UI");
        fillObj.transform.SetParent(hbObj.transform, false);
        var fr = fillObj.AddComponent<RectTransform>();
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.pivot = new Vector2(0.5f, 0.5f);
        fr.offsetMin = fr.offsetMax = Vector2.zero;
        var fi = fillObj.AddComponent<Image>();
        fi.color = Color.yellow;
        slider.fillRect = fr;

        return slider;
    }

    // ─── Enemies ──────────────────────────────────────────────────────────────

    private void SpawnEnemies()
    {
        GameObject prefab = ResolveEnemyPrefab();
        if (prefab == null)
        {
            Debug.LogError("[MapScenarioBootstrapPIBTTcp] Enemy prefab is missing.");
            return;
        }

        enemiesAlive = 0;
        enemyCountText = EnsureEnemyCountText();

        for (int i = 0; i < enemySpawnCells.Count; i++)
        {
            if (!mapLoader.TryFindWalkableNear(enemySpawnCells[i], out Vector2Int spawnCell)) continue;

            GameObject enemy = Instantiate(prefab, mapLoader.CellToWorld(spawnCell), Quaternion.identity, scenarioRoot);
            enemy.name = $"EnemyPIBT_TCP_{i + 1}";
            ConfigureEnemy(enemy, i);
            enemies.Add(enemy);
            enemiesAlive++;
        }

        AssignEnemyAttackSlots();
        UpdateEnemyCountText();
    }

    private void AssignEnemyAttackSlots()
    {
        if (mapLoader == null || eagleBase == null || enemies.Count == 0)
            return;

        List<GameObject> activeEnemies = new List<GameObject>(enemies.Count);
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] != null)
                activeEnemies.Add(enemies[i]);
        }

        if (activeEnemies.Count == 0)
            return;

        activeEnemies.Sort((a, b) =>
        {
            int idA = GetTcpAgentId(a);
            int idB = GetTcpAgentId(b);
            return idA.CompareTo(idB);
        });

        bool needsAssignment = false;
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            if (!TryGetAssignedGoalCell(activeEnemies[i], out Vector2Int assignedGoalCell)
                || !IsAttackSlotValid(assignedGoalCell))
            {
                needsAssignment = true;
                break;
            }
        }

        if (!needsAssignment)
            return;

        Vector2Int eagleCell = mapLoader.WorldToCell(eagleBase.transform.position);
        List<Vector2Int> slots = BuildEagleAttackSlots(eagleCell, activeEnemies.Count);
        if (slots.Count == 0)
            return;

        HashSet<Vector2Int> usedSlots = new HashSet<Vector2Int>();
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            GameObject enemy = activeEnemies[i];
            if (!HasTcpAgent(enemy))
                continue;

            if (TryGetAssignedGoalCell(enemy, out Vector2Int assignedSlotCell)
                && IsAttackSlotValid(assignedSlotCell)
                && !usedSlots.Contains(assignedSlotCell))
            {
                usedSlots.Add(assignedSlotCell);
                continue;
            }

            ClearTcpAgentGoalCell(enemy);

            Vector2Int enemyCell = mapLoader.WorldToCell(enemy.transform.position);
            int bestIndex = -1;
            int bestScore = int.MaxValue;
            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                Vector2Int slotCell = slots[slotIndex];
                if (usedSlots.Contains(slotCell))
                    continue;

                int score = Manhattan(enemyCell, slotCell) * 100 + Manhattan(slotCell, eagleCell);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = slotIndex;
                }
            }

            if (bestIndex >= 0)
            {
                Vector2Int assignedSlot = slots[bestIndex];
                usedSlots.Add(assignedSlot);
                AssignTcpAgentGoalCell(enemy, assignedSlot);
            }
        }
    }

    private int GetTcpAgentId(GameObject enemy)
    {
        if (enemy == null)
            return int.MaxValue;

        GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
        if (copyAgent != null)
            return copyAgent.agentId;

        GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
        return agent != null ? agent.agentId : int.MaxValue;
    }

    private bool HasTcpAgent(GameObject enemy)
    {
        return enemy != null
            && (enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>() != null
                || enemy.GetComponent<GridEnemyAgentPIBTTcp>() != null);
    }

    private bool TryGetAssignedGoalCell(GameObject enemy, out Vector2Int goalCell)
    {
        goalCell = default;
        if (enemy == null)
            return false;

        GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
        if (copyAgent != null)
        {
            goalCell = copyAgent.assignedGoalCell;
            return copyAgent.hasAssignedGoalCell;
        }

        GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
        if (agent != null)
        {
            goalCell = agent.assignedGoalCell;
            return agent.hasAssignedGoalCell;
        }

        return false;
    }

    private void ClearTcpAgentGoalCell(GameObject enemy)
    {
        if (enemy == null)
            return;

        GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
        if (copyAgent != null)
        {
            copyAgent.ClearAssignedGoalCell();
            return;
        }

        GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
        if (agent != null)
            agent.ClearAssignedGoalCell();
    }

    private void AssignTcpAgentGoalCell(GameObject enemy, Vector2Int goalCell)
    {
        if (enemy == null)
            return;

        GridEnemyAgentPIBTTcpCopy copyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcpCopy>();
        if (copyAgent != null)
        {
            copyAgent.AssignGoalCell(goalCell);
            return;
        }

        GridEnemyAgentPIBTTcp agent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
        if (agent != null)
            agent.AssignGoalCell(goalCell);
    }

    private List<Vector2Int> BuildEagleAttackSlots(Vector2Int eagleCell, int requiredCount)
    {
        List<Vector2Int> slots = new List<Vector2Int>();
        int radius = Mathf.Max(1, Mathf.CeilToInt(enemyEagleShootingRange));
        CollectEagleAttackSlotsInRadius(eagleCell, radius, true, slots);
        if (slots.Count < requiredCount)
            CollectEagleAttackSlotsInRadius(eagleCell, radius, false, slots);
        if (slots.Count < requiredCount)
            CollectFallbackWalkableSlots(eagleCell, slots);

        Vector3 eagleWorld = eagleBase.transform.position;
        slots.Sort((a, b) =>
        {
            float da = SlotPriorityScore(a, eagleWorld);
            float db = SlotPriorityScore(b, eagleWorld);
            int cmp = da.CompareTo(db);
            if (cmp != 0) return cmp;

            float angleA = Mathf.Atan2(a.y - eagleCell.y, a.x - eagleCell.x);
            float angleB = Mathf.Atan2(b.y - eagleCell.y, b.x - eagleCell.x);
            int angleCmp = angleA.CompareTo(angleB);
            if (angleCmp != 0) return angleCmp;

            int dxCmp = a.x.CompareTo(b.x);
            if (dxCmp != 0) return dxCmp;

            return a.y.CompareTo(b.y);
        });

        return slots;
    }

    private void CollectEagleAttackSlotsInRadius(Vector2Int eagleCell, int radius, bool requireLineOfSight, List<Vector2Int> slots)
    {
        Vector3 eagleWorld = eagleBase.transform.position;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                Vector2Int cell = new Vector2Int(eagleCell.x + dx, eagleCell.y + dy);
                if (cell == eagleCell || slots.Contains(cell))
                    continue;
                if (!mapLoader.IsWalkable(cell))
                    continue;

                Vector3 world = mapLoader.CellToWorld(cell);
                if (Vector3.Distance(world, eagleWorld) > enemyEagleShootingRange + 0.001f)
                    continue;
                if (requireLineOfSight && !HasLineOfSightToEagle(world))
                    continue;

                slots.Add(cell);
            }
        }
    }

    private void CollectFallbackWalkableSlots(Vector2Int eagleCell, List<Vector2Int> slots)
    {
        for (int y = mapLoader.BuildStartY; y < mapLoader.BuildStartY + mapLoader.BuildHeight; y++)
        {
            for (int x = mapLoader.BuildStartX; x < mapLoader.BuildStartX + mapLoader.BuildWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (cell == eagleCell || slots.Contains(cell))
                    continue;
                if (!mapLoader.IsWalkable(cell))
                    continue;

                slots.Add(cell);
            }
        }
    }

    private bool IsAttackSlotValid(Vector2Int cell)
    {
        return mapLoader != null && mapLoader.IsWalkable(cell);
    }

    private float SlotPriorityScore(Vector2Int cell, Vector3 eagleWorld)
    {
        Vector3 world = mapLoader.CellToWorld(cell);
        float distance = Vector3.Distance(world, eagleWorld);
        float score = distance;

        if (distance > enemyEagleShootingRange)
        {
            score += 25f;
        }
        else if (!HasLineOfSightToEagle(world))
        {
            score += 1.5f;
        }

        return score;
    }

    private bool HasLineOfSightToEagle(Vector3 origin)
    {
        if (eagleBase == null)
            return false;

        Vector2 origin2 = origin;
        Vector2 target = eagleBase.transform.position;
        Vector2 delta = target - origin2;
        float dist = delta.magnitude;
        if (dist <= Mathf.Epsilon)
            return true;

        LayerMask losMask = LayerMask.GetMask("Agent", "Enemy", "Player", "Hittable", WallLayerName, LegacyMovementObstacleLayerName);
        RaycastHit2D hit = Physics2D.Raycast(origin2, delta / dist, dist, losMask);
        if (hit.collider == null)
            return false;

        return IsEagleCollider(hit.collider);
    }

    private bool IsEagleCollider(Collider2D collider)
    {
        if (collider == null || eagleBase == null)
            return false;

        Transform t = collider.transform;
        return t == eagleBase.transform || t.IsChildOf(eagleBase.transform);
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private void ConfigureEnemy(GameObject enemy, int agentId)
    {
        enemy.transform.localScale = Vector3.one * enemyScale;

        FactionMember fm = FactionMember.Ensure(enemy, Faction.Enemy);

        TankControllerCopy copyController = null;
        if (useCopyMovementComponents)
        {
            copyController = ConfigureCopyMovementComponents(enemy);
        }
        else
        {
            TankMover tankMover = enemy.GetComponentInChildren<TankMover>();
            if (tankMover != null && tankMover.movementData == null)
                tankMover.movementData = ResolveEnemyMovementData();
        }

        ConfigureEnemyHealth(enemy);
        ConfigureEnemyHealthBar(enemy);
        TrackEnemyDeath(enemy);
        AddPlayerBlocker(enemy);
        IgnoreFriendlyCollisions(enemy, fm);

        // ── Gắn TCP agent (Sprint 4 sẽ implement đầy đủ) ──────────────────
        AddGridEnemyAgentPIBTTcp(enemy, agentId, copyController);

        // ── Tắt mọi AI runtime khác ────────────────────────────────────────
        if (disableLegacyEnemyAI)
        {
            foreach (DefaultEnemyAI ai in enemy.GetComponentsInChildren<DefaultEnemyAI>(true))
                ai.enabled = false;
        }

        AIDetector detector = enemy.GetComponentInChildren<AIDetector>(true);
        if (detector != null && eagleBase != null)
            detector.Target = eagleBase.transform;
    }

    private TankControllerCopy ConfigureCopyMovementComponents(GameObject enemy)
    {
        TankMover legacyMover = enemy.GetComponentInChildren<TankMover>(true);
        if (legacyMover != null)
            legacyMover.enabled = false;

        TankMoverCopy copyMover = enemy.GetComponentInChildren<TankMoverCopy>(true);
        if (copyMover == null)
        {
            GameObject moverHost = legacyMover != null ? legacyMover.gameObject : enemy;
            copyMover = moverHost.AddComponent<TankMoverCopy>();
        }
        copyMover.movementData = ResolveEnemyMovementDataCopy();

        TankController legacyController = enemy.GetComponentInChildren<TankController>(true);
        if (legacyController != null)
            legacyController.enabled = false;

        TankControllerCopy copyController = enemy.GetComponentInChildren<TankControllerCopy>(true);
        if (copyController == null)
        {
            GameObject controllerHost = legacyController != null ? legacyController.gameObject : enemy;
            copyController = controllerHost.AddComponent<TankControllerCopy>();
        }
        copyController.tankMover = copyMover;
        copyController.aimTurret = enemy.GetComponentInChildren<AimTurret>(true);
        copyController.turrets = enemy.GetComponentsInChildren<Turret>(true);
        return copyController;
    }

    private void AddGridEnemyAgentPIBTTcp(GameObject enemy, int agentIndex, TankControllerCopy copyController = null)
    {
        if (useCopyMovementComponents)
        {
            GridEnemyAgentPIBTTcp legacyAgent = enemy.GetComponent<GridEnemyAgentPIBTTcp>();
            if (legacyAgent != null)
                legacyAgent.enabled = false;

            TankControllerCopy resolvedCopyController = copyController != null
                ? copyController
                : enemy.GetComponentInChildren<TankControllerCopy>(true);
            if (resolvedCopyController == null || eagleBase == null) return;

            GridEnemyAgentPIBTTcpCopy copyAgent = enemy.AddComponent<GridEnemyAgentPIBTTcpCopy>();
            copyAgent.mapLoader            = mapLoader;
            copyAgent.eagleTarget          = eagleBase.transform;
            copyAgent.playerTarget         = enablePlayerCombat ? GameObject.Find("Player")?.transform : null;
            copyAgent.tankController       = resolvedCopyController;
            copyAgent.sessionState         = _sessionState;
            copyAgent.agentId              = agentIndex;
            copyAgent.eagleShootingRange   = enemyEagleShootingRange;
            copyAgent.playerShootingRange  = enablePlayerCombat ? enemyPlayerShootingRange : 0f;
            copyAgent.enablePlayerCombat   = enablePlayerCombat;
            copyAgent.lineOfSightMask      = LayerMask.GetMask("Agent", "Enemy", "Player", "Hittable",
                                            WallLayerName, LegacyMovementObstacleLayerName);
            copyAgent.obstacleContactMask  = obstacleContactMask.value != 0
                ? obstacleContactMask
                : BuildObstacleContactMask();
            copyAgent.scuffTimeout         = scuffTimeout;
            copyAgent.stepInterval         = Mathf.Max(0.05f, enemyReplanInterval);

            Debug.Log($"[MapScenarioBootstrapPIBTTcp] Agent {agentIndex} (GridEnemyAgentPIBTTcpCopy) attached to {enemy.name}.");
            return;
        }

        TankController tankController = enemy.GetComponentInChildren<TankController>();
        if (tankController == null || eagleBase == null) return;

        GridEnemyAgentPIBTTcp agent = enemy.AddComponent<GridEnemyAgentPIBTTcp>();
        agent.mapLoader            = mapLoader;
        agent.eagleTarget          = eagleBase.transform;
        agent.playerTarget         = enablePlayerCombat ? GameObject.Find("Player")?.transform : null;
        agent.tankController       = tankController;
        agent.sessionState         = _sessionState;
        agent.agentId              = agentIndex;
        agent.eagleShootingRange   = enemyEagleShootingRange;
        agent.playerShootingRange  = enablePlayerCombat ? enemyPlayerShootingRange : 0f;
        agent.enablePlayerCombat   = enablePlayerCombat;
        agent.lineOfSightMask      = LayerMask.GetMask("Agent", "Enemy", "Player", "Hittable",
                                        WallLayerName, LegacyMovementObstacleLayerName);
        agent.obstacleContactMask  = obstacleContactMask.value != 0
            ? obstacleContactMask
            : BuildObstacleContactMask();
        agent.scuffTimeout         = scuffTimeout;
        agent.stepInterval         = Mathf.Max(0.05f, enemyReplanInterval);

        Debug.Log($"[MapScenarioBootstrapPIBTTcp] Agent {agentIndex} (GridEnemyAgentPIBTTcp) attached to {enemy.name}.");
    }

    // ─── Shared helpers (same pattern as MapScenarioBootstrapPIBT) ─────────

    private void IgnoreFriendlyCollisions(GameObject enemy, FactionMember fm)
    {
        Collider2D[] mine = enemy.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] == null) continue;
            FactionMember of = enemies[i].GetComponent<FactionMember>();
            if (!FactionMember.AreFriendly(fm, of)) continue;
            Collider2D[] theirs = enemies[i].GetComponentsInChildren<Collider2D>(true);
            foreach (var a in mine)
            {
                if (a == null) continue;
                foreach (var b in theirs)
                {
                    if (b == null) continue;
                    Physics2D.IgnoreCollision(a, b, true);
                }
            }
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
        if (hb == null || !hb.TryGetComponent(out RectTransform rt)) return;
        rt.anchoredPosition = new Vector2(-0.42f, 0.34f);
        rt.sizeDelta        = new Vector2(0.84f, 0.18f);
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
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] == null) continue;
            Damagable d = enemies[i].GetComponentInChildren<Damagable>();
            if (d != null && d.Health > 0) alive++;
        }
        enemiesAlive = alive;
    }

    private Text EnsureEnemyCountText()
    {
        GameObject existing = GameObject.Find("EnemyCountHud");
        Transform t = existing?.transform.Find("EnemyCountText");
        if (t != null && t.TryGetComponent(out Text existingText)) return existingText;

        GameObject canvasObj = new GameObject("EnemyCountHud");
        canvasObj.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 20;
        var sc = canvasObj.AddComponent<CanvasScaler>();
        sc.uiScaleMode        = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new UnityEngine.Vector2(1280f, 720f);
        sc.matchWidthOrHeight  = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();
        canvasObj.AddComponent<CanvasGroup>().alpha = 0.8f;

        GameObject textObj = new GameObject("EnemyCountText");
        textObj.layer = LayerMask.NameToLayer("UI");
        textObj.transform.SetParent(canvasObj.transform, false);
        var tr = textObj.AddComponent<RectTransform>();
        tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(0f, 1f);
        tr.pivot = new Vector2(0f, 1f); tr.anchoredPosition = new Vector2(24f, -72f);
        tr.sizeDelta = new Vector2(172f, 18f);
        var txt = textObj.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 14; txt.alignment = TextAnchor.MiddleLeft; txt.color = Color.white;
        return txt;
    }

    private void UpdateEnemyCountText()
    {
        if (enemyCountText == null) enemyCountText = EnsureEnemyCountText();
        if (enemyCountText != null) enemyCountText.text = $"ENEMY: {enemiesAlive}";
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
        if (src != null)
        {
            bc.size      = src.size;
            bc.offset    = src.offset;
            bc.direction = src.direction;
        }
    }

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        _tcpClient?.Dispose();
    }

    // ─── Utility ──────────────────────────────────────────────────────────────

    private void ClearScenario()
    {
        enemies.Clear();
        winTriggered = false;
        Transform existing = transform.Find("ScenarioRuntime_PIBT_TCP");
        if (existing == null) return;
        if (Application.isPlaying) Destroy(existing.gameObject);
        else DestroyImmediate(existing.gameObject);
    }

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

    private LayerMask BuildObstacleContactMask()
    {
        int w = LayerMask.NameToLayer(WallLayerName);
        return w >= 0 ? (LayerMask)(1 << w) : LayerMask.GetMask(LegacyMovementObstacleLayerName);
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

    private TankMovementData ResolveEnemyMovementDataCopy()
    {
        if (enemyMovementDataCopy != null) return enemyMovementDataCopy;
#if UNITY_EDITOR
        var r = AssetDatabase.LoadAssetAtPath<TankMovementData>(enemyMovementDataCopyPath);
        if (r != null) return r;
#endif
        return ResolveEnemyMovementData();
    }
}
