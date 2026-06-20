using UnityEngine;

public static class UiFontProvider
{
    private const string DefaultFontResourcePath = "Fonts/NotoSans-Regular";
    private static readonly string GlyphSmokeTest = "LAN — SELECT MAP & MODE ← BACK Chọn map và chế độ AI · số enemy = 6 × số người chơi";

    private static Font cachedFont;
    private static bool warnedMissingFont;
    private static bool warnedMissingGlyphs;

    public static Font GetDefaultFont()
    {
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
