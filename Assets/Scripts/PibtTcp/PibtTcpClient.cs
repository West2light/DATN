using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PibtTcp;

/// <summary>
/// Async TCP client that communicates with the PIBT C++ server.
/// All network calls run on a background thread — Unity main thread is never blocked.
/// </summary>
public class PibtTcpClient : IDisposable
{
    // ─── Config ───────────────────────────────────────────────────────────────
    public string Host           { get; }
    public int    Port           { get; }
    public int    TimeoutMs      { get; }

    // ─── Metrics (read from main thread after each step) ─────────────────────
    public float LatencyMsLast   { get; private set; }
    public float ComputeMsLast   { get; private set; }
    public int   TimeoutCount    { get; private set; }
    public string PlannerNameLast { get; private set; }
    public int    OpLenLast       { get; private set; }
    public int    RevisitLimitLast { get; private set; }
    public int    FallbackInheritedLast { get; private set; }
    public int    MultiConflictSkippedLast { get; private set; }

    // ─── Internals ───────────────────────────────────────────────────────────
    private TcpClient       _tcp;
    private NetworkStream   _stream;
    private string          _sessionId;
    private bool            _connected;
    private bool            _disposed;
    private readonly StringBuilder _incomingLine = new StringBuilder();
    private readonly Queue<string> _pendingLines = new Queue<string>();
    private readonly byte[] _readBuffer = new byte[4096];

    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    public PibtTcpClient(string host = "127.0.0.1", int port = 7777, int timeoutMs = 1000)
    {
        Host      = host;
        Port      = port;
        TimeoutMs = timeoutMs;
    }

    // ─── Connect + Hello ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens TCP connection and performs the hello handshake.
    /// Call once before the first plan_step.
    /// </summary>
public async Task<bool> ConnectAndHelloAsync(
        string       sessionId,
        int          teamSize,
        MapDto       map,
        CancellationToken ct = default)
    {
        _sessionId = sessionId;

        try
        {
            _tcp    = new TcpClient();
            _tcp.NoDelay = true;

            // connect with timeout (compatible with Unity/.NET Standard 2.1)
            var connectTask  = _tcp.ConnectAsync(Host, Port);
            var timeoutTask  = Task.Delay(TimeoutMs * 5, ct);
            var completed    = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

            if (completed == timeoutTask)
                throw new TimeoutException("TCP connect timed out.");

            await connectTask.ConfigureAwait(false); // rethrow any socket exception

            _stream = _tcp.GetStream();
            _connected = true;

            var req = new HelloRequest
            {
                sessionId = sessionId,
                teamSize  = teamSize,
                map       = map
            };

            await WriteJsonLineAsync(req, ct).ConfigureAwait(false);

            var raw = await ReadJsonObjectAsync(ct).ConfigureAwait(false);
            var ack = JsonConvert.DeserializeObject<HelloAck>(raw);

            if (ack == null || ack.status != "ok")
            {
                Debug.LogError($"[PibtTcpClient] hello_ack error: {ack?.message}");
                return false;
            }

            PlannerNameLast = ack.planner;
            Debug.Log($"[PibtTcpClient] Connected: server={ack.server}, planner={ack.planner}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PibtTcpClient] ConnectAndHello failed: {ex.Message}");
            _connected = false;
            return false;
        }
    }

    // ─── Plan step ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sends plan_step and returns plan_result.
    /// Returns null on timeout or error.
    /// </summary>
    public async Task<PlanResult> PlanStepAsync(
        int                    requestId,
        int                    timestep,
        List<AgentStateDto>    agents,
        CancellationToken      ct = default)
    {
        if (_disposed || !_connected || _stream == null)
        {
            Debug.LogWarning("[PibtTcpClient] PlanStep called while not connected.");
            return null;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed || !_connected || _stream == null)
                return null;

            var req = new PlanStepRequest
            {
                sessionId = _sessionId,
                requestId = requestId,
                timestep  = timestep,
                agents    = agents
            };

            var sendStart = DateTimeOffset.UtcNow;
            await WriteJsonLineAsync(req, ct).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeoutMs);

            try
            {
                while (true)
                {
                    var raw = await ReadJsonObjectAsync(timeoutCts.Token).ConfigureAwait(false);
                    LatencyMsLast = (float)(DateTimeOffset.UtcNow - sendStart).TotalMilliseconds;

                    var result = JsonConvert.DeserializeObject<PlanResult>(raw);
                    if (result == null)
                        return null;

                    if (result.requestId != requestId)
                    {
                        Debug.LogWarning($"[PibtTcpClient] Ignored stale plan_result req={result.requestId}, expected={requestId}");
                        continue;
                    }

                    ComputeMsLast = result.computeMs;
                    PlannerNameLast = string.IsNullOrWhiteSpace(result.planner) ? PlannerNameLast : result.planner;
                    OpLenLast = result.opLen;
                    RevisitLimitLast = result.revisitLimit;
                    FallbackInheritedLast = result.fallbackInherited;
                    MultiConflictSkippedLast = result.multiConflictSkipped;

                    if (result.errors != null && result.errors.Count > 0)
                        Debug.LogWarning($"[PibtTcpClient] plan_result errors: {string.Join(", ", result.errors)}");

                    Debug.Log(
                        $"[PibtTcpClient] req={requestId} latency={LatencyMsLast:F1}ms compute={ComputeMsLast:F1}ms timeout={TimeoutCount} " +
                        $"planner={PlannerNameLast ?? "-"} opLen={OpLenLast} revisitLimit={RevisitLimitLast} " +
                        $"fallbackInherited={FallbackInheritedLast} multiConflictSkipped={MultiConflictSkippedLast}");
                    return result;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TimeoutCount++;
                Debug.LogWarning($"[PibtTcpClient] plan_step timed out (req={requestId}, total timeouts={TimeoutCount})");
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PibtTcpClient] PlanStep exception: {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ─── Shutdown ─────────────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        if (!_connected) return;
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_disposed && _connected && _stream != null)
            {
                var req = new ShutdownRequest { sessionId = _sessionId };
                await WriteJsonLineAsync(req).ConfigureAwait(false);
            }
        }
        catch { /* best-effort */ }
        finally
        {
            CloseSocket();
            _lock.Release();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task WriteJsonLineAsync(object payload, CancellationToken ct = default)
    {
        var json  = JsonConvert.SerializeObject(payload);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await _stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> ReadJsonObjectAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        while (true)
        {
            string line = await ReadRawLineAsync(ct).ConfigureAwait(false);
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // Resync after stale partial reads: ignore fragments until a JSON object starts.
            if (sb.Length == 0 && !trimmed.StartsWith("{"))
            {
                continue;
            }

            if (trimmed.StartsWith("{"))
            {
                sb.Clear();
            }

            sb.AppendLine(line);
            string candidate = sb.ToString();
            try
            {
                JToken.Parse(candidate);
                return candidate;
            }
            catch (JsonReaderException)
            {
                // Pretty-printed JSON spans multiple lines; keep accumulating.
            }
        }
    }

    private async Task<string> ReadRawLineAsync(CancellationToken ct = default)
    {
        if (_pendingLines.Count > 0)
            return _pendingLines.Dequeue();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length, ct).ConfigureAwait(false);
            if (n == 0) throw new System.IO.EndOfStreamException("Server closed connection.");

            for (int i = 0; i < n; i++)
            {
                char c = (char)_readBuffer[i];
                if (c == '\n')
                {
                    string line = _incomingLine.ToString();
                    _incomingLine.Clear();
                    _pendingLines.Enqueue(line);
                    continue;
                }
                if (c != '\r') _incomingLine.Append(c);
            }

            if (_pendingLines.Count > 0)
                return _pendingLines.Dequeue();
        }
    }

    private void CloseSocket()
    {
        _connected = false;
        try { _stream?.Close(); } catch { /* best-effort */ }
        try { _tcp?.Close(); } catch { /* best-effort */ }
        _stream = null;
        _tcp = null;
        _incomingLine.Clear();
        _pendingLines.Clear();
    }

    // ─── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (_connected && _stream != null)
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Best-effort shutdown; always close the socket below.
        }
        finally
        {
            _disposed = true;
            CloseSocket();
        }
    }
}
