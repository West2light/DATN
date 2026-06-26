using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Persistent singleton that brute-force fixes the Unity WebGL dynamic font
/// timing problem by re-dirtying all Text components for several frames after
/// every scene load.
///
/// WHY THIS IS NEEDED:
///   In WebGL there are no real threads. When Resources.Load returns a Font
///   object, the glyph atlas is still empty — baking happens asynchronously
///   through the browser's font-rendering API.  Text components that render
///   before the atlas is populated show blank text and, critically, may not
///   receive the Font.textureRebuilt callback if it fires before their
///   OnEnable subscribes to it.
///
///   The reliable fix: after every scene finishes loading, keep calling
///   SetAllDirty() on every Text that uses our dynamic font for REFRESH_FRAMES
///   consecutive frames.  By frame ~3 the atlas is always populated and the
///   last SetAllDirty() produces correct visible text.
/// </summary>
[DefaultExecutionOrder(-500)]
public class FontPreloader : MonoBehaviour
{
    private static FontPreloader _instance;

    // Number of frames to keep refreshing after a scene loads.
    // 10 frames (~167 ms at 60 fps) is enough for even the slowest cold loads.
    private const int RefreshFrames = 10;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (_instance != null) return;
        var go = new GameObject("[FontPreloader]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<FontPreloader>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Kick off a refresh pass for the initial scene (already loaded when
        // RuntimeInitializeOnLoadMethod fires AfterSceneLoad).
        StartCoroutine(RefreshPass());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Cancel any in-progress pass and start a fresh one for the new scene.
        StopAllCoroutines();
        StartCoroutine(RefreshPass());
    }

    private IEnumerator RefreshPass()
    {
        // Frame 0: scene objects are Awake/Start but font atlas may still be empty.
        // Prewarm glyphs for the sizes most commonly used in HUD and menus.
        Font font = UiFontProvider.GetDefaultFont();
        if (font != null)
        {
            PrimeSizes(font);
        }

        // Refresh every frame for RefreshFrames frames.
        // The first couple of frames the atlas may still be building; by the end
        // it is guaranteed complete and the final SetAllDirty() sticks.
        for (int i = 0; i < RefreshFrames; i++)
        {
            yield return null;
            UiFontProvider.ForceRefreshAllTexts();
        }
    }

    private static void PrimeSizes(Font font)
    {
        // Common chars at every font size used in-game.
        // RequestCharactersInTexture schedules async glyph baking early so
        // the atlas is populated sooner when Text components first render.
        const string chars =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
            "0123456789 .:,!?-/()" +
            "HPBASENEMYhpbaseenemy:" +
            "×•←→★✔✖▶●%+";

        int[] sizes = { 11, 12, 13, 14, 15, 16, 20, 22 };
        foreach (int sz in sizes)
        {
            font.RequestCharactersInTexture(chars, sz, FontStyle.Normal);
            font.RequestCharactersInTexture(chars, sz, FontStyle.Bold);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }
}
