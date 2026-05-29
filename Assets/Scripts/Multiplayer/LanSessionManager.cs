using UnityEngine;

/// <summary>Static session state that persists across scene loads for LAN multiplayer.</summary>
public static class LanSessionManager
{
    public static bool   IsActive    { get; private set; }
    public static bool   IsServer    { get; private set; }
    public static int    PlayerCount { get; set; } = 1;
    public static string MapFile     { get; set; } = "";
    public static string Algorithm   { get; set; } = "AStar";

    public static void ActivateHost(string mapFile, string algorithm)
    {
        IsActive   = true;
        IsServer   = true;
        MapFile    = mapFile;
        Algorithm  = algorithm;
        PlayerCount = 1;
        StoreToPrefs();
    }

    public static void ActivateClient(string mapFile, string algorithm)
    {
        IsActive  = true;
        IsServer  = false;
        MapFile   = mapFile;
        Algorithm = algorithm;
        StoreToPrefs();
    }

    public static void Deactivate()
    {
        IsActive    = false;
        IsServer    = false;
        PlayerCount = 1;
    }

    public static int    EnemyCount => 6 * Mathf.Max(1, PlayerCount);
    public static string GameScene  => Algorithm == "LNS2" ? "MapF_TankTest_LNS2" : "MapF_TankTest";

    private static void StoreToPrefs()
    {
        PlayerPrefs.SetString("SelectedMapFile", MapFile);
        PlayerPrefs.Save();
    }
}
