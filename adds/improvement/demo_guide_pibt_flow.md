# Hướng dẫn demo: Trực quan hoá traffic flow & so sánh A* / PIBT-C# / PIBT-TCP

> Áp dụng cho map **Rooms-32** (`room-32-32-4.map`, cửa hẹp 1 ô) — map bottleneck lý
> tưởng để làm nổi bật khác biệt điều phối. Overlay flow bật bằng **F1/F2/F3** khi Play.

---

## 0. TL;DR các phím

| Phím | Tác dụng |
|---|---|
| **F1** | Bật/tắt **heatmap vertex_flow** (ô đông: xanh→vàng→đỏ) |
| **F2** | Bật/tắt **mũi tên op_flow** (dòng có hướng; **đỏ = đối đầu head-on**) |
| **F3** | Bật/tắt **legend** (chú thích góc trên-trái) |

Overlay chỉ hiện ở scene **PIBT C#** và **PIBT-TCP** (A* không sinh flow). Chỉ bật trong
**Editor / Development Build** (bọc `#if UNITY_EDITOR || DEVELOPMENT_BUILD`).

---

## 1. Vì sao PIBT-TCP trông ổn hơn PIBT-C#?

Cả hai đều thuộc họ PIBT nhưng **mức độ hoàn thiện khác nhau**:

### PIBT-C# (`PIBTPlanner`)
- Thực chất là **flow-guided A\* + Frank-Wolfe** (traffic assignment): tính đường ưu tiên
  toàn cục có phạt `op_flow`/`vertex_flow`. Đây là **lớp dẫn đường (guidance)**.
- Nhưng **thực thi cục bộ còn thô**: mỗi agent bám path riêng, né nhau bằng cách coi ô
  đồng đội là **tường cứng** (`BuildDynamicBlockedCells` bên A*/agent). Không có bước
  giải va chạm 1-timestep kiểu PIBT thật (priority inheritance + swap/backtrack).
- Frank-Wolfe chạy theo **ngân sách 15 ms** mỗi replan → với nhiều agent có thể **chưa
  hội tụ**; replan theo interval, lái vật lý liên tục → dễ dao động/kẹt ở cửa hẹp.

### PIBT-TCP (server C++ ngoài)
- Là **bản PIBT tham chiếu** (default_planner — Team "No Man's Sky", giải MAPF LoRR):
  giải **1 bước đồng bộ mỗi timestep** với **priority inheritance + swap/backtrack** →
  đảm bảo không đụng độ, ít deadlock, lockstep mượt.
- Server plan **đồng thời tất cả agent** rồi trả từng bước; client chỉ bám bước đó.
- (Vừa bổ sung) client còn **né vật cản động** (thùng nổi) bằng A* cục bộ → robust hơn.

### Kết luận
> Không phải "thuật toán TCP tốt hơn về bản chất" mà là **implementation trưởng thành
> hơn**: C# mới có lớp dẫn đường flow, còn giải va chạm cục bộ đơn giản; C++ là PIBT đầy
> đủ. Vì thế TCP ổn định hơn ở map chật. Đây cũng là một **điểm thảo luận** tốt cho báo
> cáo (so sánh port C# vs reference C++).

---

## 2. Flow được lấy từ đâu ở mỗi mode?

| Mode | Nguồn flow cho overlay | Ghi chú |
|---|---|---|
| **PIBT-C#** | `PIBTPlanner._flow` THẬT (qua `PIBTPlannerFlowField`) | Đúng flow nội bộ thuật toán |
| **PIBT-TCP** | **Tái dựng client-side** (`TcpFlowTracker`) | `_flow` thật nằm trên server C++ → Unity không có; ta quan sát bước đi agent `cell→nextCell` mỗi tick, cộng dồn có **decay** |

> ⚠️ **Quan trọng khi trình bày**: heatmap/mũi tên ở TCP là **"flow hành vi quan sát
> được"** (agent thực sự đi đâu), KHÔNG phải flow-assignment nội bộ của PIBT như bản C#.
> Cùng ngữ nghĩa hiển thị (ô đông / cạnh đối đầu) nhưng nguồn gốc khác — nêu rõ điều này
> để tránh hiểu nhầm là "đọc được nội bộ server".

Kiến trúc chung: cả 2 mode cùng dùng `PIBTFlowVisualizer`, chỉ khác **nguồn** cắm vào qua
interface `IFlowField` (`SetFlowSource`).

---

## 3. Chuẩn bị

1. Mở project Unity (6000.3.10f1), để Editor compile xong (không lỗi).
2. **Chỉ với PIBT-TCP**: bật **server PIBT C++** ở `127.0.0.1:7777` (hoặc host cấu hình
   trong `PIBTTcpClient`/bootstrap). Không có server → agent đứng im, không có flow.
3. Cờ bật overlay đã mặc định `true`:
   - PIBT-C#: `MapScenarioBootstrapPIBT.showFlowVisualizer`
   - PIBT-TCP: `MapScenarioBootstrapPIBT_TCP.showFlowVisualizer`
   Có thể tắt trong Inspector nếu muốn chạy sạch.

---

## 4. Kịch bản demo so sánh (khuyến nghị)

Dùng **backtest mode**, map **Rooms-32**, **12 agent**, chạy lần lượt 3 thuật toán trên
cùng map để đối chứng:

### 4.1 A* (baseline — để thấy deadlock)
1. Menu → chọn map **Rooms-32** → thuật toán **A\***.
2. Backtest: đặt `AgentCount = 12` → Play.
3. Quan sát: một số enemy tới Eagle & bắn, **một số kẹt đứng yên** ở cửa hẹp (đồng đội
   đã tới trước "cắm chốt" bịt cửa → agent sau A* trả về "no path" → chờ). *Không có
   overlay flow ở mode này.*
4. Ghi số: `btRecoveryCount`, `btReplanCount`, số agent về đích, thời gian.

### 4.2 PIBT-C#
1. Cùng map Rooms-32 → thuật toán **PIBT**. `AgentCount = 12` → Play.
2. Bật **F1 + F2 + F3**.
3. Quan sát:
   - **Heatmap đỏ** dồn ở các cửa hẹp = `vertex_flow` cao (điểm tắc).
   - **Mũi tên đỏ** ở cửa = `op_flow` phạt đối đầu → agent tránh đâm ngược chiều.
   - Agent về đích nhiều hơn A*, ít kẹt hơn (có thể vẫn dao động nhẹ ở chokepoint).

### 4.3 PIBT-TCP (cần server)
1. Bật server. Menu → Rooms-32 → **PIBT-TCP**. `AgentCount = 12` → Play.
2. Bật **F1 + F2 + F3**.
3. Quan sát: dòng chảy **mượt và ổn định nhất**, gần như không agent nào kẹt; heatmap
   thể hiện luồng đi qua cửa được **chia lượt** gọn gàng.

### 4.4 Chụp ảnh cho báo cáo
- Chụp 3 ảnh cùng thời điểm "nhiều agent dồn 1 cửa" cho A* / PIBT-C# / PIBT-TCP.
- Với 2 mode PIBT: 1 ảnh **F1** (heatmap) + 1 ảnh **F2** (mũi tên) hoặc bật cả hai.
- Đặt cạnh nhau: A* có agent kẹt (khoanh đỏ) ↔ PIBT thông suốt.

---

## 5. Đọc hiểu overlay

- **Heatmap (F1) = vertex_flow**: tổng lưu lượng đi ra khỏi ô → **mức đông**. Đỏ = ô/cửa
  đang tắc. Dùng để chỉ ra chokepoint và cho thấy PIBT **tản** hay **dồn** lưu lượng.
- **Mũi tên (F2) = op_flow / dòng có hướng**:
  - **Đỏ** = cạnh **đối đầu** (có agent đi cả 2 chiều) → chỗ `op_flow` phạt head-on.
  - **Cam→xanh** = dòng 1 chiều, đậm nhạt theo độ lớn lưu lượng.
- **Legend (F3)**: thang màu + chú thích + `max vertex_flow` hiện tại (giá trị chuẩn hoá).

---

## 6. Bảng talking-points cho hội đồng

| Tiêu chí | A* | PIBT-C# | PIBT-TCP |
|---|---|---|---|
| Điều phối đa agent | Không (né như tường tĩnh) | Flow guidance (op/vertex_flow) | PIBT đầy đủ (priority + swap) |
| Giải va chạm 1-timestep | — | Thô (bám path + hard-block) | Có, đồng bộ lockstep |
| Kẹt ở cửa hẹp | Nhiều (deadlock) | Ít hơn, còn dao động | Ít nhất |
| Về đích | Một phần | Đa số | Gần như toàn bộ |
| Overlay flow | Không có | `_flow` thật | Tái dựng client (quan sát) |

---

## 7. File liên quan (đã implement)

| File | Vai trò |
|---|---|
| `Assets/Scripts/PIBTPlanner.cs` | API read-only flow (`GetVertexFlow`/`GetEdgeFlow`/`GetOpposingFlow`/`DirToDelta`) |
| `Assets/Scripts/Debug/PIBTFlowVisualizer.cs` | Vẽ heatmap + mũi tên + legend; đọc qua `IFlowField` |
| `Assets/Scripts/Debug/TcpFlowTracker.cs` | Tái dựng flow client-side cho PIBT-TCP (decay per tick) |
| `Assets/Scripts/MapScenarioBootstrapPIBT.cs` | Gắn visualizer (nguồn = PIBTPlanner) |
| `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs` | Gắn visualizer + tracker; feed `RecordStep` mỗi tick |

---

## 8. Khắc phục sự cố

| Hiện tượng | Nguyên nhân / cách xử lý |
|---|---|
| Overlay không hiện | Đúng scene PIBT/TCP chưa? Cờ `showFlowVisualizer` bật? Có ở Editor/Dev build? |
| TCP không có flow | Agent phải **đang di chuyển** mới sinh flow; kiểm tra server đã kết nối, agent không đứng im. |
| Heatmap "đơ" một chỗ | Bình thường lúc agent tụ; giảm `TcpFlowTracker.decayPerTick` để phai nhanh hơn. |
| Mũi tên/heatmap lệch hoặc lật | Vấn đề ma trận GL trong URP — báo để chỉnh `RenderGL` (modelview/projection). |
| Không có màu (log shader) | Shader `Hidden/Internal-Colored` bị strip trong build → chỉ chạy Editor/Dev. |
