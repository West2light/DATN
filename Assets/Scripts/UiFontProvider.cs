using UnityEngine;

public static class UiFontProvider
{
    private const string DefaultFontResourcePath = "Fonts/NotoSans-Regular";
    private static readonly string GlyphSmokeTest = "LAN — SELECT MAP & MODE ← BACK Choose a map and AI mode · enemies = 6 × player count";

    private static Font cachedFont;
    private static bool warnedMissingFont;
    private static bool warnedMissingGlyphs;

    public static Font GetDefaultFont()
    {
#if UNITY_EDITOR
        // NotoSans is packaged correctly for builds, but its dynamic atlas can fail
        // to render legacy Unity UI.Text inside the Editor Game view. The built-in
        // runtime font is always available in the Editor and avoids blank HUD text.
        Font editorFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (editorFont != null)
        {
            return editorFont;
        }
#endif

        if (cachedFont == null)
        {
            cachedFont = Resources.Load<Font>(DefaultFontResourcePath);
            if (cachedFont == null && !warnedMissingFont)
            {
                warnedMissingFont = true;
                Debug.LogWarning($"[UiFontProvider] Could not load Resources/{DefaultFontResourcePath}. Falling back to LegacyRuntime.ttf.");
            }
        }

        Font font = cachedFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        WarnIfGlyphsMissing(font);
        return font;
    }

    private static void WarnIfGlyphsMissing(Font font)
    {
        if (warnedMissingGlyphs || font == null)
            return;

        for (int i = 0; i < GlyphSmokeTest.Length; i++)
        {
            char glyph = GlyphSmokeTest[i];
            if (char.IsControl(glyph) || glyph == ' ')
                continue;

            if (!font.HasCharacter(glyph))
            {
                warnedMissingGlyphs = true;
                Debug.LogWarning($"[UiFontProvider] Active font '{font.name}' is missing glyph '{glyph}'. WebGL menu text may render incorrectly.");
                return;
            }
        }
    }
}
