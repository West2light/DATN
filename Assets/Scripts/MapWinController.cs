using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MapWinController : MonoBehaviour
{
    public string menuSceneName  = "Menu";
    public string astarSceneName = "MapF_TankTest";
    public string pibtSceneName  = "MapF_TankTest_PIBT";

    private bool winStarted;

    private static readonly Color GoldColor  = new Color(1f, 0.84f, 0f, 1f);
    private static readonly Color PanelBg    = new Color(0.06f, 0.07f, 0.10f, 0.97f);
    private static readonly Color BtnDark    = new Color(0.16f, 0.17f, 0.20f, 1f);
    private static readonly Color BtnGreen   = new Color(0.11f, 0.50f, 0.25f, 1f);

    public static MapWinController Ensure()
    {
        MapWinController existing = FindFirstObjectByType<MapWinController>();
        if (existing != null) return existing;
        return new GameObject("MapWinController").AddComponent<MapWinController>();
    }

    public void BeginWin()
    {
        if (BacktestMode.IsActive) return;
        if (winStarted) return;
        winStarted = true;

        if (LanSessionManager.IsActive && LanSessionManager.IsServer)
        {
            // Server: broadcast victory to all clients, then show own overlay.
            LanGameCoordinator.Instance?.BroadcastWin();
            ShowOverlay();
            return;
        }

        if (LanSessionManager.IsActive && !LanSessionManager.IsServer)
        {
            // Client: received win via BroadcastEventClientRpc — show overlay with navigation buttons.
            ShowOverlay();
            return;
        }

        // Single-player / non-LAN mode.
        ShowOverlay();
    }

    private void ShowOverlay()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        // In LAN mode we can't reload the scene directly (NM manages scene loading);
        // hide the replay button and only offer returning to the lobby/menu.
        bool isLan = LanSessionManager.IsActive;
        
        string nextScene = "";
        string nextSceneLabel = "";

        if (!isLan)
        {
            if (currentScene == "Lvl1") { nextScene = "Lvl2"; nextSceneLabel = "CONTINUE  ▶  LEVEL 2"; }
            else if (currentScene == "Lvl2") { nextScene = astarSceneName; nextSceneLabel = "CONTINUE  ▶  ASTAR MODE"; }
            else if (currentScene == astarSceneName) { nextScene = pibtSceneName; nextSceneLabel = "CONTINUE  ▶  PIBT MODE"; }
        }

        bool showContinue = !string.IsNullOrEmpty(nextScene);
        float panelH = showContinue ? 440f : 380f;

        // ── Canvas ─────────────────────────────────────────────────────────────
        GameObject canvasObj = new GameObject("WinHud");
        canvasObj.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 200;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        canvasObj.AddComponent<GraphicRaycaster>();

        // Full-screen dim
        MakeStretch(canvasObj, "Dim",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, 0.78f));

        // ── Panel ──────────────────────────────────────────────────────────────
        GameObject panel = new GameObject("Panel");
        panel.layer = canvasObj.layer;
        panel.transform.SetParent(canvasObj.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot     = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(520f, panelH);
        panel.AddComponent<Image>().color = PanelBg;

        // Gold accent bar at top
        MakeStretch(panel, "GoldBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -8f), new Vector2(0f, 0f),
            GoldColor);

        // Determine text based on mode
        string mainTitle = "VICTORY!";
        string subTitle = "All enemies have been eliminated!";

        if (isLan)
        {
            mainTitle = "TEAM VICTORY!";
            subTitle = "Your squad has successfully cleared the sector in Co-op mode!";
        }
        else if (currentScene == "Lvl1")
        {
            mainTitle = "LEVEL 1 COMPLETE!";
            subTitle = "You have defended the base successfully. Prepare for the next wave!";
        }
        else if (currentScene == "Lvl2")
        {
            mainTitle = "LEVEL 2 CLEARED!";
            subTitle = "Outstanding performance! The enemy forces have been pushed back.";
        }
        else if (currentScene == astarSceneName) // A* Mode
        {
            mainTitle = "SECTOR CLEARED!";
            subTitle = "Great job! All hostile units eliminated.\nReady to tackle the advanced PIBT mode?";
        }
        else if (currentScene == pibtSceneName) // PIBT Mode
        {
            mainTitle = "PIBT DOMINATION!";
            subTitle = "Excellent coordination! The enemy swarm has been decisively defeated.";
        }

        // "VICTORY!"  (anchor top-centre)
        MakeLabel(panel, "VictoryText", mainTitle, 56, FontStyle.Bold, GoldColor,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -44f), new Vector2(480f, 82f));

        // Subtitle
        MakeLabel(panel, "SubText", subTitle, 19, FontStyle.Italic,
            new Color(0.72f, 0.72f, 0.72f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -140f), new Vector2(480f, 60f));

        // ── Buttons (anchor bottom-centre, pivot bottom) ───────────────────────
        const float BtnW = 400f, BtnH = 52f, BtnGap = 12f, PadBottom = 30f;

        void NavigateToMenu()
        {
            if (LanSessionManager.IsActive)
                LanLobbyController.CleanupSession();
            SceneManager.LoadScene(menuSceneName);
        }

        if (showContinue)
        {
            float y1 = PadBottom;
            float y2 = y1 + BtnH + BtnGap;
            float y3 = y2 + BtnH + BtnGap;

            MakeButton(panel, "PlayAgainBtn", "PLAY AGAIN", GoldColor, Color.black,
                new Vector2(0f, y3), new Vector2(BtnW, BtnH),
                () => SceneManager.LoadScene(currentScene));

            MakeButton(panel, "ContinueBtn", nextSceneLabel, BtnGreen, Color.white,
                new Vector2(0f, y2), new Vector2(BtnW, BtnH),
                () => SceneManager.LoadScene(nextScene));

            MakeButton(panel, "MainMenuBtn", "MAIN MENU", BtnDark, Color.white,
                new Vector2(0f, y1), new Vector2(BtnW, BtnH),
                NavigateToMenu);
        }
        else if (isLan)
        {
            // LAN: only offer menu return — replaying requires a new lobby session.
            MakeButton(panel, "MainMenuBtn", "MAIN MENU", BtnDark, Color.white,
                new Vector2(0f, PadBottom), new Vector2(BtnW, BtnH),
                NavigateToMenu);
        }
        else
        {
            float y1 = PadBottom;
            float y2 = y1 + BtnH + BtnGap;

            MakeButton(panel, "PlayAgainBtn", "PLAY AGAIN", GoldColor, Color.black,
                new Vector2(0f, y2), new Vector2(BtnW, BtnH),
                () => SceneManager.LoadScene(currentScene));

            MakeButton(panel, "MainMenuBtn", "MAIN MENU", BtnDark, Color.white,
                new Vector2(0f, y1), new Vector2(BtnW, BtnH),
                NavigateToMenu);
        }
    }

    private static void MakeStretch(GameObject parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.layer = parent.layer;
        obj.transform.SetParent(parent.transform, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = offsetMin;
        rt.offsetMax  = offsetMax;
        obj.AddComponent<Image>().color = color;
    }

    private static void MakeLabel(GameObject parent, string name, string content,
        int fontSize, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject obj = new GameObject(name);
        obj.layer = parent.layer;
        obj.transform.SetParent(parent.transform, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;
        Text txt = obj.AddComponent<Text>();
        txt.text      = content;
        txt.font      = UiFontProvider.GetDefaultFont();
        txt.fontSize  = fontSize;
        txt.fontStyle = style;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color     = color;
    }

    private void MakeButton(GameObject parent, string name, string label,
        Color bgColor, Color textColor,
        Vector2 anchoredPos, Vector2 sizeDelta,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.layer = parent.layer;
        btnObj.transform.SetParent(parent.transform, false);
        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.anchorMin        = new Vector2(0.5f, 0f);
        btnRect.anchorMax        = new Vector2(0.5f, 0f);
        btnRect.pivot            = new Vector2(0.5f, 0f);
        btnRect.anchoredPosition = anchoredPos;
        btnRect.sizeDelta        = sizeDelta;

        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        ColorBlock cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        cb.pressedColor     = new Color(0.70f, 0.70f, 0.70f, 1f);
        cb.selectedColor    = Color.white;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        MakeLabel(btnObj, "Label", label, 18, FontStyle.Bold, textColor,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
    }
}
