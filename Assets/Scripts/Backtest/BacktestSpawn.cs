using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sinh danh sách ô spawn cho backtest theo SỐ LƯỢNG agent yêu cầu.
/// Deterministic: chỉ phụ thuộc kích thước map + count → cả 3 thuật toán
/// (A*/PIBT/PIBT-C++) nhận đúng cùng một tập ô, đảm bảo công bằng.
/// Các điểm rải đều quanh chu vi map (enemy bao vây Eagle ở trung tâm).
/// Mỗi điểm được nắn về ô walkable gần nhất; loop spawn của bootstrap
/// (TryFindAvailableSpawnNear) sẽ lo phần tránh trùng/đủ giãn cách.
/// </summary>
public static class BacktestSpawn
{
    public static List<Vector2Int> GenerateSpawnCells(MapLoader map, int count)
    {
        var cells = new List<Vector2Int>();
        if (map == null || count <= 0) return cells;

        const int inset = 1; // tránh sát viền / boundary collider
        int x0 = map.BuildStartX + inset;
        int y0 = map.BuildStartY + inset;
        int x1 = map.BuildStartX + map.BuildWidth  - 1 - inset;
        int y1 = map.BuildStartY + map.BuildHeight - 1 - inset;
        if (x1 <= x0 || y1 <= y0)
        {
            // Map quá nhỏ: fallback về tâm map.
            var c = new Vector2Int(map.BuildStartX + map.BuildWidth / 2,
                                   map.BuildStartY + map.BuildHeight / 2);
            for (int i = 0; i < count; i++) cells.Add(c);
            return cells;
        }

        int w = x1 - x0;            // chiều ngang khả dụng
        int h = y1 - y0;            // chiều dọc khả dụng
        int perim = 2 * (w + h);    // số bước đi quanh chu vi

        var seen = new HashSet<Vector2Int>();
        for (int i = 0; i < count; i++)
        {
            // Rải đều quanh chu vi (cộng offset 0.5 để không dồn 2 điểm vào 1 góc).
            int d = Mathf.RoundToInt(((i + 0.5f) / count) * perim) % perim;
            Vector2Int edge = PerimeterToCell(d, x0, y0, x1, y1, w, h);

            // Nắn về ô walkable gần nhất để seed luôn hợp lệ.
            if (map.TryFindWalkableNear(edge, out Vector2Int walk)) edge = walk;

            // Nếu trùng ô đã có, nhích quanh chu vi vài bước cho tới khi khác.
            int guard = 0;
            while (seen.Contains(edge) && guard < perim)
            {
                d = (d + 1) % perim;
                edge = PerimeterToCell(d, x0, y0, x1, y1, w, h);
                if (map.TryFindWalkableNear(edge, out Vector2Int w2)) edge = w2;
                guard++;
            }
            seen.Add(edge);
            cells.Add(edge);
        }
        return cells;
    }

    // d ∈ [0, perim): đi quanh hình chữ nhật biên
    // cạnh dưới (L→R) → cạnh phải (B→T) → cạnh trên (R→L) → cạnh trái (T→B)
    private static Vector2Int PerimeterToCell(int d, int x0, int y0, int x1, int y1, int w, int h)
    {
        if (d < w)    return new Vector2Int(x0 + d, y0);   // bottom
        d -= w;
        if (d < h)    return new Vector2Int(x1, y0 + d);   // right
        d -= h;
        if (d < w)    return new Vector2Int(x1 - d, y1);   // top
        d -= w;
        return new Vector2Int(x0, y1 - d);                 // left
    }
}
