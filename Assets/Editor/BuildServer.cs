#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildServer
{
    private const string OutputDirectory = "Builds/LinuxServer";
    private const string OutputBinary = "TankMapfServer.x86_64";

    private static readonly string[] BuildScenes =
    {
        "Assets/Scenes/Menu.unity",
        "Assets/Scenes/MapF_TankTest.unity",
        "Assets/Scenes/MapF_TankTest_PIBT.unity",
    };

    [MenuItem("Tools/Tank MAPF/Build Linux Dedicated Server")]
    public static void BuildLinuxServer()
    {
        Directory.CreateDirectory(OutputDirectory);

        string outputPath = Path.Combine(OutputDirectory, OutputBinary);
        var options = new BuildPlayerOptions
        {
            scenes = BuildScenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.StrictMode,
        };

        Debug.Log($"[BuildServer] Building Linux dedicated server to {outputPath}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[BuildServer] Build failed with result {summary.result}.");
            throw new BuildFailedException($"Linux dedicated server build failed: {summary.result}");
        }

        Debug.Log($"[BuildServer] Build succeeded: {outputPath} ({summary.totalSize} bytes)");
    }
}
#endif
