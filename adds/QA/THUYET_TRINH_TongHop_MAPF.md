# Thuyết trình tổng hợp — MAPF trong game xe tăng: A\*, PIBT C#, PIBT C++/TCP

> Bản trình bày cô đọng dùng để thuyết trình/bảo vệ. Đi từ bài toán → kiến trúc chung → ba tầng thuật toán → cơ chế replan & recovery → thực nghiệm → kết luận. Mọi khẳng định đều bám theo bộ tài liệu chi tiết trong `adds/QA/`.

---

## 1. Bài toán & mục tiêu

Đồ án ứng dụng bài toán **tìm đường đa tác nhân (Multi-Agent Pathfinding — MAPF)** vào một game bắn xe tăng 2D góc nhìn từ trên xuống, nhiều người chơi, xây trên **Unity Engine (URP)** và dùng **bản đồ chuẩn MovingAI MAPF benchmark**. Trọng tâm là thuật toán **PIBT (Priority Inheritance with Backtracking)**, lấy cảm hứng từ lời giải đoạt giải nhất toàn đoàn (Line Honours) của **Team No Man's Sky** tại *The League of Robot Runners 2024* (Amazon Robotics tài trợ) — vốn chỉ tồn tại dưới dạng **máy chủ C++ chạy benchmark, không chơi/tương tác được**.

Mục tiêu là **đưa thuật toán từ lý thuyết benchmark ra một sản phẩm chơi được**, đồng thời triển khai **ba cấp độ AI** để so sánh khách quan trong cùng một môi trường:

| Tầng | Tên | Bản chất |
|---|---|---|
| Baseline | **A\*** | Mỗi agent tự tính đường, không biết agent khác. |
| Tầng 2 | **PIBT C#** | Port tầng dẫn hướng luồng của bản đoạt giải, viết ngay trong Unity. |
| Tầng 3 | **PIBT C++/TCP** | Chạy thẳng đoạn code C++ gốc trên server, Unity là client. |

---

## 2. Kiến trúc chung — "nguyên tắc công bằng"

Điểm mấu chốt để so sánh **ba thuật toán một cách công bằng**: cả người chơi lẫn cả ba AI đều điều khiển xe qua đúng **một cửa ngõ duy nhất** — `TankController` — với ba hàm:

```csharp
HandleMoveBody(vector)         // lái thân
HandleTurretMovement(pointer)  // xoay nòng
HandleShoot()                  // bắn
```

Thuật toán **không tự lái xe** — chúng chỉ tính ra **ô kế tiếp**, rồi gọi `HandleMoveBody` giống hệt người chơi. Toàn bộ tầng vật lý (gia tốc, xoay thân, va chạm), tầng bắn và tầng phát hiện kẹt đều **dùng chung**. Do đó khác biệt quan sát được là **do thuật toán tìm đường**, không phải do "tài xế lái khéo hơn".

```
file .map ──► MapLoader ──► lưới ô + toạ độ world  (IsWalkable / CellToWorld / WorldToCell)
                               │
        ┌──────────────────────┼──────────────────────┐
     A* (mỗi xe)          PIBT C# (_flow chung)     PIBT C++ (server)
        └──────────────────────┼──────────────────────┘
                               ▼
                 GridEnemyAgent* ──► TankController ──► xe tăng
```

`MapLoader` là "ngôn ngữ chung": A\* nói bằng `Vector2Int`, PIBT nói bằng chỉ số phẳng `loc = y*cols + x`, nhưng cả hai quy về cùng một lưới. Gốc toạ độ **trên-trái, Y tăng xuống dưới** (khớp thứ tự dòng trong file `.map`).

---

## 3. Ba tầng thuật toán — workflow & khác biệt

### 3.1. A\* baseline — mỗi xe một mình

**Workflow:** mỗi agent tự chạy A\* trên lưới 4 hướng, xếp hạng ô mở bằng:

```
f = g + h          (g = số bước đã đi,  h = Manhattan tới goal)
```

**Đặc điểm cốt lõi:** **A\* không biết agent khác tồn tại.** Không ô nào bị "phạt" vì đông. Để tránh đâm nhau nghèo nàn, trước khi chạy A\* xe "chụp ảnh" vị trí **hiện tại** của đồng đội và đánh dấu chúng thành *tường cứng* → A\* buộc vẽ đường vòng. Gọi là "nghèo nàn" vì chỉ né theo vị trí hiện tại, không biết 1 giây sau đồng đội đi đâu → nhiều xe vẫn tranh nhau một ô hẹp và kẹt.

> **Cần tách bạch hai cơ chế của mode A\*** (dễ bị hỏi và dễ nhầm là một): việc "tránh cạ tường" và việc "né nhau" do **hai thành phần khác nhau** đảm nhận, đi vào A\* qua **hai tham số riêng**.
>
> | Nhiệm vụ | Cơ chế | Tham số vào A\* |
> |---|---|---|
> | Tránh cạ **tường** + trừ hao bề rộng xe | `GridNavMask` | `navMask` |
> | Né **nhau** (đồng đội) | Chụp vị trí hiện tại → tường cứng tạm | `blockedCells` |
>
> - **`GridNavMask` — chống cạ tường (tĩnh):** `MapLoader` chỉ biết ô nào là tường thật (`@`), nhưng xe tăng **có bề rộng**, không phải một điểm. `GridNavMask` là lớp "kính lọc" phủ lên lưới gốc, làm 2 việc: **cấm cứng** — "phình" vật cản ra (theo `InflateRadius` hoặc bán kính vật lý qua `Physics2D.OverlapCircle`), đánh dấu ô sát tường là *không đứng được* nên A\* không vẽ đường làm thân xe cạ tường; và **phạt mềm (soft cost)** — ô càng gần tường càng "đắt" (theo khoảng cách Chebyshev), khiến A\* **thích đi giữa lối** hơn men mép, nhưng vẫn chịu đi sát tường khi hành lang hẹp buộc phải đi.
> - **`blockedCells` — né đồng đội (động):** trước mỗi lần replan, xe chụp vị trí **hiện tại** của đồng đội và nhét vào `blockedCells`; A\* coi đó là **tường cứng tạm thời** (ưu tiên chặn cao nhất, xét trước cả `navMask`). Đây mới là cơ chế né agent — và cũng chính là chỗ "nghèo nàn" nói trên.
>
> Liên hệ gián tiếp: soft cost giữ xe đi giữa lối nên *phụ* làm giảm đâm nhau, nhưng đó chỉ là hệ quả phụ — né agent **trực tiếp** là `blockedCells`, không phải `GridNavMask`.

### 3.2. PIBT C# — chia sẻ "bản đồ giao thông"

**Workflow:** thay vì mỗi xe một mình, mọi agent PIBT **chung một bản đồ luồng** `_flow` (một `static` singleton). `_flow[cell*4 + d]` = số tuyến đang đi qua cạnh từ `cell` theo hướng `d`. Mỗi tick:

1. Agent đăng ký `id`, báo vị trí hiện tại.
2. `FrankWolfe(id, start, goal, budgetMs)`:
   - **Xóa** vết luồng cũ của agent khỏi `_flow`.
   - Chạy **A\* nhận biết luồng** (`AStarFlow`) → tuyến mới.
   - **Cộng** vết tuyến mới vào `_flow`.
   - Còn thời gian → lặp round-robin cho các agent khác tới khi hết `budgetMs`.

**Khác A\* đúng ở đâu trong code?** Chỉ **2 số hạng phạt** thêm vào priority + một lưới `_flow` dùng chung:

```
A* baseline :  f = g + h
PIBT C#     :  f = g + h + op_flow + vertex_flow
                          └─────────┬─────────┘
                    "traffic": chia sẻ qua lưới _flow chung
```

- **`op_flow`** — phạt **đi ngược chiều dòng**, dùng **phép nhân** `(flow[curr→d]+1) × flow[next→ngược]`. Đối đầu trực diện trong hành lang gây khóa cứng (deadlock) — nguồn kẹt tệ nhất trong MAPF — nên phạt nặng theo cấp số nhân.
- **`vertex_flow`** — phạt **đi qua ô đông** (nút cổ chai), dùng **phép chia** `(tổng flow mọi hướng − 1)/2`. Chỉ chậm tuyến tính nên phạt nhẹ.

Kết quả: agent đi sau nhìn vết agent đi trước và **tự động giãn ra nhiều tuyến** thay vì chen một hành lang. Đây chính là **sự phối hợp** mà A\* không có.

Hai chi tiết kỹ thuật đáng nêu: **heuristic là reverse-BFS** từ goal (khoảng cách thật né tường, chính xác hơn Manhattan, cache theo goal); và **Frank-Wolfe** giới hạn bằng `budgetMs = 15ms` — "đủ tốt trong thời gian thực" thay vì tối ưu tuyệt đối, để không tụt FPS.

> **Điểm trung thực bắt buộc:** bản C++ gốc có **hai tầng** — (1) dẫn hướng luồng (flow-guidance) và (2) `causalPIBT` phân xử va chạm từng bước bằng đệ quy priority-inheritance + backtracking. `PIBTPlanner.cs` **chỉ port Tầng 1 (flow-guidance)** — thành phần tạo khác biệt chính so với A\*. Tầng 2 `causalPIBT` được **thích nghi lại** thành cơ chế **chờ-và-replan ở mức agent**, vì Unity chạy vật lý thời gian thực chứ không phải MAPF theo timestep rời rạc. **Tuyệt đối không nói "port 100% PIBT".**

### 3.3. PIBT C++/TCP — chạy code gốc đoạt giải

**Workflow:** đẩy toàn bộ việc tính đường ra **chương trình C++ chạy riêng** (bản đoạt giải LoRR, trên WSL Ubuntu). Chuỗi 4 mắt xích, giao thức **JSON-lines qua TCP**:

```
[Server C++ PIBT] ←TCP/JSON→ [PIBTTcpClient] → [Coordinator] → [GridEnemyAgentPIBT_TCP] → xe
  (bộ não thật)               (đường dây)       (nhạc trưởng)     (tài xế câm)
```

- **`hello`** — bắt tay + **validate khớp kích thước map** (server và Unity phải chung hệ toạ độ phẳng `loc = y*width + x`; lệch width là mọi ô trỏ sai). Đây từng là gốc bug "chỉ 1/5 map chạy được".
- **`plan_step`** (mỗi tick) — gửi trạng thái toàn đội `(id, loc, orientation, goalLoc)`, nhận **hành động** + ô kế tiếp cho từng agent. `orientation` cần thiết vì xe tăng không "đi ngang như con cua" — server phải tính chi phí xoay trước khi tiến.
- **`shutdown`/`reset`** — dọn phiên / tái dùng kết nối cho ván mới.

Xe TCP là **"tài xế câm"**: cắt bỏ toàn bộ phần tính đường, chỉ giữ **đúng một ô đích** do coordinator gán rồi lái tới cho mượt. Tách "tài xế" khỏi "bộ não" vì bộ não chạy theo **bước rời rạc** ở server, còn xe cần **chuyển động vật lý liên tục** giữa hai tick.

> **Điểm mạnh nhất để khoe:** hai mode kia là **port** thuật toán; mode này **chạy thẳng chính đoạn code C++ đoạt giải**, chứng minh kiến trúc client-server tách rời (đưa được lên cloud) và giữ **phép đo sạch** (mọi quyết định đường đi đến từ server).

> **Điểm trung thực bắt buộc:** khi server bế tắc > 4s, xe có **"cú hích tham lam"** (`GreedyStepTowardEagle`) — tự nhích một ô về phía Eagle theo Manhattan. Đây **KHÔNG phải PIBT**, chỉ là heuristic cục bộ 1 bước để backtest không mất trắng dữ liệu. Đã **ghi log tách biệt** (`FALLBACK greedy nudge`) để không trộn vào số liệu PIBT thuần.

### 3.4. Bảng so sánh cô đọng

| Tiêu chí | A\* | PIBT C# | PIBT C++/TCP |
|---|---|---|---|
| Nơi tính đường | Mỗi xe, trong Unity | Chung `_flow`, trong Unity | Server C++ ngoài |
| Biết agent khác? | Không (né vị trí hiện tại) | Có (qua lưới luồng chung) | Có (server điều phối) |
| Priority | `g + h` | `g + h + op_flow + vertex_flow` | (thuật toán gốc trên server) |
| Heuristic | Manhattan | Reverse-BFS (khoảng cách thật) | (trên server) |
| Phối hợp | Nghèo nàn | Tự giãn tuyến, tránh chen | Điều phối toàn cục |
| Điểm yếu | Tranh ô hẹp → kẹt/đâm | — | **Nhạy với độ trễ mạng** |

---

## 4. Cơ chế Replan — tính lại đường (đếm `btReplanCount`)

Mỗi lần `ReplanPath()` được gọi, bộ đếm `btReplanCount` +1. Xe vạch lại đường trong các tình huống:

1. **Theo chu kỳ** — `Time.time >= nextReplanTime`: cứ mỗi `replanInterval` (vd 0.75s), AI "mở mắt" cập nhật vị trí người chơi và các xe khác.
2. **Chưa có đường** — `currentPath.Count == 0`: vừa spawn hoặc vừa đi hết đường cũ.
3. **Bị vật cản phá được** — gọi `ReplanToDestructible()`: vẽ đường ngắn nhất tới thùng/gạch để nhắm bắn.
4. **Sau spatial recovery** — vừa phát hiện chạy lòng vòng, cấm các ô cũ (`pendingBlockedCells`) rồi replan để A\* tìm lối rẽ khác.
5. **Trong quy trình cứu hộ kẹt** — bậc `ForcedReplan` (ép replan ngay) và bậc `ExpandedMask` (mở rộng bán kính né tường rồi replan).

**Khác biệt A\* vs PIBT C# ở replan:**
- **A\*** khi lòng vòng phải **cấm ô thủ công** (`pendingBlockedCells`) rồi mới replan — vì A\* không có trạng thái chung, không tự đổi tuyến.
- **PIBT C#** chỉ cần **replan** — sau mỗi lần replan, vết `_flow` đã đổi nên tuyến mới **tự nhiên khác** tuyến cũ. Ngoài ra PIBT dùng `frankWolfeMs = 15ms` cho mỗi lần replan và có `ResolveGoalCell` để "nắn" ô đích khi bị đồng đội đè.

Vì PIBT né tắc nghẽn **ngay từ khâu lập kế hoạch**, số lần phải chữa cháy bằng replan chỉ **nhỉnh hơn A\* một chút** — A\* vốn đơn giản nên replan ít nhất.

---

## 5. Cơ chế Recovery — thang cứu hộ kẹt (đếm `btRecoveryCount`)

Ba cảm biến "bắt bệnh" kẹt:

1. **Kẹt thời gian** — `Time.time - lastProgressTime > stuckTimeout` (~1.5s): đạp ga mà không nhích nổi 5cm (`progressEpsilon`).
2. **Kẹt cạ tường (scuffing)** — húc vật lý vào vách (`IsTouchingLayers`) + vận tốc ≈ 0 quá 0.4s.
3. **Kẹt không gian (mê cung)** — chạy tốc độ cao nhưng lòng vòng tại chỗ; nếu A\* trả về rỗng thì bắt buộc leo thang cứu hộ.

**Thang cứu hộ leo thang:**

| Bậc | A\* (4 bậc) | PIBT C# (3 bậc) |
|---|---|---|
| 1 | `ForcedReplan` — ép replan ngay | `ForcedReplan` |
| 2 | `Reverse` — cài số lùi + bẻ lái ~0.8s | `Reverse` |
| 3 | `ExpandedMask` — phình bán kính né tường, ép đi ra tim đường | *(bỏ)* → về `None`, tin PIBT tự tìm tuyến qua flow |
| 4 | Lặp lại | — |

> **Vì sao PIBT bỏ được bậc `ExpandedMask`?** `ExpandedMask` là cách A\* "vật lộn" thoát kẹt khi **mù thông tin** về xe khác. PIBT đã có bản đồ luồng chung — chỗ nào đông agent tự né sẵn từ khâu lập kế hoạch — nên chỉ cần lùi + replan là đủ. Đây là minh chứng cụ thể: **phối hợp tốt hơn ⇒ cần ít cơ chế chữa cháy hơn.** Số liệu `btRecoveryCount` trong backtest cho thấy PIBT C# **phục hồi tốt nhất** trong môi trường động.

Riêng **PIBT C++/TCP** có recovery đơn giản hơn nhưng thêm rủi ro: kẹt nhẹ (2.5s) bật cờ xin server tính lại; kẹt nặng (4s) dùng **cú hích tham lam NON-PIBT** (đã log tách biệt). Vì phụ thuộc round-trip mạng, **độ trễ kết nối** làm mode này dễ kẹt hơn ở mật độ cao và môi trường động.

---

## 6. Thực nghiệm — backtest tự động

Hệ thống backtest chạy tự động, xuất CSV để đánh giá định lượng:

- **5 bản đồ × 3 thuật toán × 5 mức agent (6/12/24/36/72)**, môi trường **tĩnh + chướng ngại động** → tổng **900 run**.
- Các số đo dùng chung một schema: `btReplanCount`, `btRecoveryCount`, độ dài đường, thời gian tới đích, va chạm... — nhờ ba mode **chung tầng điều khiển**, so sánh phản ánh đúng thuật toán.

Kết quả định hướng: A\* replan thấp nhất nhưng không phối hợp; PIBT C# replan chỉ nhỉnh hơn A\* mà phối hợp và phục hồi tốt nhất trong môi trường động; PIBT C++/TCP mạnh về "chạy thuật toán gốc" nhưng nhạy với độ trễ mạng nên yếu hơn ở mật độ cao.

---

## 7. Danh mục file mã nguồn cốt lõi

Bản đồ nhanh "file nào làm gì" — dùng khi trình bày hoặc khi thầy hỏi "chỗ đó nằm ở đâu trong code". Sắp theo đúng mạch kiến trúc ở Mục 2.

### 7.1. Nền móng — bản đồ & điều khiển (dùng chung cho cả 3 thuật toán)

| File | Vai trò |
|---|---|
| `MapLoader.cs` | **Đọc file `.map`** MovingAI → dựng lưới ô + gạch/tường/collider; cung cấp `IsWalkable`, `CellToWorld`, `WorldToCell`, `TryFindWalkableNear`. Là "ngôn ngữ chung" mọi spawner/pathfinder phải đi qua. |
| `TankController.cs` | **Cửa ngõ điều khiển duy nhất** (`HandleMoveBody` / `HandleTurretMovement` / `HandleShoot`) cho cả người chơi lẫn cả 3 AI — nền của "nguyên tắc công bằng". |
| `TankMover.cs` | Chuyển lệnh lái thành **chuyển động vật lý** (Rigidbody2D, gia tốc/xoay theo `TankMovementData`). |
| `Turret.cs` / `AimTurret.cs` | Xoay nòng & bắn đạn (dùng `ObjectPool`). |
| `Bullet.cs` | Đạn: `Linecast` chống xuyên tường tốc độ cao → gọi `Damagable.Hit`. |
| `Damagable.cs` | Máu & sự kiện `OnDead` (Eagle, xe tăng, tường phá được). |

### 7.2. Tầng 1 — A\* baseline

| File | Vai trò |
|---|---|
| `GridAStarPathfinder.cs` | **Thuật toán A\* tính đường, xếp hạng ô bằng `f = g + h`** (Manhattan), binary min-heap + lazy deletion. Bộ tìm đường baseline. |
| `Pathfinding/GridNavMask.cs` | **Chống cạ tường + trừ hao bề rộng xe**: cấm cứng ("phình" vật cản) + phạt mềm (soft cost) ô sát tường. |
| `GridEnemyAgent.cs` | **Tài xế xe địch mode A\***: cây quyết định (bắn > thoát kẹt > phá vật cản > đi đường), xoay-rồi-tiến, phát hiện kẹt, thang cứu hộ 4 bậc; né đồng đội qua `blockedCells`. |

### 7.3. Tầng 2 — PIBT C#

| File | Vai trò |
|---|---|
| `PIBTPlanner.cs` | **Bộ não dẫn hướng luồng** (port Tầng 1 flow-guidance của bản C++ đoạt giải): bản đồ luồng `_flow` chung, flow-aware A\* (`op_flow` + `vertex_flow`), Frank-Wolfe, heuristic reverse-BFS. |
| `GridPIBTPathfinder.cs` | **Lớp bọc** để agent gọi `PIBTPlanner` giống cách gọi A\* (đổi ô ↔ chỉ số phẳng, truyền `agentId`, `budgetMs`). |
| `GridEnemyAgentPIBT.cs` | **Tài xế mode PIBT C#**: bản song sinh của `GridEnemyAgent`, chung tầng điều khiển, chỉ khác bộ tìm đường + đăng ký `agentId` + thang cứu hộ rút gọn còn 3 bậc. |

### 7.4. Tầng 3 — PIBT C++/TCP

| File | Vai trò |
|---|---|
| `PIBTTcpClient.cs` | **Đường dây TCP/JSON-lines** nối Unity ↔ server C++ (`hello` validate map → `plan_step` mỗi tick → `shutdown`/`reset`), có timeout mọi khâu. |
| `GridEnemyAgentPIBT_TCP.cs` | **"Tài xế câm"**: không tự tính đường, nhận ô đích từ coordinator rồi lái tới cho mượt; có fallback "cú hích tham lam" NON-PIBT khi server kẹt. |
| `PIBTWebRelayClient.cs` | Biến thể client nối server C++ qua relay web (khi đưa server lên cloud). |

### 7.5. Điều phối kịch bản (spawn map + Eagle + địch)

| File | Vai trò |
|---|---|
| `MapTankTestBootstrap.cs` | Khởi động scene: build map + spawn người chơi + camera, rồi gọi scenario bootstrap. |
| `MapScenarioBootstrap.cs` | Spawn **Eagle + địch mode A\***, gắn `GridEnemyAgent`, tắt AI cũ. |
| `MapScenarioBootstrapPIBT.cs` | Bản scenario cho **mode PIBT C#** (gắn `GridEnemyAgentPIBT`). |
| `MapScenarioBootstrapPIBT_TCP.cs` | **Coordinator/"nhạc trưởng" mode TCP**: mỗi tick gom vị trí mọi xe → `client.PlanStep` → phát ô đích cho từng `GridEnemyAgentPIBT_TCP`. |

### 7.6. Thực nghiệm backtest (xuất số liệu so sánh)

| File | Vai trò |
|---|---|
| `Backtest/BacktestRunner.cs` | **Điều phối 900 run** (5 map × 3 thuật toán × 5 mức agent), chuyển kịch bản, ghi CSV. |
| `Backtest/BacktestData.cs` / `BacktestMode.cs` | Schema số đo chung (`btReplanCount`, `btRecoveryCount`, độ dài đường, thời gian, va chạm…) & định nghĩa các mode. |
| `Backtest/DynamicObstacleSpawner.cs` | Sinh/xóa **chướng ngại động** (thùng sắt) → tạo môi trường động cho backtest. |
| `Backtest/BacktestResultChart.cs` | Vẽ biểu đồ kết quả ngay trong Unity. |
| `Backtest/BacktestSpawn.cs` / `BacktestConfigUI.cs` / `BacktestCameraController.cs` | Spawn theo mật độ, UI cấu hình, camera quan sát toàn map. |

### 7.7. Multiplayer & triển khai (ngoài phạm vi thuật toán, nêu cho đủ)

| File | Vai trò |
|---|---|
| `Multiplayer/LanGameCoordinator.cs` | **Quản trò phía Server** (server-authoritative): nối bộ đàm với xe, gom trạng thái nén gửi client 30Hz. |
| `Multiplayer/LanClientView.cs` | **Mắt client**: nhận toạ độ từ server, vẽ "bóng ma" xe (không có vật lý → chống hack). |
| `Multiplayer/LanNetworkBridge.cs` | **Bộ đàm người chơi**: throttle 30Hz + client prediction che độ trễ. |
| `Multiplayer/LanDiscovery.cs` / `LanSessionManager.cs` / `RelayManager.cs` | Dò LAN, quản phiên, chế độ Relay (mã 6 ký tự). |
| `Multiplayer/DedicatedServerBootstrap.cs` / `InternetSessionClient.cs` | Dedicated server (GCP) + client kết nối qua Internet (link mời). |

---

## 8. Kết luận

Tổng hợp lại, mỗi thuật toán có thế mạnh riêng: **A\*** đơn giản, ổn định và có số lần tái hoạch định thấp nhất nhưng **không phối hợp giữa các agent**; **PIBT C#** phối hợp và thoát kẹt tốt với chi phí replan chỉ nhỉnh hơn A\*, đồng thời **phục hồi tốt nhất trong môi trường động**; **PIBT C++/TCP** tách phần lập kế hoạch sang máy chủ C++ nhưng là biến thể **nhạy cảm nhất với độ trễ kết nối**, nên yếu hơn ở mật độ cao và môi trường động.
