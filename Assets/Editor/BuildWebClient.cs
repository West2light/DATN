#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public static class BuildWebClient
{
    private const string OutputDirectory = "Builds/WebGL";
    private const int DesktopCanvasWidth = 1366;
    private const int DesktopCanvasHeight = 768;
    private const string ManifestFileName = "build-manifest.json";

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
            // Build with Brotli pre-compression. Nginx serves .wasm.br/.data.br/.framework.js.br
            // directly with Content-Encoding: br — no on-the-fly CPU cost on the server.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
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

            RewriteDesktopCanvasSize();
            WriteBuildManifest();
            Debug.Log($"[BuildWebClient] Build succeeded: {OutputDirectory} ({summary.totalSize} bytes)");
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = originalCompression;
            PlayerSettings.WebGL.decompressionFallback = originalDecompressionFallback;
        }
    }

    private static void RewriteDesktopCanvasSize()
    {
        string indexPath = Path.Combine(OutputDirectory, "index.html");
        if (!File.Exists(indexPath))
        {
            Debug.LogWarning($"[BuildWebClient] Could not find {indexPath} to rewrite desktop canvas size.");
            return;
        }

        string html = File.ReadAllText(indexPath);
        html = html.Replace("width=960 height=600", $"width={DesktopCanvasWidth} height={DesktopCanvasHeight}");
        html = html.Replace("canvas.style.width = \"960px\";", $"canvas.style.width = \"{DesktopCanvasWidth}px\";");
        html = html.Replace("canvas.style.height = \"600px\";", $"canvas.style.height = \"{DesktopCanvasHeight}px\";");
        File.WriteAllText(indexPath, html);
    }

    [Serializable]
    private class WebBuildManifest
    {
        public string gitCommit;
        public string builtAtUtc;
        public string unityVersion;
        public int desktopCanvasWidth;
        public int desktopCanvasHeight;
        public string indexHtmlSha256;
        public string loaderJsSha256;
        public string frameworkJsSha256;
        public string wasmSha256;
        public string dataSha256;
    }

    private static void WriteBuildManifest()
    {
        string rootPath = Path.GetFullPath(OutputDirectory);
        string buildPath = Path.Combine(rootPath, "Build");

        // Resolve actual file names — Brotli builds produce .br variants.
        string frameworkJs = ResolveBuiltFile(buildPath, "WebGL.framework.js");
        string wasm        = ResolveBuiltFile(buildPath, "WebGL.wasm");
        string data        = ResolveBuiltFile(buildPath, "WebGL.data");

        var manifest = new WebBuildManifest
        {
            gitCommit = ResolveGitCommit(),
            builtAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            unityVersion = Application.unityVersion,
            desktopCanvasWidth = DesktopCanvasWidth,
            desktopCanvasHeight = DesktopCanvasHeight,
            indexHtmlSha256 = ComputeSha256(Path.Combine(rootPath, "index.html")),
            loaderJsSha256 = ComputeSha256(Path.Combine(buildPath, "WebGL.loader.js")),
            frameworkJsSha256 = ComputeSha256(frameworkJs),
            wasmSha256 = ComputeSha256(wasm),
            dataSha256 = ComputeSha256(data),
        };

        string manifestPath = Path.Combine(rootPath, ManifestFileName);
        File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        Debug.Log($"[BuildWebClient] Wrote artifact manifest: {manifestPath}");
    }

    private static string ResolveGitCommit()
    {
        string githubSha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(githubSha))
            return githubSha.Trim();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return "unknown";

            string stdout = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(stdout) ? "unknown" : stdout;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuildWebClient] Could not resolve git commit: {ex.Message}");
            return "unknown";
        }
    }

    private static string ResolveBuiltFile(string buildPath, string baseName)
    {
        string br = Path.Combine(buildPath, baseName + ".br");
        if (File.Exists(br)) return br;
        return Path.Combine(buildPath, baseName);
    }

    private static string ComputeSha256(string path)
    {
        if (!File.Exists(path))
            return string.Empty;

        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte value in hash)
            sb.Append(value.ToString("x2"));
        return sb.ToString();
    }
}
#endif
