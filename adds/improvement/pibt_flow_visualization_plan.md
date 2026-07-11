# Plan: Trực quan hoá traffic flow của PIBT (op_flow & vertex_flow)

> Mục tiêu: (1) xác nhận `room-32-32-4.map` là map bottleneck để test A* vs PIBT,
> (2) dựng lớp hiển thị trực quan cho hai thành phần traffic của PIBT — `op_flow`
> (né ngược chiều) và `vertex_flow` (né ô đông) — phục vụ demo/so sánh trong báo cáo.

---

## 1. Kết luận phân tích `room-32-32-4.map`

| Chỉ số | Giá trị | Ý nghĩa |
|---|---|---|
| Kích thước | 32×32 = 1024 ô | — |
| Ô đi được | 682 (67%) | Phòng khá thoáng |
| Tường | 342 | Vách ngăn phòng |
| Cửa nối phòng | **51 cửa, tất cả rộng đúng 1 ô** | Bottleneck 1 ô |
| Ô "thắt" (chỉ 1 trục) | 106 | Cửa + hành lang hẹp |
| Bậc 1 / 2 / 3 / 4 | 16 / 237 / 278 / 151 | Nhiều ngõ cụt + chokepoint |

**Kết luận:** map gồm các phòng thoáng nối với nhau **chỉ qua cửa hẹp 1 ô**. Khi
nhiều agent cùng hướng về Eagle, chúng bị dồn qua cùng vài cửa → tắc nghẽn. Đây
chính là điều kiện làm lộ khác biệt:
- **A\*** (`GridAStarPathfinder`): mỗi agent tìm đường ngắn nhất độc lập, không biết
  agent khác → tất cả chọn cùng cửa gần nhất → kẹt, va chạm, phải recovery nhiều.
- **PIBT** (`PIBTPlanner`): `_flow` ghi lại đường của mọi agent, `op_flow` + `vertex_flow`
  phạt cửa đang đông → agent tự tản sang cửa khác → thông suốt hơn.

> Gợi ý test: spawn ≥ 8–12 enemy ở các phòng xa, cùng goal quanh Eagle, để lưu
> lượng dồn qua cửa. So sánh `btReplanCount`, `btRecoveryCount`, thời gian tới đích
> giữa scene A* (`MapF_TankTest`) và scene PIBT (`MapF_TankTest_PIBT`).

---

## 2. Hai traffic term thực chất là gì (đính chính nhẹ)

Trích từ `PIBTPlanner.AStarFlow` — `priority = g + h + cumOpFlow + cumVertexFlow`:

- Mã hoá hướng `d`: `0=Đông(+x)`, `1=Nam(+y)`, `2=Tây(-x)`, `3=Bắc(-y)`.
- `_flow[cell*4 + d]` = số trajectory đang dùng **cạnh** `cell → d`.

### op_flow — phạt đi NGƯỢC chiều dòng (head-on)
```
opEdge = (flow[from→d] + 1) × flow[to→ (d+2)%4]
```
`flow[to→(d+2)%4]` là số agent đang đi **ngược lại** trên đúng cạnh đó. Nếu ta đi
`from→to` mà có agent đang đi `to→from` ⇒ tích lớn ⇒ phạt nặng ⇒ PIBT tránh đâm đầu
đối đầu trong cửa hẹp. → **Đúng như bạn nói: "né ngược chiều".**

### vertex_flow — phạt đi qua ô ĐÔNG (congestion)
```
vEdge = (Σ_{d=0..3} flow[to→d]) / 2
```
Đây là **tổng lưu lượng đi ra khỏi ô `to` theo mọi hướng**, tức mức "đông đúc" của ô,
**không phân biệt chiều**. Nên nói chính xác là "phạt ô đang tắc" hơn là "cùng chiều".
→ Trong doc/demo nên gọi là **vertex_flow = mật độ giao thông tại ô**.

Hai term này chỉ tồn tại ở PIBT; A* không có gì tương đương → đó là thứ cần vẽ ra.

---

## 3. Thiết kế trực quan hoá

Hai lớp overlay vẽ **đè lên map trong Game view** (để chụp ảnh đưa vào báo cáo), có
phím bật/tắt:

### Lớp A — Heatmap `vertex_flow` (ô đông)
- Với mỗi ô: `vertexFlow(cell) = Σ_d flow[cell*4+d]`.
- Chuẩn hoá theo `maxVertexFlow` hiện tại → màu ramp **xanh lá → vàng → đỏ**.
- Vẽ bằng một `Texture2D` cỡ `buildWidth × buildHeight`, đặt lên 1 `SpriteRenderer`
  bán trong suốt (alpha ~0.5) phủ đúng vùng build. Cập nhật mỗi ~5 frame.
- Ý nghĩa demo: **đỏ = cửa/ô đang tắc**. Thấy ngay PIBT dồn hay tản lưu lượng.

### Lớp B — Mũi tên `op_flow` (dòng có hướng + đối đầu)
- Với mỗi cạnh `cell→d` có `flow>0`: vẽ mũi tên theo hướng `d`, độ đậm/độ dài ∝ flow.
- Cạnh **đối đầu** (`flow[cell→d]>0` **và** `flow[neighbor→(d+2)%4]>0`): tô **màu đỏ**
  để lộ đúng chỗ `op_flow` đang phạt (head-on trong cửa hẹp).
- Cách vẽ: `GL` immediate-mode trong `OnRenderObject`, hoặc pool sprite mũi tên. Ưu
  tiên `GL` (nhẹ, không tạo GameObject).

### Lớp C (tùy chọn) — Overlay số
- In `vertexFlow` / `opFlow` dạng text nhỏ trên vài ô chokepoint (dùng `Handles.Label`
  trong Editor hoặc `TextMeshPro` runtime). Chỉ bật khi debug sâu.

---

## 4. Các bước triển khai

### Bước 1 — Expose dữ liệu flow (read-only) trong `PIBTPlanner.cs`
`_flow` đang `private static`. Thêm API chỉ-đọc, không lộ mảng gốc:
```csharp
public static bool HasFlow => IsReady && _flow != null;
public static int GridCols => _cols;
public static int GridRows => _rows;

// Lưu lượng trên cạnh cell→dir (dir: 0=E,1=S,2=W,3=N)
public static int GetEdgeFlow(Vector2Int cell, int dir)
{
    if (!HasFlow) return 0;
    int f = cell.y * _cols + cell.x;
    if (f < 0 || f >= _size || dir < 0 || dir > 3) return 0;
    return _flow[f * 4 + dir];
}

// vertex_flow: tổng lưu lượng đi ra khỏi ô (mức đông)
public static int GetVertexFlow(Vector2Int cell)
{
    if (!HasFlow) return 0;
    int f = cell.y * _cols + cell.x;
    if (f < 0 || f >= _size) return 0;
    int s = 0;
    for (int d = 0; d < 4; d++) s += _flow[f * 4 + d];
    return s;
}

// op_flow (độ đối đầu) trên cạnh cell→dir = flow[cell→d] * flow[neighbor→opposite]
public static int GetOpposingFlow(Vector2Int cell, int dir)
{
    if (!HasFlow || dir < 0 || dir > 3) return 0;
    Vector2Int nb = cell + DirDelta(dir);
    if (nb.x < 0 || nb.x >= _cols || nb.y < 0 || nb.y >= _rows) return 0;
    return GetEdgeFlow(cell, dir) * GetEdgeFlow(nb, (dir + 2) % 4);
}

private static Vector2Int DirDelta(int d) => d switch {
    0 => new Vector2Int(1, 0), 1 => new Vector2Int(0, 1),
    2 => new Vector2Int(-1, 0), _ => new Vector2Int(0, -1) };
```
> Lưu ý toạ độ: PIBTPlanner dùng flat = `y*cols + x` trên **toàn grid** (không phải
> build-local). Visualizer phải truyền cell theo world-grid như `MapLoader` dùng.

### Bước 2 — Component `PIBTFlowVisualizer.cs` (mới, `Assets/Scripts/Debug/`)
- Fields: `MapLoader mapLoader;` `bool showHeatmap=true;` `bool showArrows=true;`
  `KeyCode toggleHeatmap=KeyCode.F1;` `KeyCode toggleArrows=KeyCode.F2;`
  `Gradient heatGradient;` `float alpha=0.5f;` `int refreshEveryFrames=5;`
- `BuildHeatmapTexture()`: quét vùng build, gọi `PIBTPlanner.GetVertexFlow`, map qua
  gradient, ghi `Texture2D`, gán vào `SpriteRenderer` overlay (tạo runtime, phủ build
  window, `sortingOrder` cao hơn tile).
- `OnRenderObject()`: nếu `showArrows`, duyệt vùng build, với mỗi cạnh flow>0 vẽ `GL`
  line + đầu mũi tên; cạnh đối đầu (`GetOpposingFlow>0`) vẽ đỏ.
- `Update()`: xử lý phím toggle + refresh heatmap theo `refreshEveryFrames`.

### Bước 3 — Gắn vào scene PIBT
- Chỉ có ý nghĩa ở scene dùng PIBT (`MapF_TankTest_PIBT`) vì A* không sinh `_flow`.
- Trong `MapScenarioBootstrapPIBT` (hoặc bootstrap tương ứng): sau khi map build xong,
  `AddComponent<PIBTFlowVisualizer>()`, set `mapLoader`. Bọc `#if UNITY_EDITOR ||
  DEVELOPMENT_BUILD` nếu không muốn có trong bản release.
- **Không** thêm vào scene A* — thay vào đó (mục 5) vẽ heatmap "mật độ nếu ai cũng đi
  đường ngắn nhất" để đối chiếu.

### Bước 4 — Legend + HUD
- Một `Canvas` nhỏ góc màn hình: thang màu heatmap (xanh→đỏ = ít→tắc), chú thích mũi
  tên đỏ = op_flow (đối đầu), phím tắt F1/F2.

### Bước 5 — (Tùy chọn) Ghi snapshot cho báo cáo
- Nút/hotkey chụp `ScreenCapture.CaptureScreenshot` kèm timestamp vào `BacktestResults/flow/`.
- Hoặc export heatmap `Texture2D` ra PNG từng tick để dựng ảnh động minh hoạ.

---

## 5. Đối chiếu định lượng A* vs PIBT (bổ trợ phần vẽ)

Ngoài hình ảnh, nên có số để đưa vào báo cáo. Trên `room-32-32-4` với cùng kịch bản
spawn/goal, đo:

| Metric | Nguồn | Kỳ vọng |
|---|---|---|
| `maxVertexFlow` tại cửa | `PIBTFlowVisualizer` (PIBT) | PIBT thấp hơn (tản luồng) |
| Số cạnh đối đầu (`opFlow>0`) | duyệt flow (PIBT) | PIBT ≈ 0, giảm head-on |
| `btRecoveryCount` tổng | agent metrics | A* cao hơn (kẹt cửa) |
| `btReplanCount` | agent metrics | A* cao hơn |
| Thời gian tất cả tới đích | backtest summary | PIBT nhanh/ổn định hơn |

> Cho A* (không có `_flow`): dựng heatmap "giả lập tắc" = cộng dồn số agent có ô đó
> nằm trên `CurrentPath` mỗi tick. So sánh trực tiếp với heatmap vertex_flow của PIBT
> để cho thấy A* dồn cục bộ còn PIBT trải đều.

---

## 6. File ảnh hưởng / tạo mới

| File | Thay đổi |
|---|---|
| `Assets/Scripts/PIBTPlanner.cs` | + API read-only: `GetEdgeFlow`, `GetVertexFlow`, `GetOpposingFlow`, `HasFlow`, `GridCols/Rows` |
| `Assets/Scripts/Debug/PIBTFlowVisualizer.cs` | **Mới** — heatmap + mũi tên + toggle + legend |
| `Assets/Scripts/MapScenarioBootstrapPIBT*.cs` | Gắn visualizer vào scene PIBT (editor/dev build) |
| (tùy chọn) A* path-density overlay | Heatmap đối chứng cho scene A* |

---

## 7. Thứ tự làm gợi ý (checklist)

- [x] B1: Thêm API read-only vào `PIBTPlanner` (`HasFlow`, `GetVertexFlow`, `GetEdgeFlow`, `GetOpposingFlow`, `DirToDelta`, `GridCols/Rows`).
- [x] B2: Viết `PIBTFlowVisualizer` (`Assets/Scripts/Debug/`) — heatmap vertex_flow (GL quads).
- [x] B3: Lớp mũi tên `GL` op_flow + tô đỏ cạnh đối đầu (head-on).
- [x] B4: Gắn vào scene PIBT (`MapScenarioBootstrapPIBT.EnsureFlowVisualizer`, cờ `showFlowVisualizer`) + legend `OnGUI` + phím F1/F2/F3. Vẽ qua hook URP `RenderPipelineManager.endCameraRendering` (fallback `OnRenderObject` cho built-in).
- [ ] B5: Chạy `room-32-32-4` với 8–12 agent, chụp ảnh so sánh A* vs PIBT. *(cần mở Unity)*
- [ ] B6: (tùy chọn) Overlay mật độ path cho A* + bảng số liệu mục 5.

> Ghi chú triển khai: project dùng **URP** nên `OnRenderObject` không được gọi ổn định →
> visualizer vẽ GL trong `RenderPipelineManager.endCameraRendering` (đăng ký khi có
> `GraphicsSettings.currentRenderPipeline`), giữ `OnRenderObject` làm fallback cho
> built-in RP. Toàn bộ chỉ đọc `_flow`, bọc `#if UNITY_EDITOR || DEVELOPMENT_BUILD` ở
> khâu gắn component.

> Ghi chú: toàn bộ phần vẽ chỉ **đọc** `_flow`, không can thiệp thuật toán PIBT nên
> không ảnh hưởng kết quả backtest. Bọc trong `#if UNITY_EDITOR || DEVELOPMENT_BUILD`
> để bản release không dính overhead.
