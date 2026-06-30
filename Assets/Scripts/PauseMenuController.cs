using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    public string menuSceneName = "Menu";
    public static bool IsLocalPauseActive { get; private set; }

    private bool _paused;
    private GameObject _pauseOverlay;

    private static readonly Color PanelBg  = new Color(0.06f, 0.07f, 0.10f, 0.97f);
    private static readonly Color BtnGreen = new Color(0.11f, 0.50f, 0.25f, 1f);
    private static readonly Color BtnDark  = new Color(0.16f, 0.17f, 0.20f, 1f);
    private static readonly Color AccentBlue = new Color(0.18f, 0.52f, 0.80f, 1f);

    public static PauseMenuController Ensure()
    {
        var existing = FindFirstObjectByType<PauseMenuController>();
        if (existing != null) return existing;
        return new GameObject("PauseMenuController").AddComponent<PauseMenuController>();
    }

    private void Start()
    {
        if (!LanSessionManager.IsDedicatedServer)
            CreatePauseButton();
    }

    private void Update()
    {
        if (LanSessionManager.IsDedicatedServer) return;
        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePause();
    }

    private void TogglePause()
    {
        if (_paused) Resume();
        else Pause();
    }

    public void Pause()
    {
        SetPaused(true, notifyNetwork: true);
    }

    public void Resume()
    {
        SetPaused(false, notifyNetwork: true);
    }

    public void ApplyNetworkPause(bool paused)
    {
        // Mirror the server pause locally so interpolation, bullet ghosts and other
        // client-only visuals freeze at the same instant as the authoritative world.
        Time.timeScale = paused ? 0f : 1f;
        SetPaused(paused, notifyNetwork: false);
    }

    private void SetPaused(bool paused, bool notifyNetwork)
    {
        if (_paused == paused) return;
        _paused = paused;
        IsLocalPauseActive = paused;

        if (LanSessionManager.IsActive)
        {
            if (notifyNetwork)
                LanNetworkBridge.Local?.SubmitGamePause(paused);
        }
        else
        {
            Time.timeScale = paused ? 0f : 1f;
        }

        if (paused)
            ShowModal();
        else if (_pauseOverlay != null)
        {
            Destroy(_pauseOverlay);
            _pauseOverlay = null;
        }
    }

    private void GoToMenu()
    {
        if (LanSessionManager.IsActive)
            LanNetworkBridge.Local?.SubmitGamePause(false);
        if (BacktestMode.IsActive)
        {
            BacktestRunner.Cleanup();
            BacktestMode.Deactivate();
        }
        IsLocalPauseActive = false;
        Time.timeScale = 1f;
        if (LanSessionManager.IsActive)
            LanLobbyController.CleanupSession();
        SceneManager.LoadScene(menuSceneName);
    }

    private void OnDestroy()
    {
        if (!_paused) return;
        IsLocalPauseActive = false;
        Time.timeScale = 1f;
    }

    // ── Nút tạm dừng góc trên phải ────────────────────────────────────────────

    private void CreatePauseButton()
    {
        int L = LayerMask.NameToLayer("UI");

        var canvasObj = new GameObject("PauseButtonHud");
        canvasObj.layer = L;
        var cv = canvasObj.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingLayerName = "UI"; cv.sortingOrder = 250;
        var sc = canvasObj.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        canvasObj.AddComponent<GraphicRaycaster>();

        var btnObj = new GameObject("PauseBtn");
        btnObj.layer = L;
        btnObj.transform.SetParent(canvasObj.transform, false);
        var rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -16f);
        rt.sizeDelta = new Vector2(48f, 48f);

        var img = btnObj.AddComponent<Image>();
        img.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);

        var btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.80f, 0.80f, 0.80f);
        cb.pressedColor = new Color(0.60f, 0.60f, 0.60f);
        btn.colors = cb;
        btn.onClick.AddListener(Pause);

        MakeLabel(btnObj, "Icon", "II", 20, FontStyle.Bold, Color.white,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    // ── Modal tạm dừng ────────────────────────────────────────────────────────

    private void ShowModal()
    {
        if (_pauseOverlay != null) return;

        int L = LayerMask.NameToLayer("UI");

        var canvasObj = new GameObject("PauseOverlay");
        _pauseOverlay = canvasObj;
        canvasObj.layer = L;
        var cv = canvasObj.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingLayerName = "UI"; cv.sortingOrder = 1000;
        var sc = canvasObj.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        canvasObj.AddComponent<GraphicRaycaster>();

        // Nền mờ toàn màn hình
        MakeStretch(canvasObj, "Dim", Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.72f));

        // Panel giữa màn hình
        var panel = new GameObject("Panel");
        panel.layer = L;
        panel.transform.SetParent(canvasObj.transform, false);
        var pRt = panel.AddComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
        pRt.pivot = new Vector2(0.5f, 0.5f);
        pRt.anchoredPosition = Vector2.zero;
        pRt.sizeDelta = new Vector2(400f, 260f);
        panel.AddComponent<Image>().color = PanelBg;

        // Thanh màu trên cùng
        MakeStretch(panel, "Bar",
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -5f), Vector2.zero, AccentBlue);

        // Tiêu đề
        MakeLabel(panel, "Title", "PAUSED", 38, FontStyle.Bold, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -36f), new Vector2(360f, 52f));

        // Đường kẻ ngang
        MakeStretch(panel, "Sep",
            new Vector2(0.1f, 1f), new Vector2(0.9f, 1f),
            new Vector2(0f, -98f), new Vector2(0f, -96f),
            new Color(1f, 1f, 1f, 0.12f));

        // Nút CHOI TIEP (trên)
        const float BtnW = 320f, BtnH = 52f, Gap = 12f, PadB = 32f;
        MakeButton(panel, "ResumeBtn", "RESUME",
            BtnGreen, Color.white,
            new Vector2(0f, PadB + BtnH + Gap), new Vector2(BtnW, BtnH), Resume);

        // Nút MENU (dưới)
        MakeButton(panel, "MenuBtn", "MENU",
            BtnDark, Color.white,
            new Vector2(0f, PadB), new Vector2(BtnW, BtnH), GoToMenu);
    }

    // ── Helpers UI (cùng pattern với MapWinController) ────────────────────────

    private static void MakeStretch(GameObject parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        int L = parent.layer;
        var obj = new GameObject(name); obj.layer = L;
        obj.transform.SetParent(parent.transform, false);
        var rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        obj.AddComponent<Image>().color = color;
    }

    private static void MakeLabel(GameObject parent, string name, string content,
        int fontSize, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        int L = parent.layer;
        var obj = new GameObject(name); obj.layer = L;
        obj.transform.SetParent(parent.transform, false);
        var rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = pivot; rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
        var txt = obj.AddComponent<Text>();
        txt.text = content;
        txt.font = UiFontProvider.GetDefaultFont();
        txt.fontSize = fontSize; txt.fontStyle = style;
        txt.alignment = TextAnchor.MiddleCenter; txt.color = color;
    }

    private static void MakeButton(GameObject parent, string name, string label,
        Color bgColor, Color textColor,
        Vector2 anchoredPos, Vector2 sizeDelta,
        UnityEngine.Events.UnityAction onClick)
    {
        int L = parent.layer;
        var obj = new GameObject(name); obj.layer = L;
        obj.transform.SetParent(parent.transform, false);
        var rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;

        var img = obj.AddComponent<Image>(); img.color = bgColor;
        var btn = obj.AddComponent<Button>(); btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.88f, 0.88f, 0.88f);
        cb.pressedColor = new Color(0.70f, 0.70f, 0.70f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        MakeLabel(obj, "Label", label, 20, FontStyle.Bold, textColor,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
    }
}
