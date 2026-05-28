using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal overlay shown when the user clicks BACKTEST in the main menu.
/// Lets them pick which maps (and optionally algorithms) to include,
/// then launches BacktestRunner with the selection.
/// </summary>
public static class BacktestConfigUI
{
    // ── Colors ─────────────────────────────────────────────────────────────
    private static readonly Color BgOverlay   = new Color(0f,    0f,    0f,    0.80f);
    private static readonly Color PanelBg     = new Color(0.10f, 0.11f, 0.13f, 1f);
    private static readonly Color RowSelected = new Color(0.14f, 0.30f, 0.52f, 1f);
    private static readonly Color RowNormal   = new Color(0.16f, 0.17f, 0.19f, 1f);
    private static readonly Color RowHover    = new Color(0.20f, 0.22f, 0.25f, 1f);
    private static readonly Color AccentGold  = new Color(1.00f, 0.82f, 0.22f, 1f);
    private static readonly Color BtnGreen    = new Color(0.11f, 0.50f, 0.25f, 1f);
    private static readonly Color BtnDark     = new Color(0.20f, 0.21f, 0.23f, 1f);
    private static readonly Color TextMuted   = new Color(0.55f, 0.58f, 0.62f, 1f);

    // ── State ──────────────────────────────────────────────────────────────
    private static bool[]       _selected;
    private static Image[]      _rowImages;
    private static Text[]       _checkTexts;
    private static Button       _startBtn;
    private static Text         _startBtnLabel;
    private static GameObject   _root;

    // ── Entry point ────────────────────────────────────────────────────────
    public static void Show()
    {
        if (_root != null) return; // already open

        int count = BacktestRunner.MapCount;
        _selected   = new bool[count];
        _rowImages  = new Image[count];
        _checkTexts = new Text[count];
        for (int i = 0; i < count; i++) _selected[i] = true; // all checked by default

        BuildUI(count);
    }

    // ── UI construction ────────────────────────────────────────────────────
    private static void BuildUI(int mapCount)
    {
        int uiLayer = LayerMask.NameToLayer("UI");

        // ── Root canvas (full-screen overlay) ─────────────────────────────
        _root = new GameObject("BacktestConfigOverlay");
        _root.layer = uiLayer;
        var cv = _root.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 150;
        var sc = _root.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        _root.AddComponent<GraphicRaycaster>();

        // Dim background
        var dim = MakeRt(_root, "Dim", uiLayer);
        Stretch(dim);
        dim.AddComponent<Image>().color = BgOverlay;
        // Block clicks on the menu beneath
        dim.AddComponent<Button>(); // eat clicks

        // ── Center panel ──────────────────────────────────────────────────
        float panelW = 600f;
        float rowH   = 52f;
        float headerH = 72f;
        float footerH = 64f;
        float panelH  = headerH + mapCount * rowH + 8f + footerH;

        var panel = MakeRt(_root, "Panel", uiLayer);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot     = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(panelW, panelH);
        panel.AddComponent<Image>().color = PanelBg;

        // Gold accent top bar
        var accent = MakeRt(panel, "Accent", uiLayer);
        var aRt = accent.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(0f, 1f); aRt.anchorMax = new Vector2(1f, 1f);
        aRt.pivot = new Vector2(0.5f, 1f);
        aRt.anchoredPosition = Vector2.zero; aRt.sizeDelta = new Vector2(0f, 5f);
        accent.AddComponent<Image>().color = AccentGold;

        // Title
        MakeText(panel, "Title", "CHỌN MAP ĐỂ BACKTEST", 20, FontStyle.Bold, AccentGold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -36f), new Vector2(panelW - 40f, 30f));

        MakeText(panel, "Sub", $"A* + LNS2  •  3 lần/map  •  timeout 120s", 13, FontStyle.Normal, TextMuted,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -62f), new Vector2(panelW - 40f, 20f));

        // ── Map rows ──────────────────────────────────────────────────────
        float rowsTop = headerH; // offset from panel top
        for (int i = 0; i < mapCount; i++)
        {
            int idx = i; // capture for closure
            float yOffset = -(rowsTop + i * rowH + rowH / 2f);

            var row = MakeRt(panel, $"Row_{i}", uiLayer);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0.5f, 1f); rowRt.anchorMax = new Vector2(0.5f, 1f);
            rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(0f, yOffset);
            rowRt.sizeDelta = new Vector2(panelW - 24f, rowH - 4f);

            var rowImg = row.AddComponent<Image>();
            rowImg.color = RowSelected; // starts selected
            _rowImages[i] = rowImg;

            var btn = row.AddComponent<Button>();
            var cb  = btn.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor     = new Color(0.88f, 0.88f, 0.88f, 1f);
            btn.colors = cb;
            btn.targetGraphic = rowImg;
            btn.onClick.AddListener(() => Toggle(idx));

            // Checkmark box
            var chkBox = MakeRt(row, "ChkBox", uiLayer);
            var chkRt  = chkBox.GetComponent<RectTransform>();
            chkRt.anchorMin = new Vector2(0f, 0.5f); chkRt.anchorMax = new Vector2(0f, 0.5f);
            chkRt.pivot = new Vector2(0f, 0.5f);
            chkRt.anchoredPosition = new Vector2(14f, 0f); chkRt.sizeDelta = new Vector2(26f, 26f);
            chkBox.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

            var chkText = MakeText(chkBox, "Chk", "✓", 18, FontStyle.Bold, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            _checkTexts[i] = chkText;

            // Map label
            string mapLabel = BacktestRunner.GetMapLabel(i);
            string mapFile  = System.IO.Path.GetFileName(BacktestRunner.GetMapFile(i));
            MakeText(row, "Label", mapLabel, 16, FontStyle.Bold, Color.white,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(54f, 6f), new Vector2(160f, 22f));
            MakeText(row, "File", mapFile, 12, FontStyle.Normal, new Color(0.72f, 0.78f, 0.85f, 1f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(54f, -10f), new Vector2(400f, 18f));

            // Run count badge (right side)
            MakeText(row, "Runs", "2 modes × 3 reps = 6 runs", 11, FontStyle.Normal, TextMuted,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-14f, 0f), new Vector2(200f, 22f));
        }

        // ── Footer ────────────────────────────────────────────────────────
        float footerY = -(rowsTop + mapCount * rowH + 8f + footerH / 2f);

        // SELECT ALL / DESELECT ALL (left side)
        var btnAll = MakeButton(panel, "BtnAll", "CHỌN TẤT CẢ",
            BtnDark, new Color(0.78f, 0.88f, 1f, 1f),
            new Vector2(-148f, footerY), new Vector2(170f, 42f),
            () => SetAll(true));

        MakeButton(panel, "BtnNone", "BỎ CHỌN TẤT",
            BtnDark, TextMuted,
            new Vector2(+32f, footerY), new Vector2(150f, 42f),
            () => SetAll(false));

        // CANCEL (right side, secondary)
        MakeButton(panel, "BtnCancel", "HỦY",
            BtnDark, TextMuted,
            new Vector2(+116f, footerY), new Vector2(90f, 42f),
            Close);

        // START (right side, primary)
        var startGo = MakeButton(panel, "BtnStart", "",
            BtnGreen, Color.white,
            new Vector2(+220f, footerY), new Vector2(130f, 42f),
            StartBacktest);
        _startBtn      = startGo.GetComponent<Button>();
        _startBtnLabel = startGo.GetComponentInChildren<Text>();
        RefreshStartButton();
    }

    // ── Toggle logic ───────────────────────────────────────────────────────
    private static void Toggle(int i)
    {
        _selected[i] = !_selected[i];
        RefreshRow(i);
        RefreshStartButton();
    }

    private static void SetAll(bool value)
    {
        for (int i = 0; i < _selected.Length; i++)
        {
            _selected[i] = value;
            RefreshRow(i);
        }
        RefreshStartButton();
    }

    private static void RefreshRow(int i)
    {
        if (_rowImages[i]  != null) _rowImages[i].color  = _selected[i] ? RowSelected : RowNormal;
        if (_checkTexts[i] != null) _checkTexts[i].text  = _selected[i] ? "✓" : "";
    }

    private static void RefreshStartButton()
    {
        int count = 0;
        foreach (bool b in _selected) if (b) count++;

        bool canStart = count > 0;
        if (_startBtn != null) _startBtn.interactable = canStart;
        if (_startBtnLabel != null)
            _startBtnLabel.text = canStart ? $"BẮT ĐẦU  ({count * 6})" : "BẮT ĐẦU";
    }

    // ── Actions ────────────────────────────────────────────────────────────
    private static void StartBacktest()
    {
        var indices = new List<int>();
        for (int i = 0; i < _selected.Length; i++)
            if (_selected[i]) indices.Add(i);

        Close();
        BacktestRunner.Launch(indices);
    }

    private static void Close()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
    }

    // ── UI helpers ─────────────────────────────────────────────────────────
    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static GameObject MakeRt(GameObject parent, string name, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static Text MakeText(GameObject parent, string name, string content,
        int fontSize, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = MakeRt(parent, name, parent.layer);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.text = content;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize; t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter; t.color = color;
        return t;
    }

    private static GameObject MakeButton(GameObject parent, string name, string label,
        Color bg, Color textColor, Vector2 pos, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRt(parent, name, parent.layer);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>(); img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = Color.white; cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        cb.pressedColor = new Color(0.80f, 0.80f, 0.80f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);
        MakeText(go, "Label", label, 14, FontStyle.Bold, textColor,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return go;
    }
}
