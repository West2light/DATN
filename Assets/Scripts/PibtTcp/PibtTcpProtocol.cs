using System;
using System.Collections.Generic;

namespace PibtTcp
{
    // ─── Outgoing: hello ───────────────────────────────────────────────────────
    [Serializable]
    public class HelloRequest
    {
        public string type = "hello";
        public string sessionId;
        public int    teamSize;
        public MapDto map;
    }

    [Serializable]
    public class MapDto
    {
        public int    width;
        public int    height;
        public string symbols;   // row-major, length = width * height
    }

    // ─── Incoming: hello_ack ───────────────────────────────────────────────────
    [Serializable]
    public class HelloAck
    {
        public string type;
        public string sessionId;
        public string status;   // "ok" | "error"
        public string server;
        public string planner;
        public string message;  // filled on error
    }

    // ─── Outgoing: plan_step ──────────────────────────────────────────────────
    [Serializable]
    public class PlanStepRequest
    {
        public string       type = "plan_step";
        public string       sessionId;
        public int          requestId;
        public int          timestep;
        public List<AgentStateDto> agents;
    }

    [Serializable]
    public class AgentStateDto
    {
        public int id;
        public int loc;         // row-major: y * width + x
        public int orientation; // 0=east 1=south 2=west 3=north
        public int goalLoc;     // row-major
    }

    // ─── Incoming: plan_result ────────────────────────────────────────────────
    [Serializable]
    public class PlanResult
    {
        public string            type;
        public string            sessionId;
        public int               requestId;
        public int               timestep;
        public float             computeMs;
        public bool              timeout;
        public string            planner;
        public int               opLen;
        public int               revisitLimit;
        public int               fallbackInherited;
        public int               multiConflictSkipped;
        public List<string>      errors;
        public List<ActionDto>   actions;
    }

    [Serializable]
    public class ActionDto
    {
        public int    id;
        public string action;   // "FW" | "CR" | "CCR" | "W"
        public int    nextLoc;
        public string planner;
        public string operation;
        public int    opIndex;
        public string debugReason;
    }

    // ─── Outgoing: shutdown ───────────────────────────────────────────────────
    [Serializable]
    public class ShutdownRequest
    {
        public string type      = "shutdown";
        public string sessionId;
    }
}
