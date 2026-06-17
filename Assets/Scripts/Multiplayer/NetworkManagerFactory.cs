using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class NetworkManagerFactory
{
    public static bool Ensure(
        string bindAddress,
        ushort port,
        bool isServer,
        NetworkTransportMode transportMode,
        out NetworkManager networkManager)
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager != null)
        {
            UnityTransport existingTransport = networkManager.GetComponent<UnityTransport>();
            if (existingTransport != null)
                ConfigureTransport(existingTransport, bindAddress, port, transportMode);

            ConfigureConnectionApproval(networkManager, isServer);
            return true;
        }

        GameObject go = new GameObject("NetworkManager");
        UnityEngine.Object.DontDestroyOnLoad(go);

        UnityTransport transport = go.AddComponent<UnityTransport>();
        ConfigureTransport(transport, bindAddress, port, transportMode);

        networkManager = go.AddComponent<NetworkManager>();
        if (networkManager.NetworkConfig == null)
            networkManager.NetworkConfig = new NetworkConfig();

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;

        GameObject bridgePrefab = GetOrCreateBridgePrefab();
        if (bridgePrefab != null)
            networkManager.NetworkConfig.PlayerPrefab = bridgePrefab;

        ConfigureConnectionApproval(networkManager, isServer);
        return true;
    }

    public static bool Ensure(string bindAddress, ushort port, bool isServer, out NetworkManager networkManager)
    {
        return Ensure(bindAddress, port, isServer, NetworkTransportMode.Udp, out networkManager);
    }

    private static void ConfigureTransport(
        UnityTransport transport,
        string bindAddress,
        ushort port,
        NetworkTransportMode transportMode)
    {
        if (transport == null)
            return;

        transport.SetConnectionData(bindAddress, port);
        transport.UseEncryption = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL has no UDP — the browser can only open WebSocket connections. A retry/parse
        // path that resets transportMode to UDP would silently fail here ("WebSockets were
        // used even though they're not selected"). Force WebSocket on WebGL, always.
        transport.UseWebSockets = true;
        transport.UseEncryption = LanSessionManager.UseSecureWebSocket;
        if (transport.UseEncryption)
            transport.SetClientSecrets(LanSessionManager.SecureWebSocketHost, null);
#else
        transport.UseWebSockets = transportMode == NetworkTransportMode.WebSocket;
#endif

        // The default receive/send packet queue (128) overflows over Internet WebSocket
        // during the scene-load burst + 30 Hz world-state broadcast, which tears the
        // connection down ("Receive queue is full" → client disconnect → "Mất kết nối").
        // Give 8x headroom on both client and server so bursts are absorbed.
        transport.MaxPacketQueueSize = 1024;
    }

    public static void ConfigureConnectionApproval(NetworkManager networkManager, bool isServer)
    {
        if (networkManager == null || networkManager.NetworkConfig == null)
            return;

        bool requiresApproval = LanSessionManager.IsDedicatedServer
            || !string.IsNullOrWhiteSpace(LanSessionManager.SessionCode);

        networkManager.NetworkConfig.ConnectionApproval = requiresApproval;

        if (isServer)
        {
            networkManager.ConnectionApprovalCallback = requiresApproval ? ApprovalCheck : null;
            return;
        }

        networkManager.NetworkConfig.ConnectionData = requiresApproval
            ? JoinApprovalPayload.ToBytes(new JoinApprovalPayload
            {
                sessionCode = LanSessionManager.SessionCode,
                clientVersion = Application.version,
                variantIndex = LanSessionManager.LocalVariantIndex
            })
            : Array.Empty<byte>();
    }

    private static void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = true;

        if (!LanSessionManager.IsDedicatedServer && string.IsNullOrWhiteSpace(LanSessionManager.SessionCode))
        {
            response.Approved = true;
            return;
        }

        if (!JoinApprovalPayload.TryFromBytes(request.Payload, out JoinApprovalPayload payload))
        {
            response.Approved = false;
            response.Reason = "Missing or invalid join payload.";
            return;
        }

        string clientSessionCode = (payload.sessionCode ?? string.Empty).Trim();
        string serverSessionCode = (LanSessionManager.SessionCode ?? string.Empty).Trim();
        if (!string.Equals(clientSessionCode, serverSessionCode, StringComparison.OrdinalIgnoreCase))
        {
            response.Approved = false;
            response.Reason = "Invalid session code.";
            return;
        }

        response.Approved = true;
    }

    private static GameObject GetOrCreateBridgePrefab()
    {
        GameObject prefab = Resources.Load<GameObject>("LanBridgePrefab");
        if (prefab != null) return prefab;
#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        const string path = "Assets/Resources/LanBridgePrefab.prefab";
        GameObject tmp = new GameObject("LanBridgePrefab");
        tmp.AddComponent<NetworkObject>();
        tmp.AddComponent<LanNetworkBridge>();
        bool ok;
        PrefabUtility.SaveAsPrefabAsset(tmp, path, out ok);
        UnityEngine.Object.DestroyImmediate(tmp);
        if (ok)
        {
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
#endif
        return null;
    }
}
