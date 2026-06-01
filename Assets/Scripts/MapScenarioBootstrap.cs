using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MapScenarioBootstrap : MonoBehaviour
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
        new Vector2Int(30, 1),
        new Vector2Int(1, 30),
        new Vector2Int(30, 30),
        new Vector2Int(16, 1)
    };
    public bool disableLegacyEnemyAI = true;
    public int enemyMaxHealth = 20;
    [Range(0.4f, 1.2f)]
    [Tooltip("Scale of enemy tank. Lower values let agents rotate in 1-tile gaps without getting stuck.")]
    public float enemyScale = 0.72f;
    public float enemyReplanInterval = 0.75f;
    public float enemyEagleShootingRange = 5f;
    public float enemyPlayerShootingRange = 7f;
    public bool loadNextMapWhenAllEnemiesDead = true;
    public string nextMapSceneName = "MapF_TankTest_LNS2";
    [Min(0f)] public float nextMapLoadDelay = 1f;

    [Header("Phase v4.2: Physical hard inflate (preferred)")]
    [Tooltip("Radius (world units) of the circle used by Physics2D.OverlapCircle to "
        + "decide whether a tank can stand on a given cell. Default 0.45 = tank "
        + "half-extent 0.348 + safety margin 0.1.")]
    [SerializeField, Min(0f)] private float tankPhysicalRadius = 0.45f;
    [Tooltip("Layers treated as hard obstacles for the physical inflate test. "
        + "If 0, falls back to LayerMask.GetMask(\"Walls\") at run time; legacy "
        + "ObstaclesMovement is only used if Walls is unavailable.")]
    [SerializeField] private LayerMask obstacleLayerMask = 0;
    [Tooltip("Ablation toggle (thesis). When true, navMask uses the legacy "
        + "Chebyshev cell-grid inflate (agentInflateRadius) instead of the "
        + "v4.2 physical OverlapCircle inflate.")]
    [SerializeField] private bool useChebyshevInflateLegacy = false;

    [Header("Soft cost layer (Phase A) and legacy Chebyshev inflate")]
    // agentInflateRadius: deprecated by Phase v4.2 physical inflate.
    // Retained as the radius used by the Chebyshev fallback when
    // useChebyshevInflateLegacy == true (ablation study).
    [Range(0, 3)] public int agentInflateRadius = 1;
    [Range(0, 4)] public int agentSoftRadius = 2;
    [Range(0, 50)] public int agentSoftCostNear = 8;
    [Range(0, 50)] public int agentSoftCostMid = 2;
    [SerializeField, Min(0f)] private float tankClearanceRadius = 0.4f;
    // V4 default: smoothing OFF. Path raw 4-neighbor for deterministic follow.
    // Toggle ON only for ablation study (thesis).
    [SerializeField] private bool agentEnableSmoothing = false;
    [SerializeField, Min(0f)] private float scuffTimeout = 0.4f;
    [SerializeField] private LayerMask obstacleContactMask;
    public bool drawAgentNavMask = true;
    public bool drawNavMaskHeatmap = true;

    private Transform scenarioRoot;
    private GameObject eagleBase;
    private GridNavMask navMask;
    private int enemiesAlive;
    private Text enemyCountText;
    private bool nextMapLoading;
    private readonly List<GameObject> enemies = new List<GameObject>();

    public GameObject EagleBase => eagleBase;
    public IReadOnlyList<GameObject> Enemies => enemies;

    private void Reset()
    {
        // Default the obstacle mask so freshly added components Just Work in
        // Edit Mode without manual Inspector wiring.
        obstacleLayerMask = LayerMask.GetMask(WallLayerName);
    }

    [ContextMenu("Spawn Scenario Now")]
    public void SpawnScenario()
    {
        ResolveReferences();
        if (mapLoader == null || mapLoader.BuildWidth <= 0 || mapLoader.BuildHeight <= 0)
        {
            Debug.LogError("[MapScenarioBootstrap] Map must be loaded before spawning scenario.");
            return;
        }

        ClearScenario();
        scenarioRoot = new GameObject("ScenarioRuntime").transform;
        scenarioRoot.SetParent(transform, false);
        navMask = BuildNavMask();

        eagleBase = SpawnEagleBase();
        SpawnEnemies();
    }

    private GridNavMask BuildNavMask()
    {
        if (useChebyshevInflateLegacy)
        {
            // Ablation mode: legacy Chebyshev cell-grid inflate.
            return new GridNavMask(mapLoader, agentInflateRadius, agentSoftRadius, agentSoftCostNear, agentSoftCostMid);
        }

        // V4.2 default: physical-based hard inflate via Physics2D.OverlapCircle.
        // Physics2D queries can lag a frame behind Transform writes when colliders
        // were just created; a sync makes this correct in both Play and Edit Mode.
        Physics2D.SyncTransforms();

        LayerMask mask = obstacleLayerMask.value != 0
            ? obstacleLayerMask
            : LayerMask.GetMask(WallLayerName);

        if (mask.value == 0)
        {
            mask = LayerMask.GetMask(LegacyMovementObstacleLayerName);
        }

        // TODO(MapLoader): expose obstacle collider count so we can defensively
        // warn when SpawnScenario runs before LoadAndBuild (would produce an
        // empty hard-block mask). MapLoader.cs is out of scope for this dispatch.

        return new GridNavMask(mapLoader, tankPhysicalRadius, mask, agentSoftRadius, agentSoftCostNear, agentSoftCostMid);
    }

    private void ResolveReferences()
    {
        if (mapLoader == null)
        {
            mapLoader = GetComponent<MapLoader>();
        }

        if (mapLoader == null)
        {
            mapLoader = FindFirstObjectByType<MapLoader>();
        }

    }

    private GameObject SpawnEagleBase()
    {
        if (!TryResolveEagleSpawnCell(out Vector2Int spawnCell))
        {
            Debug.LogError("[MapScenarioBootstrap] Could not find a walkable Eagle spawn cell.");
            return null;
        }

        GameObject eagle = new GameObject("EagleBase");
        eagle.layer = LayerMask.NameToLayer("Hittable");
        eagle.transform.SetParent(scenarioRoot, false);
        eagle.transform.position = mapLoader.CellToWorld(spawnCell);
        FactionMember.Ensure(eagle, Faction.Base);

        SpriteRenderer spriteRenderer = eagle.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = eagleSprite != null ? eagleSprite : CreateWhiteSprite();
        spriteRenderer.color = eagleColor;
        spriteRenderer.sortingLayerName = "Eagle";
        spriteRenderer.sortingOrder = 10;

        Rigidbody2D rigidbody2D = eagle.AddComponent<Rigidbody2D>();
        rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
        rigidbody2D.gravityScale = 0f;
        rigidbody2D.constraints = RigidbodyConstraints2D.FreezeAll;

        BoxCollider2D collider = eagle.AddComponent<BoxCollider2D>();
        collider.isTrigger = false;
        collider.size = eagleColliderSize;

        Damagable damagable = eagle.AddComponent<Damagable>();
        damagable.OnDead = new UnityEvent();
        damagable.OnHealthChange = new UnityEvent<float>();
        damagable.OnHit = new UnityEvent();
        damagable.OnHeal = new UnityEvent();
        damagable.MaxHealth = eagleHealth;
        damagable.Health = eagleHealth;

        DestroyUtil destroyUtil = eagle.AddComponent<DestroyUtil>();
        damagable.OnDead.AddListener(destroyUtil.DestroyHelper);

        MapGameOverController gameOverController = MapGameOverController.Ensure();
        damagable.OnDead.AddListener(gameOverController.BeginGameOver);

        Slider healthBar = EnsureEagleHealthBar();
        if (healthBar != null)
        {
            healthBar.value = 1f;
            damagable.OnHealthChange.AddListener(healthBar.SetValueWithoutNotify);
        }

        return eagle;
    }

    private bool TryResolveEagleSpawnCell(out Vector2Int spawnCell)
    {
        if (spawnEagleNearPlayer)
        {
            Transform player = GameObject.Find("Player")?.transform;
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
        int minDistanceSqr = minDistance * minDistance;
        int maxDistanceSqr = maxDistance * maxDistance;

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int y = playerCell.y - maxDistance; y <= playerCell.y + maxDistance; y++)
        {
            for (int x = playerCell.x - maxDistance; x <= playerCell.x + maxDistance; x++)
            {
                Vector2Int candidate = new Vector2Int(x, y);
                Vector2Int delta = candidate - playerCell;
                int distanceSqr = delta.sqrMagnitude;
                if (distanceSqr < minDistanceSqr || distanceSqr > maxDistanceSqr) continue;
                if (!mapLoader.IsWalkable(candidate)) continue;

                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            spawnCell = default;
            return false;
        }

        spawnCell = candidates[Random.Range(0, candidates.Count)];
        return true;
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
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

    private void SpawnEnemies()
    {
        GameObject prefab = ResolveEnemyPrefab();
        if (prefab == null)
        {
            Debug.LogError("[MapScenarioBootstrap] Enemy prefab is missing.");
            return;
        }

        enemiesAlive = 0;
        enemyCountText = EnsureEnemyCountText();

        for (int i = 0; i < enemySpawnCells.Count; i++)
        {
            if (!mapLoader.TryFindWalkableNear(enemySpawnCells[i], out Vector2Int spawnCell))
            {
                continue;
            }

            GameObject enemy = Instantiate(prefab, mapLoader.CellToWorld(spawnCell), Quaternion.identity, scenarioRoot);
            enemy.name = $"Enemy_{i + 1}";
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
        {
            tankMover.movementData = ResolveEnemyMovementData();
        }

        AddPlayerBlocker(enemy);
        IgnoreFriendlyCollisions(enemy, enemyFaction);
        ConfigureEnemyHealth(enemy);
        ConfigureEnemyHealthBar(enemy);
        AddGridEnemyAgent(enemy);
        TrackEnemyDeath(enemy);

        if (disableLegacyEnemyAI)
        {
            DefaultEnemyAI[] aiComponents = enemy.GetComponentsInChildren<DefaultEnemyAI>(true);
            for (int i = 0; i < aiComponents.Length; i++)
            {
                aiComponents[i].enabled = false;
            }
        }

        AIDetector detector = enemy.GetComponentInChildren<AIDetector>(true);
        if (detector != null && eagleBase != null)
        {
            detector.Target = eagleBase.transform;
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

                    Physics2D.IgnoreCollision(enemyCollider, otherCollider, true);
                }
            }
        }
    }

    private void TrackEnemyDeath(GameObject enemy)
    {
        Damagable damagable = enemy.GetComponentInChildren<Damagable>();
        if (damagable == null) return;

        damagable.OnDead.RemoveListener(OnEnemyDead);
        damagable.OnDead.AddListener(OnEnemyDead);
    }

    private void OnEnemyDead()
    {
        RecountAliveEnemies();
        UpdateEnemyCountText();

        if (enemiesAlive == 0 && !nextMapLoading)
        {
            nextMapLoading = true;
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
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

    private void AddGridEnemyAgent(GameObject enemy)
    {
        TankController tankController = enemy.GetComponentInChildren<TankController>();
        if (tankController == null || eagleBase == null)
        {
            return;
        }

        GridEnemyAgent agent = enemy.AddComponent<GridEnemyAgent>();
        agent.mapLoader = mapLoader;
        agent.navMask = navMask;
        agent.tankClearanceRadius = tankClearanceRadius;
        agent.enableSmoothing = agentEnableSmoothing;
        agent.eagleTarget = eagleBase.transform;
        agent.playerTarget = GameObject.Find("Player")?.transform;
        agent.tankController = tankController;
        agent.replanInterval = enemyReplanInterval;
        agent.eagleShootingRange = enemyEagleShootingRange;
        agent.playerShootingRange = enemyPlayerShootingRange;
        agent.lineOfSightMask = LayerMask.GetMask("Agent", "Enemy", "Player", "Hittable", WallLayerName, LegacyMovementObstacleLayerName);
        agent.obstacleContactMask = obstacleContactMask.value != 0
            ? obstacleContactMask
            : BuildObstacleContactMask();
        agent.scuffTimeout = scuffTimeout;
    }

    private void AddPlayerBlocker(GameObject enemy)
    {
        TankController tankController = enemy.GetComponentInChildren<TankController>();
        if (tankController == null)
        {
            return;
        }

        GameObject blocker = new GameObject("PlayerBlocker");
        blocker.layer = ResolveLayer(AgentBlockerLayerName, LegacyMovementObstacleLayerName);
        blocker.transform.SetParent(tankController.transform, false);

        CapsuleCollider2D sourceCollider = tankController.GetComponent<CapsuleCollider2D>();
        CapsuleCollider2D blockerCollider = blocker.AddComponent<CapsuleCollider2D>();
        if (sourceCollider != null)
        {
            blockerCollider.size = sourceCollider.size;
            blockerCollider.offset = sourceCollider.offset;
            blockerCollider.direction = sourceCollider.direction;
        }
    }

    private int ResolveLayer(string layerName, string fallbackLayerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : LayerMask.NameToLayer(fallbackLayerName);
    }

    private LayerMask BuildObstacleContactMask()
    {
        int wallLayer = LayerMask.NameToLayer(WallLayerName);
        if (wallLayer >= 0)
        {
            return 1 << wallLayer;
        }

        return LayerMask.GetMask(LegacyMovementObstacleLayerName);
    }

    private void ClearScenario()
    {
        enemies.Clear();
        Transform existingRoot = transform.Find("ScenarioRuntime");
        if (existingRoot == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(existingRoot.gameObject);
        }
        else
        {
            DestroyImmediate(existingRoot.gameObject);
        }
    }

    private Sprite CreateWhiteSprite()
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
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

    private void OnDrawGizmosSelected()
    {
        if (mapLoader == null || navMask == null)
        {
            return;
        }

        float tile = mapLoader.tileSize;
        Vector3 halfExtents = Vector3.one * tile * 0.35f;
        Vector3 cellCubeSize = new Vector3(tile, tile, 0.01f) * 0.9f;

        for (int y = 0; y < mapLoader.Height; y++)
        {
            for (int x = 0; x < mapLoader.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                bool walkable = navMask.IsAgentWalkable(cell);
                Vector3 center = mapLoader.CellToWorld(cell);

                // Heatmap: filled red squares colored by soft-cost tier.
                if (drawNavMaskHeatmap && walkable)
                {
                    int cost = navMask.GetCellCost(cell);
                    if (cost == navMask.SoftCostNear && cost > 0)
                    {
                        Gizmos.color = new Color(1f, 0f, 0f, 0.6f); // đỏ đậm
                        Gizmos.DrawCube(center, cellCubeSize);
                    }
                    else if (cost == navMask.SoftCostMid && cost > 0)
                    {
                        Gizmos.color = new Color(1f, 0f, 0f, 0.3f); // đỏ nhạt
                        Gizmos.DrawCube(center, cellCubeSize);
                    }
                    // cost == 0 → no draw.
                }

                // Inflated/blocked-cell X marker (existing visual aid).
                if (drawAgentNavMask && !walkable)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(center - halfExtents, center + halfExtents);
                    Gizmos.DrawLine(
                        center + new Vector3(-halfExtents.x, halfExtents.y, 0f),
                        center + new Vector3(halfExtents.x, -halfExtents.y, 0f));
                }
            }
        }
    }
}
