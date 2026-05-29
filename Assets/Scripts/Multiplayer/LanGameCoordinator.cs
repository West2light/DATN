using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-ONLY singleton. Lives in the game scene.
/// Links LanNetworkBridge objects (DontDestroyOnLoad) to server-side tank GOs,
/// then broadcasts world state to all clients at 20 Hz.
/// </summary>
public class LanGameCoordinator : MonoBehaviour
{
    public static LanGameCoordinator Instance { get; private set; }

    private readonly List<LanNetworkBridge> _bridges    = new List<LanNetworkBridge>();
    private readonly List<TankController>   _serverTanks = new List<TankController>();
    private readonly List<GameObject>       _serverEnemies = new List<GameObject>();

    private Coroutine _syncCoroutine;
    private bool      _linked;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Called by MapTankTestBootstrap (server) after spawning tanks ──────────

    public void RegisterServerTanks(List<TankController> tanks, List<GameObject> enemies)
    {
        _serverTanks.Clear();
        _serverTanks.AddRange(tanks);
        _serverEnemies.Clear();
        _serverEnemies.AddRange(enemies);
        TryLink();
    }

    // ── Called by LanNetworkBridge.OnNetworkSpawn (server side) ──────────────

    public void OnBridgeSpawned(LanNetworkBridge bridge)
    {
        if (!_bridges.Contains(bridge))
            _bridges.Add(bridge);
        TryLink();
    }

    // ── Link bridges ↔ tanks once both lists are ready ────────────────────────

    private void TryLink()
    {
        if (_linked) return;
        if (_bridges.Count == 0 || _serverTanks.Count == 0) return;
        if (_bridges.Count != _serverTanks.Count) return;

        for (int i = 0; i < _bridges.Count; i++)
            _bridges[i].LinkTank(_serverTanks[i], i);

        _linked = true;
        if (_syncCoroutine == null)
            _syncCoroutine = StartCoroutine(SyncLoop());

        Debug.Log($"[LanGameCoordinator] Linked {_bridges.Count} bridge(s) to tanks. Sync running.");
    }

    // ── Broadcast at 20 Hz ───────────────────────────────────────────────────

    private IEnumerator SyncLoop()
    {
        var wait = new WaitForSeconds(0.05f);
        while (true)
        {
            yield return wait;
            BroadcastPerPlayerState();
            BroadcastWorldState();
        }
    }

    private void BroadcastPerPlayerState()
    {
        for (int i = 0; i < _bridges.Count; i++)
        {
            LanNetworkBridge bridge = _bridges[i];
            TankController   tank   = bridge.ServerTank;
            if (tank == null) continue;

            Damagable dmg = tank.GetComponentInChildren<Damagable>();
            var state = new LanTankState
            {
                pos      = tank.transform.position,
                bodyRot  = tank.transform.eulerAngles.z,
                turretRot = tank.aimTurret != null ? tank.aimTurret.transform.eulerAngles.z : 0f,
                hp       = dmg != null ? Mathf.RoundToInt(dmg.Health) : 0,
            };

            // Send only to the owner of this bridge
            bridge.ReceiveTankStateClientRpc(state, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { bridge.ClientId } }
            });
        }
    }

    private void BroadcastWorldState()
    {
        if (_bridges.Count == 0) return;

        // Build player states array (all players, so others can see each other)
        var playerStates = new LanTankState[_serverTanks.Count];
        for (int i = 0; i < _serverTanks.Count; i++)
        {
            TankController tank = _serverTanks[i];
            if (tank == null) continue;
            Damagable dmg = tank.GetComponentInChildren<Damagable>();
            playerStates[i] = new LanTankState
            {
                pos      = tank.transform.position,
                bodyRot  = tank.transform.eulerAngles.z,
                turretRot = tank.aimTurret != null ? tank.aimTurret.transform.eulerAngles.z : 0f,
                hp       = dmg != null ? Mathf.RoundToInt(dmg.Health) : 0,
            };
        }

        // Build enemy states array
        var enemyStates = new LanEnemyState[_serverEnemies.Count];
        for (int i = 0; i < _serverEnemies.Count; i++)
        {
            GameObject enemy = _serverEnemies[i];
            if (enemy == null)
            {
                enemyStates[i] = new LanEnemyState { idx = i, hp = 0 };
                continue;
            }
            Damagable dmg = enemy.GetComponentInChildren<Damagable>();
            enemyStates[i] = new LanEnemyState
            {
                idx = i,
                pos = enemy.transform.position,
                rot = enemy.transform.eulerAngles.z,
                hp  = dmg != null ? Mathf.RoundToInt(dmg.Health) : 0,
            };
        }

        // Eagle HP
        int eagleHp = 0;
        GameObject eagle = GameObject.Find("EagleBase");
        if (eagle != null)
        {
            Damagable dmg = eagle.GetComponent<Damagable>();
            if (dmg != null) eagleHp = Mathf.RoundToInt(dmg.Health);
        }

        // Broadcast to all clients (the first bridge acts as the relay)
        _bridges[0].ReceiveWorldStateClientRpc(playerStates, enemyStates, eagleHp);
    }

    // ── Win / GameOver broadcast ──────────────────────────────────────────────

    public void BroadcastWin()
    {
        if (_bridges.Count > 0)
            _bridges[0].BroadcastEventClientRpc(true);
    }

    public void BroadcastGameOver()
    {
        if (_bridges.Count > 0)
            _bridges[0].BroadcastEventClientRpc(false);
    }
}
