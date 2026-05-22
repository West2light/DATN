using UnityEngine;
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

        TankMover tankMover = tank.GetComponentInChildren<TankMover>();
        if (tankMover != null && tankMover.movementData == null)
        {
            tankMover.movementData = ResolveMovementData();
        }
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
        input.OnMoveTurret.RemoveListener(controller.HandleTurretMovement);
        input.OnShoot.AddListener(controller.HandleShoot);
        input.OnMoveBody.AddListener(controller.HandleMoveBody);
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
