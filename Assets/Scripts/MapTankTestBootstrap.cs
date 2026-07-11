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
    private readonly List<Vector2Int> _lanPlayerSpawnCells = new List<Vector2Int>();
    [Min(3)]
    [Tooltip("Minimum connected walkable region size at the spawn point, preventing players from getting trapped.")]
    public int playerSpawnMinRegionSize = 9;
    [Range(1, 4)]
    [Tooltip("Minimum number of direct exits (4 directions) at the spawn cell. A value of 2 avoids dead ends.")]
    public int playerSpawnMinNeighbors = 2;

    [Range(0.5f, 3f)]
    [Tooltip("Player tank scale relative to one map cell.")]
    public float playerScale = 1.3f;
    [Min(1)]
    [Tooltip("Player health in MapF scenes, matching Lvl1.")]
    public int playerMaxHealth = 20;
    [Tooltip("Show the player health bar in the top-left HUD in MapF scenes.")]
    public bool showPlayerHealthBar = true;
    public Camera mainCamera;

    [Range(0.3f, 1f)]
    [Tooltip("Zoom factor: 1 fits the map's shorter edge; lower values zoom in.")]
    public float cameraZoom = 0.7f;
    public Vector3 cameraOffset = new Vector3(0f, 0f, -10f);

    private Transform player;
    private float mapWidthWorld;
    private float mapHeightWorld;
    private bool allowCameraFollow = true;

    // Spectator mode: activated when the local player's tank is destroyed in LAN mode.
    private bool    _spectating;
    private Vector2 _spectatorPos;

    // ── Free-look (zoom + pan) ─────────────────────────────────────────────
    private bool    _freeLook;
    private bool    _camDragging;
    private Vector3 _camDragOriginWorld;
    private float   _defaultOrthoSize;
    private float   _freeLookMaxOrtho = 20f;
    private const float FreeLookMinOrtho = 1.5f;

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
            if (!LanSessionManager.IsDedicatedServer)
                PauseMenuController.Ensure();

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
                    var agentTcp = enemy.GetComponent<GridEnemyAgentPIBT_TCP>();
                    if (agentTcp != null) agentTcp.playerTargets = playerTransforms;
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

        if (!BacktestMode.IsActive)
            PauseMenuController.Ensure();

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

        HandleCameraZoom();

        if (_spectating)
        {
            // HandleCameraZoom() may have just shifted mainCamera.transform.position
            // (zoom-toward-cursor + map clamp). Sync that into _spectatorPos before
            // HandleSpectatorCamera() re-applies it below — otherwise it stomps the
            // zoom's position shift back to the stale pre-zoom spot every frame,
            // producing a visible stutter each time the player scrolls while dead.
            _spectatorPos = mainCamera != null
                ? (Vector2)mainCamera.transform.position
                : _spectatorPos;
            HandleSpectatorCamera();
            return;
        }

        HandleFreeLookDrag();

        if (Input.GetKeyDown(KeyCode.Y))
        {
            _freeLook = false;
            _camDragging = false;
            if (_defaultOrthoSize > 0f)
                mainCamera.orthographicSize = _defaultOrthoSize;
        }

        if (_freeLook)
        {
            ClampCameraToMap();
            return;
        }

        UpdateCameraPosition();
    }

    // Scroll wheel zoom — always active, zooms toward the cursor position.
    private void HandleCameraZoom()
    {
        if (mainCamera == null || PauseMenuController.IsLocalPauseActive) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        Vector3 worldBefore = CamScreenToWorld(Input.mousePosition);
        mainCamera.orthographicSize = Mathf.Clamp(
            mainCamera.orthographicSize * (1f - scroll * 1.2f),
            FreeLookMinOrtho, _freeLookMaxOrtho);
        Vector3 worldAfter = CamScreenToWorld(Input.mousePosition);
        mainCamera.transform.position += worldBefore - worldAfter;
        ClampCameraToMap();
    }

    // Right-click / middle-click drag to pan freely; sets _freeLook until Y is pressed.
    private void HandleFreeLookDrag()
    {
        if (mainCamera == null || PauseMenuController.IsLocalPauseActive) return;

        bool btn  = Input.GetMouseButton(1) || Input.GetMouseButton(2);
        bool down = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);

        if (down)
        {
            _camDragOriginWorld = CamScreenToWorld(Input.mousePosition);
            _camDragging = true;
            _freeLook    = true;
        }

        if (_camDragging && btn)
        {
            Vector3 current = CamScreenToWorld(Input.mousePosition);
            mainCamera.transform.position += _camDragOriginWorld - current;
            _camDragOriginWorld = CamScreenToWorld(Input.mousePosition);
        }

        if (!btn) _camDragging = false;
    }

    private void ClampCameraToMap()
    {
        if (mainCamera == null) return;
        float halfH = mainCamera.orthographicSize;
        float halfW = halfH * mainCamera.aspect;
        float minX  = -mapWidthWorld  / 2f + halfW;
        float maxX  =  mapWidthWorld  / 2f - halfW;
        float minY  = -mapHeightWorld / 2f + halfH;
        float maxY  =  mapHeightWorld / 2f - halfH;
        Vector3 pos = mainCamera.transform.position;
        pos.x = minX <= maxX ? Mathf.Clamp(pos.x, minX, maxX) : 0f;
        pos.y = minY <= maxY ? Mathf.Clamp(pos.y, minY, maxY) : 0f;
        mainCamera.transform.position = pos;
    }

    private Vector3 CamScreenToWorld(Vector3 screenPos)
    {
        Vector3 w = mainCamera.ScreenToWorldPoint(screenPos);
        w.z = mainCamera.transform.position.z;
        return w;
    }

    // Free-roam camera for spectators: WASD pans within map bounds.
    private void HandleSpectatorCamera()
    {
        if (mainCamera == null || PauseMenuController.IsLocalPauseActive) return;
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

        // Mode 3: PIBT-TCP — flagged via PlayerPrefs by the menu, or component already present
        string selectedAlgorithm = PlayerPrefs.GetString("SelectedAlgorithm", "");
        bool isTcpMode = selectedAlgorithm == "PIBT_TCP"
            || (LanSessionManager.IsActive && LanSessionManager.Algorithm == "PIBT_TCP")
            || (BacktestMode.IsActive && BacktestMode.Algorithm == "PIBT_TCP");
        PlayerPrefs.DeleteKey("SelectedAlgorithm"); // consume so next load is clean

        Debug.Log($"[MapTankTestBootstrap] SpawnScenario algorithm='{selectedAlgorithm}', backtest='{BacktestMode.Algorithm}', isTcpMode={isTcpMode}");

        MapScenarioBootstrapPIBT_TCP tcpBootstrap = GetComponent<MapScenarioBootstrapPIBT_TCP>();
        if (isTcpMode && tcpBootstrap == null)
            tcpBootstrap = gameObject.AddComponent<MapScenarioBootstrapPIBT_TCP>();

        if (isTcpMode && tcpBootstrap != null)
        {
            Debug.Log("[MapTankTestBootstrap] Spawning PIBT_TCP scenario; this should connect to the TCP server.");
            tcpBootstrap.mapLoader = mapLoader;
            if (spawnCells != null) tcpBootstrap.enemySpawnCells = spawnCells;
            tcpBootstrap.SpawnScenario();
            return;
        }

        // Mode 4: MIXED (A* + PIBT) — chọn qua PlayerPrefs/LAN/BacktestMode như TCP.
        // Dùng chung scene MapF_TankTest_PIBT; add bootstrap runtime và return sớm để
        // MapScenarioBootstrapPIBT có sẵn trong scene không chạy.
        bool isMixedMode = selectedAlgorithm == "Mixed"
            || (LanSessionManager.IsActive && LanSessionManager.Algorithm == "Mixed")
            || (BacktestMode.IsActive && BacktestMode.Algorithm == "Mixed");

        MapScenarioBootstrapMixed mixedBootstrap = GetComponent<MapScenarioBootstrapMixed>();
        if (isMixedMode && mixedBootstrap == null)
            mixedBootstrap = gameObject.AddComponent<MapScenarioBootstrapMixed>();

        if (isMixedMode && mixedBootstrap != null)
        {
            Debug.Log("[MapTankTestBootstrap] Spawning Mixed (A*+PIBT) scenario.");
            mixedBootstrap.mapLoader = mapLoader;
            if (spawnCells != null) mixedBootstrap.enemySpawnCells = spawnCells;
            mixedBootstrap.SpawnScenario();
            return;
        }

        MapScenarioBootstrapPIBT pibtBootstrap = GetComponent<MapScenarioBootstrapPIBT>();
        if (pibtBootstrap != null)
        {
            Debug.Log("[MapTankTestBootstrap] Spawning local PIBT scenario.");
            pibtBootstrap.mapLoader = mapLoader;
            if (spawnCells != null) pibtBootstrap.enemySpawnCells = spawnCells;
            pibtBootstrap.SpawnScenario();
            return;
        }

        MapScenarioBootstrap scenarioBootstrap = GetComponent<MapScenarioBootstrap>();
        if (scenarioBootstrap != null)
        {
            Debug.Log("[MapTankTestBootstrap] Spawning AStar scenario.");
            scenarioBootstrap.mapLoader = mapLoader;
            if (spawnCells != null) scenarioBootstrap.enemySpawnCells = spawnCells;
            scenarioBootstrap.SpawnScenario();
        }
    }

    private Vector2Int ComputeMultiplayerPlayerCell(int playerIndex)
    {
        int sx = mapLoader.BuildStartX;
        int sy = mapLoader.BuildStartY;
        int c = mapLoader.BuildWidth;
        int r = mapLoader.BuildHeight;
        int pad = Mathf.Clamp(3, 1, Mathf.Max(1, (Mathf.Min(c, r) - 1) / 2));
        
        // Spawn all players near the Host (top-left) instead of spreading them to corners.
        // TryFindAvailableSpawnNear will automatically space them out.
        return new Vector2Int(sx + pad, sy + pad);
    }

    private List<Vector2Int> ComputeEnemySpawnCells()
    {
        if (mapLoader == null) return null;
        int sx = mapLoader.BuildStartX;
        int sy = mapLoader.BuildStartY;
        int c  = mapLoader.BuildWidth;
        int r  = mapLoader.BuildHeight;

        // Base 6 spawn positions (corners + mid-edges)
        var baseSet = new List<Vector2Int>
        {
            new Vector2Int(sx + c - 2, sy + 1),
            new Vector2Int(sx + 1,     sy + r - 2),
            new Vector2Int(sx + c - 2, sy + r - 2),
            new Vector2Int(sx + c / 2, sy + 1),
            new Vector2Int(sx + 1,     sy + r / 2),
            new Vector2Int(sx + c - 2, sy + r / 2),
        };

        // In LAN mode, need 6 * playerCount cells
        int needed = LanSessionManager.IsActive
            ? LanSessionManager.EnemyCount
            : baseSet.Count;

        // Reserve a 3-cell radius around every ACTUAL allocated player spawn.
        var reserved = new HashSet<Vector2Int>();
        foreach (Vector2Int playerCell in _lanPlayerSpawnCells)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (dx * dx + dy * dy < 9)
                        reserved.Add(playerCell + new Vector2Int(dx, dy));
        }

        // Allocate every enemy spawn through the same reservation-aware path. This
        // prevents blocked preferred cells from all resolving to one common tile.
        var result = new List<Vector2Int>(needed);
        var rng = new System.Random(42);
        for (int i = 0; i < needed; i++)
        {
            Vector2Int preferred = i < baseSet.Count
                ? baseSet[i]
                : new Vector2Int(rng.Next(sx + 1, sx + c - 1), rng.Next(sy + 1, sy + r - 1));
            if (!mapLoader.TryFindAvailableSpawnNear(preferred, reserved, 1, out Vector2Int allocated))
            {
                Debug.LogError($"[MapTankTestBootstrap] Could not allocate unique enemy spawn {i + 1}/{needed}.");
                break;
            }
            result.Add(allocated);
            reserved.Add(allocated);
        }
        return result;
    }

    // ── LAN: spawn one player tank per connected client ────────────────────────

    private List<TankController> SpawnAllLanPlayers()
    {
        var tanks = new List<TankController>();
        _lanPlayerSpawnCells.Clear();
        var reservedSpawnCells = new HashSet<Vector2Int>();
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
            var cell = ComputeMultiplayerPlayerCell(i);
            if (!mapLoader.TryFindAvailableSpawnNear(cell, reservedSpawnCells, 3, out Vector2Int spawnCell))
            {
                Debug.LogError($"[MapTankTestBootstrap] Could not allocate a unique spawn for LAN player slot {i}.");
                continue;
            }
            reservedSpawnCells.Add(spawnCell);
            _lanPlayerSpawnCells.Add(spawnCell);

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
        var tcp = GetComponent<MapScenarioBootstrapPIBT_TCP>();
        if (tcp != null) return new List<GameObject>(tcp.Enemies);

        var mixed = GetComponent<MapScenarioBootstrapMixed>();
        if (mixed != null) return new List<GameObject>(mixed.Enemies);

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
        _defaultOrthoSize = mainCamera.orthographicSize;
        _freeLookMaxOrtho = Mathf.Max(mapWidthWorld, mapHeightWorld) / 2f + 2f;

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
