# Hỏi Đáp Chuyên Sâu: GridEnemyAgentPIBT — Tài Xế Phiên Bản PIBT

Tài liệu này giải thích `GridEnemyAgentPIBT.cs` (863 dòng) — bộ điều khiển xe tăng địch cho **mode "PIBT C#"**. File này là **bản song sinh** của `GridEnemyAgent` (mode A\*): phần lớn code giống hệt, chỉ **thay bộ tìm đường và bỏ bớt vài cơ chế** vì PIBT tự lo phối hợp.

> 📎 **Đọc kèm:** `ChiTiet_GridEnemyAgent_QA.md` (bản A\*) và `ChiTiet_PIBTPlanner_QA.md` (bộ não flow-guidance). Tài liệu này **chỉ đào sâu phần KHÁC BIỆT**; phần giống nhau (bắn, bám đường, phát hiện kẹt) chỉ tóm tắt và trỏ về bản A\*.

---

## 0. Bản chất: đây là "cùng một tài xế, đổi bộ định vị"

Hình dung `GridEnemyAgent` và `GridEnemyAgentPIBT` là **hai chiếc xe giống hệt nhau** — cùng vô-lăng (`TankController`), cùng cách xoay-tiến, cùng cách bắn, cùng cách nhận biết kẹt. Khác biệt duy nhất là **cái GPS trong xe**:
- Bản A\*: mỗi xe tự tính đường **độc lập**, không biết xe khác đang tính gì (`GridAStarPathfinder` + `GridNavMask`).
- Bản PIBT: mọi xe **chung một bản đồ giao thông** (`PIBTPlanner._flow`), tính đường qua `GridPIBTPathfinder` để tự giãn tuyến, tránh chen chúc.

Chính vì "phối hợp" đã được đẩy xuống tầng lập kế hoạch chung, nên ở tầng tài xế này, PIBT **bỏ bớt được** vài cơ chế chống-kẹt mà bản A\* cần.

---

## 1. Năm điểm khác biệt so với bản A\* (đây là phần cần thuộc)

Chính comment đầu file (dòng 4-13) đã liệt kê. Diễn giải dễ hiểu:

| # | Khác biệt | Ý nghĩa |
|---|---|---|
| 1 | **Tìm đường bằng `GridPIBTPathfinder`** thay `GridAStarPathfinder` | Dùng flow-aware A\* + Frank-Wolfe của `PIBTPlanner` thay vì A\* thuần. |
| 2 | **Không dùng `GridNavMask`** | PIBT tự dựng danh sách hàng xóm từ `MapLoader.IsWalkable` (trong `PIBTPlanner.BuildNbrs`), không cần lớp mặt nạ riêng. |
| 3 | **Đăng ký `agentId` với `PIBTPlanner`** | Mỗi xe có một ID để ghi/xóa dấu vết trên bản đồ luồng chung. |
| 4 | **Bỏ bậc cứu hộ `ExpandedMask`** | Thang cứu hộ chỉ còn **3 bậc** (bản A\* có 4). Vì PIBT tự né tắc nghẽn qua flow, không cần "phình mặt nạ" để thoát kẹt. |
| 5 | **Spatial recovery không dùng `blockedCells`** | Khi đi lòng vòng, chỉ **ép tính lại đường** (flow đã đổi nên tuyến mới tự khác), không cần cấm các ô vừa đi. |

> **Câu trả lời gọn khi thầy hỏi "hai file này khác gì nhau?":**
> *"Chung toàn bộ tầng điều khiển vật lý — bắn, xoay-tiến, phát hiện kẹt — để so sánh công bằng. Chỉ khác bộ tìm đường: bản này dùng PIBT chia sẻ bản đồ luồng chung, nên tự phối hợp tránh nhau; nhờ đó bỏ được bậc cứu hộ phình-mặt-nạ mà bản A\* phải có."*

---

## 2. Vòng đời agent gắn với PIBTPlanner (phần MỚI hoàn toàn)

Đây là logic không tồn tại ở bản A\*, vì A\* không có trạng thái chung.

### `EnsurePIBTReady` (dòng 130)
Gọi mỗi frame ở đầu `Update`. Đảm bảo 2 việc theo thứ tự:
1. Nếu `PIBTPlanner` chưa khởi tạo → `Init(mapLoader)` (dựng bản đồ luồng, bảng hàng xóm...).
2. Nếu xe này chưa có ID → `Register()` lấy một `agentId`.

> **Tại sao kiểm tra mỗi frame chứ không chỉ ở Awake?** Vì `PIBTPlanner` là singleton dùng chung; xe có thể spawn **trước khi** map/planner sẵn sàng. Kiểm tra mỗi frame đảm bảo xe nào cũng "gia nhập" hệ thống ngay khi có thể, không phụ thuộc thứ tự khởi tạo.

### `OnDestroy` → `Unregister` (dòng 121)
Khi xe chết, **bắt buộc** gọi `PIBTPlanner.Unregister(agentId)` để **xóa dấu vết luồng** của nó khỏi `_flow`.

> **Tại sao cực kỳ quan trọng?** Nếu quên, "bóng ma" của xe đã chết vẫn để lại vết trên bản đồ giao thông, khiến các xe còn sống **né một chướng ngại không còn tồn tại** → đường đi méo mó vô cớ. Đây là lỗi kinh điển của hệ dùng trạng thái chia sẻ.

---

## 3. Tính lại đường: `ReplanPath` khác gì bản A\*? (dòng 318)

Khung sườn giống bản A\* (đếm replan, gom ô chặn động từ đồng đội), nhưng có 2 điểm mới:

1. **Gọi `GridPIBTPathfinder.TryFindPath(..., agentId, ..., frankWolfeMs)`** thay vì A\* thường. Truyền vào `agentId` (để planner biết đang tính cho ai) và `frankWolfeMs` (ngân sách thời gian cho vòng lặp Frank-Wolfe, mặc định 15ms).
2. **`ResolveGoalCell` (dòng 760):** trước khi tìm đường, "nắn" ô đích nếu nó bị chặn. Nếu ô Eagle đang bị đồng đội đứng đè hoặc không hợp lệ theo `PIBTPlanner.IsAgentWalkable`, quét vòng tròn lan dần tìm ô đích thay thế gần nhất. → Tránh việc PIBT trả "không có đường" chỉ vì ô đích tạm bị bận.

Phần fallback khi thất bại thì **giống hệt** bản A\*: dò xem có thùng phá được chắn đường không (`TryFindDestructibleOnPath`) rồi chuyển sang shoot-to-clear.

> Lưu ý nhỏ: `ReplanToDestructible` (dòng 389) khi đi tới chỗ thùng lại dùng **`GridAStarPathfinder` thường**, không dùng PIBT. Vì đây chỉ là chặng ngắn "bò tới cái thùng để bắn", không cần phối hợp luồng — A\* đơn giản là đủ và rẻ hơn.

---

## 4. Thang cứu hộ rút gọn còn 3 bậc (dòng 653)

Bản A\* có 4 bậc: `ForcedReplan → Reverse → ExpandedMask → (lặp)`. Bản PIBT **bỏ `ExpandedMask`**, còn 3 bậc:

| Bậc | Làm gì |
|---|---|
| 1. `ForcedReplan` | Ép tính lại đường ngay. |
| 2. `Reverse` | Lùi xe 0.8s kèm bẻ lái để dứt khỏi chỗ chèn. |
| 3. (quay về `None`) | Sau khi lùi, replan lại rồi **reset về None** — tin tưởng PIBT tự tìm tuyến mới qua flow. |

> **Tại sao bỏ được `ExpandedMask`?** `ExpandedMask` (phình mặt nạ để né xa tường hơn) là cách bản A\* "vật lộn" thoát kẹt khi không có thông tin về xe khác. PIBT thì đã có bản đồ luồng chung: chỗ nào đông, agent tự động né sẵn từ khâu lập kế hoạch. Nên chỉ cần lùi + replan là đủ, không cần leo tới bậc phình mặt nạ. Đây là minh chứng cụ thể "phối hợp tốt hơn ⇒ cần ít cơ chế chữa cháy hơn".

### `TriggerSpatialRecovery` cũng gọn hơn (dòng 637)
Bản A\* khi phát hiện đi lòng vòng thì **cấm các ô vừa đi** rồi replan. Bản PIBT **chỉ replan** (`currentPath.Clear()` + `ReplanPath`). Vì sau mỗi lần replan, dấu vết flow đã thay đổi → tuyến mới **tự nhiên khác** tuyến cũ, không cần cấm ô thủ công.

---

## 5. Những phần GIỮ NGUYÊN (chỉ tóm tắt — xem bản A\*)

Các phần sau **giống hệt** `GridEnemyAgent`, đọc chi tiết ở `ChiTiet_GridEnemyAgent_QA.md`:

- **Cây quyết định trong `Update`** (dòng 138): bắn > thoát kẹt > phá vật cản > đi đường.
- **Cơ chế bắn** (`GetShootingTarget`, `CanShootTarget` với `RaycastAll` nhìn xuyên đồng đội).
- **Shoot-to-clear** thùng phá được (BFS tìm thùng → bắn vỡ → replan).
- **`FollowPath`** — logic xoay-rồi-tiến 4 mức dot-product, duty cycle, ngưỡng chạm ô co giãn.
- **Phát hiện kẹt** — 3 cảm biến (đứng im theo thời gian / lòng vòng không gian / cạ tường vật lý).
- **Số liệu backtest** (`bt...`) và `OnDrawGizmos` (bản PIBT vẽ đường **màu xanh cyan**, dòng 859, để phân biệt với đường **đỏ** của bản A\*).

> Việc giữ nguyên tầng điều khiển là **có chủ đích**: luận văn cần so sánh **chỉ riêng chất lượng thuật toán tìm đường**. Nếu tầng điều khiển khác nhau, khác biệt kết quả sẽ lẫn giữa "thuật toán tốt hơn" và "tài xế lái khéo hơn". Chung tài xế ⇒ khác biệt đo được phản ánh đúng thuật toán.

---

## 6. Câu hỏi phản biện có thể gặp

**"Vì sao phải viết hẳn một file riêng gần như copy của `GridEnemyAgent`?"**
→ Để **cô lập biến số**: hai mode chung toàn bộ tầng điều khiển vật lý, chỉ khác bộ tìm đường. Nhờ đó khi so kết quả A\* vs PIBT, khác biệt phản ánh đúng thuật toán chứ không phải do lái xe khác nhau. (Đánh đổi: có trùng lặp code, nhưng đổi lại phép so sánh sạch.)

**"Đường màu xanh và đỏ trong demo là gì?"**
→ Gizmo vẽ lộ trình: **đỏ** = agent chạy A\* (`GridEnemyAgent`), **cyan** = agent chạy PIBT (`GridEnemyAgentPIBT`). Nhìn màu là biết ngay đang xem thuật toán nào.

**"PIBT bỏ bậc cứu hộ `ExpandedMask`, vậy có dễ kẹt hơn không?"**
→ Ngược lại. `ExpandedMask` là cơ chế chữa cháy của A\* khi nó **mù thông tin** về xe khác. PIBT né tắc nghẽn ngay từ khâu lập kế hoạch nhờ bản đồ luồng chung, nên ít kẹt hơn và không cần bậc đó. Số liệu `btRecoveryCount` trong backtest cho thấy điều này.

**"Xe chết mà quên xóa khỏi PIBT thì sao?"**
→ `OnDestroy` gọi `Unregister` để xóa vết luồng. Nếu không, các xe sống sẽ né một "bóng ma" — đây là lý do file này bắt buộc có `OnDestroy`, còn bản A\* thì không cần (A\* không có trạng thái chung).

**"`frankWolfeMs = 15` nghĩa là gì?"**
→ Ngân sách thời gian (15ms) cho vòng lặp Frank-Wolfe mỗi lần replan: tính lại đường cho chính mình rồi dùng thời gian thừa tinh chỉnh đường các agent khác cho tới khi hết budget. Giới hạn thời gian để không treo frame — đúng tinh thần "đủ tốt trong thời gian thực" của PIBT.

**"Tầng phối hợp thật (chống va chạm từng bước) nằm ở đâu?"**
→ Không nằm ở đây và cũng không phải `causalPIBT` gốc. Va chạm được xử lý ở mức agent bằng `IsCellOccupiedByFriendly` (dừng + replan khi ô kế tiếp có đồng đội) cộng hệ cứu hộ kẹt. Đây chính là phần "thích nghi tầng va chạm cho real-time" đã nêu trong `ChiTiet_PIBTPlanner_QA.md` mục 0 — điểm phản biện trung thực cần nhớ.
