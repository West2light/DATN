using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Pre-game crate placement phase (triggered for Map 2 and Map 3).
/// Player can view the map, pan (right-drag), zoom (scroll wheel),
/// and place up to 2 wooden crates before the game starts.
/// </summary>
public class MapPlacementPhase : MonoBehaviour
{
    private const int MaxCrates = 2;

    public static readonly string[] EnabledMapFiles =
    {
        "ht_mansion_n.map",
        "ht_chantry.map",
    };

    // ── State ──────────────────────────────────────────────────────────────────
    private MapLoader    mapLoader;
    private Camera       mainCam;
    private Action       onDone;
    private PlayerInput  playerInput;
    private Transform    playerTransform;

    private GameObject     ghostObj;
    private SpriteRenderer ghostSr;

    private readonly List<GameObject>    placed      = new List<GameObject>();
    private readonly HashSet<Vector2Int> placedCells = new HashSet<Vector2Int>();

    private Text statusText;

    // camera nav
    private float minOrtho;
    private float maxOrtho;
    private Vector3 panStartWorld;
    private Vector3 panStartCamPos;
    private bool    isPanning;
    private Vector3 rightDownScreen;

    private const float DragThresholdPx = 6f;
    private const float ZoomSpeed       = 0.12f;

    // visuals
    private static readonly Color GhostOk   = new Color(0.25f, 1f, 0.25f, 0.52f);
    private static readonly Color GhostBad  = new Color(1f, 0.22f, 0.22f, 0.52f);
    private static readonly Color CrateTint = new Color(0.85f, 0.56f, 0.18f, 1f);

    // ── Public API ─────────────────────────────────────────────────────────────

    public static bool ShouldTrigger()
    {
        string sel = PlayerPrefs.GetString("SelectedMapFile", "");
        if (string.IsNullOrEmpty(sel)) return false;
        string fn = Path.GetFileName(sel);
        foreach (string m in EnabledMapFiles)
            if (fn == m) return true;
        return false;
    }

    public void BeginPhase(MapLoader loader, Camera cam, Action callback, Transform pTransform = null)
    {
        mapLoader       = loader;
        mainCam         = cam;
        onDone          = callback;
        playerTransform = pTransform;

        if (pTransform != null)
        {
            playerInput = pTransform.GetComponent<PlayerInput>();
            if (playerInput != null) playerInput.enabled = false;
        }

        SetupPlacementCamera();
        BuildGhost();
        BuildUI();
    }

    // ── Camera setup ───────────────────────────────────────────────────────────

    private void SetupPlacementCamera()
    {
        float mapW = mapLoader.BuildWidth  * mapLoader.tileSize;
        float mapH = mapLoader.BuildHeight * mapLoader.tileSize;

        float fullByH = mapH / 2f;
        float fullByW = mapW / (2f * mainCam.aspect);
        maxOrtho = Mathf.Max(fullByH, fullByW) * 0.95f;
        minOrtho = 3f;

        // Start at gameplay zoom (shows ~20 cells, not the full tiny map)
        mainCam.orthographicSize = Mathf.Clamp(10f, minOrtho, maxOrtho);

        // Centre on player or map centre
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

        Vector3 p = mainCam.transform.position;

        if (halfW * 2f >= mapW) p.x = 0f;
        else p.x = Mathf.Clamp(p.x, -mapW / 2f + halfW, mapW / 2f - halfW);

        if (halfH * 2f >= mapH) p.y = 0f;
        else p.y = Mathf.Clamp(p.y, -mapH / 2f + halfH, mapH / 2f - halfH);

        p.z = -10f;
        mainCam.transform.position = p;
    }

    // ── Ghost ──────────────────────────────────────────────────────────────────

    private Sprite LoadCrateSprite()
    {
#if UNITY_EDITOR
        string[] paths =
        {
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateWood_dark.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateWood.png",
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateMetal.png",
        };
        foreach (string p in paths)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            if (s != null) return s;
        }
#endif
        return null;
    }

    private void BuildGhost()
    {
        ghostObj = new GameObject("PlacementGhost");
        ghostSr  = ghostObj.AddComponent<SpriteRenderer>();
        ghostSr.sprite       = LoadCrateSprite();
        ghostSr.sortingOrder = 10;
        ghostSr.color        = GhostOk;
        ghostObj.transform.localScale = Vector3.one * mapLoader.tileSize;
        ghostObj.SetActive(false);
    }

    // ── UI ─────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        int uiLayer = LayerMask.NameToLayer("UI");

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

        // ── Top bar ───────────────────────────────────────────────────────────
        GameObject topBar = MakeRt(canvasObj, "TopBar", uiLayer);
        RectTransform tbRt = topBar.GetComponent<RectTransform>();
        tbRt.anchorMin        = new Vector2(0f, 1f);
        tbRt.anchorMax        = new Vector2(1f, 1f);
        tbRt.pivot            = new Vector2(0.5f, 1f);
        tbRt.anchoredPosition = Vector2.zero;
        tbRt.sizeDelta        = new Vector2(0f, 76f);
        topBar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        MakeText(topBar, "Hint1",
            "Đặt tối đa 2 thùng gỗ để bảo vệ Base  |  Chuột trái: Đặt  •  Chuột phải: Xóa",
            16, FontStyle.Normal, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(960f, 24f));

        MakeText(topBar, "Hint2",
            "Giữ chuột phải kéo: Di chuyển  •  Cuộn chuột: Zoom",
            14, FontStyle.Normal, new Color(0.72f, 0.72f, 0.72f, 1f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -4f), new Vector2(700f, 20f));

        statusText = MakeText(topBar, "Status",
            $"Đã đặt: 0 / {MaxCrates}",
            15, FontStyle.Normal, new Color(1f, 0.84f, 0f, 1f),
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-20f, 0f), new Vector2(180f, 22f));

        // ── Bottom button bar ─────────────────────────────────────────────────
        GameObject botBar = MakeRt(canvasObj, "BotBar", uiLayer);
        RectTransform bbRt = botBar.GetComponent<RectTransform>();
        bbRt.anchorMin        = new Vector2(0.5f, 0f);
        bbRt.anchorMax        = new Vector2(0.5f, 0f);
        bbRt.pivot            = new Vector2(0.5f, 0f);
        bbRt.anchoredPosition = Vector2.zero;
        bbRt.sizeDelta        = new Vector2(560f, 64f);
        botBar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        MakeButton(botBar, "StartBtn", "BẮT ĐẦU",
            new Color(0.11f, 0.50f, 0.25f, 1f), Color.white,
            new Vector2(-142f, 32f), new Vector2(250f, 44f),
            () => Finish(false, canvasObj));

        MakeButton(botBar, "SkipBtn", "BỎ QUA (không đặt)",
            new Color(0.18f, 0.18f, 0.20f, 1f), new Color(0.78f, 0.78f, 0.78f, 1f),
            new Vector2(142f, 32f), new Vector2(250f, 44f),
            () => Finish(true, canvasObj));
    }

    // ── Update: camera nav + placement ────────────────────────────────────────

    private void Update()
    {
        HandleZoom();
        HandlePan();
        UpdateGhost();

        if (Input.GetMouseButtonDown(0))
            TryPlace();
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        // Zoom toward mouse cursor
        Vector3 worldBefore = mainCam.ScreenToWorldPoint(Input.mousePosition);
        mainCam.orthographicSize = Mathf.Clamp(
            mainCam.orthographicSize * (1f - scroll * ZoomSpeed * 10f),
            minOrtho, maxOrtho);
        Vector3 worldAfter = mainCam.ScreenToWorldPoint(Input.mousePosition);

        Vector3 pos = mainCam.transform.position + (worldBefore - worldAfter);
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
            float moved = Vector3.Distance(Input.mousePosition, rightDownScreen);
            if (!isPanning && moved > DragThresholdPx)
                isPanning = true;

            if (isPanning)
            {
                Vector3 cur  = mainCam.ScreenToWorldPoint(Input.mousePosition);
                Vector3 diff = panStartWorld - cur;
                Vector3 pos  = panStartCamPos + diff;
                pos.z = -10f;
                mainCam.transform.position = pos;
                ClampCamera();
            }
        }

        if (Input.GetMouseButtonUp(1) && !isPanning)
            TryRemove();
    }

    // ── Placement ──────────────────────────────────────────────────────────────

    private void UpdateGhost()
    {
        Vector3 wp = mainCam.ScreenToWorldPoint(Input.mousePosition);
        wp.z = 0f;
        Vector2Int cell = mapLoader.WorldToCell(wp);
        bool ok = placed.Count < MaxCrates
               && mapLoader.IsWalkable(cell)
               && !placedCells.Contains(cell);
        ghostObj.SetActive(true);
        ghostObj.transform.position = mapLoader.CellToWorld(cell);
        ghostSr.color = ok ? GhostOk : GhostBad;
    }

    private void TryPlace()
    {
        Vector3 wp = mainCam.ScreenToWorldPoint(Input.mousePosition);
        wp.z = 0f;
        Vector2Int cell = mapLoader.WorldToCell(wp);
        if (placed.Count >= MaxCrates)         return;
        if (!mapLoader.IsWalkable(cell))        return;
        if (placedCells.Contains(cell))         return;
        SpawnCrate(cell);
    }

    private void TryRemove()
    {
        Vector3 wp = mainCam.ScreenToWorldPoint(Input.mousePosition);
        wp.z = 0f;
        Vector2Int cell = mapLoader.WorldToCell(wp);
        for (int i = placed.Count - 1; i >= 0; i--)
        {
            if (mapLoader.WorldToCell(placed[i].transform.position) != cell) continue;
            mapLoader.UnmarkCellBlocked(cell);
            Destroy(placed[i]);
            placed.RemoveAt(i);
            placedCells.Remove(cell);
            RefreshStatus();
            return;
        }
    }

    private void SpawnCrate(Vector2Int cell)
    {
        int wallLayer = LayerMask.NameToLayer("Walls");
        if (wallLayer < 0) wallLayer = LayerMask.NameToLayer("ObstaclesMovement");

        GameObject crate = new GameObject($"WoodCrate_{cell.x}_{cell.y}");
        crate.layer                = wallLayer;
        crate.transform.position   = mapLoader.CellToWorld(cell);
        crate.transform.localScale = Vector3.one * mapLoader.tileSize;

        SpriteRenderer sr = crate.AddComponent<SpriteRenderer>();
        sr.sprite       = LoadCrateSprite();
        sr.color        = CrateTint;
        sr.sortingOrder = 3;

        crate.AddComponent<BoxCollider2D>().size = Vector2.one;

        GameObject hb = new GameObject("BulletHitbox");
        hb.layer = LayerMask.NameToLayer("Hittable");
        hb.transform.SetParent(crate.transform, false);
        BoxCollider2D hbCol = hb.AddComponent<BoxCollider2D>();
        hbCol.isTrigger = true;
        hbCol.size      = Vector2.one;

        placed.Add(crate);
        placedCells.Add(cell);
        mapLoader.MarkCellBlocked(cell);
        RefreshStatus();
    }

    private void Finish(bool clearAll, GameObject uiRoot)
    {
        if (clearAll)
        {
            foreach (GameObject c in placed)
            {
                mapLoader.UnmarkCellBlocked(mapLoader.WorldToCell(c.transform.position));
                Destroy(c);
            }
            placed.Clear();
            placedCells.Clear();
        }

        if (ghostObj != null) Destroy(ghostObj);
        if (uiRoot   != null) Destroy(uiRoot);
        if (playerInput != null) playerInput.enabled = true;

        onDone?.Invoke();
        Destroy(this);
    }

    private void RefreshStatus()
    {
        if (statusText != null)
            statusText.text = $"Đã đặt: {placed.Count} / {MaxCrates}";
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
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;
        Text txt = obj.AddComponent<Text>();
        txt.text      = content;
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = fontSize;
        txt.fontStyle = style;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color     = color;
        return txt;
    }

    private static void MakeButton(GameObject parent, string name, string label,
        Color bgColor, Color textColor, Vector2 anchoredPos, Vector2 sizeDelta,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(name);
        obj.layer = parent.layer;
        obj.AddComponent<RectTransform>();
        obj.transform.SetParent(parent.transform, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;

        Image img = obj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        cb.pressedColor     = new Color(0.70f, 0.70f, 0.70f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        MakeText(obj, "Label", label, 16, FontStyle.Bold, textColor,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
    }
}
