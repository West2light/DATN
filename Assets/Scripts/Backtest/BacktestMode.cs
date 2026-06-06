public static class BacktestMode
{
    public static bool   IsActive             { get; private set; }
    public static string Algorithm            { get; private set; } // "AStar" | "PIBT"
    public static string MapLabel             { get; private set; }
    public static bool   DynamicObstacleMode  { get; private set; }

    public static void Activate(string algorithm, string mapLabel, bool dynamicObstacles = false)
    {
        IsActive            = true;
        Algorithm           = algorithm;
        MapLabel            = mapLabel;
        DynamicObstacleMode = dynamicObstacles;
    }

    public static void Deactivate()
    {
        IsActive            = false;
        Algorithm           = string.Empty;
        MapLabel            = string.Empty;
        DynamicObstacleMode = false;
    }
}
