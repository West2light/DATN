using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MapTankTestBootstrap : MonoBehaviour
{
    public MapLoader mapLoader;
    public GameObject tankPrefab;
    public string tankPrefabPath = "Assets/Prefabs/Tank.prefab";
    public TankMovementData movementData;
    public string movementDataPath = "Assets/Data/TankData/PlayerTankMovementData.asset";
    public Vector2Int playerSpawnCell = new Vector2Int(1, 1);

    [Range(0.5f, 3f)]
    [Tooltip("Hệ số phóng to tank người chơi so với 1 ô map")]
    public float playerScale = 1.3f;
    [Min(1)]
    [Tooltip("Máu player trong các scene MapF, khớp với Lvl1.")]
    public int playerMaxHealth = 20;
    [Tooltip("Hiện thanh máu player ở HUD góc trái trong các scene MapF.")]
    public bool showPlayerHealthBar = true;
    public Camera mainCamera;

    [Range(0.3f, 1f)]
    [Tooltip("Hệ số zoom: 1 = viewport vừa khít cạnh ngắn của map, nhỏ hơn = zoom gần hơn")]
    public float cameraZoom = 0.7f;
    public Vector3 cameraOffset = new Vector3(0f, 0f, -10f);

    private Transform player;
    private float mapWidthWorld;
    private float mapHeightWorld;

    private void Start()
    {
        if (mapLoader == null)
        {
            mapLoader = GetComponent<MapLoader>();
        }

        if (mapLoader == null)
        {
            mapLoader = FindFirstObjectByType<MapLoader>();
        }

        if (mapLoader == null)
        {
            Debug.LogError("[MapTankTestBootstrap] MapLoader is missing.");
            return;
        }

        mapLoader.LoadAndBuild();
        SpawnPlayer();
        SetupCamera();
        SpawnScenario();
    }

    private void LateUpdate()
    {
        UpdateCameraPosition();
    }

    private void SpawnScenario()
    {
        MapScenarioBootstrapLNS2 lns2Bootstrap = GetComponent<MapScenarioBootstrapLNS2>();
        if (lns2Bootstrap != null)
        {
            lns2Bootstrap.mapLoader = mapLoader;
            lns2Bootstrap.SpawnScenario();
            return;
        }

        MapScenarioBootstrap scenarioBootstrap = GetComponent<MapScenarioBootstrap>();
        if (scenarioBootstrap != null)
        {
            scenarioBootstrap.mapLoader = mapLoader;
            scenarioBootstrap.SpawnScenario();
        }
    }

    private void SpawnPlayer()
    {
        if (!mapLoader.TryFindWalkableNear(playerSpawnCell, out Vector2Int spawnCell))
        {
            Debug.LogError("[MapTankTestBootstrap] Could not find a walkable spawn cell.");
            return;
        }

        GameObject prefab = ResolveTankPrefab();
        if (prefab == null)
        {
            Debug.LogError("[MapTankTestBootstrap] Tank prefab is missing.");
            return;
        }

        GameObject tank = Instantiate(prefab, mapLoader.CellToWorld(spawnCell), Quaternion.identity);
        tank.name = "Player";
        player = tank.transform;
        ConfigureTank(tank);
        WirePlayerInput(tank);
    }

    private void ConfigureTank(GameObject tank)
    {
        tank.transform.localScale = Vector3.one * playerScale;
        FactionMember.Ensure(tank, Faction.Player);

        TankMover tankMover = tank.GetComponentInChildren<TankMover>();
        if (tankMover != null && tankMover.movementData == null)
        {
            tankMover.movementData = ResolveMovementData();
        }

        Damagable damagable = tank.GetComponentInChildren<Damagable>();
        if (damagable != null)
        {
            damagable.MaxHealth = playerMaxHealth;
            damagable.Health = playerMaxHealth;

            DestroyUtil destroyUtil = tank.GetComponent<DestroyUtil>();
            if (destroyUtil == null)
            {
                destroyUtil = tank.AddComponent<DestroyUtil>();
            }

            damagable.OnDead.RemoveListener(destroyUtil.DestroyHelper);
            damagable.OnDead.AddListener(destroyUtil.DestroyHelper);

            MapGameOverController gameOverController = MapGameOverController.Ensure();
            damagable.OnDead.RemoveListener(gameOverController.BeginGameOver);
            damagable.OnDead.AddListener(gameOverController.BeginGameOver);

            if (showPlayerHealthBar)
            {
                Slider healthBar = EnsurePlayerHealthBar();
                if (healthBar != null)
                {
                    healthBar.value = 1f;
                    damagable.OnHealthChange.RemoveListener(healthBar.SetValueWithoutNotify);
                    damagable.OnHealthChange.AddListener(healthBar.SetValueWithoutNotify);
                }
            }
        }
    }

    private Slider EnsurePlayerHealthBar()
    {
        GameObject existingCanvas = GameObject.Find("PlayerHealthHud");
        Transform existing = existingCanvas != null ? existingCanvas.transform.Find("HealthBar") : null;
        if (existing != null && existing.TryGetComponent(out Slider existingSlider))
        {
            return existingSlider;
        }

        GameObject canvasObject = new GameObject("PlayerHealthHud");
        canvasObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 20;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasGroup canvasGroup = canvasObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0.8f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.localScale = Vector3.one;
        canvasRect.sizeDelta = Vector2.zero;

        GameObject labelObject = new GameObject("HpLabel");
        labelObject.layer = LayerMask.NameToLayer("UI");
        labelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(24f, -24f);
        labelRect.sizeDelta = new Vector2(46f, 18f);

        Text label = labelObject.AddComponent<Text>();
        label.text = "HP";
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
        healthBarRect.anchoredPosition = new Vector2(76f, -24f);
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
        fill.color = Color.red;
        slider.fillRect = fillRect;

        return slider;
    }

    private void WirePlayerInput(GameObject tank)
    {
        TankController controller = tank.GetComponent<TankController>();
        if (controller == null)
        {
            Debug.LogError("[MapTankTestBootstrap] Spawned tank has no TankController.");
            return;
        }

        PlayerInput input = tank.GetComponent<PlayerInput>();
        if (input == null)
        {
            input = tank.AddComponent<PlayerInput>();
        }

        input.OnShoot.RemoveListener(controller.HandleShoot);
        input.OnMoveBody.RemoveListener(controller.HandleMoveBody);
        input.OnMoveBody.RemoveListener(controller.HandleMoveWorldDirection);
        input.OnMoveTurret.RemoveListener(controller.HandleTurretMovement);
        input.OnShoot.AddListener(controller.HandleShoot);
        input.useWorldMovement = true;
        input.OnMoveTurret.AddListener(controller.HandleTurretMovement);
    }

    private void SetupCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            Debug.LogError("[MapTankTestBootstrap] Main Camera is missing.");
            return;
        }

        mainCamera.orthographic = true;

        mapWidthWorld = mapLoader.BuildWidth * mapLoader.tileSize;
        mapHeightWorld = mapLoader.BuildHeight * mapLoader.tileSize;

        // Kích thước orthographic lớn nhất mà viewport vẫn nằm gọn trong map
        // (không lộ vùng nền xanh ngoài map). cameraZoom < 1 để zoom gần hơn.
        float maxSizeByHeight = mapHeightWorld / 2f;
        float maxSizeByWidth = mapWidthWorld / (2f * mainCamera.aspect);
        float fitSize = Mathf.Min(maxSizeByHeight, maxSizeByWidth);

        mainCamera.orthographicSize = Mathf.Max(0.01f, fitSize * cameraZoom);

        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        if (player == null || mainCamera == null)
        {
            return;
        }

        Vector3 target = player.position + cameraOffset;

        // Kẹp camera trong biên map để không bao giờ lộ vùng ngoài map.
        float halfHeight = mainCamera.orthographicSize;
        float halfWidth = halfHeight * mainCamera.aspect;

        float minX = -mapWidthWorld / 2f + halfWidth;
        float maxX = mapWidthWorld / 2f - halfWidth;
        float minY = -mapHeightWorld / 2f + halfHeight;
        float maxY = mapHeightWorld / 2f - halfHeight;

        // Nếu viewport rộng/cao hơn map theo trục nào đó thì căn giữa trục đó.
        target.x = minX <= maxX ? Mathf.Clamp(target.x, minX, maxX) : 0f;
        target.y = minY <= maxY ? Mathf.Clamp(target.y, minY, maxY) : 0f;

        mainCamera.transform.position = target;
    }

    private GameObject ResolveTankPrefab()
    {
        if (tankPrefab != null)
        {
            return tankPrefab;
        }

#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<GameObject>(tankPrefabPath);
#else
        return null;
#endif
    }

    private TankMovementData ResolveMovementData()
    {
        if (movementData != null)
        {
            return movementData;
        }

#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<TankMovementData>(movementDataPath);
#else
        return null;
#endif
    }
}
