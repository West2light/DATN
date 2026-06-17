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
            "wss" => NetworkTransportMode.WebSocket,
            "websocket" => NetworkTransportMode.WebSocket,
            "websockets" => NetworkTransportMode.WebSocket,
            "securewebsocket" => NetworkTransportMode.WebSocket,
            "securewebsockets" => NetworkTransportMode.WebSocket,
            "websocketsecure" => NetworkTransportMode.WebSocket,
            "websocketssecure" => NetworkTransportMode.WebSocket,
            "webgl" => NetworkTransportMode.WebSocket,
            _ => fallback,
        };
    }

    public static bool IsSecureWebSocket(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "wss" => true,
            "securewebsocket" => true,
            "securewebsockets" => true,
            "websocketsecure" => true,
            "websocketssecure" => true,
            _ => false,
        };
    }

    public static string ToArgumentValue(this NetworkTransportMode transportMode)
    {
        return transportMode == NetworkTransportMode.WebSocket ? "websocket" : "udp";
    }
}
