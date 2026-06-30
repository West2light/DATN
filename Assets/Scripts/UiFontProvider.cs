using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Provides the shared runtime font for all procedural UI.
///
/// WebGL dynamic-font race condition:
///   Unity's dynamic font renders glyphs into a texture atlas at runtime.
///   On a cold browser the atlas starts empty; when Text components first
///   render, their glyphs are not in the atlas yet — so they show blank.
///   Unity then rebuilds the atlas and fires Font.textureRebuilt, but the
///   existing Text components don't automatically redraw their geometry.
///
/// Fixes applied here:
///   1. Subscribe to Font.textureRebuilt → call SetAllDirty on every
///      matching Text so their geometry is rebuilt with correct glyph UVs.
///   2. Call RequestCharactersInTexture once (common chars, size 14) right
///      after the font loads, to start pre-populating the atlas early.
///
/// FontPreloader.cs (companion script) covers the rest:
///   it force-refreshes all Text components every frame for the first
///   several frames after each scene load, catching any remaining races.
/// </summary>
public static class UiFontProvider
{
    private const string FontResourcePath = "Fonts/Roboto-Regular";

    // Characters needed by the in-game HUD and menus
    private const string PrewarmChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
        "0123456789 .,:;!?-/()[]" +
        "HPBASENEMYhpbaseenemy:" +
        "×•←→★✔✖▶●%+_@#&'\"";

    private static Font _font;
    private static bool _warnedMissing;
    private static bool _subscribed;

    public static Font GetDefaultFont()
    {
#if UNITY_EDITOR
        // Trong Editor Game view, atlas của font động (Roboto) có thể chưa kịp dựng
        // khiến UI.Text render trắng. Font builtin LegacyRuntime.ttf luôn sẵn sàng và
        // hiển thị ổn định, nên dùng nó cho Editor (single mode test ở đây). Bản build
        // WebGL vẫn dùng Roboto + FontPreloader như thiết kế.
        Font editorFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (editorFont != null)
        {
            return editorFont;
        }
#endif
        if (_font == null)
        {
            _font = Resources.Load<Font>(FontResourcePath);

            if (_font == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Debug.LogWarning($"[UiFontProvider] Cannot find {FontResourcePath}. Falling back to LegacyRuntime.ttf.");
                }
                // Fallback runtime: không bao giờ trả null để Text khỏi bị trắng.
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (_font != null)
            {
                Subscribe();
                _font.RequestCharactersInTexture(PrewarmChars, 14, FontStyle.Normal);
                _font.RequestCharactersInTexture(PrewarmChars, 15, FontStyle.Bold);
            }
        }

        return _font;
    }

    /// <summary>
    /// Force every active Text component to rebuild its geometry.
    /// Covers all fonts (NotoSans, LegacyRuntime, etc.) so no text is missed.
    /// </summary>
    public static void ForceRefreshAllTexts()
    {
        var all = Object.FindObjectsByType<Text>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t != null)
                t.SetAllDirty();
        }
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private static void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        Font.textureRebuilt += OnAtlasRebuilt;
    }

    // Called by Unity whenever a font's texture atlas is rebuilt.
    // Any Text that already rendered with the old (stale) UVs needs to
    // rebuild its vertex buffer with the new glyph positions.
    private static void OnAtlasRebuilt(Font rebuilt)
    {
        if (_font == null || rebuilt != _font) return;
        ForceRefreshAllTexts();
    }
}
