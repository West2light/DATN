using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manages the full menu flow: Main Menu → Outfit Select → Map Select.
/// Builds all UI procedurally; no prefab required.
/// </summary>
public class MenuViewBootstrap : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────────────────
    private const string MenuSceneName   = "Menu";
    private const string PrefKeyVariant  = "MenuTankVariant";
    private const string PrefKeyMapFile  = "SelectedMapFile";
    private const string SpritesRoot     = "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/";

    // Palette
    private static readonly Color BgDark      = new Color(0.07f, 0.12f, 0.18f, 1f);
    private static readonly Color PanelDark   = new Color(0.10f, 0.19f, 0.27f, 0.94f);
    private static readonly Color CardBg      = new Color(0.12f, 0.21f, 0.30f, 1f);
    private static readonly Color AccentGold  = new Color(0.95f, 0.74f, 0.22f, 1f);
    private static readonly Color AccentGreen = new Color(0.22f, 0.70f, 0.38f, 1f);
    private static readonly Color TextLight   = new Color(0.97f, 0.97f, 0.93f, 1f);
    private static readonly Color TextMuted   = new Color(0.60f, 0.78f, 0.84f, 1f);
    private static readonly Color BtnDisabled = new Color(0.28f, 0.32f, 0.36f, 0.80f);

    // ── Tank variant data ──────────────────────────────────────────────────
    private struct TankVariant
    {
        public string label;
        public string bodyFile;
        public Color  swatch;
        public bool   locked;
    }

    private static readonly TankVariant[] Variants =
    {
        new TankVariant { label = "Blue",    bodyFile = "tankBody_blue.png",      swatch = new Color(0.22f, 0.50f, 0.90f), locked = false },
        new TankVariant { label = "Red",     bodyFile = "tankBody_red.png",       swatch = new Color(0.88f, 0.22f, 0.22f), locked = false },
        new TankVariant { label = "Green",   bodyFile = "tankBody_green.png",     swatch = new Color(0.25f, 0.70f, 0.30f), locked = false },
        new TankVariant { label = "Dark",    bodyFile = "tankBody_dark.png",      swatch = new Color(0.28f, 0.30f, 0.35f), locked = false },
        new TankVariant { label = "Sand",    bodyFile = "tankBody_sand.png",      swatch = new Color(0.82f, 0.72f, 0.38f), locked = false },
        new TankVariant { label = "Big Red", bodyFile = "tankBody_bigRed.png",    swatch = new Color(0.75f, 0.12f, 0.12f), locked = true  },
        new TankVariant { label = "Dark XL", bodyFile = "tankBody_darkLarge.png", swatch = new Color(0.20f, 0.20f, 0.25f), locked = true  },
        new TankVariant { label = "Huge",    bodyFile = "tankBody_huge.png",      swatch = new Color(0.30f, 0.30f, 0.36f), locked = true  },
    };

    // ── Map data ───────────────────────────────────────────────────────────
    private struct MapDef
    {
        public string label;
        public string sizeLabel;
        public Color  previewTint;
        public string mapFile;
        public string sceneAStar;
        public string sceneLNS2;
        public bool   available;
    }

    private static readonly MapDef[] Maps =
    {
        new MapDef { label = "Alpha-32",  sizeLabel = "32 × 32  •  10% walls",  previewTint = new Color(0.20f, 0.55f, 0.30f), mapFile = "Assets/MapData/random-32-32-10.map",    sceneAStar = "MapF_TankTest", sceneLNS2 = "MapF_TankTest_LNS2", available = true },
        new MapDef { label = "Mansion",   sizeLabel = "133 × 270",              previewTint = new Color(0.50f, 0.38f, 0.20f), mapFile = "Assets/MapData/ht_mansion_n.map",       sceneAStar = "MapF_TankTest", sceneLNS2 = "MapF_TankTest_LNS2", available = true },
        new MapDef { label = "Chantry",   sizeLabel = "162 × 141",              previewTint = new Color(0.20f, 0.40f, 0.65f), mapFile = "Assets/MapData/ht_chantry.map",         sceneAStar = "MapF_TankTest", sceneLNS2 = "MapF_TankTest_LNS2", available = true },
        new MapDef { label = "Gallows",   sizeLabel = "251 × 180",              previewTint = new Color(0.60f, 0.18f, 0.18f), mapFile = "Assets/MapData/lt_gallowstemplar_n.map",sceneAStar = "MapF_TankTest", sceneLNS2 = "MapF_TankTest_LNS2", available = true },
        new MapDef { label = "Maze-128",  sizeLabel = "128 × 128  •  10% walls", previewTint = new Color(0.55f, 0.18f, 0.65f), mapFile = "Assets/MapData/maze-128-128-10.map", sceneAStar = "MapF_TankTest", sceneLNS2 = "MapF_TankTest_LNS2", available = true },
    };

    // ── Runtime state ──────────────────────────────────────────────────────
    private enum Screen { Main, Outfit, MapSelect, LanMapSelect }

    private Canvas     _canvas;
    private GameObject _screenMain, _screenOutfit, _screenMap, _screenLan;
    private Image      _tankPreviewImage;
    private Text       _tankPreviewLabel;
    private Text       _tankTypeLabel;
    private int        _selectedVariant;
    private readonly List<Image>  _variantSwatchImages = new List<Image>();
    private readonly List<GameObject> _variantRings    = new List<GameObject>();

    // ── Bootstrap ──────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyIfMenuScene(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyIfMenuScene(scene);

    private static void ApplyIfMenuScene(Scene scene)
    {
        if (scene.name != MenuSceneName || FindFirstObjectByType<MenuViewBootstrap>() != null) return;
        new GameObject("MenuViewBootstrap").AddComponent<MenuViewBootstrap>().Apply();
    }

    private void Apply()
    {
        _selectedVariant = Mathf.Clamp(PlayerPrefs.GetInt(PrefKeyVariant, 0), 0, Variants.Length - 1);
        SetupCamera();
        SetupCanvas();
        BuildScreenMain();
        BuildScreenOutfit();
        BuildScreenMapSelect();
        BuildScreenLanMapSelect();
        ShowScreen(Screen.Main);
    }

    // ── Setup ──────────────────────────────────────────────────────────────

    private void SetupCamera()
    {
        if (Camera.main != null) Camera.main.backgroundColor = BgDark;
    }

    private void SetupCanvas()
    {
        // Destroy any existing canvas so legacy scene objects (without RectTransform)
        // cannot interfere with the layout rebuild.
        Canvas[] old = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas c in old)
            Destroy(c.gameObject);

        GameObject canvasGO = new GameObject("MenuCanvas");
        canvasGO.layer = LayerMask.NameToLayer("UI");
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10;

        CanvasScaler scaler      = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode       = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode   = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCREEN: Main Menu
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildScreenMain()
    {
        _screenMain = MakePanel(_canvas.transform, "ScreenMain", Vector2.zero,
            new Vector2(1280f, 720f), BgDark);

        // Center card
        GameObject card = MakePanel(_screenMain.transform, "MainCard",
            new Vector2(0f, 0f), new Vector2(420f, 500f), PanelDark);
        SetImageRounded(card, PanelDark);

        // Title
        MakeText(card.transform, "Title", "TANK MAPF",
            54, FontStyle.Bold, AccentGold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(380f, 72f));

        // Subtitle
        MakeText(card.transform, "Subtitle", "Multi-Agent Pathfinding",
            18, FontStyle.Italic, TextMuted,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 115f), new Vector2(380f, 28f));

        // Divider line
        MakePanel(card.transform, "Divider",
            new Vector2(0f, 80f), new Vector2(280f, 2f),
            new Color(0.40f, 0.60f, 0.80f, 0.35f));

        // SINGLE PLAY
        Button btnStart = MakeButton(card.transform, "BtnSingle",
            "SINGLE PLAY", new Vector2(0f, 22f), new Vector2(290f, 60f), AccentGold);
        SetTextColor(btnStart.transform, new Color(0.10f, 0.09f, 0.09f));
        btnStart.onClick.AddListener(() => ShowScreen(Screen.Outfit));

        // MULTIPLAYER LAN
        Button btnHost = MakeButton(card.transform, "BtnHost",
            "MULTIPLAYER  LAN", new Vector2(0f, -58f), new Vector2(290f, 60f),
            new Color(0.12f, 0.32f, 0.58f, 1f));
        SetTextColor(btnHost.transform, new Color(0.75f, 0.90f, 1f));
        btnHost.onClick.AddListener(() => ShowScreen(Screen.LanMapSelect));

        // SHOP — disabled
        Button btnShop = MakeButton(card.transform, "BtnShop",
            "SHOP  —  Coming Soon", new Vector2(0f, -138f), new Vector2(290f, 60f), BtnDisabled);
        SetTextColor(btnShop.transform, TextMuted);
        btnShop.interactable = false;

        // BACKTEST
        Button btnBacktest = MakeButton(card.transform, "BtnBacktest",
            "BACKTEST", new Vector2(0f, -218f), new Vector2(290f, 44f),
            new Color(0.18f, 0.34f, 0.54f, 1f));
        SetTextColor(btnBacktest.transform, new Color(0.75f, 0.88f, 1f, 1f));
        btnBacktest.onClick.AddListener(() => BacktestConfigUI.Show());

        // Version footer
        MakeText(_screenMain.transform, "Version",
            "v0.4  •  Thesis Demo",
            13, FontStyle.Normal, new Color(0.45f, 0.55f, 0.60f, 1f),
            new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(220f, 24f));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCREEN: Outfit Select
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildScreenOutfit()
    {
        _screenOutfit = MakePanel(_canvas.transform, "ScreenOutfit",
            Vector2.zero, new Vector2(1280f, 720f), BgDark);

        // Page title
        MakeText(_screenOutfit.transform, "Title", "SELECT YOUR TANK",
            34, FontStyle.Bold, TextLight,
            new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(640f, 52f));

        // Layout: two cards centred, total content width = 340+60+460 = 860px
        // previewCard centre: -860/2+340/2 = -430+170 = -260
        // selectCard centre:   860/2-460/2 =  430-230 =  200

        // ── Left: Preview card  (340 × 460) ──────────────────────────────
        GameObject previewCard = MakePanel(_screenOutfit.transform, "PreviewCard",
            new Vector2(-260f, 0f), new Vector2(340f, 460f), CardBg);

        MakeText(previewCard.transform, "CardLabel", "PREVIEW",
            13, FontStyle.Bold, TextMuted,
            new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(300f, 22f));

        // Tank image holder — upper half of card
        GameObject holder = MakePanel(previewCard.transform, "TankHolder",
            new Vector2(0f, 55f), new Vector2(200f, 200f),
            new Color(0.06f, 0.12f, 0.18f, 1f));

        // Preview image inside holder
        GameObject previewImgObj = new GameObject("TankImage");
        previewImgObj.layer = LayerMask.NameToLayer("UI");
        var pRect = previewImgObj.AddComponent<RectTransform>();
        previewImgObj.transform.SetParent(holder.transform, false);
        pRect.anchorMin = new Vector2(0.08f, 0.08f);
        pRect.anchorMax = new Vector2(0.92f, 0.92f);
        pRect.offsetMin = pRect.offsetMax = Vector2.zero;
        _tankPreviewImage = previewImgObj.AddComponent<Image>();
        _tankPreviewImage.preserveAspect = true;

        // Lock overlay (hidden by default)
        GameObject lockOverlay = MakePanel(holder.transform, "LockOverlay",
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.72f));
        RectTransform loRect = lockOverlay.GetComponent<RectTransform>();
        loRect.anchorMin = Vector2.zero; loRect.anchorMax = Vector2.one;
        loRect.offsetMin = loRect.offsetMax = Vector2.zero;
        MakeText(lockOverlay.transform, "LockText", "LOCKED\nBuy in Shop",
            18, FontStyle.Bold, new Color(1f, 0.82f, 0.20f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 56f));
        lockOverlay.SetActive(false);

        // Variant name + type — below holder
        // holder bottom = 55 - 100 = -45; leave 18px gap → name centre = -45-18-17 = -80
        _tankPreviewLabel = MakeText(previewCard.transform, "VariantName", "Blue",
            22, FontStyle.Bold, AccentGold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(300f, 32f));

        _tankTypeLabel = MakeText(previewCard.transform, "VariantType", "BASE TANK",
            13, FontStyle.Normal, TextMuted,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -136f), new Vector2(300f, 22f));

        // ── Right: Selector card  (460 × 460) ────────────────────────────
        GameObject selectCard = MakePanel(_screenOutfit.transform, "SelectCard",
            new Vector2(200f, 0f), new Vector2(460f, 460f), CardBg);

        MakeText(selectCard.transform, "CardLabel", "CHOOSE OUTFIT",
            13, FontStyle.Bold, TextMuted,
            new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(420f, 22f));

        // Section labels (centre-anchored, positioned just above their swatch rows)
        // Row1Y = 85, swatch top = 85+38 = 123 → label bottom = 123+10 = 133 → label centre = 133+10 = 143
        MakeText(selectCard.transform, "LabelBase", "BASE  (Unlocked)",
            11, FontStyle.Normal, TextMuted,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 138f), new Vector2(420f, 20f));

        // Row2Y = -45, swatch top = -45+38 = -7 → label centre = -7+10+10 = 13
        MakeText(selectCard.transform, "LabelBig", "BIG  (Shop)",
            11, FontStyle.Normal, TextMuted,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 13f), new Vector2(420f, 20f));

        // Build swatch grid
        BuildVariantSwatches(selectCard.transform, lockOverlay);

        // ── Navigation ─────────────────────────────────────────────────────
        Button btnBack = MakeButton(_screenOutfit.transform, "BtnBack",
            "← BACK", new Vector2(-500f, -320f), new Vector2(130f, 46f),
            new Color(0.28f, 0.38f, 0.48f, 1f));
        SetTextColor(btnBack.transform, TextLight);
        btnBack.onClick.AddListener(() => ShowScreen(Screen.Main));

        Button btnNext = MakeButton(_screenOutfit.transform, "BtnNext",
            "NEXT →", new Vector2(500f, -320f), new Vector2(130f, 46f), AccentGold);
        SetTextColor(btnNext.transform, new Color(0.10f, 0.09f, 0.09f));
        btnNext.onClick.AddListener(() => ShowScreen(Screen.MapSelect));

        // Apply initial selection
        SelectVariant(_selectedVariant, lockOverlay);
    }

    private void BuildVariantSwatches(Transform parent, GameObject lockOverlay)
    {
        // Base (0-4): row 1 — 5 swatches centred in 460px card → spans 420px, 20px margin each side
        // Big  (5-7): row 2 — 3 swatches
        const float SwW = 76f, SwH = 76f, Gap = 10f;

        // Rows positioned above centre so both fit well inside the 460-tall card
        // Row1 centre y= 85  → top=123, bottom=47   (card top=230)
        // Row2 centre y=-45  → top=−7,  bottom=−83  (card bottom=−230)
        const float Row1Y = 85f, Row2Y = -45f;

        // Row 1: 5 items
        float totalRow1 = 5 * SwW + 4 * Gap;
        float r1Start   = -totalRow1 / 2f + SwW / 2f;

        // Row 2: 3 items
        float totalRow2 = 3 * SwW + 2 * Gap;
        float r2Start   = -totalRow2 / 2f + SwW / 2f;

        for (int i = 0; i < Variants.Length; i++)
        {
            int   capturedIndex = i;
            float x = i < 5
                ? r1Start + i * (SwW + Gap)
                : r2Start + (i - 5) * (SwW + Gap);
            float y = i < 5 ? Row1Y : Row2Y;

            // Selection ring (gold border behind swatch)
            GameObject ring = MakePanel(parent, "Ring_" + i,
                new Vector2(x, y), new Vector2(SwW + 6f, SwH + 6f),
                new Color(1f, 0.82f, 0.15f, 0.90f));
            ring.SetActive(false);
            _variantRings.Add(ring);

            // Swatch button
            Button btn = MakeButton(parent, "Swatch_" + i,
                string.Empty, new Vector2(x, y), new Vector2(SwW, SwH),
                Variants[i].swatch);
            _variantSwatchImages.Add(btn.GetComponent<Image>());

            // Variant label below swatch
            MakeText(btn.transform, "SwLabel", Variants[i].label,
                11, FontStyle.Normal, TextLight,
                new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(SwW, 18f));

            if (Variants[i].locked)
            {
                // Dark tint + LOCK text for locked variants
                GameObject dimObj = MakePanel(btn.transform, "Dim",
                    Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.58f));
                RectTransform dimRect = dimObj.GetComponent<RectTransform>();
                dimRect.anchorMin = Vector2.zero; dimRect.anchorMax = Vector2.one;
                dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
                MakeText(dimObj.transform, "LockTxt", "LOCK",
                    13, FontStyle.Bold, new Color(1f, 0.80f, 0.15f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(SwW, 24f));
            }

            btn.onClick.AddListener(() =>
            {
                if (Variants[capturedIndex].locked) return;
                SelectVariant(capturedIndex, lockOverlay);
            });
        }
    }

    private void SelectVariant(int index, GameObject lockOverlay)
    {
        if (index < 0 || index >= Variants.Length) return;
        if (Variants[index].locked) return;

        _selectedVariant = index;
        PlayerPrefs.SetInt(PrefKeyVariant, index);

        // Update rings
        for (int i = 0; i < _variantRings.Count; i++)
            if (_variantRings[i] != null) _variantRings[i].SetActive(i == index);

        // Update preview sprite
        Sprite spr = LoadSprite(SpritesRoot + Variants[index].bodyFile);
        _tankPreviewImage.sprite = spr;
        _tankPreviewImage.color  = spr != null ? Color.white : Variants[index].swatch;

        // Update labels
        if (_tankPreviewLabel != null) _tankPreviewLabel.text = Variants[index].label;
        if (_tankTypeLabel   != null) _tankTypeLabel.text    = index < 5 ? "BASE TANK" : "BIG TANK";

        // Lock overlay
        if (lockOverlay != null) lockOverlay.SetActive(Variants[index].locked);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCREEN: Map Select
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildScreenMapSelect()
    {
        _screenMap = MakePanel(_canvas.transform, "ScreenMapSelect",
            Vector2.zero, new Vector2(1280f, 720f), BgDark);

        MakeText(_screenMap.transform, "Title", "SELECT MAP",
            34, FontStyle.Bold, TextLight,
            new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(640f, 52f));

        MakeText(_screenMap.transform, "Hint", "Choose a map and select algorithm mode",
            14, FontStyle.Italic, TextMuted,
            new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(640f, 24f));

        // 5 cards, CardW=210, Gap=10 → total=1090px, centred in 1280
        const float CardW = 210f, CardH = 270f, Gap = 10f;
        float totalW = Maps.Length * CardW + (Maps.Length - 1) * Gap;
        float startX = -totalW / 2f + CardW / 2f;

        for (int i = 0; i < Maps.Length; i++)
            BuildMapCard(_screenMap.transform, i, startX + i * (CardW + Gap), -15f, CardW, CardH);

        Button btnBack = MakeButton(_screenMap.transform, "BtnBack",
            "← BACK", new Vector2(-540f, -320f), new Vector2(130f, 46f),
            new Color(0.28f, 0.38f, 0.48f, 1f));
        SetTextColor(btnBack.transform, TextLight);
        btnBack.onClick.AddListener(() => ShowScreen(Screen.Outfit));
    }

    private void BuildMapCard(Transform parent, int idx, float x, float y, float w, float h)
    {
        MapDef map       = Maps[idx];
        Color  cardColor = map.available ? CardBg : new Color(0.08f, 0.10f, 0.12f, 1f);

        GameObject card = MakePanel(parent, "MapCard_" + idx,
            new Vector2(x, y), new Vector2(w, h), cardColor);

        // ── Accent bar (top) ────────────────────────────────────────────
        if (map.available)
        {
            GameObject accent = MakePanel(card.transform, "Accent",
                new Vector2(0f, h / 2f - 3f), new Vector2(w, 6f), map.previewTint);
            accent.GetComponent<Image>().color = map.previewTint;
        }

        // ── Mini map preview area ────────────────────────────────────────
        // card top = h/2, accent 6px, then 4px gap → preview top = h/2-10
        // preview height = 130 → preview centre y = h/2-10-65
        // Preview is always square so the 32×32 map renders without distortion.
        const float PreviewSize = 130f;
        float previewCentreY = h / 2f - 10f - PreviewSize / 2f;
        Color previewBg = map.available
            ? Color.Lerp(map.previewTint, Color.black, 0.72f)
            : new Color(0.07f, 0.08f, 0.10f, 1f);

        GameObject preview = MakePanel(card.transform, "MapPreview",
            new Vector2(0f, previewCentreY), new Vector2(PreviewSize, PreviewSize), previewBg);

        if (map.available && !string.IsNullOrEmpty(map.mapFile))
        {
            BuildMiniMapRawImage(preview.transform, map.mapFile, map.previewTint);
        }
        else
        {
            // Unavailable placeholder
            MakeText(preview.transform, "Placeholder", "?",
                42, FontStyle.Bold, new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, 60f));
        }

        // Map index badge (bottom-right of preview)
        MakeText(preview.transform, "Badge", "#" + (idx + 1),
            11, FontStyle.Bold,
            map.available ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.15f),
            new Vector2(1f, 0f), new Vector2(-6f, 6f), new Vector2(30f, 18f));

        // ── Map name ─────────────────────────────────────────────────────
        float nameCentreY = previewCentreY - PreviewSize / 2f - 8f - 13f;
        MakeText(card.transform, "MapName", map.label,
            15, FontStyle.Bold, map.available ? TextLight : new Color(0.45f, 0.48f, 0.52f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, nameCentreY), new Vector2(w - 12f, 26f));

        if (!map.available)
        {
            MakeText(card.transform, "Soon", "COMING SOON",
                10, FontStyle.Bold, new Color(0.40f, 0.43f, 0.48f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, nameCentreY - 22f), new Vector2(w - 12f, 18f));
            return;
        }

        // ── Mode size label ───────────────────────────────────────────────
        if (!string.IsNullOrEmpty(map.sizeLabel))
            MakeText(card.transform, "SizeLabel", map.sizeLabel,
                9, FontStyle.Normal, TextMuted,
                new Vector2(0.5f, 0.5f), new Vector2(0f, nameCentreY - 22f), new Vector2(w - 12f, 16f));

        // ── Algorithm mode buttons ────────────────────────────────────────
        string[] modeLabels = { "A*", "LNS2" };
        string[] modeScenes = { map.sceneAStar, map.sceneLNS2 };
        Color[] modeColors =
        {
            new Color(0.20f, 0.52f, 0.88f, 1f),
            new Color(0.18f, 0.65f, 0.38f, 1f),
        };

        const float BtnGap = 8f;
        float btnW   = (w - 16f - BtnGap) / 2f;
        const float BtnH = 44f;
        float btnCentreY = -h / 2f + BtnH / 2f + 12f;
        float firstBtnX  = -(btnW + BtnGap) / 2f;

        for (int m = 0; m < 2; m++)
        {
            int    capturedM  = m;
            bool   avail      = modeScenes[m] != null;
            Color  col        = avail ? modeColors[m] : BtnDisabled;

            Button modeBtn = MakeButton(card.transform, "Mode_" + m,
                modeLabels[m],
                new Vector2(firstBtnX + m * (btnW + BtnGap), btnCentreY),
                new Vector2(btnW, BtnH), col);

            if (avail)
            {
                string sceneName  = modeScenes[capturedM];
                string mapFileCap = map.mapFile;
                modeBtn.onClick.AddListener(() =>
                {
                    PlayerPrefs.SetString(PrefKeyMapFile, mapFileCap);
                    PlayerPrefs.Save();
                    SceneManager.LoadScene(sceneName);
                });
                SetTextColor(modeBtn.transform, new Color(0.06f, 0.06f, 0.06f));
            }
            else
            {
                modeBtn.interactable = false;
                SetTextColor(modeBtn.transform, TextMuted);
            }
        }
    }

    // ── Mini-map helpers ───────────────────────────────────────────────────

    private static void BuildMiniMapRawImage(Transform previewParent, string mapFilePath, Color tint)
    {
        char[][] grid = LoadMapGrid(mapFilePath);
        if (grid == null || grid.Length == 0) return;

        int rows = grid.Length;
        int cols = grid[0].Length;

        Texture2D tex = new Texture2D(cols, rows, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;

        Color walkColor = Color.Lerp(tint, Color.white, 0.45f);
        Color wallColor = new Color(0.06f, 0.06f, 0.08f, 1f);

        for (int r = 0; r < rows; r++)
        {
            string row = r < grid.Length ? new string(grid[r]) : string.Empty;
            for (int c = 0; c < cols; c++)
            {
                char ch     = c < row.Length ? row[c] : '.';
                bool isWall = ch == '@' || ch == 'T' || ch == 'W' || ch == 'S';
                tex.SetPixel(c, rows - 1 - r, isWall ? wallColor : walkColor);
            }
        }
        tex.Apply();

        // Compute display size that preserves map aspect ratio inside the square container
        const float MaxSize = 122f; // parent is 130×130, leave 4px padding each side
        float aspect = (float)cols / rows;
        float dispW  = aspect >= 1f ? MaxSize : MaxSize * aspect;
        float dispH  = aspect >= 1f ? MaxSize / aspect : MaxSize;

        GameObject rawObj = new GameObject("MiniMapTex");
        rawObj.layer = LayerMask.NameToLayer("UI");
        var rt = rawObj.AddComponent<RectTransform>();
        rawObj.transform.SetParent(previewParent, false);
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(dispW, dispH);

        RawImage raw = rawObj.AddComponent<RawImage>();
        raw.texture = tex;
        raw.color   = Color.white;
    }

    private static char[][] LoadMapGrid(string assetPath)
    {
        string fileName = Path.GetFileName(assetPath);
        string fullPath = Path.Combine(Application.streamingAssetsPath, "MapData", fileName);
        if (!File.Exists(fullPath))
        {
            string relativePart = assetPath.StartsWith("Assets/")
                ? assetPath.Substring("Assets/".Length)
                : assetPath;
            fullPath = Path.Combine(Application.dataPath, relativePart);
        }
        if (!File.Exists(fullPath)) return null;
        string text = File.ReadAllText(fullPath);
        if (string.IsNullOrEmpty(text)) return null;

        var gridLines = new List<string>();
        bool inMap = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!inMap) { if (line.Trim() == "map") inMap = true; continue; }
            if (line.Length > 0) gridLines.Add(line);
        }
        if (gridLines.Count == 0) return null;

        var grid = new char[gridLines.Count][];
        for (int i = 0; i < gridLines.Count; i++)
            grid[i] = gridLines[i].ToCharArray();
        return grid;
    }

    // ── Screen transition ──────────────────────────────────────────────────

    // ═══════════════════════════════════════════════════════════════════════
    // SCREEN: LAN Map Select
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildScreenLanMapSelect()
    {
        _screenLan = MakePanel(_canvas.transform, "ScreenLanMapSelect",
            Vector2.zero, new Vector2(1280f, 720f), BgDark);

        MakeText(_screenLan.transform, "Title", "LAN  —  SELECT MAP & MODE",
            30, FontStyle.Bold, new Color(0.75f, 0.90f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(720f, 48f));

        MakeText(_screenLan.transform, "Hint", "Chọn map và chế độ AI · số enemy = 6 × số người chơi",
            13, FontStyle.Italic, TextMuted,
            new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(720f, 22f));

        const float CardW = 210f, CardH = 270f, Gap = 10f;
        float totalW = Maps.Length * CardW + (Maps.Length - 1) * Gap;
        float startX = -totalW / 2f + CardW / 2f;

        for (int i = 0; i < Maps.Length; i++)
            BuildLanMapCard(_screenLan.transform, i, startX + i * (CardW + Gap), -15f, CardW, CardH);

        Button btnBack = MakeButton(_screenLan.transform, "BtnBack",
            "← BACK", new Vector2(-540f, -320f), new Vector2(130f, 46f),
            new Color(0.28f, 0.38f, 0.48f, 1f));
        SetTextColor(btnBack.transform, TextLight);
        btnBack.onClick.AddListener(() => ShowScreen(Screen.Main));
    }

    private void BuildLanMapCard(Transform parent, int idx, float x, float y, float w, float h)
    {
        MapDef map = Maps[idx];
        if (!map.available) return;

        GameObject card = MakePanel(parent, "LanMapCard_" + idx,
            new Vector2(x, y), new Vector2(w, h), CardBg);

        GameObject accent = MakePanel(card.transform, "Accent",
            new Vector2(0f, h / 2f - 3f), new Vector2(w, 6f), map.previewTint);
        accent.GetComponent<Image>().color = map.previewTint;

        const float PreviewSize = 130f;
        float previewCentreY = h / 2f - 10f - PreviewSize / 2f;
        Color previewBg = Color.Lerp(map.previewTint, Color.black, 0.72f);
        GameObject preview = MakePanel(card.transform, "MapPreview",
            new Vector2(0f, previewCentreY), new Vector2(PreviewSize, PreviewSize), previewBg);
        BuildMiniMapRawImage(preview.transform, map.mapFile, map.previewTint);
        MakeText(preview.transform, "Badge", "#" + (idx + 1),
            11, FontStyle.Bold, new Color(1f, 1f, 1f, 0.55f),
            new Vector2(1f, 0f), new Vector2(-6f, 6f), new Vector2(30f, 18f));

        float nameCentreY = previewCentreY - PreviewSize / 2f - 8f - 13f;
        MakeText(card.transform, "MapName", map.label, 15, FontStyle.Bold, TextLight,
            new Vector2(0.5f, 0.5f), new Vector2(0f, nameCentreY), new Vector2(w - 12f, 26f));
        MakeText(card.transform, "SizeLabel", map.sizeLabel,
            9, FontStyle.Normal, TextMuted,
            new Vector2(0.5f, 0.5f), new Vector2(0f, nameCentreY - 22f), new Vector2(w - 12f, 16f));

        // A* and LNS2 buttons open the LAN lobby
        string[] modeLabels = { "A*", "LNS2" };
        string[] modeAlgos  = { "AStar", "LNS2" };
        Color[]  modeColors = { new Color(0.20f, 0.52f, 0.88f), new Color(0.18f, 0.65f, 0.38f) };

        const float BtnGap = 8f;
        float btnW        = (w - 16f - BtnGap) / 2f;
        const float BtnH  = 44f;
        float btnCentreY  = -h / 2f + BtnH / 2f + 12f;
        float firstBtnX   = -(btnW + BtnGap) / 2f;

        for (int m = 0; m < 2; m++)
        {
            string mapFileCap = map.mapFile;
            string algoCap    = modeAlgos[m];

            Button modeBtn = MakeButton(card.transform, "LanMode_" + m, modeLabels[m],
                new Vector2(firstBtnX + m * (btnW + BtnGap), btnCentreY),
                new Vector2(btnW, BtnH), modeColors[m]);
            SetTextColor(modeBtn.transform, new Color(0.06f, 0.06f, 0.06f));
            modeBtn.onClick.AddListener(() => LanLobbyController.Show(mapFileCap, algoCap));
        }
    }

    // ── Screen transition ──────────────────────────────────────────────────

    private void ShowScreen(Screen screen)
    {
        if (_screenMain  != null) _screenMain.SetActive(screen == Screen.Main);
        if (_screenOutfit != null) _screenOutfit.SetActive(screen == Screen.Outfit);
        if (_screenMap   != null) _screenMap.SetActive(screen == Screen.MapSelect);
        if (_screenLan   != null) _screenLan.SetActive(screen == Screen.LanMapSelect);
    }

    // ── UI helpers ─────────────────────────────────────────────────────────

    private static GameObject MakePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var go   = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");

        var rect = go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);

        rect.anchorMin        = new Vector2(0.5f, 0.5f);
        rect.anchorMax        = new Vector2(0.5f, 0.5f);
        rect.pivot            = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta        = size;

        go.AddComponent<Image>().color = color;
        return go;
    }

    private static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color)
    {
        var go = MakePanel(parent, name, pos, size, color);
        var btn = go.AddComponent<Button>();

        ColorBlock cb         = btn.colors;
        cb.normalColor        = color;
        cb.highlightedColor   = Color.Lerp(color, Color.white, 0.18f);
        cb.pressedColor       = Color.Lerp(color, Color.black, 0.22f);
        cb.selectedColor      = color;
        cb.disabledColor      = new Color(0.38f, 0.38f, 0.40f, 0.65f);
        cb.colorMultiplier    = 1f;
        btn.colors            = cb;

        if (!string.IsNullOrEmpty(label))
        {
            MakeText(go.transform, "Label", label,
                19, FontStyle.Bold, new Color(0.10f, 0.09f, 0.09f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
        }
        return btn;
    }

    private static Text MakeText(Transform parent, string name, string value,
        int fontSize, FontStyle style, Color color,
        Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");

        var rect = go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);

        rect.anchorMin        = anchor;
        rect.anchorMax        = anchor;
        rect.pivot            = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta        = size;

        var text = go.AddComponent<Text>();
        text.text      = value;
        text.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize  = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color     = color;
        return text;
    }

    private static void SetTextColor(Transform parent, Color color)
    {
        Text t = parent.GetComponentInChildren<Text>(true);
        if (t != null) t.color = color;
    }

    private static void SetImageRounded(GameObject go, Color color)
    {
        Image img = go.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private static Sprite LoadSprite(string path)
    {
#if UNITY_EDITOR
        var spr = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (spr != null) return spr;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        return null;
#else
        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        Sprite spr = Resources.Load<Sprite>("TankSprites/" + fileName);
        if (spr != null) return spr;
        Texture2D rtex = Resources.Load<Texture2D>("TankSprites/" + fileName);
        if (rtex == null) return null;
        return Sprite.Create(rtex, new Rect(0, 0, rtex.width, rtex.height), new Vector2(0.5f, 0.5f), 128f);
#endif
    }
}
