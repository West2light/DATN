using UnityEngine;

/// <summary>
/// Tái dựng traffic-flow field cho PIBT-TCP phía client. Trong mode TCP, thuật toán PIBT
/// (và _flow thật) chạy trên server C++ ngoài — Unity không có dữ liệu flow. Thành phần
/// này quan sát bước đi thực tế của agent mỗi server-tick (currentCell → nextCell) và cộng
/// dồn vào một lưới flow cạnh, với DECAY theo tick để heatmap phản ánh traffic GẦN ĐÂY
/// thay vì tích luỹ vĩnh viễn.
///
/// Cấp cùng interface IFlowField mà PIBTFlowVisualizer tiêu thụ → phím F1/F2/F3 hoạt động
/// giống hệt scene PIBT C#. Chỉ quan sát, KHÔNG can thiệp điều phối của server.
///
/// Lưu ý: đây là flow "hành vi quan sát được", không phải flow-assignment nội bộ của PIBT
/// như bản C#. Nhưng cùng ngữ nghĩa hiển thị: vertex_flow = ô đông, op_flow = cạnh đối đầu.
/// </summary>
public class TcpFlowTracker : MonoBehaviour, IFlowField
{
    [Tooltip("Hệ số giảm dần mỗi tick (càng thấp càng nhanh phai). 1 = tích luỹ mãi.")]
    [Range(0.5f, 0.98f)] public float decayPerTick = 0.85f;

    private int _cols, _rows;
    private float[] _flow;   // _flow[(y*cols + x) * 4 + dir]

    public void Init(MapLoader ml)
    {
        if (ml == null) return;
        _cols = ml.Width;
        _rows = ml.Height;
        _flow = new float[_cols * _rows * 4];
    }

    /// <summary>Gọi đầu mỗi server-tick trước khi RecordStep: phai dần flow cũ.</summary>
    public void BeginTick()
    {
        if (_flow == null) return;
        for (int i = 0; i < _flow.Length; i++) _flow[i] *= decayPerTick;
    }

    /// <summary>Ghi một bước di chuyển của agent (chỉ tính bước 4-hướng, bỏ wait/rotate).</summary>
    public void RecordStep(Vector2Int from, Vector2Int to)
    {
        if (_flow == null) return;
        int d = DirOf(to - from);
        if (d < 0) return;
        if (from.x < 0 || from.x >= _cols || from.y < 0 || from.y >= _rows) return;
        _flow[(from.y * _cols + from.x) * 4 + d] += 1f;
    }

    private static int DirOf(Vector2Int delta)
    {
        if (delta.x == 1 && delta.y == 0) return 0;   // E
        if (delta.x == 0 && delta.y == 1) return 1;   // S (+y = xuống, khớp CellToWorld)
        if (delta.x == -1 && delta.y == 0) return 2;  // W
        if (delta.x == 0 && delta.y == -1) return 3;  // N
        return -1;
    }

    // ── IFlowField ───────────────────────────────────────────────────────────

    public bool HasFlow => _flow != null;

    public int GetEdgeFlow(Vector2Int cell, int dir)
    {
        if (_flow == null || dir < 0 || dir > 3) return 0;
        if (cell.x < 0 || cell.x >= _cols || cell.y < 0 || cell.y >= _rows) return 0;
        return Mathf.RoundToInt(_flow[(cell.y * _cols + cell.x) * 4 + dir]);
    }

    public int GetVertexFlow(Vector2Int cell)
    {
        if (_flow == null) return 0;
        if (cell.x < 0 || cell.x >= _cols || cell.y < 0 || cell.y >= _rows) return 0;
        int b = (cell.y * _cols + cell.x) * 4;
        return Mathf.RoundToInt(_flow[b] + _flow[b + 1] + _flow[b + 2] + _flow[b + 3]);
    }

    public int GetOpposingFlow(Vector2Int cell, int dir)
    {
        int here = GetEdgeFlow(cell, dir);
        if (here == 0) return 0;
        return here * GetEdgeFlow(cell + PIBTPlanner.DirToDelta(dir), (dir + 2) & 3);
    }
}
