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
    private static bool _refreshQueued;

    public static Font GetDefaultFont()
    {
        if (_font != null) return _font;

#if !UNITY_WEBGL || UNITY_EDITOR
        // Editor + desktop/standalone: dùng font builtin LegacyRuntime.ttf (Liberation Sans).
        // Nó render ỔN ĐỊNH với Text được gán .text lúc runtime — chính là các nhãn HUD
        // (HP / BASE / bộ đếm ENEMY) và ô IP của host. Roboto là font ĐỘNG, atlas của nó
        // có thể để lại Text cập-nhật-muộn bị TRẮNG trong bản build standalone (Editor vốn
        // đã dùng font này nên bug chỉ xuất hiện ở build). Chỉ WebGL mới giữ Roboto +
        // FontPreloader vì ở đó builtin runtime font không đáng tin cậy.
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font != null)
        {
            Subscribe();
            return _font;
        }
#endif

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
    //
    // CRITICAL: textureRebuilt frequently fires from *inside* a Canvas graphic-rebuild
    // loop (a Text requesting a glyph that isn't in the atlas triggers the rebuild
    // synchronously while its own mesh is being built). Calling SetAllDirty() now is
    // rejected by Unity — "Trying to add <Text> for graphic rebuild while we are already
    // inside a graphic rebuild loop. This is not supported." — so the Text is never
    // re-meshed and shows BLANK in standalone builds (HP / BASE / ENEMY labels).
    // Defer to the next frame instead; FontPreloader drains the flag from Update(),
    // which runs before the rebuild loop, so SetAllDirty is honored.
    private static void OnAtlasRebuilt(Font rebuilt)
    {
        if (_font == null || rebuilt != _font) return;
        _refreshQueued = true;
    }

    /// <summary>
    /// Returns true (once) when an atlas rebuild has queued a deferred text refresh.
    /// FontPreloader calls this every frame from Update() and, when true, runs
    /// ForceRefreshAllTexts() outside the Canvas graphic-rebuild loop.
    /// </summary>
    public static bool DequeueRefreshRequest()
    {
        if (!_refreshQueued) return false;
        _refreshQueued = false;
        return true;
    }
}
