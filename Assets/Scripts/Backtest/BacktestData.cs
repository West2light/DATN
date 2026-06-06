using System.Collections.Generic;

/// <summary>
/// Shared data types cho BacktestRunner và BacktestResultChart.
/// </summary>
public struct BacktestRunRecord
{
    public string map, algorithm, outcome;
    public int    rep, eagleHpAtEnd, enemiesAliveAtEnd, agentCount;
    public float  duration;
    public int    totalReplans, totalRecoveries, totalShots, totalCells;
    public List<BacktestAgentRecord> agents;
}

public struct BacktestAgentRecord
{
    public string agentName;
    public int    replanCount, recoveryCount, shotCount, cellsVisited, initialPathLength;
    public bool   deadAtEnd;
}
