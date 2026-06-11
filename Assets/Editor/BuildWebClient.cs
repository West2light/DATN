#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWebClient
{
    private const string OutputDirectory = "Builds/WebGL";

    private static readonly string[] BuildScenes =
    {
        "Assets/Scenes/Menu.unity",
        "Assets/Scenes/MapF_TankTest.unity",
        "Assets/Scenes/MapF_TankTest_PIBT.unity",
    };

    [MenuItem("Tools/Tank MAPF/Build WebGL Client")]
    public static void BuildWebGL()
    {
        Directory.CreateDirectory(OutputDirectory);

        WebGLCompressionFormat originalCompression = PlayerSettings.WebGL.compressionFormat;
        bool originalDecompressionFallback = PlayerSettings.WebGL.decompressionFallback;

        try
        {
            // This VM currently serves the WebGL build over plain HTTP, so the build must remain
            // browser-loadable even when Brotli response handling or HTTPS is unavailable.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            var options = new BuildPlayerOptions
            {
                scenes = BuildScenes,
                locationPathName = OutputDirectory,
                target = BuildTarget.WebGL,
                options = BuildOptions.StrictMode,
            };

            Debug.Log($"[BuildWebClient] Building WebGL client to {OutputDirectory} with compression {PlayerSettings.WebGL.compressionFormat}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BuildWebClient] Build failed with result {summary.result}.");
                throw new BuildFailedException($"WebGL build failed: {summary.result}");
            }

            Debug.Log($"[BuildWebClient] Build succeeded: {OutputDirectory} ({summary.totalSize} bytes)");
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = originalCompression;
            PlayerSettings.WebGL.decompressionFallback = originalDecompressionFallback;
        }
    }
}
#endif
