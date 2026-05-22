using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MenuViewBootstrap : MonoBehaviour
{
    private const string MenuSceneName = "Menu";
    private const string MapTankTestSceneName = "MapF_TankTest";
    private static readonly Color BackgroundColor = new Color(0.08f, 0.16f, 0.22f, 1f);
    private static readonly Color PanelColor = new Color(0.10f, 0.25f, 0.32f, 0.88f);
    private static readonly Color ButtonColor = new Color(0.95f, 0.74f, 0.23f, 1f);
    private static readonly Color ButtonPressedColor = new Color(0.78f, 0.52f, 0.14f, 1f);
    private static readonly Color TextColor = new Color(0.98f, 0.98f, 0.94f, 1f);
    private static readonly Color MutedTextColor = new Color(0.70f, 0.86f, 0.88f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyIfMenuScene(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyIfMenuScene(scene);
    }

    private static void ApplyIfMenuScene(Scene scene)
    {
        if (scene.name != MenuSceneName || Object.FindFirstObjectByType<MenuViewBootstrap>() != null)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject("MenuViewBootstrap");
        bootstrapObject.AddComponent<MenuViewBootstrap>().Apply();
    }

    private void Apply()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        ConfigureCanvas(canvas);
        ConfigureCamera();
        ConfigureExistingPanel(canvas.transform);
        CreateHeader(canvas.transform);
        ConfigureButtons();
    }

    private void ConfigureCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void ConfigureCamera()
    {
        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.backgroundColor = BackgroundColor;
        }
    }

    private void ConfigureExistingPanel(Transform canvasTransform)
    {
        RectTransform panel = canvasTransform.Find("Panel") as RectTransform;
        if (panel == null)
        {
            return;
        }

        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(720f, 430f);

        Image image = panel.GetComponent<Image>();
        if (image != null)
        {
            image.color = PanelColor;
        }
    }

    private void CreateHeader(Transform canvasTransform)
    {
        CreateText(
            canvasTransform,
            "MenuTitle",
            "TANK MAPF",
            48,
            FontStyle.Bold,
            TextColor,
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, 145f),
            new Vector2(560f, 64f));

        Transform subtitle = canvasTransform.Find("MenuSubtitle");
        if (subtitle != null)
        {
            Destroy(subtitle.gameObject);
        }
    }

    private void ConfigureButtons()
    {
        ConfigureButton("ContinueBtn", new Vector2(0f, 28f), "EXIT");
        ConfigureButton("StartBtn", new Vector2(0f, -82f), "START");
        ConfigureExitButtonAction();
        ConfigureStartButtonAction();
    }

    private void ConfigureExitButtonAction()
    {
        GameObject buttonObject = GameObject.Find("ContinueBtn");
        if (buttonObject == null || !buttonObject.TryGetComponent(out Button button))
        {
            return;
        }

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(ExitGame);
    }

    private void ExitGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ConfigureStartButtonAction()
    {
        GameObject buttonObject = GameObject.Find("StartBtn");
        if (buttonObject == null || !buttonObject.TryGetComponent(out Button button))
        {
            return;
        }

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(LoadMapTankTest);
    }

    private void LoadMapTankTest()
    {
        SceneManager.LoadScene(MapTankTestSceneName);
    }

    private void ConfigureButton(string name, Vector2 anchoredPosition, string label)
    {
        GameObject buttonObject = GameObject.Find(name);
        if (buttonObject == null)
        {
            return;
        }

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(300f, 64f);
        }

        Image image = buttonObject.GetComponent<Image>();
        if (image != null)
        {
            image.color = ButtonColor;
        }

        Button button = buttonObject.GetComponent<Button>();
        if (button != null)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = Color.Lerp(ButtonColor, Color.white, 0.18f);
            colors.pressedColor = ButtonPressedColor;
            colors.selectedColor = ButtonColor;
            colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.65f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        Text text = buttonObject.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.12f, 0.13f, 0.12f, 1f);
        }
    }

    private Text CreateText(
        Transform parent,
        string name,
        string value,
        int fontSize,
        FontStyle fontStyle,
        Color color,
        Vector2 anchor,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        Transform existing = parent.Find(name);
        GameObject textObject = existing != null ? existing.gameObject : new GameObject(name);
        textObject.layer = LayerMask.NameToLayer("UI");
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = textObject.AddComponent<RectTransform>();
        }

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = textObject.GetComponent<Text>();
        if (text == null)
        {
            text = textObject.AddComponent<Text>();
        }

        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;

        return text;
    }
}
