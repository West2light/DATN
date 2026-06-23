using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// JSON-lines TCP client for the external PIBT server.
/// Flow: hello -> plan_step* -> shutdown.
/// </summary>
public class PIBTTcpClient : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("Raw TCP host name/IP or URL. Example: 127.0.0.1 (local server, incl. WSL2 localhost forwarding) or http://110.172.28.110:7777/")]
    public string host = "127.0.0.1";
    public int port = 7777;
    [Min(1)] public int connectTimeoutMs = 5000;
    [Min(1000)] public int helloTimeoutMs = 60000;
    [Min(1000)] public int planTimeoutMs = 15000;

    private TcpClient _client;
    private Stream _stream;
    private string _sessionId;
    private bool _helloAccepted;

    public bool IsConnected => _client != null && _client.Connected;
    public string LastError { get; private set; }

    // Server-authoritative next cell (build-local flat index) per agent id from the last plan_result.
    // -1 means the server did not provide a usable nextLoc for that agent.
    public int[] LastNextLocs { get; private set; }

    public bool Connect()
    {
        Disconnect();  // Close any existing connection before opening a new one.
        try
        {
            LastError = null;
            ResolveEndpoint(host, port, out string connectHost, out int connectPort, out bool useTls);
            Debug.Log($"[PIBTTcpClient] Connecting raw TCP endpoint host='{host}', port={port} -> {connectHost}:{connectPort}, tls={useTls}, connectTimeoutMs={connectTimeoutMs}");

            _client = new TcpClient();
            _client.SendTimeout = planTimeoutMs;
            _client.ReceiveTimeout = helloTimeoutMs;
            IAsyncResult connectResult = _client.BeginConnect(connectHost, connectPort, null, null);
            if (!connectResult.AsyncWaitHandle.WaitOne(connectTimeoutMs))
            {
                throw new TimeoutException($"Timed out connecting to {connectHost}:{connectPort}");
            }
            _client.EndConnect(connectResult);

            Stream stream = _client.GetStream();
            if (useTls)
            {
                var ssl = new SslStream(stream, false, (sender, certificate, chain, errors) => true);
                ssl.AuthenticateAsClient(connectHost);
                stream = ssl;
            }

            _stream = stream;
            Debug.Log($"[PIBTTcpClient] Connected to {connectHost}:{connectPort}" + (useTls ? " over TLS" : ""));
            return true;
        }
        catch (Exception e)
        {
            LastError = $"Connect failed: {e.Message}";
            Debug.LogError($"[PIBTTcpClient] Connect failed: {e.Message}");
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
        _helloAccepted = false;
        _sessionId = null;
    }

    public bool Shutdown()
    {
        if (!_helloAccepted || string.IsNullOrEmpty(_sessionId))
        {
            Disconnect();
            return true;
        }

        bool sent = SendLine($"{{\"type\":\"shutdown\",\"sessionId\":\"{Escape(_sessionId)}\"}}");
        string resp = sent ? RecvLine() : null;
        Disconnect();

        if (resp == null) return false;
        if (resp.Contains("\"shutdown_ack\"")) return true;

        Debug.LogWarning($"[PIBTTcpClient] Unexpected shutdown response: {resp}");
        return false;
    }

    // OnDestroy just disconnects — avoids race with background threads that may still be
    // reading the stream. Callers that need a graceful shutdown must call Shutdown() explicitly
    // before the object is destroyed (e.g., MapScenarioBootstrapPIBT_TCP.ShutdownGracefully).
    private void OnDestroy() => Disconnect();

    public bool Hello(string sessionId, int width, int height, string symbols, int teamSize)
    {
        _sessionId = sessionId;
        LastError = null;

        string json = BuildHelloRequest(sessionId, width, height, symbols, teamSize);

        Debug.Log($"[PIBTTcpClient] Sending hello session={sessionId}, teamSize={teamSize}, map={width}x{height}, symbolsLength={symbols?.Length ?? 0}, payloadBytes={Encoding.UTF8.GetByteCount(json) + 1}");
        if (!SendLine(json)) return false;

        if (_client != null) _client.ReceiveTimeout = helloTimeoutMs;
        string resp = RecvLine();
        if (resp == null)
        {
            LastError = "No hello_ack received; server closed connection or timed out.";
            Debug.LogError($"[PIBTTcpClient] {LastError}");
            return false;
        }

        Debug.Log($"[PIBTTcpClient] hello response raw: {resp}");

        bool ok = resp.Contains("\"hello_ack\"") &&
                  (!resp.Contains("\"error\"") || resp.Contains("\"status\":\"ok\""));
        if (ok)
        {
            _helloAccepted = true;
            // F-U1: validate echoed map dims if server supports F-S2
            try
            {
                HelloAckDto ack = JsonUtility.FromJson<HelloAckDto>(resp);
                if (ack != null && ack.width > 0 && ack.height > 0)
                {
                    if (ack.width != width || ack.height != height)
                    {
                        Debug.LogError($"[PIBTTcpClient] hello_ack map dims MISMATCH! sent={width}x{height} server_acked={ack.width}x{ack.height} — aborting session");
                        LastError = $"hello_ack dims mismatch: sent={width}x{height} acked={ack.width}x{ack.height}";
                        _helloAccepted = false;
                        return false;
                    }
                    Debug.Log($"[PIBTTcpClient] hello_ack dims validated OK: {ack.width}x{ack.height} ({ack.cellCount} cells)");
                }
            }
            catch (Exception) { /* server without F-S2 — skip validation */ }

            Debug.Log("[PIBTTcpClient] Hello OK");
            return true;
        }

        Debug.LogError($"[PIBTTcpClient] Hello failed: {resp}");
        LastError = $"Hello failed: {resp}";
        return false;
    }

    // C-R1: explicit reset — reuse a live connection for a new game without reconnecting.
    // Server (S-R2) clears all planner globals and returns reset_ack.
    // Caller must call Hello() with a new sessionId after this returns true.
    // Falls back gracefully if server does not support reset (pre-S-R2 build).
    public bool Reset()
    {
        if (!IsConnected)
        {
            LastError = "Not connected.";
            return false;
        }

        string sid = _sessionId ?? string.Empty;
        if (!SendLine($"{{\"type\":\"reset\",\"sessionId\":\"{Escape(sid)}\"}}"))
        {
            LastError = "Failed to send reset.";
            return false;
        }

        if (_client != null) _client.ReceiveTimeout = planTimeoutMs;
        string resp = RecvLine();
        if (resp == null)
        {
            LastError = "No reset_ack received; server closed connection or timed out.";
            Debug.LogError($"[PIBTTcpClient] {LastError}");
            return false;
        }

        _helloAccepted = false;
        _sessionId = null;

        bool ok = resp.Contains("\"reset_ack\"") && resp.Contains("\"status\":\"ok\"");
        if (!ok)
        {
            LastError = $"Reset not acknowledged (old server without S-R2?): {resp}";
            Debug.LogWarning($"[PIBTTcpClient] {LastError}");
            return false;
        }

        Debug.Log("[PIBTTcpClient] Reset OK");
        LastError = null;
        return true;
    }

    // C-R2: fire-and-forget shutdown line — used from OnDestroy to give server an explicit
    // reset signal without blocking the main thread waiting for shutdown_ack.
    // Always call Disconnect() after this.
    public void SendShutdown()
    {
        if (!_helloAccepted || string.IsNullOrEmpty(_sessionId)) return;
        try { SendLine($"{{\"type\":\"shutdown\",\"sessionId\":\"{Escape(_sessionId)}\"}}"); }
        catch { /* ignore errors during teardown */ }
    }

    public string[] PlanStep(int requestId, int timestep, (int id, int loc, int orientation, int goalLoc)[] agents)
    {
        string request = BuildPlanStepRequest(_sessionId, requestId, timestep, agents);

        if (!SendLine(request))
        {
            LastError = "Failed to send plan_step.";
            return null;
        }

        if (_client != null) _client.ReceiveTimeout = planTimeoutMs;
        string resp = RecvLine();
        if (resp == null)
        {
            LastError = "No plan_result received; server closed connection or timed out.";
            return null;
        }
        if (resp.Contains("\"errors\"") && !resp.Contains("\"errors\":[]"))
        {
            Debug.LogWarning($"[PIBTTcpClient] plan_step returned errors: {resp}");
        }
        if (!resp.Contains("\"plan_result\""))
        {
            Debug.LogError($"[PIBTTcpClient] Unexpected plan_step response: {resp}");
            LastError = $"Unexpected plan_step response: {resp}";
            return null;
        }

        string[] actions = ParseActions(resp, agents.Length);
        LastNextLocs = ParseNextLocs(resp, agents.Length);
        if (actions == null)
        {
            Debug.LogError($"[PIBTTcpClient] Could not parse actions from plan_result: {resp}");
            LastError = $"Could not parse actions from plan_result: {resp}";
            return null;
        }

        LastError = null;
        return actions;
    }

    internal static string BuildHelloRequest(string sessionId, int width, int height, string symbols, int teamSize)
    {
        return
            "{\"type\":\"hello\"" +
            $",\"sessionId\":\"{Escape(sessionId)}\"" +
            $",\"teamSize\":{teamSize}" +
            ",\"map\":{" +
            $"\"width\":{width},\"height\":{height},\"symbols\":\"{Escape(symbols)}\"" +
            "}}";
    }

    internal static string BuildPlanStepRequest(
        string sessionId,
        int requestId,
        int timestep,
        (int id, int loc, int orientation, int goalLoc)[] agents)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"plan_step\"");
        sb.Append(",\"sessionId\":\"").Append(Escape(sessionId)).Append('"');
        sb.Append(",\"requestId\":").Append(requestId);
        sb.Append(",\"timestep\":").Append(timestep);
        sb.Append(",\"agents\":[");

        for (int i = 0; i < agents.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('{');
            sb.Append("\"id\":").Append(agents[i].id);
            sb.Append(",\"loc\":").Append(agents[i].loc);
            sb.Append(",\"orientation\":").Append(agents[i].orientation);
            sb.Append(",\"goalLoc\":").Append(agents[i].goalLoc);
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private bool SendLine(string json)
    {
        if (_stream == null) return false;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            _stream.Write(data, 0, data.Length);
            _stream.Flush();
            return true;
        }
        catch (Exception e)
        {
            LastError = $"Send error: {e.Message}";
            Debug.LogError($"[PIBTTcpClient] Send error: {e.Message}");
            return false;
        }
    }

    private string RecvLine()
    {
        if (_stream == null) return null;

        var sb = new StringBuilder();
        try
        {
            int b;
            while ((b = _stream.ReadByte()) != -1)
            {
                if ((char)b == '\n') return sb.ToString().TrimEnd('\r');
                sb.Append((char)b);
            }
        }
        catch (Exception e)
        {
            LastError = $"Recv error: {e.Message}";
            Debug.LogError($"[PIBTTcpClient] Recv error: {e.Message}");
        }

        return null;
    }

    private static void ResolveEndpoint(string configuredHost, int configuredPort, out string connectHost, out int connectPort, out bool useTls)
    {
        useTls = false;
        connectHost = configuredHost;
        connectPort = configuredPort;

        if (Uri.TryCreate(configuredHost, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Host))
        {
            connectHost = uri.Host;
            useTls = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Scheme, "tls", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Scheme, "ssl", StringComparison.OrdinalIgnoreCase);
            connectPort = uri.IsDefaultPort
                ? (useTls ? 443 : 80)
                : uri.Port;
            return;
        }

        int colon = configuredHost.LastIndexOf(':');
        if (colon > 0 && colon < configuredHost.Length - 1 &&
            int.TryParse(configuredHost.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort))
        {
            connectHost = configuredHost.Substring(0, colon);
            connectPort = parsedPort;
        }
    }

    [Serializable]
    private class HelloAckDto
    {
        public string type;
        public string status;
        public int width;
        public int height;
        public int cellCount;
    }

    [Serializable]
    private class PlanResultDto
    {
        public string type;
        public PlanActionDto[] actions;
    }

    [Serializable]
    private class PlanActionDto
    {
        public int id;
        public string action;
        public int nextLoc;
    }

    internal static string[] ParseActions(string json, int expectedLength)
    {
        try
        {
            PlanResultDto dto = JsonUtility.FromJson<PlanResultDto>(json);
            if (dto?.actions != null && dto.actions.Length > 0)
            {
                int n = expectedLength > 0 ? expectedLength : dto.actions.Length;
                string[] result = new string[n];

                for (int i = 0; i < dto.actions.Length; i++)
                {
                    PlanActionDto entry = dto.actions[i];
                    if (entry == null || string.IsNullOrEmpty(entry.action)) continue;

                    int idx = entry.id >= 0 && entry.id < n ? entry.id : i;
                    if (idx >= 0 && idx < n)
                        result[idx] = entry.action;
                }

                for (int i = 0; i < result.Length; i++)
                    if (string.IsNullOrEmpty(result[i])) result[i] = "W";

                return result;
            }
        }
        catch (Exception)
        {
            // Fall through to the legacy string-array parser.
        }

        return ParseStringArray(json, "actions", expectedLength);
    }

    internal static int[] ParseNextLocs(string json, int expectedLength)
    {
        var result = new int[expectedLength];
        for (int i = 0; i < expectedLength; i++) result[i] = -1;
        try
        {
            PlanResultDto dto = JsonUtility.FromJson<PlanResultDto>(json);
            if (dto?.actions != null)
            {
                for (int i = 0; i < dto.actions.Length; i++)
                {
                    PlanActionDto entry = dto.actions[i];
                    if (entry == null) continue;
                    int idx = entry.id >= 0 && entry.id < expectedLength ? entry.id : i;
                    if (idx >= 0 && idx < expectedLength) result[idx] = entry.nextLoc;
                }
            }
        }
        catch (System.Exception) { /* leave as -1 → coordinator falls back to dead-reckon */ }
        return result;
    }

    private static string[] ParseStringArray(string json, string property, int expectedLength)
    {
        int key = json.IndexOf($"\"{property}\"", StringComparison.Ordinal);
        if (key < 0) return null;

        int start = json.IndexOf('[', key);
        if (start < 0) return null;

        var values = new List<string>();
        int i = start + 1;
        while (i < json.Length)
        {
            while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
            if (i >= json.Length || json[i] == ']') break;
            if (json[i] != '"') return null;

            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '\\' && i < json.Length)
                {
                    sb.Append(json[i++]);
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            values.Add(sb.ToString());
        }

        if (expectedLength >= 0 && values.Count < expectedLength)
        {
            Debug.LogWarning($"[PIBTTcpClient] Expected {expectedLength} actions, got {values.Count}.");
        }

        return values.ToArray();
    }

    internal static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
