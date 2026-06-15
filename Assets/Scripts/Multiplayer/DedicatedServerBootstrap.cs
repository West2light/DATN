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

    // Once enough players have joined, wait this brief settle window before loading the
    // gameplay scene so every connected client is stable when the scene sync fires.
    private const float SettleDelaySeconds = 2f;
    // Fallback so a lone tester is never stuck waiting for a second player. The old code
    // loaded the scene 3s after the FIRST client connected — that locked out a second tab
    // because it arrived after the scene had already loaded. We now wait for a second
    // player (see MinPlayersToStart) and only fall back to a solo start after this grace.
    private const float SoloGraceSeconds = 25f;

    private NetworkLaunchArgs _launchArgs;
    private bool _started;
    private bool _graceScheduled;
    private bool _sceneLoaded;

    // Minimum connected players before the room starts on its own. Clamped to MaxPlayers
    // so a maxPlayers=1 room still starts. Internet rooms default to waiting for 2.
    private int MinPlayersToStart => Mathf.Min(2, Mathf.Max(1, LanSessionManager.MaxPlayers));

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

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[DedicatedServer] Client connected: {clientId}");
        if (_sceneLoaded)
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            return;

        int connectedPlayers = Mathf.Max(0, networkManager.ConnectedClients.Count);

        // Room full → start immediately, no need to wait.
        if (connectedPlayers >= LanSessionManager.MaxPlayers)
        {
            CancelInvoke(nameof(BeginGameplayScene));
            BeginGameplayScene();
            return;
        }

        // Enough players have joined → start after a short settle delay. CancelInvoke first
        // so the solo-grace timer (if armed) is replaced by this sooner, intentional start.
        if (connectedPlayers >= MinPlayersToStart)
        {
            CancelInvoke(nameof(BeginGameplayScene));
            Invoke(nameof(BeginGameplayScene), SettleDelaySeconds);
            return;
        }

        // Only one player so far → arm a one-shot grace timer so a solo tester is not stuck,
        // but keep waiting for a second player rather than starting right away.
        if (!_graceScheduled)
        {
            _graceScheduled = true;
            Invoke(nameof(BeginGameplayScene), SoloGraceSeconds);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[DedicatedServer] Client disconnected: {clientId}");
        if (_sceneLoaded) return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null) return;

        // If every client has left before the scene started, cancel all pending timers
        // and reset the grace flag so the next client to connect re-arms them fresh.
        // Without this, a solo-grace timer fired after a brief connection would load
        // the game scene with 0 connected players.
        if (networkManager.ConnectedClients.Count == 0)
        {
            CancelInvoke(nameof(BeginGameplayScene));
            _graceScheduled = false;
            Debug.Log("[DedicatedServer] All clients disconnected — timers reset, waiting for fresh connections.");
        }
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
