using System;

[Serializable]
public struct NetworkEndpointConfig
{
    public string host;
    public ushort port;
    public NetworkTransportMode transportMode;
    public string sessionCode;
    public string mapFile;
    public string algorithm;
    public string registryUrl;
    public int maxPlayers;
    public bool isDedicatedServer;
    public bool secureWebSocket;
    public string secureWebSocketHost;

    public static NetworkEndpointConfig DefaultLan(string mapFile, string algorithm)
    {
        return new NetworkEndpointConfig
        {
            host = "0.0.0.0",
            port = 7777,
            transportMode = NetworkTransportMode.Udp,
            sessionCode = string.Empty,
            mapFile = mapFile,
            algorithm = algorithm,
            registryUrl = string.Empty,
            maxPlayers = 8,
            isDedicatedServer = false,
            secureWebSocket = false,
            secureWebSocketHost = string.Empty,
        };
    }
}
