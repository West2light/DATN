using UnityEngine;
using UnityEngine.UI;

public static class UiFontProvider
{
    private const string DefaultFontResourcePath = "Fonts/NotoSans-Regular";

    // All characters used across game HUD and menus — pre-warmed into the atlas on load.
    // This prevents the WebGL atlas-rebuild race where Text components render before
    // their glyphs exist, then never recover because no one re-dirtied them.
    private const string PrewarmChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
        "0123456789 .,:;!?-/\\()[]{}×•←→★✔✖▶●%+_@#&'\"" +
        "HPBASENEMYhpbaseenemy:";

    private static readonly int[]       PrewarmSizes  = { 11, 12, 13, 14, 15, 16, 20, 22 };
    private static readonly FontStyle[] PrewarmStyles = { FontStyle.Normal, FontStyle.Bold };

    private static Font _cachedFont;
    private static bool _warnedMissingFont;
    private static bool _subscribedToRebuild;

    public static Font GetDefaultFont()
    {
#if UNITY_EDITOR
        // NotoSans is packaged correctly for builds, but its dynamic atlas can fail to render
        // legacy Unity UI.Text inside the Editor Game view. The built-in runtime font is
        // always available in the Editor and avoids blank HUD text.
        Font editorFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (editorFont != null)
            return editorFont;
#endif

        if (_cachedFont == null)
        {
            _cachedFont = Resources.Load<Font>(DefaultFontResourcePath);
            if (_cachedFont == null && !_warnedMissingFont)
            {
                _warnedMissingFont = true;
                Debug.LogWarning(
                    $"[UiFontProvider] Could not load Resources/{DefaultFontResourcePath}. " +
                    "Falling back to LegacyRuntime.ttf.");
            }

            if (_cachedFont != null)
            {
                // Subscribe BEFORE pre-warming so no rebuild event is missed.
                EnsureTextureRebuildSubscription();
                PrewarmFontAtlas(_cachedFont);
            }
        }

        return _cachedFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // Subscribe once to the global Font.textureRebuilt event.
    // When the dynamic atlas is rebuilt (new glyphs needed), every Text using this font
    // must be re-dirtied so Unity re-fetches the updated UV coordinates.
    private static void EnsureTextureRebuildSubscription()
    {
        if (_subscribedToRebuild) return;
        _subscribedToRebuild = true;
        Font.textureRebuilt += OnFontTextureRebuilt;
    }

    // Request all common glyphs at every used size so the atlas is populated BEFORE
    // any Text component tries to render.  Drastically reduces mid-render rebuilds.
    private static void PrewarmFontAtlas(Font font)
    {
        foreach (int sz in PrewarmSizes)
            foreach (FontStyle st in PrewarmStyles)
                font.RequestCharactersInTexture(PrewarmChars, sz, st);
    }

    // Called by Unity whenever the font texture atlas is rebuilt.
    // In WebGL, the rebuild is deferred (no threads), so Text components that already
    // rendered with stale UVs will stay blank unless we explicitly mark them dirty.
    private static void OnFontTextureRebuilt(Font changedFont)
    {
        if (changedFont != _cachedFont) return;

        var allTexts = Object.FindObjectsByType<Text>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in allTexts)
        {
            if (t != null && t.font == changedFont)
                t.SetAllDirty();
        }
    }
}
