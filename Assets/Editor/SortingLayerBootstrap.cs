#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[InitializeOnLoad]
public static class SortingLayerBootstrap
{
    static SortingLayerBootstrap()
    {
        EnsureSortingLayer("Eagle");
    }

    private static void EnsureSortingLayer(string layerName)
    {
        foreach (SortingLayer layer in SortingLayer.layers)
        {
            if (layer.name == layerName)
            {
                return;
            }
        }

        System.Type internalEditorUtility = typeof(InternalEditorUtility);
        MethodInfo addSortingLayer = internalEditorUtility.GetMethod("AddSortingLayer", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo setSortingLayerName = internalEditorUtility.GetMethod("SetSortingLayerName", BindingFlags.Static | BindingFlags.NonPublic);
        if (addSortingLayer == null || setSortingLayerName == null)
        {
            return;
        }

        addSortingLayer.Invoke(null, null);
        SortingLayer[] layers = SortingLayer.layers;
        int lastLayerIndex = layers.Length - 1;
        setSortingLayerName.Invoke(null, new object[] { lastLayerIndex, layerName });
    }
}
#endif
