using UnityEngine;

public class HealthBarVisibilityController : MonoBehaviour
{
    [Range(0f, 1f)] public float visibleAlpha = 0.8f;
    [Min(0f)] public float hideDelay = 2f;

    private CanvasGroup canvasGroup;
    private float hideAt = -1f;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void Update()
    {
        if (hideAt >= 0f && Time.time >= hideAt)
        {
            HideNow();
        }
    }

    public void Show()
    {
        canvasGroup.alpha = visibleAlpha;
        hideAt = -1f;
    }

    public void ShowTemporarily()
    {
        canvasGroup.alpha = visibleAlpha;
        hideAt = Time.time + hideDelay;
    }

    public void HideNow()
    {
        canvasGroup.alpha = 0f;
        hideAt = -1f;
    }
}
