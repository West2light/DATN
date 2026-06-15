using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DedicatedServerBootstrap : MonoBehaviour
{
    // Live instance on the dedicated server, so the owner-start RPC (LanNetworkBridge)
    // can trigger the scene load through the same guarded path the grace timer uses.
    public static DedicatedServerBootstrap Instance { get; private set; }

    private static bool _bootRequested;

    private NetworkLaunchArgs _launchArgs;
    private bool _started;
    private bool _sceneLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        if (_bootRequested)
            return;

        NetworkLaunchArgs launchArgs = NetworkLaunchArgs.Parse(Environment.GetCommandLineArgs());
        if (!launchArgs.isDedicatedServer)
            return;

        _bootRequested = true;

        GameObject go = new GameObject("DedicatedServerBootstrap");
        UnityEngine.Object.DontDestroyOnLoad(go);
        DedicatedServerBootstrap bootstrap = go.AddComponent<DedicatedServerBootstrap>();
        bootstrap.StartWith(launchArgs);
    }

    public void StartWith(NetworkLaunchArgs launchArgs)
    {
        _launchArgs = launchArgs;
        Instance = this;
    }

    private void Start()
    {
        TryStartDedicatedServer();
    }

    private void TryStartDedicatedServer()
    {
        if (_started)
            return;

        _started = true;

        // Cap the headless server's frame rate. Without this the dedicated build spins
        // uncapped at ~100% CPU on the 2-core VM, starving the registry/nginx and the
        // transport's packet processing. 60 fps is plenty for a 30 Hz world-state sync.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        NetworkEndpointConfig cfg = _launchArgs.ToEndpointConfig();
        LanSessionManager.ActivateDedicatedServer(cfg);

        if (!NetworkManagerFactory.Ensure(
            "0.0.0.0",
            LanSessionManager.GamePort,
            isServer: true,
            LanSessionManager.TransportMode,
            out NetworkManager networkManager))
        {
            Debug.LogError("[DedicatedServer] Failed to create NetworkManager.");
            return;
        }

        if (networkManager.IsListening)
        {
            Debug.LogWarning("[DedicatedServer] NetworkManager is already listening.");
            return;
        }

        Debug.Log($"[DedicatedServer] Starting {LanSessionManager.TransportMode.ToArgumentValue()} on 0.0.0.0:{LanSessionManager.GamePort}");

        if (!networkManager.StartServer())
        {
            Debug.LogError("[DedicatedServer] StartServer failed.");
            return;
        }

        Debug.Log("[DedicatedServer] StartServer ok");
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        Debug.Log("[DedicatedServer] Waiting for clients before loading gameplay scene.");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= OnClientConnected;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // No auto-start: the server enters the gameplay scene ONLY when the room owner triggers
    // LanNetworkBridge.RequestStartServerRpc -> BeginGameplayScene. Players gather in the
    // lobby (driven client-side by LanLobbyController) and the owner presses START.
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[DedicatedServer] Client connected: {clientId} (waiting for owner START)");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[DedicatedServer] Client disconnected: {clientId}");
    }

    public void BeginGameplayScene()
    {
        if (_sceneLoaded)
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer)
            return;

        int connectedPlayers = Mathf.Clamp(networkManager.ConnectedClients.Count, 1, LanSessionManager.MaxPlayers);
        LanSessionManager.PlayerCount = connectedPlayers;
        _sceneLoaded = true;

        if (SceneManager.GetActiveScene().name == LanSessionManager.GameScene)
            return;

        Debug.Log($"[DedicatedServer] Loading {LanSessionManager.GameScene} with {LanSessionManager.PlayerCount} player(s)");
        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.LoadScene(LanSessionManager.GameScene, LoadSceneMode.Single);
            return;
        }

        Debug.LogWarning("[DedicatedServer] NGO SceneManager missing, falling back to SceneManager.LoadScene.");
        SceneManager.LoadScene(LanSessionManager.GameScene, LoadSceneMode.Single);
    }
}
