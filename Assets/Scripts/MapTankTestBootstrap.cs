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
    public Camera mainCamera;
    public float cameraOrthographicSize = 9f;
    public Vector3 cameraOffset = new Vector3(0f, 0f, -10f);

    private Transform player;

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
        if (player != null && mainCamera != null)
        {
            mainCamera.transform.position = player.position + cameraOffset;
        }
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
        mainCamera.orthographicSize = cameraOrthographicSize;
        if (player != null)
        {
            mainCamera.transform.position = player.position + cameraOffset;
        }
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
