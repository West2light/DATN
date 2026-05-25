using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MapWinController : MonoBehaviour
{
    public string menuSceneName = "Menu";

    private bool winStarted;

    private static readonly Color GoldColor = new Color(1f, 0.84f, 0f, 1f);

    public static MapWinController Ensure()
    {
        MapWinController existing = FindFirstObjectByType<MapWinController>();
        if (existing != null) return existing;
        return new GameObject("MapWinController").AddComponent<MapWinController>();
    }

    public void BeginWin()
    {
        if (winStarted) return;
        winStarted = true;
        ShowOverlay();
    }

    private void ShowOverlay()
    {
        GameObject canvasObj = new GameObject("WinHud");
        canvasObj.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 200;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Full-screen dark overlay
        MakeImage(canvasObj, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, 0.72f));

        // Centre panel
        GameObject panel = new GameObject("Panel");
        panel.layer = canvasObj.layer;
        panel.transform.SetParent(canvasObj.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(460f, 300f);
        panel.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.92f);

        // "VICTORY!" — anchored top-centre, offset 60px from top
        MakeLabel(panel, "VictoryText", "VICTORY!", 56, FontStyle.Bold, GoldColor,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -60f), new Vector2(440f, 72f));

        // Sub-message
        MakeLabel(panel, "SubText", "All enemies defeated!", 22, FontStyle.Normal, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -142f), new Vector2(400f, 32f));

        // Buttons — anchored bottom-centre
        string currentScene = SceneManager.GetActiveScene().name;
        MakeButton(panel, "PlayAgainBtn", "PLAY AGAIN", GoldColor, Color.black,
            new Vector2(0f, 88f), new Vector2(200f, 46f),
            () => SceneManager.LoadScene(currentScene));

        MakeButton(panel, "MainMenuBtn", "MAIN MENU", new Color(0.22f, 0.22f, 0.22f, 1f), Color.white,
            new Vector2(0f, 36f), new Vector2(200f, 46f),
            () => SceneManager.LoadScene(menuSceneName));
    }

    private static void MakeImage(GameObject parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.layer = parent.layer;
        obj.transform.SetParent(parent.transform, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
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
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        Text txt = obj.AddComponent<Text>();
        txt.text = content;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = color;
    }

    private void MakeButton(GameObject parent, string name, string label,
        Color bgColor, Color textColor, Vector2 anchoredPos, Vector2 sizeDelta,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.layer = parent.layer;
        btnObj.transform.SetParent(parent.transform, false);
        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0f);
        btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0f);
        btnRect.anchoredPosition = anchoredPos;
        btnRect.sizeDelta = sizeDelta;
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = bgColor;
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(onClick);

        MakeLabel(btnObj, "Label", label, 18, FontStyle.Bold, textColor,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
    }
}
