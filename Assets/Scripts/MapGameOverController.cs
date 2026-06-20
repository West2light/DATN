using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MapGameOverController : MonoBehaviour
{
    public string menuSceneName = "Menu";
    [Min(0f)] public float returnDelay = 2f;

    private bool gameOverStarted;
    private CanvasGroup overlayGroup;

    public static MapGameOverController Ensure()
    {
        MapGameOverController existing = FindFirstObjectByType<MapGameOverController>();
        if (existing != null)
        {
            return existing;
        }

        GameObject controllerObject = new GameObject("MapGameOverController");
        return controllerObject.AddComponent<MapGameOverController>();
    }

    public void BeginGameOver()
    {
        if (BacktestMode.IsActive) return;
        if (gameOverStarted) return;
        gameOverStarted = true;

        if (LanSessionManager.IsActive && LanSessionManager.IsServer)
        {
            // Server: broadcast to all clients, then show own overlay.
            LanGameCoordinator.Instance?.BroadcastGameOver();
            ShowOverlay();
            StartCoroutine(ReturnToMenuAfterDelay());
            return;
        }

        if (LanSessionManager.IsActive && !LanSessionManager.IsServer)
        {
            // Client: received game-over via BroadcastEventClientRpc — show overlay and return to menu.
            ShowOverlay();
            StartCoroutine(ReturnToMenuAfterDelay());
            return;
        }

        // Single-player / non-LAN mode.
        ShowOverlay();
        StartCoroutine(ReturnToMenuAfterDelay());
    }

    private IEnumerator ReturnToMenuAfterDelay()
    {
        Time.timeScale = 1f;
        yield return new WaitForSeconds(returnDelay);

        SaveSystem saveSystem = FindFirstObjectByType<SaveSystem>();
        if (saveSystem != null)
            saveSystem.ResetData();

        if (LanSessionManager.IsActive)
        {
            // Destroy NM + bridge GOs before loading menu so session 2 starts clean.
            LanLobbyController.CleanupSession();
            yield return null;
        }

        SceneManager.LoadScene(menuSceneName);
    }

    private void ShowOverlay()
    {
        if (overlayGroup != null)
        {
            overlayGroup.alpha = 1f;
            return;
        }

        GameObject canvasObject = new GameObject("GameOverHud");
        canvasObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 100;
        { var _sc = canvasObject.AddComponent<CanvasScaler>(); _sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; _sc.referenceResolution = new UnityEngine.Vector2(1280f, 720f); _sc.matchWidthOrHeight = 0.5f; }
        canvasObject.AddComponent<GraphicRaycaster>();

        overlayGroup = canvasObject.AddComponent<CanvasGroup>();
        overlayGroup.alpha = 1f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        GameObject backgroundObject = new GameObject("Background");
        backgroundObject.layer = LayerMask.NameToLayer("UI");
        backgroundObject.transform.SetParent(canvasObject.transform, false);

        RectTransform backgroundRect = backgroundObject.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        Image background = backgroundObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.65f);

        GameObject textObject = new GameObject("GameOverText");
        textObject.layer = LayerMask.NameToLayer("UI");
        textObject.transform.SetParent(canvasObject.transform, false);

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(420f, 90f);

        Text text = textObject.AddComponent<Text>();
        text.text = "GAME OVER";
        text.font = UiFontProvider.GetDefaultFont();
        text.fontSize = 48;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.red;
    }
}
