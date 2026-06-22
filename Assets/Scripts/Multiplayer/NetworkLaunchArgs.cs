using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class NetworkLaunchArgs
{
    public bool isDedicatedServer;
    public string host = "0.0.0.0";
    public ushort port = 7777;
    public NetworkTransportMode transportMode = NetworkTransportMode.Udp;
    public string mapFile = string.Empty;
    public string algorithm = "AStar";
    public int maxPlayers = 8;
    public int enemyMultiplier = 3;
    public string sessionCode = string.Empty;
    public string registryUrl = string.Empty;

    public static NetworkLaunchArgs Parse(string[] args)
    {
        var parsed = new NetworkLaunchArgs();
        var values = ParsePairs(args);

        parsed.isDedicatedServer = values.ContainsKey("server");
        parsed.host = GetString(values, "host", "TANK_HOST", parsed.host);
        parsed.port = GetUShort(values, "port", "TANK_PORT", parsed.port);
        parsed.transportMode = NetworkTransportModeUtility.Parse(
            GetString(values, "transport", "TANK_TRANSPORT", parsed.transportMode.ToArgumentValue()),
            parsed.transportMode);
        parsed.mapFile = GetString(values, "map", "TANK_MAP", parsed.mapFile);
        parsed.algorithm = GetString(values, "algorithm", "TANK_ALGORITHM", parsed.algorithm);
        parsed.maxPlayers = GetInt(values, "maxPlayers", "TANK_MAX_PLAYERS", parsed.maxPlayers);
        parsed.enemyMultiplier = GetInt(values, "enemyMultiplier", "TANK_ENEMY_MULTIPLIER", parsed.enemyMultiplier);
        parsed.sessionCode = GetString(values, "sessionCode", "TANK_SESSION_CODE", parsed.sessionCode);
        parsed.registryUrl = GetString(values, "registryUrl", "TANK_REGISTRY_URL", parsed.registryUrl);

        return parsed;
    }

    public NetworkEndpointConfig ToEndpointConfig()
    {
        return new NetworkEndpointConfig
        {
            host = host,
            port = port,
            transportMode = transportMode,
            sessionCode = sessionCode ?? string.Empty,
            mapFile = mapFile ?? string.Empty,
            algorithm = string.IsNullOrWhiteSpace(algorithm) ? "AStar" : algorithm,
            registryUrl = registryUrl ?? string.Empty,
            maxPlayers = Mathf.Max(1, maxPlayers),
            enemyMultiplier = LanSessionManager.NormalizeEnemyMultiplier(enemyMultiplier),
            isDedicatedServer = isDedicatedServer,
            secureWebSocket = false,
            secureWebSocketHost = string.Empty,
        };
    }

    private static Dictionary<string, string> ParsePairs(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args == null)
            return values;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.IsNullOrWhiteSpace(arg) || !arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            string key = arg.Substring(2);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            string value = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[i + 1];
                i++;
            }

            values[key] = value;
        }

        return values;
    }

    private static string GetString(
        IReadOnlyDictionary<string, string> values,
        string argKey,
        string envKey,
        string fallback)
    {
        if (values.TryGetValue(argKey, out string argValue) && !string.IsNullOrWhiteSpace(argValue))
            return argValue;

        string envValue = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(envValue) ? fallback : envValue;
    }

    private static ushort GetUShort(
        IReadOnlyDictionary<string, string> values,
        string argKey,
        string envKey,
        ushort fallback)
    {
        if (TryParseUShort(GetString(values, argKey, envKey, string.Empty), out ushort value))
            return value;
        return fallback;
    }

    private static int GetInt(
        IReadOnlyDictionary<string, string> values,
        string argKey,
        string envKey,
        int fallback)
    {
        if (int.TryParse(GetString(values, argKey, envKey, string.Empty), out int value))
            return value;
        return fallback;
    }

    private static bool TryParseUShort(string value, out ushort parsed)
    {
        return ushort.TryParse(value, out parsed);
    }
}
