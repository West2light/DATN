using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class BacktestConfigUI
{
    // ── Palette ────────────────────────────────────────────────────────────
    private static readonly Color BgOverlay    = new Color(0f,    0f,    0f,    0.82f);
    private static readonly Color PanelBg      = new Color(0.09f, 0.10f, 0.12f, 1f);
    private static readonly Color SeparatorCol = new Color(1f,    1f,    1f,    0.08f);
    private static readonly Color RowOn        = new Color(0.13f, 0.28f, 0.50f, 1f);
    private static readonly Color RowOff       = new Color(0.14f, 0.15f, 0.17f, 1f);
    private static readonly Color ChkBg        = new Color(0f,    0f,    0f,    0.35f);
    private static readonly Color AccentGold   = new Color(1.00f, 0.82f, 0.22f, 1f);
    private static readonly Color BtnGreen     = new Color(0.10f, 0.48f, 0.24f, 1f);
    private static readonly Color BtnSlate     = new Color(0.18f, 0.19f, 0.22f, 1f);
    private static readonly Color TextWhite    = Color.white;
    private static readonly Color TextMuted    = new Color(0.52f, 0.56f, 0.62f, 1f);
    private static readonly Color TextBlue     = new Color(0.65f, 0.82f, 1.00f, 1f);

    // ── Layout constants ───────────────────────────────────────────────────
    private const float PanelW   = 640f;
    private const float HeaderH  = 96f;   // title + reps stepper
    private const float RowH     = 56f;   // each map row
    private const float RowGap   = 6f;    // vertical gap between rows
    private const float FooterH  = 72f;   // buttons area
    private const float PadX     = 20f;   // horizontal outer padding

    // ── State ──────────────────────────────────────────────────────────────
    private static bool[]     _sel;
    private static Image[]    _rowImg;
    private static Text[]     _chkTxt;
    private static Text[]     _badgeTxt;
    private static Button     _startBtn;
    private static Text       _startLbl;
    private static GameObject _root;
    private static int        _reps = BacktestRunner.Reps;
    private static Text       _repsTxt;

    // ── Entry point ────────────────────────────────────────────────────────
    public static void Show()
    {
        if (_root != null) return;

        int n    = BacktestRunner.MapCount;
        _sel     = new bool[n];
        _rowImg  = new Image[n];
        _chkTxt  = new Text[n];
        _reps    = BacktestRunner.Reps;
        for (int i = 0; i < n; i++) _sel[i] = true;

        Build(n);
    }

    // ── Construction ───────────────────────────────────────────────────────
    private static void Build(int n)
    {
        int layer = LayerMask.NameToLayer("UI");

        // Root canvas
        _root = new GameObject("BacktestConfigOverlay");
        _root.layer = layer;
        var cv = _root.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 150;
        var sc = _root.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        _root.AddComponent<GraphicRaycaster>();

        // Dimmer — also eats stray clicks
        var dim = Child(_root, "Dim", layer);
        Stretch(dim); dim.AddComponent<Image>().color = BgOverlay;
        var dimBtn = dim.AddComponent<Button>(); dimBtn.transition = Selectable.Transition.None;

        // Rows area height = n rows + (n-1) gaps
        float rowsH  = n * RowH + (n - 1) * RowGap;
        float panelH = HeaderH + 1f + rowsH + 1f + FooterH;

        // Panel
        var panel = Child(_root, "Panel", layer);
        var pRt   = panel.GetComponent<RectTransform>();
        Center(pRt, new Vector2(PanelW, panelH));
        panel.AddComponent<Image>().color = PanelBg;

        BuildHeader(panel, layer);
        HSep(panel, layer, -HeaderH);                      // separator under header
        BuildRows(panel, layer, n);
        HSep(panel, layer, -(HeaderH + 1f + rowsH + 1f)); // separator above footer
        BuildFooter(panel, layer, n);
    }

    // ── Header ─────────────────────────────────────────────────────────────
    private static void BuildHeader(GameObject panel, int layer)
    {
        int n = _sel.Length;

        // Gold top bar (5 px)
        var bar = Child(panel, "GoldBar", layer);
        var bRt = bar.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(1f, 1f);
        bRt.pivot = new Vector2(0.5f, 1f);
        bRt.anchoredPosition = Vector2.zero; bRt.sizeDelta = new Vector2(0f, 5f);
        bar.AddComponent<Image>().color = AccentGold;

        // Title — centered inside header area
        var title = Child(panel, "Title", layer);
        var tRt   = title.GetComponent<RectTransform>();
        tRt.anchorMin = new Vector2(0f, 1f); tRt.anchorMax = new Vector2(1f, 1f);
        tRt.pivot = new Vector2(0.5f, 1f);
        tRt.anchoredPosition = new Vector2(0f, -14f);
        tRt.sizeDelta = new Vector2(-PadX * 2f, 30f);
        var tTxt = title.AddComponent<Text>();
        tTxt.text = "CHỌN MAP ĐỂ BACKTEST";
        tTxt.font = Fnt(); tTxt.fontSize = 20; tTxt.fontStyle = FontStyle.Bold;
        tTxt.alignment = TextAnchor.MiddleCenter; tTxt.color = AccentGold;

        // Static subtitle
        var sub = Child(panel, "Sub", layer);
        var sRt = sub.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 1f); sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, -48f);
        sRt.sizeDelta = new Vector2(-PadX * 2f, 18f);
        var sTxt = sub.AddComponent<Text>();
        sTxt.text = $"A* và PIBT  •  Timeout {BacktestRunner.RunTimeoutSec:F0}s";
        sTxt.font = Fnt(); sTxt.fontSize = 12; sTxt.fontStyle = FontStyle.Normal;
        sTxt.alignment = TextAnchor.MiddleCenter; sTxt.color = TextMuted;

        // Reps stepper row
        BuildRepsStepper(panel, layer);
    }

    // ── Map rows ───────────────────────────────────────────────────────────
    private static void BuildRows(GameObject panel, int layer, int n)
    {
        _badgeTxt = new Text[n];

        // rows container — sits below header + separator
        float containerTop = HeaderH + 1f;
        float rowsH        = n * RowH + (n - 1) * RowGap;

        for (int i = 0; i < n; i++)
        {
            int   idx    = i;
            float rowTop = containerTop + i * (RowH + RowGap);

            // Row background
            var row  = Child(panel, $"Row{i}", layer);
            var rRt  = row.GetComponent<RectTransform>();
            rRt.anchorMin = new Vector2(0f, 1f); rRt.anchorMax = new Vector2(1f, 1f);
            rRt.pivot = new Vector2(0.5f, 1f);
            rRt.anchoredPosition = new Vector2(0f, -rowTop);
            rRt.offsetMin = new Vector2(PadX, rRt.offsetMin.y);
            rRt.offsetMax = new Vector2(-PadX, rRt.offsetMax.y);
            rRt.sizeDelta = new Vector2(rRt.sizeDelta.x, RowH);

            var rImg = row.AddComponent<Image>(); rImg.color = RowOn;
            _rowImg[i] = rImg;

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rImg;
            var bc = btn.colors;
            bc.normalColor = Color.white; bc.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            bc.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            btn.colors = bc;
            btn.onClick.AddListener(() => Toggle(idx));

            // ── Checkbox (left, vertically centered) ──────────────────────
            var chk  = Child(row, "Chk", layer);
            var cRt  = chk.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 0.5f); cRt.anchorMax = new Vector2(0f, 0.5f);
            cRt.pivot = new Vector2(0f, 0.5f);
            cRt.anchoredPosition = new Vector2(14f, 0f);
            cRt.sizeDelta = new Vector2(26f, 26f);
            chk.AddComponent<Image>().color = ChkBg;

            var ct = Child(chk, "Mark", layer);
            var ctRt = ct.GetComponent<RectTransform>();
            ctRt.anchorMin = Vector2.zero; ctRt.anchorMax = Vector2.one;
            ctRt.offsetMin = Vector2.zero; ctRt.offsetMax = Vector2.zero;
            var ctTxt = ct.AddComponent<Text>();
            ctTxt.text = "✓"; ctTxt.font = Fnt(); ctTxt.fontSize = 17;
            ctTxt.fontStyle = FontStyle.Bold; ctTxt.color = TextWhite;
            ctTxt.alignment = TextAnchor.MiddleCenter;
            _chkTxt[i] = ctTxt;

            // ── Map name (upper line) ──────────────────────────────────────
            string lbl  = BacktestRunner.GetMapLabel(i);
            string file = System.IO.Path.GetFileName(BacktestRunner.GetMapFile(i));

            var nameGo = Child(row, "Name", layer);
            var nRt    = nameGo.GetComponent<RectTransform>();
            nRt.anchorMin = new Vector2(0f, 0.5f); nRt.anchorMax = new Vector2(1f, 0.5f);
            nRt.pivot = new Vector2(0f, 0.5f);
            nRt.offsetMin = new Vector2(54f, 4f);     // left edge offset
            nRt.offsetMax = new Vector2(-120f, 28f);  // right edge offset, top offset
            var nTxt = nameGo.AddComponent<Text>();
            nTxt.text = lbl; nTxt.font = Fnt(); nTxt.fontSize = 15;
            nTxt.fontStyle = FontStyle.Bold; nTxt.color = TextWhite;
            nTxt.alignment = TextAnchor.MiddleLeft;

            // ── File name (lower line) ─────────────────────────────────────
            var fileGo = Child(row, "File", layer);
            var fRt    = fileGo.GetComponent<RectTransform>();
            fRt.anchorMin = new Vector2(0f, 0.5f); fRt.anchorMax = new Vector2(1f, 0.5f);
            fRt.pivot = new Vector2(0f, 0.5f);
            fRt.offsetMin = new Vector2(54f, -28f);   // left edge, bottom
            fRt.offsetMax = new Vector2(-120f, -4f);  // right edge, top
            var fTxt = fileGo.AddComponent<Text>();
            fTxt.text = file; fTxt.font = Fnt(); fTxt.fontSize = 11;
            fTxt.color = TextBlue; fTxt.alignment = TextAnchor.MiddleLeft;

            // ── Run-count badge (right, vertically centered) ───────────────
            var badge = Child(row, "Badge", layer);
            var bdRt  = badge.GetComponent<RectTransform>();
            bdRt.anchorMin = new Vector2(1f, 0.5f); bdRt.anchorMax = new Vector2(1f, 0.5f);
            bdRt.pivot = new Vector2(1f, 0.5f);
            bdRt.anchoredPosition = new Vector2(-14f, 0f);
            bdRt.sizeDelta = new Vector2(108f, RowH);
            var bdTxt = badge.AddComponent<Text>();
            bdTxt.text = $"{_reps * 2} runs";
            bdTxt.font = Fnt(); bdTxt.fontSize = 12;
            bdTxt.color = TextMuted; bdTxt.alignment = TextAnchor.MiddleRight;
            _badgeTxt[i] = bdTxt;
        }
    }

    // ── Footer ─────────────────────────────────────────────────────────────
    private static void BuildFooter(GameObject panel, int layer, int n)
    {
        // Footer container — anchored to the bottom of the panel
        var foot  = Child(panel, "Footer", layer);
        var footRt = foot.GetComponent<RectTransform>();
        footRt.anchorMin = new Vector2(0f, 0f); footRt.anchorMax = new Vector2(1f, 0f);
        footRt.pivot = new Vector2(0.5f, 0f);
        footRt.anchoredPosition = Vector2.zero;
        footRt.sizeDelta = new Vector2(0f, FooterH);

        float btnH  = 42f;
        float btnY  = FooterH / 2f;   // vertically centered in footer

        // Left cluster ─────────────────────────────────────────────────────
        // Anchor left buttons to left edge of footer
        FootBtn(foot, layer, "BtnAll",  "CHỌN TẤT",   BtnSlate, TextBlue,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(PadX, 0f), new Vector2(112f, btnH),
            () => SetAll(true));

        FootBtn(foot, layer, "BtnNone", "BỎ CHỌN",    BtnSlate, TextMuted,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(PadX + 112f + 8f, 0f), new Vector2(104f, btnH),
            () => SetAll(false));

        // Right cluster ────────────────────────────────────────────────────
        // Anchor right buttons to right edge of footer
        FootBtn(foot, layer, "BtnCancel", "HỦY",      BtnSlate, TextMuted,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-PadX - 148f - 8f, 0f), new Vector2(88f, btnH),
            Close);

        var startGo = FootBtn(foot, layer, "BtnStart", "",  BtnGreen, TextWhite,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-PadX, 0f), new Vector2(148f, btnH),
            StartBacktest);
        _startBtn = startGo.GetComponent<Button>();
        _startLbl = startGo.GetComponentInChildren<Text>();
        RefreshStart();
    }

    // ── Horizontal separator ───────────────────────────────────────────────
    private static void HSep(GameObject panel, int layer, float yFromTop)
    {
        var sep  = Child(panel, "Sep", layer);
        var sRt  = sep.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 1f); sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, yFromTop);
        sRt.sizeDelta = new Vector2(0f, 1f);
        sep.AddComponent<Image>().color = SeparatorCol;
    }

    // ── Logic ──────────────────────────────────────────────────────────────
    private static void Toggle(int i)
    {
        _sel[i] = !_sel[i];
        ApplyRow(i);
        RefreshStart();
    }

    private static void SetAll(bool v)
    {
        for (int i = 0; i < _sel.Length; i++) { _sel[i] = v; ApplyRow(i); }
        RefreshStart();
    }

    private static void ApplyRow(int i)
    {
        if (_rowImg[i] != null) _rowImg[i].color = _sel[i] ? RowOn : RowOff;
        if (_chkTxt[i] != null) _chkTxt[i].text  = _sel[i] ? "✓" : "";
    }

    private static void RefreshStart()
    {
        int cnt = 0; foreach (bool b in _sel) if (b) cnt++;
        bool ok = cnt > 0;
        if (_startBtn != null) _startBtn.interactable = ok;
        if (_startLbl != null)
            _startLbl.text = ok ? $"BẮT ĐẦU  ({cnt * _reps * 2})" : "BẮT ĐẦU";
    }

    private static void StartBacktest()
    {
        var indices = new List<int>();
        for (int i = 0; i < _sel.Length; i++) if (_sel[i]) indices.Add(i);
        Close();
        BacktestRunner.Launch(indices, _reps);
    }

    // ── Reps stepper ───────────────────────────────────────────────────────
    private static void BuildRepsStepper(GameObject panel, int layer)
    {
        // Container: centered, placed below subtitle
        var row  = Child(panel, "RepsRow", layer);
        var rRt  = row.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(0.5f, 1f); rRt.anchorMax = new Vector2(0.5f, 1f);
        rRt.pivot = new Vector2(0.5f, 1f);
        rRt.anchoredPosition = new Vector2(0f, -72f);
        rRt.sizeDelta = new Vector2(270f, 22f);

        // "Số lần / map:" label
        var lbl  = Child(row, "Lbl", layer);
        var lRt  = lbl.GetComponent<RectTransform>();
        lRt.anchorMin = new Vector2(0f, 0f); lRt.anchorMax = new Vector2(0f, 1f);
        lRt.pivot = new Vector2(0f, 0.5f);
        lRt.anchoredPosition = Vector2.zero; lRt.sizeDelta = new Vector2(128f, 0f);
        var lTxt = lbl.AddComponent<Text>();
        lTxt.text = "Số lần / map:"; lTxt.font = Fnt(); lTxt.fontSize = 13;
        lTxt.color = TextMuted; lTxt.alignment = TextAnchor.MiddleLeft;

        // [−] button
        StepBtn(row, layer, "Minus", "−", new Vector2(130f, 0f), new Vector2(22f, 22f),
            () => ChangeReps(-1));

        // Count label
        var cnt  = Child(row, "Count", layer);
        var cRt  = cnt.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0f, 0f); cRt.anchorMax = new Vector2(0f, 1f);
        cRt.pivot = new Vector2(0f, 0.5f);
        cRt.anchoredPosition = new Vector2(154f, 0f); cRt.sizeDelta = new Vector2(40f, 0f);
        _repsTxt = cnt.AddComponent<Text>();
        _repsTxt.text = _reps.ToString(); _repsTxt.font = Fnt(); _repsTxt.fontSize = 15;
        _repsTxt.fontStyle = FontStyle.Bold; _repsTxt.color = AccentGold;
        _repsTxt.alignment = TextAnchor.MiddleCenter;

        // [+] button
        StepBtn(row, layer, "Plus", "+", new Vector2(196f, 0f), new Vector2(22f, 22f),
            () => ChangeReps(+1));
    }

    private static void StepBtn(GameObject parent, int layer,
        string name, string symbol, Vector2 pos, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go  = Child(parent, name, layer);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f); rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>(); img.color = BtnSlate;
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
        var bc  = btn.colors;
        bc.normalColor      = Color.white;
        bc.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        bc.pressedColor     = new Color(0.75f, 0.75f, 0.75f);
        btn.colors = bc;
        btn.onClick.AddListener(onClick);
        var lbl  = Child(go, "L", layer);
        var lRt  = lbl.GetComponent<RectTransform>();
        lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
        lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;
        var t = lbl.AddComponent<Text>();
        t.text = symbol; t.font = Fnt(); t.fontSize = 16; t.fontStyle = FontStyle.Bold;
        t.color = AccentGold; t.alignment = TextAnchor.MiddleCenter;
    }

    private static void ChangeReps(int delta)
    {
        _reps = Mathf.Clamp(_reps + delta, 1, 20);
        if (_repsTxt != null) _repsTxt.text = _reps.ToString();
        RefreshBadges();
        RefreshStart();
    }

    private static void RefreshBadges()
    {
        if (_badgeTxt == null) return;
        foreach (var t in _badgeTxt)
            if (t != null) t.text = $"{_reps * 2} runs";
    }

    private static void Close()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static Font Fnt() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
    }

    private static GameObject Child(GameObject parent, string name, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static GameObject FootBtn(GameObject parent, int layer,
        string name, string label, Color bg, Color textColor,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = Child(parent, name, layer);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        var img = go.AddComponent<Image>(); img.color = bg;
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
        var bc  = btn.colors;
        bc.normalColor    = Color.white;
        bc.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
        bc.pressedColor   = new Color(0.80f, 0.80f, 0.80f);
        bc.disabledColor  = new Color(0.40f, 0.40f, 0.40f, 0.6f);
        btn.colors = bc;
        btn.onClick.AddListener(onClick);

        var lbl = Child(go, "Lbl", layer);
        var lRt = lbl.GetComponent<RectTransform>();
        lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
        lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;
        var t = lbl.AddComponent<Text>();
        t.text = label; t.font = Fnt(); t.fontSize = 13; t.fontStyle = FontStyle.Bold;
        t.color = textColor; t.alignment = TextAnchor.MiddleCenter;
        return go;
    }
}
