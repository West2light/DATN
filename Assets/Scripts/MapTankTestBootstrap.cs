using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
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
    public AudioClip playerEngineClip;
    public string playerEngineClipPath = "Assets/Audio/Kenny Assets/Si-fi sounds/Audio/spaceEngineSmall_001.ogg";
    public Vector2Int playerSpawnCell = new Vector2Int(1, 1);
    [Min(3)]
    [Tooltip("Số ô walkable liên thông tối thiểu tại điểm spawn, tránh player bị kẹt trong hốc.")]
    public int playerSpawnMinRegionSize = 9;
    [Range(1, 4)]
    [Tooltip("Số lối thoát trực tiếp (4 hướng) tối thiểu tại ô spawn. Giá trị 2 đảm bảo không phải ngõ cụt.")]
    public int playerSpawnMinNeighbors = 2;

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
    private bool allowCameraFollow = true;

    // Spectator mode: activated when the local player's tank is destroyed in LAN mode.
    private bool    _spectating;
    private Vector2 _spectatorPos;

    private IEnumerator Start()
    {
        if (mapLoader == null)
            mapLoader = GetComponent<MapLoader>();
        if (mapLoader == null)
            mapLoader = FindFirstObjectByType<MapLoader>();
        if (mapLoader == null)
        {
            Debug.LogError("[MapTankTestBootstrap] MapLoader is missing.");
            yield break;
        }

        mapLoader.LoadAndBuild();
        while (mapLoader.IsLoading)
            yield return null;

        if (!mapLoader.IsLoaded)
        {
            Debug.LogError($"[MapTankTestBootstrap] Map failed to load. {mapLoader.LastLoadError}");
            yield break;
        }

        // ── LAN multiplayer ──────────────────────────────────────────────────
        if (LanSessionManager.IsActive)
        {
            SetupCamera();

            if (LanSessionManager.IsServer)
            {
                // Server: spawn all player tanks + enemies, then link to bridges
                var lanCoord = gameObject.AddComponent<LanGameCoordinator>();
                var spawnedTanks = SpawnAllLanPlayers();
                var spawnCells   = ComputeEnemySpawnCells();
                MapScenarioBootstrap scenario = GetComponent<MapScenarioBootstrap>()
                    ?? GetComponentInChildren<MapScenarioBootstrap>();
                if (scenario != null)
                {
                    scenario.mapLoader = mapLoader;
                    scenario.enemySpawnCells = spawnCells;
                }
                SpawnScenario();                       // spawns enemies
                var enemies = GetSpawnedEnemies();

                // Wire all player transforms so every enemy targets every player.
                var playerTransforms = new Transform[spawnedTanks.Count];
                for (int i = 0; i < spawnedTanks.Count; i++)
                    playerTransforms[i] = spawnedTanks[i].transform;
                foreach (var enemy in enemies)
                {
                    var agent = enemy.GetComponent<GridEnemyAgent>();
                    if (agent != null) agent.playerTargets = playerTransforms;
                    var agentLns2 = enemy.GetComponent<GridEnemyAgentPIBT>();
                    if (agentLns2 != null) agentLns2.playerTargets = playerTransforms;
                }

                lanCoord.RegisterServerTanks(spawnedTanks, enemies);
            }
            else
            {
                // Client: add view immediately so Instance is ready before first RPC arrives.
                gameObject.AddComponent<LanClientView>();
                Debug.Log("[LAN Client] LanClientView created. Waiting for server state...");
            }
            yield break;
        }
        // ── End LAN multiplayer ──────────────────────────────────────────────

        if (!BacktestMode.IsActive)
            SpawnPlayer();

        SetupCamera();

        if (!BacktestMode.IsActive && MapPlacementPhase.ShouldTrigger())
        {
            allowCameraFollow = false;
            MapPlacementPhase phase = gameObject.AddComponent<MapPlacementPhase>();
            phase.BeginPhase(mapLoader, mainCamera, OnPlacementDone, player);
        }
        else
        {
            SpawnScenario();
        }
    }

    private void OnPlacementDone()
    {
        allowCameraFollow = true;
        SetupCamera();
        SpawnScenario();
    }

    private void LateUpdate()
    {
        // LAN client: pick up OwnGhost once lazy-init creates it, then re-setup camera.
        if (!_spectating && LanSessionManager.IsActive && !LanSessionManager.IsServer)
        {
            Transform ghost = LanClientView.Instance?.OwnGhost;
            if (ghost != null && player != ghost) { player = ghost; SetupCamera(); }

            // Own tank just died — switch to spectator mode.
            if (LanClientView.Instance?.IsSpectating == true)
            {
                _spectating   = true;
                _spectatorPos = mainCamera != null
                    ? (Vector2)mainCamera.transform.position
                    : Vector2.zero;
            }
        }

        if (_spectating)
        {
            HandleSpectatorCamera();
            return;
        }

        UpdateCameraPosition();
    }

    // Free-roam camera for spectators: WASD pans within map bounds.
    private void HandleSpectatorCamera()
    {
        if (mainCamera == null) return;
        float speed = mainCamera.orthographicSize * 2f;
        _spectatorPos.x += Input.GetAxisRaw("Horizontal") * speed * Time.deltaTime;
        _spectatorPos.y += Input.GetAxisRaw("Vertical")   * speed * Time.deltaTime;

        float halfH = mainCamera.orthographicSize;
        float halfW = halfH * mainCamera.aspect;
        _spectatorPos.x = Mathf.Clamp(_spectatorPos.x,
            -mapWidthWorld  / 2f + halfW, mapWidthWorld  / 2f - halfW);
        _spectatorPos.y = Mathf.Clamp(_spectatorPos.y,
            -mapHeightWorld / 2f + halfH, mapHeightWorld / 2f - halfH);

        mainCamera.transform.position = new Vector3(
            _spectatorPos.x, _spectatorPos.y, cameraOffset.z);
    }

    private void SpawnScenario()
    {
        List<Vector2Int> spawnCells = ComputeEnemySpawnCells();

        MapScenarioBootstrapPIBT pibtBootstrap = GetComponent<MapScenarioBootstrapPIBT>();
        if (pibtBootstrap != null)
        {
            pibtBootstrap.mapLoader = mapLoader;
            if (spawnCells != null) pibtBootstrap.enemySpawnCells = spawnCells;
            pibtBootstrap.SpawnScenario();
            return;
        }

        MapScenarioBootstrap scenarioBootstrap = GetComponent<MapScenarioBootstrap>();
        if (scenarioBootstrap != null)
        {
            scenarioBootstrap.mapLoader = mapLoader;
            if (spawnCells != null) scenarioBootstrap.enemySpawnCells = spawnCells;
            scenarioBootstrap.SpawnScenario();
        }
    }

    private List<Vector2Int> ComputeEnemySpawnCells()
    {
        if (mapLoader == null) return null;
        int c = mapLoader.BuildWidth;
        int r = mapLoader.BuildHeight;

        // Base 6 spawn positions (corners + mid-edges)
        var baseSet = new List<Vector2Int>
        {
            new Vector2Int(c - 2, 1),
            new Vector2Int(1,     r - 2),
            new Vector2Int(c - 2, r - 2),
            new Vector2Int(c / 2, 1),
            new Vector2Int(1,     r / 2),
            new Vector2Int(c - 2, r / 2),
        };

        // In LAN mode, need 6 * playerCount cells
        int needed = LanSessionManager.IsActive
            ? LanSessionManager.EnemyCount
            : baseSet.Count;

        // Collect player spawn cells so we can keep enemies away from them.
        var playerCells = new System.Collections.Generic.HashSet<Vector2Int>();
        if (LanSessionManager.IsActive)
        {
            int n = LanSessionManager.PlayerCount;
            for (int i = 0; i < n; i++)
            {
                int row = Mathf.Clamp(playerSpawnCell.y + i * 3, 1, r - 2);
                playerCells.Add(new Vector2Int(playerSpawnCell.x, row));
            }
            // Remove any base-set enemy cells that coincide with player cells.
            baseSet.RemoveAll(cell => playerCells.Contains(cell));
        }

        if (needed <= baseSet.Count) return baseSet.GetRange(0, needed);

        // Generate additional random walkable cells for extra enemies.
        var result = new List<Vector2Int>(baseSet);
        var rng = new System.Random(42);
        int attempts = 0;
        while (result.Count < needed && attempts < 10000)
        {
            attempts++;
            var cell = new Vector2Int(rng.Next(1, c - 1), rng.Next(1, r - 1));
            if (mapLoader.IsWalkable(cell) && !result.Contains(cell)
                && !playerCells.Contains(cell))
                result.Add(cell);
        }
        return result;
    }

    // ── LAN: spawn one player tank per connected client ────────────────────────

    private List<TankController> SpawnAllLanPlayers()
    {
        var tanks = new List<TankController>();
        // Read live connected-client count from NGO rather than the cached
        // LanSessionManager.PlayerCount, which can be stale if a client joins
        // during the scene-load transition.
        int n = LanSessionManager.PlayerCount;
        if (LanSessionManager.IsDedicatedServer
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsServer)
        {
            n = Mathf.Clamp(
                NetworkManager.Singleton.ConnectedClients.Count,
                1,
                LanSessionManager.MaxPlayers);
            LanSessionManager.PlayerCount = n; // keep EnemyCount = 6*n in sync
        }
        Debug.Log($"[MapTankTestBootstrap] SpawnAllLanPlayers n={n} (ConnectedClients={NetworkManager.Singleton?.ConnectedClients.Count})");

        for (int i = 0; i < n; i++)
        {
            int row = Mathf.Clamp(playerSpawnCell.y + i * 3, 1, mapLoader.BuildHeight - 2);
            var cell = new Vector2Int(playerSpawnCell.x, row);
            if (!mapLoader.TryFindWalkableNear(cell, out Vector2Int spawnCell)) continue;

            GameObject prefab = ResolveTankPrefab();
            if (prefab == null) continue;

            GameObject tank = Instantiate(prefab, mapLoader.CellToWorld(spawnCell), Quaternion.identity);
            tank.name = i == 0 && !LanSessionManager.IsDedicatedServer ? "Player" : $"Player_{i}";
            if (i == 0 && !LanSessionManager.IsDedicatedServer) player = tank.transform;

            // In host mode, only the host's own tank (slot 0) shows the HP bar locally.
            // Dedicated server mode has no local player view, so all player tanks are
            // purely server-side representations.
            bool savedShowHp = showPlayerHealthBar;
            if (LanSessionManager.IsDedicatedServer || i > 0) showPlayerHealthBar = false;
            ConfigureTank(tank);
            showPlayerHealthBar = savedShowHp;

            // In LAN mode, individual player deaths don't end the game.
            // Remove the global game-over trigger wired by ConfigureTank and replace it
            // with per-slot tracking in LanGameCoordinator.
            Damagable d = tank.GetComponentInChildren<Damagable>();
            if (d != null)
            {
                MapGameOverController goc = MapGameOverController.Ensure();
                d.OnDead.RemoveListener(goc.BeginGameOver);
                int slot = i;
                d.OnDead.AddListener(() => LanGameCoordinator.Instance?.OnPlayerTankDead(slot));

                // Only host-mode has a local server-side player to switch into spectator mode.
                if (i == 0 && !LanSessionManager.IsDedicatedServer)
                    d.OnDead.AddListener(EnterSpectatorMode);
            }

            // Trong LAN mode, input đi qua LanNetworkBridge — không cần PlayerInput.
            // Dùng cả enabled=false (ngăn frame này) VÀ Destroy (xóa vĩnh viễn kể cả
            // persistent listener được serialize trong prefab mà RemoveListener không xóa được).
            foreach (var pi in tank.GetComponentsInChildren<PlayerInput>(true))
            {
                pi.enabled = false;
                Destroy(pi);
            }

            tanks.Add(tank.GetComponent<TankController>());
        }
        return tanks;
    }

    // Called when the host's own player tank is destroyed.
    private void EnterSpectatorMode()
    {
        _spectating    = true;
        _spectatorPos  = mainCamera != null
            ? (Vector2)mainCamera.transform.position
            : Vector2.zero;
    }

    private List<GameObject> GetSpawnedEnemies()
    {
        // MapScenarioBootstrapPIBT takes priority (matches SpawnScenario() dispatch order)
        var pibt = GetComponent<MapScenarioBootstrapPIBT>();
        if (pibt != null) return new List<GameObject>(pibt.Enemies);

        var bootstrap = GetComponent<MapScenarioBootstrap>()
            ?? GetComponentInChildren<MapScenarioBootstrap>();
        if (bootstrap == null) return new List<GameObject>();
        return new List<GameObject>(bootstrap.Enemies);
    }

    private void SpawnPlayer()
    {
        if (!mapLoader.TryFindWalkableWithSpace(playerSpawnCell, playerSpawnMinRegionSize, playerSpawnMinNeighbors, out Vector2Int spawnCell))
        {
            // Fallback: ô đủ rộng không tìm được → lấy ô walkable gần nhất
            if (!mapLoader.TryFindWalkableNear(playerSpawnCell, out spawnCell))
            {
                Debug.LogError("[MapTankTestBootstrap] Could not find a walkable spawn cell.");
                return;
            }
            Debug.LogWarning($"[MapTankTestBootstrap] No ideal spawn found — falling back to {spawnCell}. Player may be in a tight spot.");
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

    private static readonly string[] VariantBodyFiles =
    {
        "tankBody_blue.png",
        "tankBody_red.png",
        "tankBody_green.png",
        "tankBody_dark.png",
        "tankBody_sand.png",
        "tankBody_bigRed.png",
        "tankBody_darkLarge.png",
        "tankBody_huge.png",
    };
    private const string VariantSpritesRoot = "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/";
    private const string VariantPrefKey = "MenuTankVariant";

    private void ConfigureTank(GameObject tank)
    {
        tank.transform.localScale = Vector3.one * playerScale;
        FactionMember.Ensure(tank, Faction.Player);
        ApplyTankVariant(tank);

        TankMover tankMover = tank.GetComponentInChildren<TankMover>();
        if (tankMover != null && tankMover.movementData == null)
        {
            tankMover.movementData = ResolveMovementData();
        }

        ConfigurePlayerEngineAudio(tank, tankMover);

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

    private void ConfigurePlayerEngineAudio(GameObject tank, TankMover tankMover)
    {
        if (tank == null || tankMover == null)
        {
            return;
        }

        EngineAudio engineAudio = tank.GetComponentInChildren<EngineAudio>(true);
        if (engineAudio == null)
        {
            GameObject engineAudioObject = new GameObject("EngineAudio");
            engineAudioObject.transform.SetParent(tank.transform, false);
            engineAudioObject.AddComponent<AudioSource>();
            engineAudio = engineAudioObject.AddComponent<EngineAudio>();
        }

        AudioSource audioSource = engineAudio.GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = engineAudio.gameObject.AddComponent<AudioSource>();
        }

        engineAudio.minVloume = 0.08f;
        engineAudio.maxVolume = 0.22f;
        engineAudio.volumeIncrease = 0.18f;

        AudioClip clip = ResolvePlayerEngineClip();
        if (clip != null)
        {
            audioSource.clip = clip;
        }

        audioSource.loop = true;
        audioSource.playOnAwake = true;
        audioSource.spatialBlend = 0f;
        audioSource.volume = engineAudio.minVloume;

        if (audioSource.clip != null && Application.isPlaying && !audioSource.isPlaying)
        {
            audioSource.Play();
        }

        tankMover.OnSpeedChange.RemoveListener(engineAudio.ControlEngineVolume);
        tankMover.OnSpeedChange.AddListener(engineAudio.ControlEngineVolume);
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
        { var _sc = canvasObject.AddComponent<CanvasScaler>(); _sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; _sc.referenceResolution = new UnityEngine.Vector2(1280f, 720f); _sc.matchWidthOrHeight = 0.5f; }
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

        // Fit toàn bộ map trong viewport, nhưng cap để large map không quá zoom-out.
        // maxGameplayOrtho = 12 → hiển thị ~24 ô theo chiều dọc (gameplay thoải mái).
        // Map nhỏ (32×32) cho fitSize ~9 < 12 nên không bị ảnh hưởng.
        float maxSizeByHeight = mapHeightWorld / 2f;
        float maxSizeByWidth = mapWidthWorld / (2f * mainCamera.aspect);
        float fitSize = Mathf.Min(maxSizeByHeight, maxSizeByWidth);
        const float MaxGameplayOrtho = 7f;

        mainCamera.orthographicSize = Mathf.Max(0.01f, Mathf.Min(fitSize * cameraZoom, MaxGameplayOrtho));

        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        if (!allowCameraFollow || player == null || mainCamera == null)
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

    // Called by LanGameCoordinator after it links bridges to tanks, so each player
    // gets the sprite they chose in the lobby rather than the host's default.
    public static void ApplyVariantToTank(GameObject tank, int index)
    {
        int clampedIndex = Mathf.Clamp(index, 0, VariantBodyFiles.Length - 1);
        ApplyVariantInternal(tank, clampedIndex);
    }

    private void ApplyTankVariant(GameObject tank)
    {
        int index = Mathf.Clamp(PlayerPrefs.GetInt(VariantPrefKey, 0), 0, VariantBodyFiles.Length - 1);
        ApplyVariantInternal(tank, index);
    }

    private static void ApplyVariantInternal(GameObject tank, int index)
    {
        string spritePath = VariantSpritesRoot + VariantBodyFiles[index];

        Sprite sprite = null;
#if UNITY_EDITOR
        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite == null)
        {
            Texture2D edTex = AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath);
            if (edTex != null)
                sprite = Sprite.Create(edTex, new Rect(0, 0, edTex.width, edTex.height), new Vector2(0.5f, 0.5f), 100f);
        }
#endif
        if (sprite == null)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(VariantBodyFiles[index]);
            sprite = Resources.Load<Sprite>("TankSprites/" + fileName);
            if (sprite == null)
            {
                Texture2D tex = Resources.Load<Texture2D>("TankSprites/" + fileName);
                if (tex != null)
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 128f);
            }
        }
        if (sprite == null) return;

        Transform bodyTransform = tank.transform.Find("TankBase");
        if (bodyTransform == null)
        {
            // Fallback: find any SpriteRenderer whose sprite name contains "tankBody"
            SpriteRenderer[] renderers = tank.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer sr in renderers)
            {
                if (sr.sprite != null && sr.sprite.name.ToLower().Contains("tankbody"))
                {
                    sr.sprite = sprite;
                    return;
                }
            }
            return;
        }

        SpriteRenderer bodyRenderer = bodyTransform.GetComponent<SpriteRenderer>();
        if (bodyRenderer != null)
            bodyRenderer.sprite = sprite;
    }

    private GameObject ResolveTankPrefab()
    {
        if (tankPrefab != null) return tankPrefab;
#if UNITY_EDITOR
        var result = AssetDatabase.LoadAssetAtPath<GameObject>(tankPrefabPath);
        if (result != null) return result;
#endif
        return Resources.Load<GameObject>("Prefabs/Tank");
    }

    private TankMovementData ResolveMovementData()
    {
        if (movementData != null) return movementData;
#if UNITY_EDITOR
        var result = AssetDatabase.LoadAssetAtPath<TankMovementData>(movementDataPath);
        if (result != null) return result;
#endif
        return Resources.Load<TankMovementData>("Data/PlayerTankMovementData");
    }

    private AudioClip ResolvePlayerEngineClip()
    {
        if (playerEngineClip != null) return playerEngineClip;
#if UNITY_EDITOR
        var result = AssetDatabase.LoadAssetAtPath<AudioClip>(playerEngineClipPath);
        if (result != null) return result;
#endif
        return Resources.Load<AudioClip>("Audio/spaceEngineSmall_001");
    }
}
