#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time setup: creates Assets/Resources/LanBridgePrefab.prefab.
/// Run via  Tools > Tank MAPF > Setup LAN Networking
/// </summary>
public static class LanNetworkPrefabSetup
{
    private const string PrefabPath = "Assets/Resources/LanBridgePrefab.prefab";

    [MenuItem("Tools/Tank MAPF/Setup LAN Networking")]
    private static void Setup()
    {
        // Create Resources folder if missing
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        // Overwrite only if it doesn't already have the right components
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null && existing.GetComponent<LanNetworkBridge>() != null)
        {
            Debug.Log("[LanNetworkPrefabSetup] LanBridgePrefab already exists and is valid.");
            return;
        }

        // Build the prefab in memory
        var go = new GameObject("LanBridgePrefab");
        go.AddComponent<NetworkObject>();
        go.AddComponent<LanNetworkBridge>();

        // Save as a prefab asset
        bool success;
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out success);
        Object.DestroyImmediate(go);

        if (success)
        {
            AssetDatabase.Refresh();
            Debug.Log($"[LanNetworkPrefabSetup] Created {PrefabPath}. LAN multiplayer is ready.");
        }
        else
        {
            Debug.LogError("[LanNetworkPrefabSetup] Failed to save prefab. Check console.");
        }
    }
}
#endif
