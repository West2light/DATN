using System;

public static class InternetJoinParser
{
    public static bool TryParse(
        string input,
        string fallbackRegistryUrl,
        out NetworkEndpointConfig config,
        out string error)
    {
        config = new NetworkEndpointConfig
        {
            host = string.Empty,
            port = 7777,
            transportMode = NetworkTransportMode.Udp,
            sessionCode = string.Empty,
            mapFile = string.Empty,
            algorithm = string.Empty,
            registryUrl = string.Empty,
            maxPlayers = 8,
            isDedicatedServer = false,
        };
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Join target is empty.";
            return false;
        }

        string trimmed = input.Trim();

        if (TryParseHostPort(trimmed, out string host, out ushort port))
        {
            config.host = host;
            config.port = port;
            return true;
        }

        if (LooksLikeDirectHost(trimmed))
        {
            config.host = trimmed;
            return true;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
        {
            if (uri.Scheme.Equals("tankmapf", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseTankMapfUri(uri, out config, out error);
            }

            if ((uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                && TryParseRegistryInviteUri(uri, out config))
            {
                return true;
            }

            error = "Unsupported invite link format.";
            return false;
        }

        config.sessionCode = trimmed;
        config.registryUrl = fallbackRegistryUrl ?? string.Empty;
        return true;
    }

    private static bool TryParseHostPort(string input, out string host, out ushort port)
    {
        host = string.Empty;
        port = 7777;

        int colonIndex = input.LastIndexOf(':');
        if (colonIndex <= 0 || colonIndex >= input.Length - 1)
            return false;

        string portText = input.Substring(colonIndex + 1);
        if (!ushort.TryParse(portText, out port))
            return false;

        string hostText = input.Substring(0, colonIndex).Trim();
        if (string.IsNullOrWhiteSpace(hostText))
            return false;

        host = hostText;
        return true;
    }

    private static bool LooksLikeDirectHost(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Contains("/") || input.Contains("?") || input.Contains(" "))
            return false;

        if (input.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return input.Contains(".");
    }

    private static bool TryParseTankMapfUri(
        Uri uri,
        out NetworkEndpointConfig config,
        out string error)
    {
        config = new NetworkEndpointConfig
        {
            host = string.Empty,
            port = 7777,
            transportMode = NetworkTransportMode.Udp,
            sessionCode = string.Empty,
            mapFile = string.Empty,
            algorithm = string.Empty,
            registryUrl = string.Empty,
            maxPlayers = 8,
            isDedicatedServer = false,
        };
        error = string.Empty;

        string query = uri.Query.TrimStart('?');
        string[] pairs = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string pair in pairs)
        {
            string[] keyValue = pair.Split(new[] { '=' }, 2);
            string key = Uri.UnescapeDataString(keyValue[0]);
            string value = keyValue.Length > 1 ? Uri.UnescapeDataString(keyValue[1]) : string.Empty;

            if (key.Equals("host", StringComparison.OrdinalIgnoreCase))
                config.host = value;
            else if (key.Equals("port", StringComparison.OrdinalIgnoreCase) && ushort.TryParse(value, out ushort port))
                config.port = port;
            else if (key.Equals("transport", StringComparison.OrdinalIgnoreCase))
                config.transportMode = NetworkTransportModeUtility.Parse(value, config.transportMode);
            else if (key.Equals("session", StringComparison.OrdinalIgnoreCase))
                config.sessionCode = value;
            else if (key.Equals("map", StringComparison.OrdinalIgnoreCase))
                config.mapFile = value;
            else if (key.Equals("algo", StringComparison.OrdinalIgnoreCase)
                || key.Equals("algorithm", StringComparison.OrdinalIgnoreCase))
                config.algorithm = value;
        }

        if (string.IsNullOrWhiteSpace(config.host))
        {
            error = "Invite link is missing host.";
            return false;
        }

        return true;
    }

    private static bool TryParseRegistryInviteUri(Uri uri, out NetworkEndpointConfig config)
    {
        config = new NetworkEndpointConfig
        {
            host = string.Empty,
            port = 7777,
            transportMode = NetworkTransportMode.Udp,
            sessionCode = string.Empty,
            mapFile = string.Empty,
            algorithm = string.Empty,
            registryUrl = string.Empty,
            maxPlayers = 8,
            isDedicatedServer = false,
        };

        string sessionFromQuery = GetQueryValue(uri, "session");
        if (!string.IsNullOrWhiteSpace(sessionFromQuery))
        {
            config.sessionCode = sessionFromQuery.Trim();
            config.registryUrl = $"{uri.Scheme}://{uri.Authority}";
            return true;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || !segments[0].Equals("s", StringComparison.OrdinalIgnoreCase))
            return false;

        config.sessionCode = segments[1];
        config.registryUrl = $"{uri.Scheme}://{uri.Authority}";
        return true;
    }

    private static string GetQueryValue(Uri uri, string key)
    {
        string query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        string[] pairs = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string pair in pairs)
        {
            string[] keyValue = pair.Split(new[] { '=' }, 2);
            if (keyValue.Length == 0)
                continue;

            string currentKey = Uri.UnescapeDataString(keyValue[0]);
            if (!currentKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return keyValue.Length > 1 ? Uri.UnescapeDataString(keyValue[1]) : string.Empty;
        }

        return string.Empty;
    }
}
