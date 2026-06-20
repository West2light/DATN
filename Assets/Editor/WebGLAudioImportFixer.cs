#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class WebGLAudioImportFixer : AssetPostprocessor
{
    private static readonly string InterfaceSoundsPath = "Assets/Audio/Kenny Assets/InterfaceSounds/Audio/";
    private const string WebGLPlatform = "WebGL";

    void OnPreprocessAudio()
    {
        if (assetPath == null || !assetPath.StartsWith(InterfaceSoundsPath, System.StringComparison.OrdinalIgnoreCase))
            return;

        AudioImporter importer = (AudioImporter)assetImporter;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.SetOverrideSampleSettings(WebGLPlatform, settings);
        importer.forceToMono = false;
    }
}
#endif
