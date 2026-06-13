using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Biểu đồ cột so sánh A* vs PIBT hiển thị sau khi backtest hoàn thành.
/// Mỗi metric có 1 section riêng; thanh A* màu xanh, PIBT màu cam.
/// ESC hoặc nút [✕] để đóng.
/// </summary>
public class BacktestResultChart : MonoBehaviour
{
    // ── Entry ──────────────────────────────────────────────────────────────
    public static void Show(IReadOnlyList<BacktestRunRecord> results)
    {
        if (results == null || results.Count == 0) return;
        var go = new GameObject("BacktestResultChart");
        DontDestroyOnLoad(go);
        go.AddComponent<BacktestResultChart>().Build(results);
    }

    // ── Colours ────────────────────────────────────────────────────────────
    private static readonly Color C_BG      = new Color(0.07f, 0.08f, 0.10f, 0.97f);
    private static readonly Color C_Panel   = new Color(0.11f, 0.12f, 0.15f, 1.00f);
    private static readonly Color C_Header  = new Color(0.08f, 0.09f, 0.12f, 1.00f);
    private static readonly Color C_Sep     = new Color(1.00f, 1.00f, 1.00f, 0.07f);
    private static readonly Color C_Grid    = new Color(1.00f, 1.00f, 1.00f, 0.05f);
    private static readonly Color C_AStar   = new Color(0.28f, 0.60f, 1.00f, 1.00f);
    private static readonly Color C_PIBT    = new Color(1.00f, 0.55f, 0.15f, 1.00f);
    private static readonly Color C_Gold    = new Color(1.00f, 0.82f, 0.22f, 1.00f);
    private static readonly Color C_White   = new Color(0.93f, 0.95f, 1.00f, 1.00f);
    private static readonly Color C_Muted   = new Color(0.50f, 0.55f, 0.62f, 1.00f);
    private static readonly Color C_WinA    = new Color(0.18f, 0.70f, 0.35f, 0.22f); // A* wins tint
    private static readonly Color C_WinP    = new Color(0.80f, 0.45f, 0.10f, 0.18f); // PIBT wins tint
    private static readonly Color C_Close   = new Color(0.50f, 0.10f, 0.10f, 1.00f);

    // ── Layout ─────────────────────────────────────────────────────────────
    private const float PanelW      = 940f;
    private const float PadX        = 54f;   // left/right chart padding
    private const float HeaderH     = 72f;   // title+legend block
    private const float SectionLblH = 22f;   // metric label row
    private const float ChartAreaH  = 150f;  // bar area
    private const float XLabelH     = 18f;   // map-name row below bars
    private const float SectionGap  = 14f;   // vertical gap between sections
    private const float SummaryH    = 80f;   // summary table block
    private const float FootH       = 54f;   // bottom padding (includes exit button)
    private const float MaxPanelH   = 540f;  // ≈75 % of 720 reference height; content scrolls
    private const float BarW        = 26f;
    private const float BarGap      = 6f;
    private const float GroupGap    = 16f;

    // ── Metric definitions ─────────────────────────────────────────────────
    private struct Metric
    {
        public string                            label;
        public System.Func<BacktestRunRecord, float> get;
        public float                             maxHint;   // 0 = auto
        public bool                              lowerBetter;
    }

    private static readonly Metric[] Metrics =
    {
        new Metric { label="Thời gian trung bình (s)", get=r=>r.duration,    maxHint=120f, lowerBetter=true  },
        new Metric { label="Số lần replan tổng",       get=r=>r.totalReplans,maxHint=0,    lowerBetter=false },
        new Metric { label="Tổng số shot",             get=r=>r.totalShots,  maxHint=0,    lowerBetter=false },
    };

    // ── State ──────────────────────────────────────────────────────────────
    private int           _layer;
    private RectTransform _contentRt;
    private RectTransform _panelRt;
    private float         _contentH;
    private float         _viewportH;
    private float         _scrollOffset;

    // ── Build ──────────────────────────────────────────────────────────────
    private void Build(IReadOnlyList<BacktestRunRecord> results)
    {
        _layer = LayerMask.NameToLayer("UI");

        // Aggregate: map × algo → (sum, count) per metric
        var maps = new List<string>();
        // agg[metricIdx][(map,algo)] = (sum, count)
        var agg = new Dictionary<(string map, string algo), float[]>();

        foreach (var r in results)
        {
            if (!maps.Contains(r.map)) maps.Add(r.map);
            var key = (r.map, r.algorithm);
            if (!agg.TryGetValue(key, out float[] sums))
            {
                sums = new float[Metrics.Length * 2]; // [sum0, cnt0, sum1, cnt1, ...]
                agg[key] = sums;
            }
            for (int m = 0; m < Metrics.Length; m++)
            {
                sums[m * 2]     += Metrics[m].get(r);
                sums[m * 2 + 1] += 1f;
            }
        }

        float Avg(string map, string algo, int mi)
        {
            var key = (map, algo);
            if (!agg.TryGetValue(key, out float[] s)) return 0f;
            float cnt = s[mi * 2 + 1];
            return cnt > 0 ? s[mi * 2] / cnt : 0f;
        }

        // Heights — panel is capped; content scrolls inside
        // perSection layout reality: label(22) + gap(4) + chart(150) + xlabel(18) + sectionGap(14) = 208
        // plus 4px initial curY offset → use (perSection+4) per section + 4 preamble
        float perSection = SectionLblH + ChartAreaH + XLabelH + SectionGap;          // 204 (for scale ref)
        float contentH   = 4f + Metrics.Length * (perSection + 4f) + SummaryH + FootH; // actual layout height
        float panelH     = Mathf.Min(5f + HeaderH + contentH, MaxPanelH);

        // ── Canvas ────────────────────────────────────────────────────────
        gameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        gameObject.GetComponent<Canvas>().sortingOrder = 300;
        var cs = gameObject.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1280f, 720f);
        gameObject.AddComponent<GraphicRaycaster>();

        // Dim overlay
        var dim = Mk("Dim", gameObject); Stretch(dim);
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
        var db = dim.AddComponent<Button>(); db.transition = Selectable.Transition.None;

        // Panel
        var panel = Mk("Panel", gameObject);
        SetRT(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
              Vector2.zero, new Vector2(PanelW, panelH));
        panel.AddComponent<Image>().color = C_Panel;

        // Gold top bar
        var gold = Mk("Gold", panel);
        SetRT(gold, new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(0.5f,1f),
              Vector2.zero, new Vector2(0f, 5f));
        gold.AddComponent<Image>().color = C_Gold;

        // Header background
        var hdrBg = Mk("HdrBg", panel);
        SetRT(hdrBg, new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(0.5f,1f),
              new Vector2(0f, -5f), new Vector2(0f, HeaderH));
        hdrBg.AddComponent<Image>().color = C_Header;

        // Title — anchor top-left + pos (30,-18) so rect spans [30, PanelW-30], text centered within
        Lbl("Title", panel, new Vector2(0f,1f), new Vector2(30f,-18f),
            new Vector2(PanelW-60f, 26f), "KẾT QUẢ BACKTEST — SO SÁNH A* vs PIBT",
            18, FontStyle.Bold, C_Gold, TextAnchor.MiddleCenter);

        // Legend
        BuildLegend(panel, new Vector2(0.5f,1f), new Vector2(-90f,-52f));

        // Close button [✕] — top-right, closes chart only
        var closeGo = Mk("Close", panel);
        SetRT(closeGo, new Vector2(1f,1f), new Vector2(1f,1f), new Vector2(1f,1f),
              new Vector2(-8f,-8f), new Vector2(26f,26f));
        closeGo.AddComponent<Image>().color = C_Close;
        var cb = closeGo.AddComponent<Button>(); cb.targetGraphic = closeGo.GetComponent<Image>();
        cb.onClick.AddListener(ExitToMenu);
        Lbl("X", closeGo, new Vector2(0.5f,0.5f), Vector2.zero, new Vector2(26f,26f),
            "✕", 13, FontStyle.Bold, C_White, TextAnchor.MiddleCenter, fill: true);

        // Horizontal separator under header
        Sep("SepH", panel, -(5f + HeaderH));

        // ── Viewport — RectMask2D clips; scroll handled manually in Update ─
        var vpGo = Mk("Viewport", panel);
        var vpRt = vpGo.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(0f, -(5f + HeaderH + 1f));
        vpGo.AddComponent<RectMask2D>();

        // Content — anchored to top; Update() shifts anchoredPosition.y on scroll
        var contentGo = Mk("Content", vpGo);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin        = new Vector2(0f, 1f);
        contentRt.anchorMax        = new Vector2(1f, 1f);
        contentRt.pivot            = new Vector2(0f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta        = new Vector2(0f, contentH);

        // ── Chart sections (inside scrollable content) ────────────────────
        float curY = -4f;
        for (int mi = 0; mi < Metrics.Length; mi++)
        {
            BuildSection(contentGo, maps, Avg, mi, ref curY);
            if (mi < Metrics.Length - 1)
                Sep($"SepM{mi}", contentGo, curY);
        }

        // ── Summary table (inside scrollable content) ─────────────────────
        Sep("SepS", contentGo, curY);
        curY -= 4f;
        BuildSummary(contentGo, maps, Avg, curY);

        // ── Exit button — pinned to bottom of content ─────────────────────
        Sep("SepExit", contentGo, -(contentH - FootH));
        var exitGo = Mk("Exit", contentGo);
        SetRT(exitGo, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
              new Vector2(0f, 10f), new Vector2(240f, 34f));
        var exitImg = exitGo.AddComponent<Image>();
        exitImg.color = new Color(0.55f, 0.12f, 0.12f, 1f);
        var eb = exitGo.AddComponent<Button>();
        eb.targetGraphic = exitImg;
        eb.onClick.AddListener(ExitToMenu);
        Lbl("ExitTxt", exitGo, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 34f),
            "Thoát & Về Menu", 13, FontStyle.Bold, C_White, TextAnchor.MiddleCenter, fill: true);

        // ── Save scroll references ─────────────────────────────────────────
        _contentRt = contentRt;
        _panelRt   = panel.GetComponent<RectTransform>();
        _contentH  = contentH;
        _viewportH = panelH - 5f - HeaderH - 1f;
    }

    private void ExitToMenu()
    {
        BacktestRunner.Cleanup();
        Destroy(gameObject);
        SceneManager.LoadScene("Menu");
    }

    // ── Section (one metric) ───────────────────────────────────────────────
    private void BuildSection(GameObject panel, List<string> maps,
        System.Func<string, string, int, float> avg, int mi, ref float curY)
    {
        var met = Metrics[mi];

        // Metric label row
        Lbl($"ML{mi}", panel, new Vector2(0f,1f), new Vector2(PadX, curY),
            new Vector2(PanelW - PadX*2f, SectionLblH),
            met.label, 11, FontStyle.Bold, C_Muted, TextAnchor.MiddleLeft);
        curY -= SectionLblH + 4f;

        // Auto-scale
        float maxVal = met.maxHint;
        if (maxVal <= 0f)
        {
            foreach (var map in maps)
                foreach (var algo in new[]{"AStar","PIBT"})
                    maxVal = Mathf.Max(maxVal, avg(map, algo, mi));
            maxVal = maxVal > 0 ? maxVal * 1.2f : 1f;
        }

        float chartTop = curY;

        // Grid lines (5 lines: 0%, 25%, 50%, 75%, 100%)
        for (int g = 0; g <= 4; g++)
        {
            float t  = g / 4f;
            float gy = chartTop - (1f - t) * ChartAreaH;

            var gl = Mk($"GL{mi}_{g}", panel);
            SetRT(gl, new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(0f,1f),
                  new Vector2(PadX, gy), new Vector2(-PadX*2f, 1f));
            gl.AddComponent<Image>().color = C_Grid;

            // Y-axis value
            string valStr = (t * maxVal).ToString(maxVal >= 100 ? "F0" : "F1");
            Lbl($"GV{mi}_{g}", panel, new Vector2(0f,1f),
                new Vector2(2f, gy + 6f), new Vector2(PadX - 4f, 14f),
                valStr, 8, FontStyle.Normal, C_Muted, TextAnchor.MiddleRight);
        }

        // Bars
        int nMaps = maps.Count;
        float totalBarW  = (BarW * 2 + BarGap);
        float totalGroupW = totalBarW + GroupGap;
        float chartW     = PanelW - PadX * 2f;
        float groupSpacing = nMaps > 1 ? chartW / nMaps : chartW;

        float baseline = chartTop - ChartAreaH; // y coordinate of chart bottom

        for (int i = 0; i < nMaps; i++)
        {
            string map   = maps[i];
            float  gCenterX = PadX + groupSpacing * i + groupSpacing * 0.5f;
            float  aX = gCenterX - BarW - BarGap * 0.5f;
            float  pX = gCenterX + BarGap * 0.5f;

            float aVal = avg(map, "AStar", mi);
            float pVal = avg(map, "PIBT",  mi);

            // Win tint background behind the pair
            Color tint = Color.clear;
            if (aVal > 0 && pVal > 0)
            {
                bool aWins = met.lowerBetter ? aVal < pVal : aVal > pVal;
                tint = aWins ? C_WinA : C_WinP;
            }
            if (tint != Color.clear)
            {
                var bg = Mk($"Bg{mi}_{i}", panel);
                SetRT(bg, new Vector2(0f,1f), new Vector2(0f,1f), new Vector2(0f,1f),
                      new Vector2(aX - 4f, chartTop), new Vector2(totalBarW + 8f, ChartAreaH));
                bg.AddComponent<Image>().color = tint;
            }

            DrawBar($"BA{mi}_{i}", panel, aX, baseline, aVal, maxVal, C_AStar, chartTop, TextAnchor.LowerRight);
            DrawBar($"BP{mi}_{i}", panel, pX, baseline, pVal, maxVal, C_PIBT,  chartTop, TextAnchor.LowerLeft);

            // Map label
            Lbl($"MapL{mi}_{i}", panel, new Vector2(0f,1f),
                new Vector2(gCenterX - groupSpacing*0.5f + 2f, baseline - 2f),
                new Vector2(groupSpacing - 4f, XLabelH),
                map, 9, FontStyle.Normal, C_Muted, TextAnchor.UpperCenter);
        }

        curY = baseline - XLabelH - SectionGap;
    }

    private void DrawBar(string name, GameObject panel,
        float x, float baseline, float val, float maxVal, Color col, float chartTop, TextAnchor valueAlign)
    {
        if (maxVal <= 0f) return;

        float h = Mathf.Clamp01(val / maxVal) * ChartAreaH;
        if (h < 1f) h = 1f;

        // Bar grows upward from baseline
        var go = Mk(name, panel);
        SetRT(go, new Vector2(0f,1f), new Vector2(0f,1f), new Vector2(0f,0f),
              new Vector2(x, baseline), new Vector2(BarW, h));
        go.AddComponent<Image>().color = col;

        // Value label above bar (if non-zero)
        if (val > 0.01f)
        {
            string txt = val >= 10f ? val.ToString("F0") : val.ToString("F1");
            const float labelW = 54f;
            float labelX = valueAlign == TextAnchor.LowerRight ? x + BarW - labelW : x;
            Lbl(name + "V", panel, new Vector2(0f,1f),
                new Vector2(labelX, baseline + h + 13f),
                new Vector2(labelW, 13f),
                txt, 8, FontStyle.Normal, C_White, valueAlign);
        }
    }

    // ── Summary table ──────────────────────────────────────────────────────
    private void BuildSummary(GameObject panel, List<string> maps,
        System.Func<string, string, int, float> avg, float startY)
    {
        Lbl("SumTitle", panel, new Vector2(0f,1f), new Vector2(PadX, startY - 8f),
            new Vector2(300f, 18f), "Tổng kết", 11, FontStyle.Bold, C_Gold, TextAnchor.MiddleLeft);

        float rowY = startY - 28f;
        float colW = (PanelW - PadX*2f) / (Metrics.Length + 1);

        // Header
        Lbl("ShMap", panel, new Vector2(0f,1f), new Vector2(PadX, rowY),
            new Vector2(colW, 16f), "Map", 9, FontStyle.Bold, C_Muted, TextAnchor.MiddleLeft);
        for (int mi = 0; mi < Metrics.Length; mi++)
        {
            string short_ = Metrics[mi].label.Length > 14
                ? Metrics[mi].label.Substring(0, 14) + "…"
                : Metrics[mi].label;
            Lbl($"Sh{mi}", panel, new Vector2(0f,1f),
                new Vector2(PadX + colW*(mi+1), rowY),
                new Vector2(colW, 16f), short_, 8, FontStyle.Normal, C_Muted, TextAnchor.MiddleCenter);
        }

        rowY -= 16f;
        Sep("SepSH", panel, rowY);
        rowY -= 4f;

        foreach (var map in maps)
        {
            Lbl($"Sr_{map}", panel, new Vector2(0f,1f), new Vector2(PadX, rowY),
                new Vector2(colW, 14f), map, 9, FontStyle.Normal, C_White, TextAnchor.MiddleLeft);

            for (int mi = 0; mi < Metrics.Length; mi++)
            {
                float a = avg(map, "AStar", mi);
                float p = avg(map, "PIBT", mi);
                bool aWins = Metrics[mi].lowerBetter ? a < p : a > p;
                string cell = $"A:{a:F0}  P:{p:F0}";
                Color txtCol = (a == 0 && p == 0) ? C_Muted : (aWins ? C_AStar : C_PIBT);

                Lbl($"Sr{mi}_{map}", panel, new Vector2(0f,1f),
                    new Vector2(PadX + colW*(mi+1), rowY),
                    new Vector2(colW, 14f), cell, 8, FontStyle.Normal, txtCol, TextAnchor.MiddleCenter);
            }
            rowY -= 16f;
        }
    }

    // ── Legend ────────────────────────────────────────────────────────────
    private void BuildLegend(GameObject panel, Vector2 anchor, Vector2 pos)
    {
        LegDot("LA", panel, anchor, pos,                    C_AStar, "A* (xanh)");
        LegDot("LP", panel, anchor, new Vector2(pos.x+120f, pos.y), C_PIBT,  "PIBT (cam)");
    }

    private void LegDot(string id, GameObject panel, Vector2 anchor, Vector2 pos, Color col, string lbl)
    {
        var dot = Mk(id+"D", panel);
        SetRT(dot, anchor, anchor, new Vector2(0f,0.5f), new Vector2(pos.x-16f, pos.y), new Vector2(12f,12f));
        dot.AddComponent<Image>().color = col;
        Lbl(id+"L", panel, anchor, pos, new Vector2(110f, 16f),
            lbl, 11, FontStyle.Normal, C_Muted, TextAnchor.MiddleLeft);
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static Font Fnt() => UiFontProvider.GetDefaultFont();

    private GameObject Mk(string name, GameObject parent)
    {
        var go = new GameObject(name);
        go.layer = _layer;
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    // anchorMin/Max collapsed to one point; pivot explicit
    private static void SetRT(GameObject go,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private void Sep(string name, GameObject panel, float y)
    {
        var s = Mk(name, panel);
        SetRT(s, new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(0.5f,1f),
              new Vector2(0f, y), new Vector2(0f, 1f));
        s.AddComponent<Image>().color = C_Sep;
    }

    private void Lbl(string name, GameObject parent,
        Vector2 anchor, Vector2 pos, Vector2 size,
        string text, int fontSize, FontStyle style, Color color, TextAnchor align,
        bool fill = false)
    {
        var go = Mk(name, parent);
        var rt = go.GetComponent<RectTransform>();
        if (fill)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
        }
        var t = go.AddComponent<Text>();
        t.text      = text; t.font       = Fnt();
        t.fontSize  = fontSize; t.fontStyle = style;
        t.color     = color;   t.alignment = align;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) ExitToMenu();
        HandleScroll();
    }

    private void HandleScroll()
    {
        if (_contentRt == null || _panelRt == null) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        // Only scroll when mouse is inside the panel rect
        if (!RectTransformUtility.RectangleContainsScreenPoint(_panelRt, Input.mousePosition, null)) return;

        float maxScroll = Mathf.Max(0f, _contentH - _viewportH);
        // scroll > 0 = wheel up = see top  →  decrease offset
        // scroll < 0 = wheel down = see bottom  →  increase offset
        _scrollOffset = Mathf.Clamp(_scrollOffset - scroll * 500f, 0f, maxScroll);
        _contentRt.anchoredPosition = new Vector2(0f, _scrollOffset);
    }
}
