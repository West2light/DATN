using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Bản sao MapScenarioBootstrap dùng GridEnemyAgentPIBT thay GridEnemyAgent.
///
/// Thay đổi so với MapScenarioBootstrap:
///  - AddGridEnemyAgentPIBT: dùng GridEnemyAgentPIBT + frankWolfeMs
///  - Không dùng GridNavMask (PIBT tự build neighbor list)
///  - Bỏ inflate/soft-cost fields (thesis: compare A* vs PIBT thuần)
///
/// Dùng cho scene MapF_TankTest_PIBT.
/// Gắn component này cùng chỗ với MapLoader trên cùng một GameObject.
/// </summary>
public class MapScenarioBootstrapPIBT : MonoBehaviour
{
    private const string WallLayerName = "Walls";
    private const string AgentBlockerLayerName = "AgentBlocker";
    private const string LegacyMovementObstacleLayerName = "ObstaclesMovement";

    public MapLoader mapLoader;

    [Header("Eagle Base")]
    public Vector2Int eagleCell = new Vector2Int(16, 16);
    public int eagleHealth = 500;
    public Sprite eagleSprite;
    public Color eagleColor = Color.white;
    public Vector2 eagleColliderSize = new Vector2(1.3f, 1.3f);
    public bool spawnEagleNearPlayer = true;
    [Min(1)] public int eagleMinPlayerDistanceCells = 3;
    [Min(1)] public int eagleMaxPlayerDistanceCells = 7;

    [Header("Enemies")]
    public GameObject enemyPrefab;
    public string enemyPrefabPath = "Assets/Prefabs/StaticEnemy.prefab";
    public TankMovementData enemyMovementData;
    public string enemyMovementDataPath = "Assets/Data/TankData/EnemyTankMovementData.asset";
    public List<Vector2Int> enemySpawnCells = new List<Vector2Int>
    {
        new Vector2Int(30,  1),
        new Vector2Int( 1, 30),
        new Vector2Int(30, 30),
        new Vector2Int(16,  1)
    };
    public bool disableLegacyEnemyAI = true;
    [Range(0.4f, 1.2f)]
    [Tooltip("Scale of enemy tank. Lower values let agents rotate in 1-tile gaps without getting stuck.")]
    public float enemyScale = 0.72f;
    public float enemyReplanInterval = 0.75f;
    public float enemyEagleShootingRange = 5f;
    public float enemyPlayerShootingRange = 7f;
    [Min(1)]
    [Tooltip("Enemy health in the PIBT scene, matching Lvl1.")]
    public int enemyMaxHealth = 20;

    [Header("PIBT")]
    [Tooltip("Time budget (ms) for Frank-Wolfe iterations on each replan.")]
    [Min(1f)] public float frankWolfeMs = 15f;

    [Header("Stuck recovery")]
    [SerializeField, Min(0f)] private float scuffTimeout = 0.4f;
    [SerializeField] private LayerMask obstacleContactMask;

    private Transform scenarioRoot;
    private GameObject eagleBase;
    private int enemiesAlive;
    private Text enemyCountText;
    private readonly List<GameObject> enemies = new List<GameObject>();

    public GameObject EagleBase => eagleBase;
    public IReadOnlyList<GameObject> Enemies => enemies;

    // ── Entry point ────────────────────────────────────────────────────────

    [ContextMenu("Spawn Scenario Now")]
    public void SpawnScenario()
    {
        ResolveReferences();
        if (mapLoader == null || mapLoader.BuildWidth <= 0 || mapLoader.BuildHeight <= 0)
        {
            Debug.LogError("[MapScenarioBootstrapPIBT] Map must be loaded before spawning scenario.");
            return;
        }

        // Reset PIBTPlanner khi spawn lại để tránh stale agent IDs
        // (PIBTPlanner.Init sẽ tự clear nếu cùng MapLoader instance)
        PIBTPlanner.Init(mapLoader);

        ClearScenario();
        scenarioRoot = new GameObject("ScenarioRuntime_PIBT").transform;
        scenarioRoot.SetParent(transform, false);

        eagleBase = SpawnEagleBase();
        SpawnEnemies();
    }

    // ── Eagle ──────────────────────────────────────────────────────────────

    private GameObject SpawnEagleBase()
    {
        if (!TryResolveEagleSpawnCell(out Vector2Int spawnCell))
        {
            Debug.LogError("[MapScenarioBootstrapPIBT] Could not find walkable Eagle spawn cell.");
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
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeAll;

        BoxCollider2D col = eagle.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
        col.size = eagleColliderSize;

        Damagable dmg = eagle.AddComponent<Damagable>();
        dmg.OnDead = new UnityEvent();
        dmg.OnHealthChange = new UnityEvent<float>();
        dmg.OnHit = new UnityEvent();
        dmg.OnHeal = new UnityEvent();
        dmg.MaxHealth = eagleHealth;
        dmg.Health = eagleHealth;

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
        // Backtest mode không có player → spawn Eagle ở trung tâm map để công bằng.
        if (BacktestMode.IsActive)
        {
            var center = new Vector2Int(
                mapLoader.BuildStartX + mapLoader.BuildWidth  / 2,
                mapLoader.BuildStartY + mapLoader.BuildHeight / 2);
            return mapLoader.TryFindWalkableNear(center, out spawnCell);
        }

        if (spawnEagleNearPlayer)
        {
            Transform player = GameObject.Find("Player")?.transform
                ?? GameObject.Find("Player_0")?.transform;
            if (player != null && TryFindRandomWalkableNearPlayer(player, out spawnCell))
            {
                return true;
            }
        }

        return mapLoader.TryFindWalkableNear(eagleCell, out spawnCell);
    }

    private bool TryFindRandomWalkableNearPlayer(Transform player, out Vector2Int spawnCell)
    {
        Vector2Int playerCell = mapLoader.WorldToCell(player.position);
        int minDistance = Mathf.Max(1, eagleMinPlayerDistanceCells);
        int maxDistance = Mathf.Max(minDistance, eagleMaxPlayerDistanceCells);
        var allPlayerCells = new HashSet<Vector2Int>();
        foreach (var fm in FindObjectsByType<FactionMember>(FindObjectsSortMode.None))
            if (fm.CurrentFaction == Faction.Player)
                allPlayerCells.Add(mapLoader.WorldToCell(fm.GetWorldPosition()));
        allPlayerCells.Add(playerCell);

        return mapLoader.TryFindAvailableSpawnInRange(
            playerCell, allPlayerCells, minDistance, maxDistance, out spawnCell);
    }

    private Slider EnsureEagleHealthBar()
    {
        GameObject existingCanvas = GameObject.Find("EagleHealthHud");
        Transform existing = existingCanvas != null ? existingCanvas.transform.Find("HealthBar") : null;
        if (existing != null && existing.TryGetComponent(out Slider existingSlider))
        {
            return existingSlider;
        }

        GameObject canvasObject = new GameObject("EagleHealthHud");
        canvasObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 20;
        { var _sc = canvasObject.AddComponent<CanvasScaler>(); _sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; _sc.referenceResolution = new UnityEngine.Vector2(1280f, 720f); _sc.matchWidthOrHeight = 0.5f; }
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasGroup canvasGroup = canvasObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0.8f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.localScale = Vector3.one;
        canvasRect.sizeDelta = Vector2.zero;

        GameObject labelObject = new GameObject("BaseLabel");
        labelObject.layer = LayerMask.NameToLayer("UI");
        labelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(24f, -48f);
        labelRect.sizeDelta = new Vector2(46f, 18f);

        Text label = labelObject.AddComponent<Text>();
        label.text = "BASE";
        label.font = UiFontProvider.GetDefaultFont();
        label.fontSize = 14;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = Color.white;

        GameObject healthBarObject = new GameObject("HealthBar");
        healthBarObject.layer = LayerMask.NameToLayer("UI");
        healthBarObject.transform.SetParent(canvasObject.transform, false);

        RectTransform healthBarRect = healthBarObject.AddComponent<RectTransform>();
        healthBarRect.anchorMin = new Vector2(0f, 1f);
        healthBarRect.anchorMax = new Vector2(0f, 1f);
        healthBarRect.pivot = new Vector2(0f, 1f);
        healthBarRect.anchoredPosition = new Vector2(76f, -48f);
        healthBarRect.sizeDelta = new Vector2(120f, 18f);

        Image background = healthBarObject.AddComponent<Image>();
        background.color = Color.black;

        Slider slider = healthBarObject.AddComponent<Slider>();
        slider.interactable = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        slider.targetGraphic = background;

        GameObject fillObject = new GameObject("HealthFill");
        fillObject.layer = LayerMask.NameToLayer("UI");
        fillObject.transform.SetParent(healthBarObject.transform, false);

        RectTransform fillRect = fillObject.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0.5f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fill = fillObject.AddComponent<Image>();
        fill.color = Color.yellow;
        slider.fillRect = fillRect;

        return slider;
    }

    // ── Enemies ────────────────────────────────────────────────────────────

    private void SpawnEnemies()
    {
        GameObject prefab = ResolveEnemyPrefab();
        if (prefab == null)
        {
            Debug.LogError("[MapScenarioBootstrapPIBT] Enemy prefab is missing.");
            return;
        }

        enemiesAlive = 0;
        enemyCountText = EnsureEnemyCountText();
        var occupiedSpawnCells = new HashSet<Vector2Int>();
        if (eagleBase != null)
            occupiedSpawnCells.Add(mapLoader.WorldToCell(eagleBase.transform.position));
        foreach (var fm in FindObjectsByType<FactionMember>(FindObjectsSortMode.None))
            if (fm.CurrentFaction == Faction.Player)
                occupiedSpawnCells.Add(mapLoader.WorldToCell(fm.GetWorldPosition()));

        List<Vector2Int> spawnSeeds =
            (BacktestMode.IsActive && BacktestMode.AgentCount > 0)
                ? BacktestSpawn.GenerateSpawnCells(mapLoader, BacktestMode.AgentCount)
                : enemySpawnCells;

        for (int i = 0; i < spawnSeeds.Count; i++)
        {
            if (!mapLoader.TryFindAvailableSpawnNear(
                    spawnSeeds[i], occupiedSpawnCells, 2, out Vector2Int spawnCell)) continue;
            occupiedSpawnCells.Add(spawnCell);

            GameObject enemy = Instantiate(prefab, mapLoader.CellToWorld(spawnCell), Quaternion.identity, scenarioRoot);
            enemy.name = $"EnemyPIBT_{i + 1}";
            ConfigureEnemy(enemy);
            enemies.Add(enemy);
            enemiesAlive++;
        }

        UpdateEnemyCountText();
    }

    private void ConfigureEnemy(GameObject enemy)
    {
        enemy.transform.localScale = Vector3.one * enemyScale;
        FactionMember enemyFaction = FactionMember.Ensure(enemy, Faction.Enemy);
        TankMover tankMover = enemy.GetComponentInChildren<TankMover>();
        if (tankMover != null && tankMover.movementData == null)
            tankMover.movementData = ResolveEnemyMovementData();

        ConfigureEnemyHealth(enemy);
        ConfigureEnemyHealthBar(enemy);
        TrackEnemyDeath(enemy);
        AddPlayerBlocker(enemy);
        IgnoreFriendlyCollisions(enemy, enemyFaction);
        AddGridEnemyAgentPIBT(enemy);

        if (disableLegacyEnemyAI)
        {
            foreach (DefaultEnemyAI ai in enemy.GetComponentsInChildren<DefaultEnemyAI>(true))
                ai.enabled = false;
        }

        AIDetector detector = enemy.GetComponentInChildren<AIDetector>(true);
        if (detector != null && eagleBase != null)
            detector.Target = eagleBase.transform;
    }

    private void IgnoreFriendlyCollisions(GameObject enemy, FactionMember factionMember)
    {
        Collider2D[] enemyColliders = enemy.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject otherEnemy = enemies[i];
            if (otherEnemy == null)
            {
                continue;
            }

            FactionMember otherFaction = otherEnemy.GetComponent<FactionMember>();
            if (!FactionMember.AreFriendly(factionMember, otherFaction))
            {
                continue;
            }

            Collider2D[] otherColliders = otherEnemy.GetComponentsInChildren<Collider2D>(true);
            for (int enemyIndex = 0; enemyIndex < enemyColliders.Length; enemyIndex++)
            {
                Collider2D enemyCollider = enemyColliders[enemyIndex];
                if (enemyCollider == null)
                {
                    continue;
                }

                for (int otherIndex = 0; otherIndex < otherColliders.Length; otherIndex++)
                {
                    Collider2D otherCollider = otherColliders[otherIndex];
                    if (otherCollider == null)
                    {
                        continue;
                    }

                    bool shouldIgnore =
                        enemyCollider.isTrigger                            ||
                        otherCollider.isTrigger                            ||
                        enemyCollider.gameObject.name == "PlayerBlocker"   ||
                        otherCollider.gameObject.name == "PlayerBlocker";
                    if (shouldIgnore)
                    {
                        Physics2D.IgnoreCollision(enemyCollider, otherCollider, true);
                    }
                }
            }
        }
    }

    private void ConfigureEnemyHealth(GameObject enemy)
    {
        Damagable damagable = enemy.GetComponentInChildren<Damagable>();
        if (damagable == null) return;

        damagable.MaxHealth = enemyMaxHealth;
        damagable.Health = enemyMaxHealth;
    }

    private void ConfigureEnemyHealthBar(GameObject enemy)
    {
        Transform healthBar = enemy.transform.Find("Canvas/HealthBar");
        if (healthBar == null || !healthBar.TryGetComponent(out RectTransform rectTransform)) return;

        rectTransform.anchoredPosition = new Vector2(-0.42f, 0.34f);
        rectTransform.sizeDelta = new Vector2(0.84f, 0.18f);
    }

    private void TrackEnemyDeath(GameObject enemy)
    {
        Damagable damagable = enemy.GetComponentInChildren<Damagable>();
        if (damagable == null) return;

        damagable.OnDead.RemoveListener(OnEnemyDead);
        damagable.OnDead.AddListener(OnEnemyDead);
    }

    private bool winTriggered;

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
            GameObject enemy = enemies[i];
            if (enemy == null) continue;

            Damagable damagable = enemy.GetComponentInChildren<Damagable>();
            if (damagable != null && damagable.Health > 0)
            {
                alive++;
            }
        }

        enemiesAlive = alive;
    }

    private Text EnsureEnemyCountText()
    {
        GameObject existingCanvas = GameObject.Find("EnemyCountHud");
        Transform existing = existingCanvas != null ? existingCanvas.transform.Find("EnemyCountText") : null;
        if (existing != null && existing.TryGetComponent(out Text existingText))
        {
            return existingText;
        }

        GameObject canvasObject = new GameObject("EnemyCountHud");
        canvasObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 20;
        { var _sc = canvasObject.AddComponent<CanvasScaler>(); _sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; _sc.referenceResolution = new UnityEngine.Vector2(1280f, 720f); _sc.matchWidthOrHeight = 0.5f; }
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasGroup canvasGroup = canvasObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0.8f;

        GameObject textObject = new GameObject("EnemyCountText");
        textObject.layer = LayerMask.NameToLayer("UI");
        textObject.transform.SetParent(canvasObject.transform, false);

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = new Vector2(24f, -72f);
        textRect.sizeDelta = new Vector2(172f, 18f);

        Text text = textObject.AddComponent<Text>();
        text.font = UiFontProvider.GetDefaultFont();
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;

        return text;
    }

    private void UpdateEnemyCountText()
    {
        if (enemyCountText == null)
        {
            enemyCountText = EnsureEnemyCountText();
        }

        if (enemyCountText != null)
        {
            enemyCountText.text = $"ENEMY: {enemiesAlive}";
        }
    }

    private void AddGridEnemyAgentPIBT(GameObject enemy)
    {
        TankController tankController = enemy.GetComponentInChildren<TankController>();
        if (tankController == null || eagleBase == null) return;

        GridEnemyAgentPIBT agent = enemy.AddComponent<GridEnemyAgentPIBT>();
        agent.mapLoader = mapLoader;
        agent.eagleTarget = eagleBase.transform;
        agent.playerTarget = GameObject.Find("Player")?.transform;
        agent.tankController = tankController;
        agent.replanInterval = enemyReplanInterval;
        agent.frankWolfeMs = frankWolfeMs;
        agent.eagleShootingRange = enemyEagleShootingRange;
        agent.playerShootingRange = enemyPlayerShootingRange;
        agent.lineOfSightMask = LayerMask.GetMask("Agent", "Enemy", "Player", "Hittable",
                                       WallLayerName, LegacyMovementObstacleLayerName);
        agent.obstacleContactMask = obstacleContactMask.value != 0
            ? obstacleContactMask
            : BuildObstacleContactMask();
        agent.scuffTimeout = scuffTimeout;
    }

    private void AddPlayerBlocker(GameObject enemy)
    {
        TankController tankController = enemy.GetComponentInChildren<TankController>();
        if (tankController == null) return;

        GameObject blocker = new GameObject("PlayerBlocker");
        blocker.layer = ResolveLayer(AgentBlockerLayerName, LegacyMovementObstacleLayerName);
        blocker.transform.SetParent(tankController.transform, false);

        CapsuleCollider2D src = tankController.GetComponent<CapsuleCollider2D>();
        CapsuleCollider2D blockerCol = blocker.AddComponent<CapsuleCollider2D>();
        if (src != null)
        {
            blockerCol.size = src.size;
            blockerCol.offset = src.offset;
            blockerCol.direction = src.direction;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ResolveReferences()
    {
        if (mapLoader == null) mapLoader = GetComponent<MapLoader>();
        if (mapLoader == null) mapLoader = FindFirstObjectByType<MapLoader>();
    }

    private int ResolveLayer(string layerName, string fallback)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : LayerMask.NameToLayer(fallback);
    }

    private LayerMask BuildObstacleContactMask()
    {
        int wallLayer = LayerMask.NameToLayer(WallLayerName);
        return wallLayer >= 0 ? (LayerMask)(1 << wallLayer) : LayerMask.GetMask(LegacyMovementObstacleLayerName);
    }

    private void ClearScenario()
    {
        enemies.Clear();
        Transform existing = transform.Find("ScenarioRuntime_PIBT");
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
        var result = AssetDatabase.LoadAssetAtPath<GameObject>(enemyPrefabPath);
        if (result != null) return result;
#endif
        return Resources.Load<GameObject>("Prefabs/StaticEnemy");
    }

    private TankMovementData ResolveEnemyMovementData()
    {
        if (enemyMovementData != null) return enemyMovementData;
#if UNITY_EDITOR
        var result = AssetDatabase.LoadAssetAtPath<TankMovementData>(enemyMovementDataPath);
        if (result != null) return result;
#endif
        return Resources.Load<TankMovementData>("Data/EnemyTankMovementData");
    }
}
