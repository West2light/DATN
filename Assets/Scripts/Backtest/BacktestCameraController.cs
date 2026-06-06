using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Camera tự do cho chế độ backtest: zoom (scroll wheel) và pan (RMB drag / WASD).
/// BacktestRunner gắn component này vào Main Camera sau khi scene load.
/// LateUpdate chạy SAU MapTankTestBootstrap.LateUpdate, nhưng vì trong backtest
/// player == null nên UpdateCameraPosition() của bootstrap là no-op.
/// </summary>
public class BacktestCameraController : MonoBehaviour
{
    // Zoom
    private float _zoomSpeed  = 0.12f;  // fraction per scroll unit
    private float _minOrtho   = 1.5f;
    private float _maxOrtho;

    // Pan (keyboard)
    private float _keyPanSpeed = 6f;    // world units / (s * orthoSize)

    // Map bounds
    private float _mapW, _mapH;

    // Drag state
    private bool    _dragging;
    private Vector3 _dragOriginWorld;

    private Camera _cam;

    public void Init(Camera cam, float mapWorldWidth, float mapWorldHeight)
    {
        _cam     = cam;
        _mapW    = mapWorldWidth;
        _mapH    = mapWorldHeight;
        _maxOrtho = Mathf.Max(mapWorldWidth, mapWorldHeight) / 2f + 2f;
    }

    private void LateUpdate()
    {
        if (_cam == null) return;
        HandleZoom();
        HandleDrag();
        HandleKeyPan();
        ClampCameraToMap();
    }

    // ── Zoom ────────────────────────────────────────────────────────────────
    private void HandleZoom()
    {
        // Skip zoom when mouse is over a UI element (e.g. the chart modal)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        // Zoom toward cursor position
        Vector3 worldBefore = _cam.ScreenToWorldPoint(Input.mousePosition);

        // scroll > 0 → zoom in (smaller ortho), scroll < 0 → zoom out
        _cam.orthographicSize = Mathf.Clamp(
            _cam.orthographicSize * (1f - scroll * _zoomSpeed * 10f),
            _minOrtho, _maxOrtho);

        // Adjust position so the world point under cursor stays fixed
        Vector3 worldAfter = _cam.ScreenToWorldPoint(Input.mousePosition);
        _cam.transform.position += worldBefore - worldAfter;
    }

    // ── RMB / MMB drag pan ──────────────────────────────────────────────────
    private void HandleDrag()
    {
        // Don't pan if mouse is over UI (e.g. the chart modal or overlay bar)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            _dragging = false;
            return;
        }

        bool btn = Input.GetMouseButton(1) || Input.GetMouseButton(2);
        bool down = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);

        if (down)
        {
            _dragOriginWorld = ScreenToWorld(Input.mousePosition);
            _dragging = true;
        }

        if (_dragging && btn)
        {
            Vector3 current = ScreenToWorld(Input.mousePosition);
            _cam.transform.position += _dragOriginWorld - current;
            // Recalculate origin after move so next frame delta is correct
            _dragOriginWorld = ScreenToWorld(Input.mousePosition);
        }

        if (!btn) _dragging = false;
    }

    // ── WASD / Arrow key pan ────────────────────────────────────────────────
    private void HandleKeyPan()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(h) < 0.01f && Mathf.Abs(v) < 0.01f) return;

        float speed = _keyPanSpeed * _cam.orthographicSize * Time.deltaTime;
        _cam.transform.position += new Vector3(h * speed, v * speed, 0f);
    }

    // ── Clamp to map bounds ─────────────────────────────────────────────────
    private void ClampCameraToMap()
    {
        float halfH = _cam.orthographicSize;
        float halfW = halfH * _cam.aspect;

        float minX = -_mapW / 2f + halfW;
        float maxX =  _mapW / 2f - halfW;
        float minY = -_mapH / 2f + halfH;
        float maxY =  _mapH / 2f - halfH;

        Vector3 pos = _cam.transform.position;
        pos.x = minX <= maxX ? Mathf.Clamp(pos.x, minX, maxX) : 0f;
        pos.y = minY <= maxY ? Mathf.Clamp(pos.y, minY, maxY) : 0f;
        _cam.transform.position = pos;
    }

    private Vector3 ScreenToWorld(Vector3 screenPos)
    {
        Vector3 w = _cam.ScreenToWorldPoint(screenPos);
        w.z = _cam.transform.position.z;
        return w;
    }
}
