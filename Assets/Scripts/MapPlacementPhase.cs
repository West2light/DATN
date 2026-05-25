using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Pre-game crate/barrier placement phase.
/// Map 2 & 3: up to 2 wooden crates.
/// Map 4: up to 2 wooden crates + 1 metal barrier.
/// Controls: left-click = place, right-click point = remove, right-drag = pan, scroll = zoom.
/// </summary>
public class MapPlacementPhase : MonoBehaviour
{
    // ── Per-map limits ─────────────────────────────────────────────────────────
    private static readonly Dictionary<string, (int crates, int barriers)> MapLimits =
        new Dictionary<string, (int, int)>
        {
            { "ht_mansion_n.map",        (2, 0) },
            { "ht_chantry.map",          (2, 0) },
            { "lt_gallowstemplar_n.map", (2, 1) },
        };

    private enum PlaceMode { Crate, Barrier }

    // ── Runtime state ──────────────────────────────────────────────────────────
    private MapLoader   mapLoader;
    private Camera      mainCam;
    private Action      onDone;
    private PlayerInput playerInput;
    private Transform   playerTransform;

    private int       maxCrates;
    private int       maxBarriers;
    private PlaceMode currentMode = PlaceMode.Crate;

    private struct PlacedItem { public Vector2Int cell; public GameObject go; }
    private readonly List<PlacedItem>    placedCrates   = new List<PlacedItem>();
    private readonly List<PlacedItem>    placedBarriers = new List<PlacedItem>();
    private readonly HashSet<Vector2Int> placedCells    = new HashSet<Vector2Int>();

    // sprites
    private Sprite crateSprite;
    private Sprite barrierSprite;

    // ghost
    private GameObject     ghostObj;
    private SpriteRenderer ghostSr;

    // UI refs
    private Text  statusText;
    private Image crateModeImg;
    private Image barrierModeImg;
    private Text  crateModeLabel;
    private Text  barrierModeLabel;

    // camera nav
    private float   minOrtho;
    private float   maxOrtho;
    private Vector3 panStartWorld;
    private Vector3 panStartCamPos;
    private bool    isPanning;
    private Vector3 rightDownScreen;
    private const float DragThresholdPx = 6f;
    private const float ZoomSpeed       = 0.12f;

    // colours
    private static readonly Color GhostCrateOk    = new Color(0.25f, 1f,    0.25f, 0.52f);
    private static readonly Color GhostBarrierOk  = new Color(0.25f, 0.75f, 1f,    0.52f);
    private static readonly Color GhostBad        = new Color(1f,    0.22f, 0.22f, 0.52f);
    private static readonly Color CrateTint       = new Color(0.85f, 0.56f, 0.18f, 1f);
    private static readonly Color BarrierTint     = new Color(0.42f, 0.64f, 0.92f, 1f);
    private static readonly Color ModeActiveCrate  = new Color(0.82f, 0.50f, 0.10f, 1f);
    private static readonly Color ModeActiveBarrier= new Color(0.18f, 0.48f, 0.82f, 1f);
    private static readonly Color ModeInactive    = new Color(0.18f, 0.18f, 0.20f, 1f);

    // ── Public API ─────────────────────────────────────────────────────────────

    public static bool ShouldTrigger()
    {
        string sel = PlayerPrefs.GetString("SelectedMapFile", "");
        if (string.IsNullOrEmpty(sel)) return false;
        return MapLimits.ContainsKey(Path.GetFileName(sel));
    }

    public void BeginPhase(MapLoader loader, Camera cam, Action callback, Transform pTransform = null)
    {
        mapLoader       = loader;
        mainCam         = cam;
        onDone          = callback;
        playerTransform = pTransform;

        string fn = Path.GetFileName(PlayerPrefs.GetString("SelectedMapFile", ""));
        if (MapLimits.TryGetValue(fn, out var lim)) { maxCrates = lim.crates; maxBarriers = lim.barriers; }
        else                                         { maxCrates = 2;          maxBarriers = 0; }

        if (pTransform != null)
        {
            playerInput = pTransform.GetComponent<PlayerInput>();
            if (playerInput != null) playerInput.enabled = false;
        }

        crateSprite   = LoadCrateSprite();
        barrierSprite = LoadBarrierSprite();

        SetupPlacementCamera();
        BuildGhost();
        BuildUI();
    }

    // ── Camera ─────────────────────────────────────────────────────────────────

    private void SetupPlacementCamera()
    {
        float mapW = mapLoader.BuildWidth  * mapLoader.tileSize;
        float mapH = mapLoader.BuildHeight * mapLoader.tileSize;
        maxOrtho = Mathf.Max(mapH / 2f, mapW / (2f * mainCam.aspect)) * 0.95f;
        minOrtho = 3f;
        mainCam.orthographicSize = Mathf.Clamp(10f, minOrtho, maxOrtho);
        Vector3 pos = playerTransform != null ? playerTransform.position : Vector3.zero;
        pos.z = -10f;
        mainCam.transform.position = pos;
        ClampCamera();
    }

    private void ClampCamera()
    {
        float halfH = mainCam.orthographicSize;
        float halfW = halfH * mainCam.aspect;
        float mapW  = mapLoader.BuildWidth  * mapLoader.tileSize;
        float mapH  = mapLoader.BuildHeight * mapLoader.tileSize;
        Vector3 p   = mainCam.transform.position;
        p.x = halfW * 2f >= mapW ? 0f : Mathf.Clamp(p.x, -mapW / 2f + halfW, mapW / 2f - halfW);
        p.y = halfH * 2f >= mapH ? 0f : Mathf.Clamp(p.y, -mapH / 2f + halfH, mapH / 2f - halfH);
        p.z = -10f;
        mainCam.transform.position = p;
    }

    // ── Sprites ────────────────────────────────────────────────────────────────

    private static Sprite LoadCrateSprite()
    {
#if UNITY_EDITOR
        string[] paths = {
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateWood_dark.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateWood.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateMetal.png",
        };
        foreach (string p in paths) { Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(p); if (s) return s; }
#endif
        return null;
    }

    private static Sprite LoadBarrierSprite()
    {
#if UNITY_EDITOR
        string[] paths = {
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/barricadeMetal.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/barricadeWood.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/barricade.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/fence.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateMetal.png",
        };
        foreach (string p in paths) { Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(p); if (s) return s; }
#endif
        return null;
    }

    // ── Ghost ──────────────────────────────────────────────────────────────────

    private void BuildGhost()
    {
        ghostObj = new GameObject("PlacementGhost");
        ghostSr  = ghostObj.AddComponent<SpriteRenderer>();
        ghostSr.sprite       = crateSprite;
        ghostSr.sortingOrder = 10;
        ghostObj.transform.localScale = Vector3.one * mapLoader.tileSize;
        ghostObj.SetActive(false);
    }

    // ── UI ─────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        int  uiLayer    = LayerMask.NameToLayer("UI");
        bool hasBarriers = maxBarriers > 0;

        GameObject canvasObj = new GameObject("PlacementUI");
        canvasObj.layer = uiLayer;
        Canvas cv = canvasObj.AddComponent<Canvas>();
        cv.renderMode       = RenderMode.ScreenSpaceOverlay;
        cv.sortingLayerName = "UI";
        cv.sortingOrder     = 100;
        CanvasScaler sc = canvasObj.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        canvasObj.AddComponent<GraphicRaycaster>();

        // ── Top bar ────────────────────────────────────────────────────────────
        GameObject topBar = MakeRt(canvasObj, "TopBar", uiLayer);
        RectTransform tbRt = topBar.GetComponent<RectTransform>();
        tbRt.anchorMin = new Vector2(0f, 1f); tbRt.anchorMax = new Vector2(1f, 1f);
        tbRt.pivot = new Vector2(0.5f, 1f);
        tbRt.anchoredPosition = Vector2.zero;
        tbRt.sizeDelta = new Vector2(0f, 76f);
        topBar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        string hint1 = hasBarriers
            ? "Đặt thùng gỗ / rào chắn để bảo vệ Base  |  Chuột trái: Đặt  •  Chuột phải: Xóa"
            : "Đặt tối đa 2 thùng gỗ để bảo vệ Base  |  Chuột trái: Đặt  •  Chuột phải: Xóa";

        MakeText(topBar, "Hint1", hint1, 16, FontStyle.Normal, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(980f, 24f));

        MakeText(topBar, "Hint2",
            "Giữ chuột phải kéo: Di chuyển  •  Cuộn chuột: Zoom",
            14, FontStyle.Normal, new Color(0.72f, 0.72f, 0.72f, 1f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -4f), new Vector2(700f, 20f));

        statusText = MakeText(topBar, "Status", GetStatusString(),
            15, FontStyle.Normal, new Color(1f, 0.84f, 0f, 1f),
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-20f, 0f), new Vector2(280f, 22f));

        // ── Bottom bar ─────────────────────────────────────────────────────────
        // With barriers: 2 rows (mode selector + action buttons) = 116px
        // Without: 1 row (action buttons) = 64px
        float botH = hasBarriers ? 116f : 64f;

        GameObject botBar = MakeRt(canvasObj, "BotBar", uiLayer);
        RectTransform bbRt = botBar.GetComponent<RectTransform>();
        bbRt.anchorMin = new Vector2(0.5f, 0f); bbRt.anchorMax = new Vector2(0.5f, 0f);
        bbRt.pivot = new Vector2(0.5f, 0f);
        bbRt.anchoredPosition = Vector2.zero;
        bbRt.sizeDelta = new Vector2(560f, botH);
        botBar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        if (hasBarriers)
        {
            // Row 1 (top) — mode selector: offset +29 from bar center (bar center = 58)
            GameObject cbObj = MakeButton(botBar, "CrateBtn",
                CrateModeLabel(), ModeActiveCrate, Color.white,
                new Vector2(-142f, +29f), new Vector2(250f, 42f),
                () => SetMode(PlaceMode.Crate));
            crateModeImg   = cbObj.GetComponent<Image>();
            crateModeLabel = cbObj.GetComponentInChildren<Text>();

            GameObject bbObj = MakeButton(botBar, "BarrierBtn",
                BarrierModeLabel(), ModeInactive, new Color(0.65f, 0.65f, 0.65f, 1f),
                new Vector2(+142f, +29f), new Vector2(250f, 42f),
                () => SetMode(PlaceMode.Barrier));
            barrierModeImg   = bbObj.GetComponent<Image>();
            barrierModeLabel = bbObj.GetComponentInChildren<Text>();

            // Thin separator between rows
            MakeSeparator(botBar, uiLayer);

            // Row 2 (bottom) — action buttons: offset -29 from bar center
            MakeButton(botBar, "StartBtn", "BẮT ĐẦU",
                new Color(0.11f, 0.50f, 0.25f, 1f), Color.white,
                new Vector2(-142f, -29f), new Vector2(250f, 42f),
                () => Finish(false, canvasObj));

            MakeButton(botBar, "SkipBtn", "BỎ QUA (không đặt)",
                new Color(0.18f, 0.18f, 0.20f, 1f), new Color(0.78f, 0.78f, 0.78f, 1f),
                new Vector2(+142f, -29f), new Vector2(250f, 42f),
                () => Finish(true, canvasObj));
        }
        else
        {
            // Single row centered in 64px bar (offset 0 from center = centred)
            MakeButton(botBar, "StartBtn", "BẮT ĐẦU",
                new Color(0.11f, 0.50f, 0.25f, 1f), Color.white,
                new Vector2(-142f, 0f), new Vector2(250f, 44f),
                () => Finish(false, canvasObj));

            MakeButton(botBar, "SkipBtn", "BỎ QUA (không đặt)",
                new Color(0.18f, 0.18f, 0.20f, 1f), new Color(0.78f, 0.78f, 0.78f, 1f),
                new Vector2(+142f, 0f), new Vector2(250f, 44f),
                () => Finish(true, canvasObj));
        }
    }

    private void SetMode(PlaceMode mode)
    {
        currentMode    = mode;
        ghostSr.sprite = mode == PlaceMode.Crate ? crateSprite : barrierSprite;
        RefreshModeButtons();
    }

    private void RefreshModeButtons()
    {
        if (crateModeImg != null)
        {
            bool active = currentMode == PlaceMode.Crate;
            crateModeImg.color = active ? ModeActiveCrate : ModeInactive;
            if (crateModeLabel != null) crateModeLabel.color = active ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);
        }
        if (barrierModeImg != null)
        {
            bool active = currentMode == PlaceMode.Barrier;
            barrierModeImg.color = active ? ModeActiveBarrier : ModeInactive;
            if (barrierModeLabel != null) barrierModeLabel.color = active ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);
        }
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        HandleZoom();
        HandlePan();
        UpdateGhost();
        if (Input.GetMouseButtonDown(0)) TryPlace();
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;
        Vector3 before = mainCam.ScreenToWorldPoint(Input.mousePosition);
        mainCam.orthographicSize = Mathf.Clamp(
            mainCam.orthographicSize * (1f - scroll * ZoomSpeed * 10f), minOrtho, maxOrtho);
        Vector3 after = mainCam.ScreenToWorldPoint(Input.mousePosition);
        Vector3 pos   = mainCam.transform.position + (before - after);
        pos.z = -10f;
        mainCam.transform.position = pos;
        ClampCamera();
    }

    private void HandlePan()
    {
        if (Input.GetMouseButtonDown(1))
        {
            panStartWorld   = mainCam.ScreenToWorldPoint(Input.mousePosition);
            panStartCamPos  = mainCam.transform.position;
            rightDownScreen = Input.mousePosition;
            isPanning       = false;
        }

        if (Input.GetMouseButton(1))
        {
            if (!isPanning && Vector3.Distance(Input.mousePosition, rightDownScreen) > DragThresholdPx)
                isPanning = true;
            if (isPanning)
            {
                Vector3 diff = panStartWorld - mainCam.ScreenToWorldPoint(Input.mousePosition);
                Vector3 pos  = panStartCamPos + diff;
                pos.z = -10f;
                mainCam.transform.position = pos;
                ClampCamera();
            }
        }

        if (Input.GetMouseButtonUp(1) && !isPanning) TryRemove();
    }

    // ── Placement logic ────────────────────────────────────────────────────────

    private void UpdateGhost()
    {
        Vector3    wp   = mainCam.ScreenToWorldPoint(Input.mousePosition);
        Vector2Int cell = mapLoader.WorldToCell(new Vector3(wp.x, wp.y, 0f));
        bool ok = CanPlaceAt(cell);
        ghostObj.SetActive(true);
        ghostObj.transform.position = mapLoader.CellToWorld(cell);
        ghostSr.color = ok
            ? (currentMode == PlaceMode.Crate ? GhostCrateOk : GhostBarrierOk)
            : GhostBad;
    }

    private bool CanPlaceAt(Vector2Int cell)
    {
        if (!mapLoader.IsWalkable(cell)) return false;
        if (placedCells.Contains(cell))  return false;
        return currentMode == PlaceMode.Crate
            ? placedCrates.Count < maxCrates
            : placedBarriers.Count < maxBarriers;
    }

    private void TryPlace()
    {
        Vector3    wp   = mainCam.ScreenToWorldPoint(Input.mousePosition);
        Vector2Int cell = mapLoader.WorldToCell(new Vector3(wp.x, wp.y, 0f));
        if (!CanPlaceAt(cell)) return;
        if (currentMode == PlaceMode.Crate)
            SpawnItem(cell, crateSprite, CrateTint, $"WoodCrate_{cell.x}_{cell.y}", placedCrates);
        else
            SpawnItem(cell, barrierSprite, BarrierTint, $"Barrier_{cell.x}_{cell.y}", placedBarriers);
    }

    private void TryRemove()
    {
        Vector3    wp   = mainCam.ScreenToWorldPoint(Input.mousePosition);
        Vector2Int cell = mapLoader.WorldToCell(new Vector3(wp.x, wp.y, 0f));
        if (!TryRemoveFrom(cell, placedCrates))
            TryRemoveFrom(cell, placedBarriers);
        RefreshStatus();
    }

    private bool TryRemoveFrom(Vector2Int cell, List<PlacedItem> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].cell != cell) continue;
            mapLoader.UnmarkCellBlocked(cell);
            Destroy(list[i].go);
            list.RemoveAt(i);
            placedCells.Remove(cell);
            return true;
        }
        return false;
    }

    private void SpawnItem(Vector2Int cell, Sprite sprite, Color tint, string goName, List<PlacedItem> list)
    {
        int wallLayer = LayerMask.NameToLayer("Walls");
        if (wallLayer < 0) wallLayer = LayerMask.NameToLayer("ObstaclesMovement");

        GameObject go = new GameObject(goName);
        go.layer                = wallLayer;
        go.transform.position   = mapLoader.CellToWorld(cell);
        go.transform.localScale = Vector3.one * mapLoader.tileSize;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = sprite;
        sr.color        = tint;
        sr.sortingOrder = 3;

        go.AddComponent<BoxCollider2D>().size = Vector2.one;

        GameObject hb = new GameObject("BulletHitbox");
        hb.layer = LayerMask.NameToLayer("Hittable");
        hb.transform.SetParent(go.transform, false);
        BoxCollider2D hbCol = hb.AddComponent<BoxCollider2D>();
        hbCol.isTrigger = true;
        hbCol.size      = Vector2.one;

        list.Add(new PlacedItem { cell = cell, go = go });
        placedCells.Add(cell);
        mapLoader.MarkCellBlocked(cell);
        RefreshStatus();
    }

    private void Finish(bool clearAll, GameObject uiRoot)
    {
        if (clearAll)
        {
            foreach (PlacedItem pi in placedCrates)   { mapLoader.UnmarkCellBlocked(pi.cell); Destroy(pi.go); }
            foreach (PlacedItem pi in placedBarriers) { mapLoader.UnmarkCellBlocked(pi.cell); Destroy(pi.go); }
            placedCrates.Clear(); placedBarriers.Clear(); placedCells.Clear();
        }

        if (ghostObj    != null) Destroy(ghostObj);
        if (uiRoot      != null) Destroy(uiRoot);
        if (playerInput != null) playerInput.enabled = true;

        onDone?.Invoke();
        Destroy(this);
    }

    // ── Status & labels ────────────────────────────────────────────────────────

    private string GetStatusString()
    {
        if (maxBarriers > 0)
            return $"Thùng: {placedCrates.Count}/{maxCrates}  •  Rào: {placedBarriers.Count}/{maxBarriers}";
        return $"Đã đặt: {placedCrates.Count} / {maxCrates}";
    }

    private string CrateModeLabel()   => $"THÙNG GO ({placedCrates.Count}/{maxCrates})";
    private string BarrierModeLabel() => $"RAO CHAN ({placedBarriers.Count}/{maxBarriers})";

    private void RefreshStatus()
    {
        if (statusText    != null) statusText.text    = GetStatusString();
        if (crateModeLabel  != null) crateModeLabel.text  = CrateModeLabel();
        if (barrierModeLabel != null) barrierModeLabel.text = BarrierModeLabel();
        RefreshModeButtons();
    }

    // ── UI helpers ─────────────────────────────────────────────────────────────

    private static GameObject MakeRt(GameObject parent, string name, int layer)
    {
        GameObject obj = new GameObject(name);
        obj.layer = layer;
        obj.AddComponent<RectTransform>();
        obj.transform.SetParent(parent.transform, false);
        return obj;
    }

    private static void MakeSeparator(GameObject parent, int layer)
    {
        GameObject obj = new GameObject("Sep");
        obj.layer = layer;
        obj.AddComponent<RectTransform>();
        obj.transform.SetParent(parent.transform, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.05f, 0.5f);
        rt.anchorMax = new Vector2(0.95f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 1f);
        obj.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
    }

    private static Text MakeText(GameObject parent, string name, string content,
        int fontSize, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject obj = new GameObject(name);
        obj.layer = parent.layer;
        obj.AddComponent<RectTransform>();
        obj.transform.SetParent(parent.transform, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
        Text txt = obj.AddComponent<Text>();
        txt.text = content; txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize; txt.fontStyle = style;
        txt.alignment = TextAnchor.MiddleCenter; txt.color = color;
        return txt;
    }

    private static GameObject MakeButton(GameObject parent, string name, string label,
        Color bgColor, Color textColor, Vector2 anchoredPos, Vector2 sizeDelta,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(name);
        obj.layer = parent.layer;
        obj.AddComponent<RectTransform>();
        obj.transform.SetParent(parent.transform, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;

        Image img = obj.AddComponent<Image>();
        img.color = bgColor;
        Button btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white; cb.highlightedColor = new Color(0.88f, 0.88f, 0.88f);
        cb.pressedColor = new Color(0.70f, 0.70f, 0.70f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        MakeText(obj, "Label", label, 15, FontStyle.Bold, textColor,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return obj;
    }
}
