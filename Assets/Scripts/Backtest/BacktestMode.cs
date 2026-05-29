public static class BacktestMode
{
    public static bool   IsActive  { get; private set; }
    public static string Algorithm { get; private set; } // "AStar" | "LNS2"
    public static string MapLabel  { get; private set; }

    public static void Activate(string algorithm, string mapLabel)
    {
        IsActive  = true;
        Algorithm = algorithm;
        MapLabel  = mapLabel;
    }

    public static void Deactivate()
    {
        IsActive  = false;
        Algorithm = string.Empty;
        MapLabel  = string.Empty;
    }
}
