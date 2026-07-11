public static class BacktestMode
{
    public static bool   IsActive             { get; private set; }
    public static string Algorithm            { get; private set; } // "AStar" | "PIBT" | "PIBT_TCP"
    public static string MapLabel             { get; private set; }
    public static bool   DynamicObstacleMode  { get; private set; }
    public static int    AgentCount           { get; private set; } // 0 = dùng enemySpawnCells mặc định

    public static void Activate(string algorithm, string mapLabel,
                                bool dynamicObstacles = false, int agentCount = 0)
    {
        IsActive            = true;
        Algorithm           = algorithm;
        MapLabel            = mapLabel;
        DynamicObstacleMode = dynamicObstacles;
        AgentCount          = agentCount;
    }

    public static void Deactivate()
    {
        IsActive            = false;
        Algorithm           = string.Empty;
        MapLabel            = string.Empty;
        DynamicObstacleMode = false;
        AgentCount          = 0;
    }
}
