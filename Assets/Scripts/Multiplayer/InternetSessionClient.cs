using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public static class InternetSessionClient
{
    [Serializable]
    private class CreateRoomResponse
    {
        public string code;
        public string joinUrl;
        public string webUrl;
    }

    [Serializable]
    private class SessionLookupResponse
    {
        public string code;
        public string host;
        public ushort gamePort;
        public string transport;
        public string webHost;
        public ushort webGamePort;
        public string webTransport;
        public string map;
        public string algorithm;
        public int maxPlayers;
        public long expiresAt;
    }

    public static IEnumerator ResolveSession(
        string registryBaseUrl,
        string code,
        Action<NetworkEndpointConfig> onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(registryBaseUrl))
        {
            onError?.Invoke("Registry URL is missing.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            onError?.Invoke("Session code is missing.");
            yield break;
        }

        string baseUrl = registryBaseUrl.Trim().TrimEnd('/');
        string normalizedCode = code.Trim().ToUpperInvariant();
        string requestUrl = $"{baseUrl}/api/sessions/{UnityWebRequest.EscapeURL(normalizedCode)}";

        using UnityWebRequest request = UnityWebRequest.Get(requestUrl);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message = !string.IsNullOrWhiteSpace(request.error)
                ? request.error
                : "Unknown HTTP error.";
            onError?.Invoke($"Session lookup failed: {message}");
            yield break;
        }

        string json = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        SessionLookupResponse response = null;
        try
        {
            response = JsonUtility.FromJson<SessionLookupResponse>(json);
        }
        catch (Exception)
        {
            // Ignore parse exception and handle as invalid payload below.
        }

        if (response == null || string.IsNullOrWhiteSpace(response.host))
        {
            onError?.Invoke("Registry returned an invalid session payload.");
            yield break;
        }

        NetworkTransportMode defaultTransportMode =
#if UNITY_WEBGL && !UNITY_EDITOR
            NetworkTransportMode.WebSocket;
#else
            NetworkTransportMode.Udp;
#endif

        bool preferWebEndpoint =
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        string resolvedHost = preferWebEndpoint && !string.IsNullOrWhiteSpace(response.webHost)
            ? response.webHost
            : response.host;
        ushort resolvedPort = preferWebEndpoint && response.webGamePort != 0
            ? response.webGamePort
            : (response.gamePort == 0 ? (ushort)7777 : response.gamePort);
        string transportValue = preferWebEndpoint && !string.IsNullOrWhiteSpace(response.webTransport)
            ? response.webTransport
            : response.transport;
        bool secureWebSocket = NetworkTransportModeUtility.IsSecureWebSocket(transportValue)
            || (preferWebEndpoint && resolvedPort == 443);
        string secureWebSocketHost = secureWebSocket
            ? (!string.IsNullOrWhiteSpace(response.webHost) ? response.webHost : resolvedHost)
            : string.Empty;
        string connectionHost = secureWebSocket && !string.IsNullOrWhiteSpace(response.host)
            ? response.host
            : resolvedHost;

        NetworkEndpointConfig endpoint = new NetworkEndpointConfig
        {
            host = connectionHost,
            port = resolvedPort,
            transportMode = NetworkTransportModeUtility.Parse(transportValue, defaultTransportMode),
            sessionCode = string.IsNullOrWhiteSpace(response.code) ? normalizedCode : response.code,
            mapFile = response.map ?? string.Empty,
            algorithm = string.IsNullOrWhiteSpace(response.algorithm) ? "AStar" : response.algorithm,
            registryUrl = baseUrl,
            maxPlayers = Mathf.Max(1, response.maxPlayers),
            isDedicatedServer = false,
            secureWebSocket = secureWebSocket,
            secureWebSocketHost = secureWebSocketHost,
        };

        onSuccess?.Invoke(endpoint);
    }

    public static IEnumerator CreateRoom(
        string registryBaseUrl,
        string mapFile,
        string algorithm,
        int maxPlayers,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(registryBaseUrl))
        {
            onError?.Invoke("Registry URL is missing.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(mapFile))
        {
            onError?.Invoke("Map is required.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(algorithm))
        {
            onError?.Invoke("Algorithm is required.");
            yield break;
        }

        string normalizedMap = System.IO.Path.GetFileName(mapFile.Trim());
        string requestUrl = $"{registryBaseUrl.Trim().TrimEnd('/')}/api/rooms";
        string payload = JsonUtility.ToJson(new RoomCreateRequest
        {
            map = normalizedMap,
            algorithm = algorithm.Trim(),
            maxPlayers = Mathf.Max(1, maxPlayers),
        });

        using UnityWebRequest request = new UnityWebRequest(requestUrl, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(payload));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message = !string.IsNullOrWhiteSpace(request.error)
                ? request.error
                : "Unknown HTTP error.";
            onError?.Invoke($"Create room failed: {message}");
            yield break;
        }

        string json = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        CreateRoomResponse response = null;
        try
        {
            response = JsonUtility.FromJson<CreateRoomResponse>(json);
        }
        catch (Exception)
        {
        }

        string normalizedBaseUrl = registryBaseUrl.Trim().TrimEnd('/');
        string normalizedCode = response?.code != null ? response.code.Trim() : string.Empty;
        string joinTarget = !string.IsNullOrWhiteSpace(response?.webUrl)
            ? response.webUrl.Trim()
            : (!string.IsNullOrWhiteSpace(response?.joinUrl) && !response.joinUrl.Trim().EndsWith($"/s/{normalizedCode}", StringComparison.OrdinalIgnoreCase)
                ? response.joinUrl.Trim()
                : (!string.IsNullOrWhiteSpace(normalizedCode)
                    ? $"{normalizedBaseUrl}/play?session={UnityWebRequest.EscapeURL(normalizedCode)}"
                    : string.Empty));
        if (string.IsNullOrWhiteSpace(joinTarget))
        {
            onError?.Invoke("Create room returned an invalid response.");
            yield break;
        }

        onSuccess?.Invoke(joinTarget);
    }

    [Serializable]
    private class RoomCreateRequest
    {
        public string map;
        public string algorithm;
        public int maxPlayers;
    }
}
