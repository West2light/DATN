using UnityEngine;

/// <summary>Static session state that persists across scene loads for LAN multiplayer.</summary>
public static class LanSessionManager
{
    public static bool   IsActive         { get; private set; }
    public static bool   IsServer         { get; private set; }
    public static bool   IsDedicatedServer { get; private set; }
    public static int    PlayerCount      { get; set; } = 1;
    public static string MapFile          { get; set; } = "";
    public static string Algorithm        { get; set; } = "AStar";
    public static string SessionCode      { get; private set; } = "";
    public static string RegistryUrl      { get; private set; } = "";
    public static ushort GamePort         { get; private set; } = 7777;
    public static NetworkTransportMode TransportMode { get; private set; } = NetworkTransportMode.Udp;
    public static bool   UseSecureWebSocket { get; private set; }
    public static string SecureWebSocketHost { get; private set; } = "";
    public static int    MaxPlayers       { get; private set; } = 8;
    public static int    EnemyMultiplier  { get; private set; } = 3;
    // Tank variant (color) chosen by this machine in the lobby (0-4 = unlocked, 5-7 = locked).
    public static int    LocalVariantIndex { get; set; } = 0;

    public static void ActivateHost(string mapFile, string algorithm, int enemyMultiplier = 3)
    {
        NetworkEndpointConfig cfg = NetworkEndpointConfig.DefaultLan(mapFile, algorithm);
        cfg.enemyMultiplier = NormalizeEnemyMultiplier(enemyMultiplier);
        ApplyConfig(cfg, isServer: true, isDedicatedServer: false, resetPlayerCount: true);
        LocalVariantIndex = LoadLocalVariant();
    }

    public static void ActivateClient(string mapFile, string algorithm, int enemyMultiplier = 3)
    {
        NetworkEndpointConfig cfg = NetworkEndpointConfig.DefaultLan(mapFile, algorithm);
        cfg.enemyMultiplier = NormalizeEnemyMultiplier(enemyMultiplier);
        ApplyConfig(cfg, isServer: false, isDedicatedServer: false, resetPlayerCount: false);
        LocalVariantIndex = LoadLocalVariant();
    }

    public static void ActivateInternetClient(NetworkEndpointConfig cfg)
    {
        ApplyConfig(cfg, isServer: false, isDedicatedServer: false, resetPlayerCount: false);
        LocalVariantIndex = LoadLocalVariant();
    }

    public static void ActivateDedicatedServer(NetworkEndpointConfig cfg)
    {
        ApplyConfig(cfg, isServer: true, isDedicatedServer: true, resetPlayerCount: true);
        LocalVariantIndex = 0;
    }

    public static void Deactivate()
    {
        IsActive    = false;
        IsServer    = false;
        IsDedicatedServer = false;
        PlayerCount = 1;
        SessionCode = string.Empty;
        RegistryUrl = string.Empty;
        GamePort = 7777;
        TransportMode = NetworkTransportMode.Udp;
        UseSecureWebSocket = false;
        SecureWebSocketHost = string.Empty;
        MaxPlayers = 8;
        EnemyMultiplier = 3;
        MapFile = string.Empty;
        Algorithm = "AStar";
    }

    public static int    EnemyCount => EnemyMultiplier * Mathf.Max(1, PlayerCount);
    public static string GameScene  => Algorithm == "PIBT" || Algorithm == "PIBT_TCP"
        ? "MapF_TankTest_PIBT"
        : "MapF_TankTest";

    private static void ApplyConfig(
        NetworkEndpointConfig cfg,
        bool isServer,
        bool isDedicatedServer,
        bool resetPlayerCount)
    {
        IsActive = true;
        IsServer = isServer;
        IsDedicatedServer = isDedicatedServer;
        MapFile = cfg.mapFile ?? string.Empty;
        Algorithm = string.IsNullOrWhiteSpace(cfg.algorithm) ? "AStar" : cfg.algorithm;
        SessionCode = cfg.sessionCode ?? string.Empty;
        RegistryUrl = cfg.registryUrl ?? string.Empty;
        GamePort = cfg.port == 0 ? (ushort)7777 : cfg.port;
        TransportMode = cfg.transportMode;
        UseSecureWebSocket = cfg.secureWebSocket;
        SecureWebSocketHost = string.IsNullOrWhiteSpace(cfg.secureWebSocketHost)
            ? cfg.host ?? string.Empty
            : cfg.secureWebSocketHost;
        MaxPlayers = Mathf.Max(1, cfg.maxPlayers);
        EnemyMultiplier = NormalizeEnemyMultiplier(cfg.enemyMultiplier);
        if (resetPlayerCount)
            PlayerCount = 1;
        StoreToPrefs();
    }

    private static int LoadLocalVariant()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt("MenuTankVariant", 0), 0, 7);
    }

    public static int NormalizeEnemyMultiplier(int value)
    {
        return value == 1 || value == 3 || value == 6 ? value : 3;
    }

    private static void StoreToPrefs()
    {
        PlayerPrefs.SetString("SelectedMapFile", MapFile);
        PlayerPrefs.Save();
    }
}
