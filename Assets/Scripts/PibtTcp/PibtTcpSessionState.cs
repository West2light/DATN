using System.Collections.Generic;
using UnityEngine;
using PibtTcp;

/// <summary>
/// Shared state written by PibtTcpClient (background thread)
/// and read by GridEnemyAgentPIBTTcp (Unity main thread).
/// All access is protected by lock(_actions).
/// </summary>
public class PibtTcpSessionState
{
    // ─── Actions (written by client callback, read by agents) ────────────────
    private readonly Dictionary<int, ActionDto> _actions = new();
    private int _resultVersion;

    public void SetActions(IEnumerable<ActionDto> newActions)
    {
        lock (_actions)
        {
            _actions.Clear();
            if (newActions == null) return;
            foreach (var a in newActions)
                _actions[a.id] = a;
            _resultVersion++;
        }
    }

    public void SetPlannerMetadata(PlanResult result)
    {
        if (result == null)
            return;

        PlannerName = string.IsNullOrWhiteSpace(result.planner) ? null : result.planner;
        OpLen = result.opLen;
        RevisitLimit = result.revisitLimit;
        FallbackInherited = result.fallbackInherited;
        MultiConflictSkipped = result.multiConflictSkipped;
    }

    /// <summary>Returns the action for the agent, or null if none available.</summary>
    public ActionDto GetAction(int agentId)
    {
        lock (_actions)
        {
            return _actions.TryGetValue(agentId, out var a) ? a : null;
        }
    }

    public bool HasAction(int agentId)
    {
        lock (_actions)
        {
            return _actions.ContainsKey(agentId);
        }
    }

    public int ResultVersion
    {
        get
        {
            lock (_actions)
            {
                return _resultVersion;
            }
        }
    }

    // ─── Session metadata ────────────────────────────────────────────────────
    public string SessionId  { get; set; }
    public int    TeamSize   { get; set; }

    // ─── Latest metrics (written by client) ──────────────────────────────────
    public float LatencyMsLast  { get; set; }
    public float ComputeMsLast  { get; set; }
    public int   TimeoutCount   { get; set; }
    public int   LastRequestId  { get; set; }
    public int   LastTimestep   { get; set; }
    public string PlannerName   { get; private set; }
    public int   OpLen          { get; private set; }
    public int   RevisitLimit   { get; private set; }
    public int   FallbackInherited { get; private set; }
    public int   MultiConflictSkipped { get; private set; }

    // ─── Readiness flag ──────────────────────────────────────────────────────
    /// <summary>True after a successful plan_result is stored and ready for agents.</summary>
    public bool ResultReady { get; set; }
}
