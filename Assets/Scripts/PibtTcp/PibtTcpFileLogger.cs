using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class PibtTcpFileLogger
{
    private static readonly object Sync = new object();
    private static readonly string[] FilterTokens =
    {
        "[PIBT_TCP_TRACE]",
        "[PIBT_TCP_DIAG]",
        "[PibtTcpClient]",
        "[MapScenarioBootstrapPIBTTcp]",
        "[PIBT_TCP_ROT]",
        "[PIBT_TCP_PHASE_D]",
        "PlanStep exception"
    };

    private static StreamWriter _writer;
    private static string _activePath;
    private static bool _subscribed;

    public static bool IsCapturing
    {
        get
        {
            lock (Sync)
            {
                return _writer != null;
            }
        }
    }

    public static string ActivePath
    {
        get
        {
            lock (Sync)
            {
                return _activePath;
            }
        }
    }

    public static string StartCapture(string sessionId, string filePrefix = "epibt_m0_unity_console")
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = "no_session";

        lock (Sync)
        {
            EnsureSubscribed();
            StopCaptureLocked(writeStopLine: false);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputDir = Path.Combine(projectRoot, "adds", "output");
            Directory.CreateDirectory(outputDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeSession = SanitizeToken(sessionId);
            string safePrefix = SanitizeToken(filePrefix);
            string fileName = $"{safePrefix}_{timestamp}_{safeSession}.txt";
            string path = Path.Combine(outputDir, fileName);

            _writer = new StreamWriter(
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            _activePath = path;

            WriteLineLocked($"capture_start session={sessionId} unityVersion={Application.unityVersion}");
            return path;
        }
    }

    public static void StopCapture()
    {
        lock (Sync)
        {
            StopCaptureLocked(writeStopLine: true);
        }
    }

    public static void WriteLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (Sync)
        {
            if (_writer == null)
                return;

            WriteLineLocked(message);
        }
    }

    private static void EnsureSubscribed()
    {
        if (_subscribed)
            return;

        Application.logMessageReceivedThreaded += HandleUnityLog;
        _subscribed = true;
    }

    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        lock (Sync)
        {
            if (_writer == null || !ShouldPersist(condition, type))
                return;

            WriteLineLocked($"[{type}] {condition}");
            if ((type == LogType.Error || type == LogType.Assert || type == LogType.Exception) &&
                !string.IsNullOrWhiteSpace(stackTrace))
            {
                _writer.WriteLine(stackTrace.TrimEnd());
            }
        }
    }

    private static bool ShouldPersist(string condition, LogType type)
    {
        if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
            return true;

        if (string.IsNullOrWhiteSpace(condition))
            return false;

        for (int i = 0; i < FilterTokens.Length; i++)
        {
            if (condition.IndexOf(FilterTokens[i], StringComparison.Ordinal) >= 0)
                return true;
        }

        return false;
    }

    private static void StopCaptureLocked(bool writeStopLine)
    {
        if (_writer == null)
        {
            _activePath = null;
            return;
        }

        if (writeStopLine)
            WriteLineLocked("capture_stop");

        _writer.Dispose();
        _writer = null;
        _activePath = null;
    }

    private static void WriteLineLocked(string message)
    {
        if (_writer == null)
            return;

        _writer.WriteLine($"{DateTime.Now:O} {message}");
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "log";

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(Path.GetInvalidFileNameChars().AsSpan().IndexOf(c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }
}
