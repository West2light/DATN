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
    public static int    MaxPlayers       { get; private set; } = 8;
    // Tank variant (color) chosen by this machine in the lobby (0-4 = unlocked, 5-7 = locked).
    public static int    LocalVariantIndex { get; set; } = 0;

    public static void ActivateHost(string mapFile, string algorithm)
    {
        NetworkEndpointConfig cfg = NetworkEndpointConfig.DefaultLan(mapFile, algorithm);
        ApplyConfig(cfg, isServer: true, isDedicatedServer: false, resetPlayerCount: true);
        LocalVariantIndex = LoadLocalVariant();
    }

    public static void ActivateClient(string mapFile, string algorithm)
    {
        NetworkEndpointConfig cfg = NetworkEndpointConfig.DefaultLan(mapFile, algorithm);
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
        MaxPlayers = 8;
        MapFile = string.Empty;
        Algorithm = "AStar";
    }

    public static int    EnemyCount => 6 * Mathf.Max(1, PlayerCount);
    public static string GameScene  => Algorithm == "PIBT" ? "MapF_TankTest_PIBT" : "MapF_TankTest";

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
        MaxPlayers = Mathf.Max(1, cfg.maxPlayers);
        if (resetPlayerCount)
            PlayerCount = 1;
        StoreToPrefs();
    }

    private static int LoadLocalVariant()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt("MenuTankVariant", 0), 0, 7);
    }

    private static void StoreToPrefs()
    {
        PlayerPrefs.SetString("SelectedMapFile", MapFile);
        PlayerPrefs.Save();
    }
}
