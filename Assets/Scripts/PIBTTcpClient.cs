using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// TCP client that talks to the C++ PIBT server.
/// Attach to a GameObject in the PIBT game scene.
/// Call Init() once after the map is loaded, then Step() each AI tick.
/// </summary>
public class PIBTTcpClient : MonoBehaviour
{
    [Header("Server")]
    public string host = "127.0.0.1";
    public int    port = 9999;

    // -- state --
    private TcpClient   _client;
    private NetworkStream _stream;
    private readonly object _lock = new();

    public bool IsConnected => _client != null && _client.Connected;

    // ── Connect / Disconnect ─────────────────────────────────────────────────

    public bool Connect()
    {
        try {
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
            return true;
        } catch (Exception e) {
            Debug.LogError($"[PIBTTcpClient] Connect failed: {e.Message}");
            _client = null;
            return false;
        }
    }

    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _client = null;
        _stream = null;
    }

    private void OnDestroy() => Disconnect();

    // ── Protocol helpers ────────────────────────────────────────────────────

    private bool SendLine(string json)
    {
        if (_stream == null) return false;
        try {
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            _stream.Write(data, 0, data.Length);
            return true;
        } catch (Exception e) {
            Debug.LogError($"[PIBTTcpClient] Send error: {e.Message}");
            return false;
        }
    }

    // Read exactly one newline-terminated line.
    private string RecvLine()
    {
        if (_stream == null) return null;
        var sb = new StringBuilder();
        try {
            int b;
            while ((b = _stream.ReadByte()) != -1) {
                if ((char)b == '\n') return sb.ToString();
                sb.Append((char)b);
            }
        } catch (Exception e) {
            Debug.LogError($"[PIBTTcpClient] Recv error: {e.Message}");
        }
        return null;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Send map + initial agent positions to the PIBT server.
    /// mapData: flat array [0=walkable, 1=obstacle], row-major (top-left origin).
    /// agents: list of (flatPos, flatGoal).
    /// </summary>
    public bool Init(int rows, int cols, int[] mapData, (int pos, int goal)[] agents)
    {
        var agentArr = new List<object>();
        for (int i = 0; i < agents.Length; i++)
            agentArr.Add(new { id = i, pos = agents[i].pos, goal = agents[i].goal });

        string json = SimpleJson(new {
            type   = "init",
            rows,
            cols,
            map    = mapData,
            agents = agentArr
        });

        if (!SendLine(json)) return false;

        string resp = RecvLine();
        if (resp == null) return false;

        if (resp.Contains("\"init_ack\"")) {
            Debug.Log("[PIBTTcpClient] Init OK");
            return true;
        }
        Debug.LogError($"[PIBTTcpClient] Init failed: {resp}");
        return false;
    }

    /// <summary>
    /// Run one PIBT step. Returns next flat cell index per agent, or null on error.
    /// </summary>
    public int[] Step(int frame, (int id, int pos, int goal)[] agents)
    {
        var agentArr = new List<object>();
        foreach (var a in agents)
            agentArr.Add(new { a.id, a.pos, a.goal });

        string json = SimpleJson(new {
            type   = "step",
            frame,
            agents = agentArr
        });

        if (!SendLine(json)) return null;

        string resp = RecvLine();
        if (resp == null) return null;

        return ParseStepAck(resp, agents.Length);
    }

    // ── JSON helpers (no dependency on Newtonsoft) ───────────────────────────

    private static int[] ParseStepAck(string json, int n)
    {
        int[] result = new int[n];
        // Extract "agents":[{"id":0,"next":1},...] using simple string scanning.
        int arrStart = json.IndexOf("\"agents\"", StringComparison.Ordinal);
        if (arrStart < 0) return null;
        int bracket = json.IndexOf('[', arrStart);
        if (bracket < 0) return null;
        int depth = 0;
        int i = bracket;
        while (i < json.Length) {
            char c = json[i];
            if (c == '{') {
                depth++;
                // parse one entry
                int id   = ExtractInt(json, i, "\"id\"");
                int next = ExtractInt(json, i, "\"next\"");
                if (id >= 0 && id < n) result[id] = next;
            } else if (c == ']' && depth == 0) break;
            if (c == '}') depth--;
            i++;
        }
        return result;
    }

    private static int ExtractInt(string s, int start, string key)
    {
        int k = s.IndexOf(key, start, StringComparison.Ordinal);
        if (k < 0) return -1;
        int colon = s.IndexOf(':', k + key.Length);
        if (colon < 0) return -1;
        int numStart = colon + 1;
        while (numStart < s.Length && (s[numStart] == ' ')) numStart++;
        int numEnd = numStart;
        while (numEnd < s.Length && (char.IsDigit(s[numEnd]) || s[numEnd] == '-')) numEnd++;
        if (numEnd == numStart) return -1;
        return int.Parse(s.Substring(numStart, numEnd - numStart));
    }

    // Minimal serializer for anonymous objects (no reflection needed at runtime).
    private static string SimpleJson(object obj)
    {
        // We leverage C# anonymous type toString patterns manually.
        // For reliability in IL2CPP/AOT builds, build JSON strings directly.
        // This method is only called with known shapes, so we use a typed helper.
        return Newtonsoft_Fallback(obj);
    }

    private static string Newtonsoft_Fallback(object obj)
    {
        // Build JSON manually from known anonymous type shapes.
        // Avoids requiring Newtonsoft.Json in the project.
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var prop in obj.GetType().GetProperties()) {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(prop.Name).Append("\":");
            AppendValue(sb, prop.GetValue(obj));
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendValue(StringBuilder sb, object val)
    {
        if (val == null)            { sb.Append("null"); return; }
        if (val is bool b)          { sb.Append(b ? "true" : "false"); return; }
        if (val is string s)        { sb.Append('"').Append(s).Append('"'); return; }
        if (val is int i)           { sb.Append(i); return; }
        if (val is float f)         { sb.Append(f); return; }
        if (val is int[] arr)       { AppendIntArray(sb, arr); return; }
        if (val is List<object> lst){ AppendList(sb, lst); return; }
        // Nested anonymous object — recurse
        sb.Append(Newtonsoft_Fallback(val));
    }

    private static void AppendIntArray(StringBuilder sb, int[] arr)
    {
        sb.Append('[');
        for (int i = 0; i < arr.Length; i++) {
            if (i > 0) sb.Append(',');
            sb.Append(arr[i]);
        }
        sb.Append(']');
    }

    private static void AppendList(StringBuilder sb, List<object> lst)
    {
        sb.Append('[');
        for (int i = 0; i < lst.Count; i++) {
            if (i > 0) sb.Append(',');
            AppendValue(sb, lst[i]);
        }
        sb.Append(']');
    }
}
