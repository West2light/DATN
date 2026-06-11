using System;

public enum NetworkTransportMode
{
    Udp = 0,
    WebSocket = 1,
}

public static class NetworkTransportModeUtility
{
    public static NetworkTransportMode Parse(string value, NetworkTransportMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "udp" => NetworkTransportMode.Udp,
            "ws" => NetworkTransportMode.WebSocket,
            "websocket" => NetworkTransportMode.WebSocket,
            "websockets" => NetworkTransportMode.WebSocket,
            "webgl" => NetworkTransportMode.WebSocket,
            _ => fallback,
        };
    }

    public static string ToArgumentValue(this NetworkTransportMode transportMode)
    {
        return transportMode == NetworkTransportMode.WebSocket ? "websocket" : "udp";
    }
}
