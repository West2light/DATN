using UnityEngine;

/// <summary>Static session state that persists across scene loads for LAN multiplayer.</summary>
public static class LanSessionManager
{
    public static bool   IsActive         { get; private set; }
    public static bool   IsServer         { get; private set; }
    public static int    PlayerCount      { get; set; } = 1;
    public static string MapFile          { get; set; } = "";
    public static string Algorithm        { get; set; } = "AStar";
    // Tank variant (color) chosen by this machine in the lobby (0-4 = unlocked, 5-7 = locked).
    public static int    LocalVariantIndex { get; set; } = 0;
    // True when the active session is running over Unity Relay (internet) instead of direct LAN.
    public static bool   UseRelay         { get; set; }

    public static void ActivateHost(string mapFile, string algorithm)
    {
        IsActive   = true;
        IsServer   = true;
        MapFile    = mapFile;
        Algorithm  = algorithm;
        PlayerCount = 1;
        LocalVariantIndex = Mathf.Clamp(UnityEngine.PlayerPrefs.GetInt("MenuTankVariant", 0), 0, 7);
        StoreToPrefs();
    }

    public static void ActivateClient(string mapFile, string algorithm)
    {
        IsActive  = true;
        IsServer  = false;
        MapFile   = mapFile;
        Algorithm = algorithm;
        LocalVariantIndex = Mathf.Clamp(UnityEngine.PlayerPrefs.GetInt("MenuTankVariant", 0), 0, 7);
        StoreToPrefs();
    }

    public static void Deactivate()
    {
        IsActive    = false;
        IsServer    = false;
        PlayerCount = 1;
        UseRelay    = false;
    }

    public static int    EnemyCount => 6 * Mathf.Max(1, PlayerCount);
    public static string GameScene  => Algorithm == "PIBT" ? "MapF_TankTest_PIBT" : "MapF_TankTest";

    private static void StoreToPrefs()
    {
        PlayerPrefs.SetString("SelectedMapFile", MapFile);
        PlayerPrefs.Save();
    }
}
