using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Trực quan hoá traffic flow của PIBT (chỉ ĐỌC PIBTPlanner._flow qua API read-only):
///  • Heatmap vertex_flow: tô ô theo mức "đông" (xanh lá → vàng → đỏ).
///  • Mũi tên op_flow: vẽ dòng có hướng trên từng cạnh; cạnh ĐỐI ĐẦU (head-on) tô đỏ.
///
/// Chỉ để debug/demo — KHÔNG can thiệp thuật toán, nên không ảnh hưởng kết quả backtest.
/// Vẽ bằng GL immediate-mode. Vì project dùng URP, GL được phát qua hook
/// RenderPipelineManager.endCameraRendering (có fallback OnRenderObject cho built-in RP)
/// để hiện trong Game view + ảnh chụp màn hình.
///
/// Phím tắt: F1 = heatmap, F2 = mũi tên, F3 = legend.
/// Chỉ có ý nghĩa ở scene dùng PIBT (A* không sinh _flow → HasFlow=false → không vẽ gì).
/// </summary>
[DefaultExecutionOrder(1000)]
public class PIBTFlowVisualizer : MonoBehaviour
{
    [Header("Refs")]
    public MapLoader mapLoader;

    [Header("Toggles")]
    public bool showHeatmap = true;
    public bool showArrows  = true;
    public bool showLegend  = true;
    public KeyCode toggleHeatmapKey = KeyCode.F1;
    public KeyCode toggleArrowsKey  = KeyCode.F2;
    public KeyCode toggleLegendKey  = KeyCode.F3;

    [Header("Style")]
    [Range(0f, 1f)] public float heatmapAlpha = 0.45f;
    [Range(0f, 1f)] public float arrowAlpha   = 0.9f;
    [Tooltip("Chiều dài mũi tên theo tỉ lệ tileSize.")]
    [Range(0.2f, 0.9f)] public float arrowLengthScale = 0.42f;
    [Tooltip("Số frame giữa 2 lần tính lại max flow (để chuẩn hoá màu).")]
    [Min(1)] public int refreshEveryFrames = 5;

    private Material _glMat;
    private int _maxVertexFlow = 1;
    private int _maxEdgeFlow = 1;
    private int _frameCounter;

    // Nguồn flow: mặc định đọc PIBTPlanner (scene PIBT C#); scene PIBT-TCP gán
    // TcpFlowTracker qua SetFlowSource vì _flow thật nằm trên server C++.
    private IFlowField _flow;

    /// <summary>Đặt nguồn flow để vẽ (PIBTPlanner-backed hoặc TcpFlowTracker).</summary>
    public void SetFlowSource(IFlowField source) => _flow = source;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (mapLoader == null) mapLoader = FindFirstObjectByType<MapLoader>();
        if (_flow == null) _flow = new PIBTPlannerFlowField();
        EnsureMaterial();
    }

    private void OnEnable()
    {
        if (GraphicsSettings.currentRenderPipeline != null)
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnDestroy()
    {
        if (_glMat != null) DestroyImmediate(_glMat);
    }

    private void EnsureMaterial()
    {
        if (_glMat != null) return;
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            Debug.LogWarning("[PIBTFlowVisualizer] Shader 'Hidden/Internal-Colored' not found — flow overlay disabled.");
            return;
        }
        _glMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _glMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _glMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _glMat.SetInt("_Cull", (int)CullMode.Off);
        _glMat.SetInt("_ZWrite", 0);
        _glMat.SetInt("_ZTest", (int)CompareFunction.Always);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleHeatmapKey)) showHeatmap = !showHeatmap;
        if (Input.GetKeyDown(toggleArrowsKey))  showArrows  = !showArrows;
        if (Input.GetKeyDown(toggleLegendKey))  showLegend  = !showLegend;

        if (++_frameCounter >= refreshEveryFrames)
        {
            _frameCounter = 0;
            RecomputeMaxima();
        }
    }

    // ── Render hooks (URP + built-in) ────────────────────────────────────────

    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam) => RenderGL(cam);

    // Fallback for the legacy built-in pipeline (SRP hook handles URP).
    private void OnRenderObject()
    {
        if (GraphicsSettings.currentRenderPipeline == null) RenderGL(Camera.current);
    }

    private void RenderGL(Camera cam)
    {
        if (cam == null || mapLoader == null || _flow == null || !_flow.HasFlow) return;
        if (!showHeatmap && !showArrows) return;
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;

        EnsureMaterial();
        if (_glMat == null) return;
        _glMat.SetPass(0);
        GL.PushMatrix();
        GL.modelview = cam.worldToCameraMatrix;
        GL.LoadProjectionMatrix(cam.projectionMatrix);

        if (showHeatmap) DrawHeatmap();
        if (showArrows)  DrawArrows();

        GL.PopMatrix();
    }

    // ── Layer A: heatmap vertex_flow ─────────────────────────────────────────

    private void DrawHeatmap()
    {
        int x0 = mapLoader.BuildStartX, y0 = mapLoader.BuildStartY;
        int w = mapLoader.BuildWidth, h = mapLoader.BuildHeight;
        float half = mapLoader.tileSize * 0.5f;

        GL.Begin(GL.QUADS);
        for (int ry = 0; ry < h; ry++)
        {
            for (int rx = 0; rx < w; rx++)
            {
                var cell = new Vector2Int(x0 + rx, y0 + ry);
                int v = _flow.GetVertexFlow(cell);
                if (v <= 0) continue;

                Color c = HeatColor((float)v / _maxVertexFlow);
                c.a = heatmapAlpha;
                GL.Color(c);

                Vector3 p = mapLoader.CellToWorld(cell);
                GL.Vertex3(p.x - half, p.y - half, 0f);
                GL.Vertex3(p.x - half, p.y + half, 0f);
                GL.Vertex3(p.x + half, p.y + half, 0f);
                GL.Vertex3(p.x + half, p.y - half, 0f);
            }
        }
        GL.End();
    }

    // ── Layer B: mũi tên op_flow ─────────────────────────────────────────────

    private void DrawArrows()
    {
        int x0 = mapLoader.BuildStartX, y0 = mapLoader.BuildStartY;
        int w = mapLoader.BuildWidth, h = mapLoader.BuildHeight;
        float tile = mapLoader.tileSize;
        float len = tile * arrowLengthScale;

        GL.Begin(GL.LINES);
        for (int ry = 0; ry < h; ry++)
        {
            for (int rx = 0; rx < w; rx++)
            {
                var cell = new Vector2Int(x0 + rx, y0 + ry);
                for (int d = 0; d < 4; d++)
                {
                    int e = _flow.GetEdgeFlow(cell, d);
                    if (e <= 0) continue;

                    Vector3 c0 = mapLoader.CellToWorld(cell);
                    Vector3 c1 = mapLoader.CellToWorld(cell + PIBTPlanner.DirToDelta(d));
                    Vector3 dir = (c1 - c0).normalized;
                    Vector3 perp = new Vector3(-dir.y, dir.x, 0f);

                    bool headOn = _flow.GetOpposingFlow(cell, d) > 0;
                    Color col = headOn
                        ? new Color(0.95f, 0.15f, 0.12f, arrowAlpha)                    // đối đầu = đỏ
                        : FlowArrowColor((float)e / _maxEdgeFlow);
                    GL.Color(col);

                    // Lệch nhẹ sang phải theo hướng đi để cạnh 2 chiều hiện 2 mũi tên song song.
                    Vector3 start = c0 + perp * (tile * 0.12f);
                    Vector3 end = start + dir * len;
                    GL.Vertex(start); GL.Vertex(end);

                    // Đầu mũi tên
                    Vector3 back = -dir * (len * 0.32f);
                    Vector3 side = perp * (len * 0.22f);
                    GL.Vertex(end); GL.Vertex(end + back + side);
                    GL.Vertex(end); GL.Vertex(end + back - side);
                }
            }
        }
        GL.End();
    }

    // ── Normalisation ────────────────────────────────────────────────────────

    private void RecomputeMaxima()
    {
        if (mapLoader == null || _flow == null || !_flow.HasFlow) return;
        int x0 = mapLoader.BuildStartX, y0 = mapLoader.BuildStartY;
        int w = mapLoader.BuildWidth, h = mapLoader.BuildHeight;
        int maxV = 1, maxE = 1;
        for (int ry = 0; ry < h; ry++)
        {
            for (int rx = 0; rx < w; rx++)
            {
                var cell = new Vector2Int(x0 + rx, y0 + ry);
                int v = _flow.GetVertexFlow(cell);
                if (v > maxV) maxV = v;
                for (int d = 0; d < 4; d++)
                {
                    int e = _flow.GetEdgeFlow(cell, d);
                    if (e > maxE) maxE = e;
                }
            }
        }
        _maxVertexFlow = maxV;
        _maxEdgeFlow = maxE;
    }

    private static Color HeatColor(float t)
    {
        t = Mathf.Clamp01(t);
        Color green  = new Color(0.15f, 0.80f, 0.25f);
        Color yellow = new Color(1.00f, 0.90f, 0.10f);
        Color red    = new Color(0.95f, 0.15f, 0.10f);
        return t < 0.5f ? Color.Lerp(green, yellow, t * 2f)
                        : Color.Lerp(yellow, red, (t - 0.5f) * 2f);
    }

    private Color FlowArrowColor(float t)
    {
        Color lo = new Color(0.35f, 0.75f, 1.00f);   // ít = xanh nhạt
        Color hi = new Color(1.00f, 0.55f, 0.10f);   // nhiều = cam
        Color c = Color.Lerp(lo, hi, Mathf.Clamp01(t));
        c.a = arrowAlpha;
        return c;
    }

    // ── Legend (OnGUI — hoạt động cả URP lẫn built-in) ───────────────────────

    private void OnGUI()
    {
        if (!showLegend) return;

        const int pad = 10, w = 232, lineH = 18;
        var box = new Rect(pad, pad, w, lineH * 6 + 16);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float y = box.y + 8;
        GUI.Label(new Rect(box.x + 8, y, w - 16, lineH), "PIBT Flow  (F1 heat • F2 arrows • F3 legend)");
        y += lineH + 2;

        // Thanh gradient vertex_flow
        int bars = 40;
        float bw = (w - 16f) / bars;
        for (int i = 0; i < bars; i++)
        {
            GUI.color = HeatColor(i / (float)(bars - 1));
            GUI.DrawTexture(new Rect(box.x + 8 + i * bw, y, bw + 1, 10), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
        y += 12;
        GUI.Label(new Rect(box.x + 8, y, w - 16, lineH), $"vertex_flow: ít → tắc (max={_maxVertexFlow})");
        y += lineH;

        GUI.color = new Color(0.95f, 0.15f, 0.12f);
        GUI.DrawTexture(new Rect(box.x + 8, y + 5, 22, 4), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 36, y, w - 44, lineH), "op_flow: mũi tên đỏ = đối đầu");
        y += lineH;

        GUI.color = new Color(1f, 0.55f, 0.10f);
        GUI.DrawTexture(new Rect(box.x + 8, y + 5, 22, 4), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 36, y, w - 44, lineH), "mũi tên cam/xanh = dòng 1 chiều");
    }
}

/// <summary>
/// Nguồn traffic-flow cho PIBTFlowVisualizer. Trừu tượng hoá để cùng một visualizer
/// vẽ được cho cả PIBT C# (đọc PIBTPlanner._flow) lẫn PIBT-TCP (flow tái dựng client).
/// dir: 0=E(+x), 1=S(+y), 2=W(-x), 3=N(-y).
/// </summary>
public interface IFlowField
{
    bool HasFlow { get; }
    int GetVertexFlow(Vector2Int cell);
    int GetEdgeFlow(Vector2Int cell, int dir);
    int GetOpposingFlow(Vector2Int cell, int dir);
}

/// <summary>Nguồn flow đọc trực tiếp PIBTPlanner (scene PIBT C#).</summary>
public sealed class PIBTPlannerFlowField : IFlowField
{
    public bool HasFlow => PIBTPlanner.HasFlow;
    public int GetVertexFlow(Vector2Int cell) => PIBTPlanner.GetVertexFlow(cell);
    public int GetEdgeFlow(Vector2Int cell, int dir) => PIBTPlanner.GetEdgeFlow(cell, dir);
    public int GetOpposingFlow(Vector2Int cell, int dir) => PIBTPlanner.GetOpposingFlow(cell, dir);
}
