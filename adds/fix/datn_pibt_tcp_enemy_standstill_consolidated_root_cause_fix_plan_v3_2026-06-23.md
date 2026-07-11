# PLAN V3 (HỢP NHẤT): Root-cause & fix bug Enemy đứng yên / đi sai hướng trong PIBT TCP

Ngày lập: 2026-06-23
Tác giả: điều tra dựa trên source hiện tại (không phải bản cũ trong các plan v1/v2).
Phạm vi: Mode `PIBT_TCP`, server C++ chạy local `./build/pibt_tcp_server --host 0.0.0.0 --port 7777`
(client Unity đã trỏ về `127.0.0.1:7777`). Cả Single Play lẫn Backtest.

> **Mục tiêu cuối:** Enemy di chuyển hợp lý trên 5/5 map (Alpha32/random32, Mansion, Chantry,
> Gallows, Maze128) để **Backtest chạy được và lấy số liệu ghi vào quyển ĐATN**.

---

## 0. TL;DR — kết luận điều tra

Triệu chứng người dùng báo:
- Alpha32 single: 6 enemy thì 3 con tìm về Eagle và bắn, 3 con đâm vào tường / đi ra mép map rồi đứng yên.
- Các map khác: enemy chỉ nhúc nhích một chút rồi đứng hẳn ⇒ backtest không lấy được số liệu.

Đây **không phải** lỗi kết nối (server nhận `plan_step` đều) và **không phải** lỗi mapping toạ độ tĩnh
(đã verify `CellToWorld`/`OrientationToDelta`/`ReadAgentOrientation`/`AgentFlat` đều nhất quán và khớp
convention LoRR `0=east,1=south,2=west,3=north`).

Lỗi nằm ở **mô hình thực thi (execution model)** giữa planner rời rạc (C++) và physics liên tục (Unity),
cộng với hành vi PIBT khi nhiều agent chung một goal. Cụ thể, source hiện tại có 5 vấn đề chồng nhau:

1. **Unity vứt bỏ `nextLoc` của server** và tự "dead-reckon" cell kế tiếp ⇒ dễ lệch với plan thật.
2. **Trộn nguồn sự thật**: gửi `loc` = cell vật lý thật nhưng `orientation` = orientation logic suy diễn.
3. **CR/CCR không được xoay vật lý**: tank đứng im 1 tick (0.5s) mỗi lần xoay ⇒ enemy bò cực chậm,
   trong khung thời gian backtest trông như "đứng yên".
4. **Tick mở vòng (open-loop)**: không chờ agent commit action cũ, không phát hiện kẹt, không recovery.
5. **Một goal chung (Eagle) cho cả 6 agent** ⇒ PIBT đẩy các agent ưu tiên thấp ra xa goal; với heuristic
   không hoàn hảo quanh tường, chúng trượt vào tường / mép map (khớp đúng triệu chứng "3 con đi sai").

→ Hướng fix chính: **bám sát plan của server** (dùng `nextLoc`, báo cáo orientation vật lý), **đóng vòng
thực thi** (action-ack gate + stuck recovery), và **phân tán goal quanh Eagle**. Mục 6 của server (heuristic/map)
cần verify bằng evidence trước khi đụng C++.

---

## 1. Hiện trạng SOURCE (đính chính các plan cũ)

> ⚠️ Các plan `*_mansion_*`, `*_non_alpha_*`, `*_5map_*_v2_*` mô tả phiên bản code đã có
> `NotifyServerAction()`, `_hasRotationTarget`, `PlanStepDetailed()`, `FlatToBuildCell()`, `DebugSnapshot()`,
> dùng `nextLoc`. **Những thứ đó KHÔNG còn trong source hiện tại** — code đã quay về bản tối giản. Plan V3 này
> mô tả đúng code đang chạy và thay thế (supersede) cả 3 plan kia về mặt thực thi.

Luồng thực tế hiện tại:

- `MapScenarioBootstrapPIBT_TCP.Update()` — mỗi `tcpTickInterval` (0.5s, ~2 tick/s) gọi `DoStepAsync()`,
  chỉ gate bằng `_serverReady`, `_client.IsConnected`, `_stepInFlight`. **Không có action-ack gate.**
- `BuildStepData(rows, cols)` → mỗi agent: `(id=i, loc=AgentFlat(i), orientation=AgentOrientation(i), goalLoc=EagleFlat())`.
  - `AgentFlat(i)` = cell vật lý thật (`WorldToCell` của body), clamp vào bounds.
  - `AgentOrientation(i)` = `_agentOrientations[i]` (logic dead-reckoned, chỉ đổi khi CR/CCR).
  - `goalLoc` = **Eagle flat, giống hệt nhau cho cả 6 agent.**
- `PIBTTcpClient.PlanStep()` → `ParseActions()` parse DTO có cả `action` lẫn `nextLoc`, nhưng **chỉ trả về mảng
  `action` (string), vứt `nextLoc`.**
- `ApplyStepActions()` → `ApplyAction(i, action, …)`:
  - `FW`: `next = realCell + OrientationToDelta(logicalOri)`; nếu in-bounds + walkable thì target = next, else giữ cell.
  - `CR`: `logicalOri = (ori+1)&3`; target = **cell hiện tại** (đứng im).
  - `CCR`: `logicalOri = (ori+3)&3`; target = cell hiện tại (đứng im).
  - `W`: target = cell hiện tại.
  - rồi `_agents[i].SetNextTarget(targetCell)`.
- `GridEnemyAgentPIBT_TCP`:
  - `Update()`: nếu có shoot target → đứng bắn; else nếu `_hasTarget` → `SteerTowardTarget()`.
  - `SetNextTarget(cell)`: `_currentTarget = cell; _hasTarget = true;` (kể cả khi cell == currentCell).
  - `SteerTowardTarget()`: nếu `dist <= waypointReachDistanceStraight (0.3)` → `HandleMoveBody(zero)` (đứng);
    else xoay-rồi-chạy bằng dot/cross. **Không có khái niệm action server, không xoay riêng cho CR/CCR,
    không phát hiện kẹt.**

Hệ quả trực tiếp:
- Mỗi CR/CCR = tank đứng im 0.5s. Quay 90° tốn 1 tick đứng im trước khi đi 1 cell; quay 180° tốn 2 tick.
- Khi server trả `W` hoặc xoay qua lại, agent ở `target==currentCell` vĩnh viễn, velocity 0, **không gì phá vòng**.

---

## 2. Root-cause, xếp theo độ chắc chắn

### RC1 — Execution model phí tick + CR/CCR không xoay vật lý (CHẮC CHẮN là tác nhân)
Bằng chứng code ở Mục 1. Ngay cả khi mọi thứ khác đúng, enemy vẫn bò ~1 cell mỗi 1–1.5s và đứng im trong các
tick xoay. Trong khung backtest có giới hạn thời gian, điều này một mình đã đủ làm "không có số liệu di chuyển".

### RC2 — Unity bỏ `nextLoc`, dead-reckon từ state trộn (CAO)
`ApplyAction` tự suy ra cell kế tiếp từ `realCell + OrientationToDelta(logicalOri)` thay vì dùng `nextLoc`
authoritative của server. Vì `loc` gửi đi là cell vật lý thật còn `orientation` là logic suy diễn, nếu body
trôi/đè cell khác hoặc orientation logic lệch heading thật, cell suy ra sẽ khác plan server ⇒ oscillation/đứng.

### RC3 — Tick mở vòng vs planner rời rạc (CAO, đặc biệt map hẹp)
Server là planner 1-bước kỳ vọng lock-step. Unity bắn `plan_step` theo đồng hồ bất kể tank đã commit action
cũ chưa. Map mở (Alpha32) có "slack" nên che lỗi; map có hành lang/tường thì drift nhỏ thành deadlock ⇒
"đi một chút rồi đứng".

### RC4 — Một goal chung ⇒ PIBT đẩy agent ưu tiên thấp vào tường (CAO — giải thích đúng "3/6 đi sai")
Cả 6 agent nhận `goalLoc = EagleFlat`. PIBT (reactive, priority inheritance + backtracking) để agent ưu tiên cao
chiếm goal và **đẩy agent ưu tiên thấp ra xa**. Với heuristic không hoàn hảo quanh chướng ngại, các agent bị đẩy
trượt về phía tường / mép map rồi kẹt. Đây là cơ chế tự nhiên cho "3 con về Eagle, 3 con đâm tường ra mép" trên
map mở, và "gần như cả team kẹt" trên map cấu trúc.

### RC5 — Không có stuck-detection/recovery (CHẮC CHẮN là tác nhân của "đứng yên vĩnh viễn")
Khi `target==currentCell` lặp lại (W liên tục, FW vào ô bị chặn → giữ nguyên, hoặc xoay qua lại), không có cơ chế
phát hiện + replan bằng physical state + fallback. Agent đứng im không lý do, không log.

### RC6 — Tính đúng của map/heuristic phía server (TRUNG BÌNH — cần inspect C++)
Nếu server dùng heuristic kiểu Manhattan/Euclid (không BFS né tường) thì local-minima/đâm tường là hệ quả tất
yếu của PIBT. Hoặc nếu server dựng map bị lật Y/transpose so với Unity thì "về phía goal" của server = "vào tường"
của Unity. Cần evidence (Mục 3) + xem `~/projectY/Server-PIBT-TeamNoMan-sSky` trước khi sửa C++.

---

## 3. M0 — Thí nghiệm quyết định (làm TRƯỚC khi sửa)

Mục tiêu: tách bạch RC2/RC3/RC4/RC6 bằng dữ liệu thật, vì chúng đều biểu hiện ra "đứng yên".

### 3.1 Instrumentation tối thiểu (có cờ bật/tắt `enableTcpTrace`, mặc định off)
- `PIBTTcpClient`: lưu `LastPlanRawResponse`, đếm action `FW/CR/CCR/W/UNKNOWN` mỗi tick, và **expose `nextLoc`**
  đã parse (đang bị bỏ) để so sánh.
- `MapScenarioBootstrapPIBT_TCP`: trace bounded (10 tick đầu, sau đó mỗi 30 tick, và ngay khi cả team velocity≈0).
  Mỗi agent log: `cell`, `loc`, `logicalOri`, `physicalOri (=ReadAgentOrientation)`, `goalLoc`, `action`,
  `serverNextLoc`, `deadReckonNextCell`, `FlatToCell(serverNextLoc)`.
- `GridEnemyAgentPIBT_TCP`: expose `HasMovementTarget`, `MovementTarget`, `IsShooting`, và velocity
  (`tankController.tankMover` Rigidbody2D linearVelocity).

### 3.2 Quy trình chạy (Single Play, qua MCP nếu Unity Editor mở)
1. WSL: `./build/pibt_tcp_server --host 0.0.0.0 --port 7777` (giữ terminal để đọc log server).
2. Unity: clear console.
3. `PlayerPrefs SelectedAlgorithm=PIBT_TCP`, `SelectedMapFile=Assets/MapData/random-32-32-10.map`.
4. Load `Assets/Scenes/MapF_TankTest_PIBT.unity`, Play, skip Placement UI nếu có.
5. Snapshot 3s / 10s / 20s: action-counts, per-agent state ở trên, screenshot.
6. Lặp lại với `ht_mansion_n.map` và `maze-128-128-10.map`.
7. Lưu cả **log terminal server** (tìm chữ `sanitize`, `wrap`, `blocked`, `W`).

### 3.3 Bảng quyết định
| Evidence | Kết luận | Nhánh fix |
| --- | --- | --- |
| `FlatToCell(serverNextLoc) != deadReckonNextCell` thường xuyên | Dead-reckon lệch plan | **F-A** (dùng nextLoc) |
| `logicalOri != physicalOri` kéo dài > 1–2 tick | Trộn nguồn / xoay không thực thi | **F-A + F-C** |
| Nhiều `W`/`CR/CCR`, velocity≈0, agent bị đẩy xa Eagle | PIBT congestion một goal | **F-D** (phân tán goal) |
| Nhiều `FW`, `nextCell != currentCell`, velocity≈0 | Physics kẹt corridor | **F-E** (steering/clearance) |
| Server log `sanitize/wrap/blocked` lặp, hoặc nextLoc đi vào tường của Unity | Map/heuristic server | **F-F** (inspect C++) |
| Agent đứng `target==currentCell` không reason | Thiếu recovery | **F-B** (stuck recovery) — luôn cần |

---

## 4. Thiết kế fix (phased). Mọi thay đổi chỉ trong 3 file TCP, KHÔNG đụng local PIBT/A*.

Nguyên tắc: bám plan server, đóng vòng thực thi, không deadlock cả team vì 1 tank, mọi fallback có log.

### F-A (M1) — Bám `nextLoc` + orientation vật lý làm source of truth  *(ưu tiên cao nhất, ít rủi ro)*
1. `PIBTTcpClient`: thêm API trả về cả `nextLoc` (giữ `ParseActions` cũ cho tương thích; thêm
   `PlanStepDetailed` hoặc out-param `int[] nextLocs`).
2. `MapScenarioBootstrapPIBT_TCP.ApplyStepActions`: nếu `nextLoc >= 0` và hợp lệ → `targetCell = FlatToCell(nextLoc)`
   (bỏ dead-reckon cho FW). Dead-reckon chỉ còn là fallback khi server không trả nextLoc.
3. `BuildStepData`: gửi `orientation = ReadAgentOrientation(agent)` (heading vật lý thật), bỏ `_agentOrientations`
   logic làm nguồn gửi đi. Như vậy server luôn plan trên heading Unity thật, không cần đoán.
4. Giữ invariant log: `assert FlatToCell(nextLoc) ∈ {currentCell, 4-neighbors}`; nếu sai → log, không áp.

### F-B (M1) — Stuck detection + recovery + diagnostic  *(luôn cần, cứu backtest khỏi stall im lặng)*
- `GridEnemyAgentPIBT_TCP`: đếm thời gian ở cùng cell (`btSameCellSeconds`). Nếu > threshold (vd 2.5s) và không
  shooting → set cờ `needsForcedReplan` + `LastStallReason`.
- `MapScenarioBootstrapPIBT_TCP`: khi có `needsForcedReplan`, gửi `plan_step` ngay (phá gate) với physical state,
  và nếu vẫn không tiến triển sau N lần → fallback **F1** (Mục 5) cho riêng agent đó (quarantine), không để cả team đứng.

### F-C (M2) — Action-ack gate (đóng vòng tick theo commit, có timeout)
- Thêm `ServerActionState {Pending,Executing,Committed,Failed}` + `ActionTargetCell` trong agent.
- `CR/CCR` complete khi heading vật lý đạt hướng đích trong threshold; `FW` complete khi tới gần center
  `ActionTargetCell`; `W` complete nhanh.
- `Update()` coordinator: không tick mới theo đồng hồ nếu phần lớn agent còn `Executing`; **timeout per-agent +
  forced replan + quarantine** để 1 tank kẹt không deadlock team.
- Khi CR/CCR: **xoay vật lý tại chỗ** (steering forward≈0) thay vì đứng im đếm counter.

### F-D (M2) — Phân tán goal quanh Eagle  *(nhắm thẳng triệu chứng "3 con đâm tường")*
- Thay vì `goalLoc = EagleFlat` cho cả 6 agent, cấp **staging goal** riêng: vòng các ô walkable quanh Eagle
  (cùng connected component, không trùng nhau). Agent nào vào range bắn thì giữ goal=Eagle/loc.
- Giảm congestion ⇒ PIBT bớt đẩy agent ưu tiên thấp ra tường.

### F-E (M3) — Corridor/clearance hardening (chỉ làm nếu evidence chỉ ra physics kẹt)
- Spawn + staging goal yêu cầu clearance theo collider (Physics2D.OverlapBox), không chỉ grid walkable.
- Steering: khi lệch góc lớn, xoay tại chỗ thay vì forward cọ tường; detect scuff theo layer cụ thể.

### F-F (M3, có điều kiện) — Verify/sửa phía server C++
Chỉ khi M0 cho thấy `nextLoc` của server dẫn vào tường của Unity hoặc server sanitize/wrap lặp:
- Xem `~/projectY/Server-PIBT-TeamNoMan-sSky`: cách parse `symbols`, indexing `loc = row*width+col`, convention
  orientation, và **heuristic** (BFS né tường hay Manhattan?). DefaultPlanner có thể là greedy.
- Nếu heuristic không né tường → đó là gốc của local-minima; cân nhắc planner BFS-heuristic hoặc bù bằng F-D + F1.
- Mọi patch server tách riêng patch Unity, log before/after.

---

## 5. Yêu cầu riêng cho BACKTEST (đích cuối của user)

- `BacktestRunner` tìm `MapScenarioBootstrapPIBT_TCP` qua `FindFirstObjectByType` và gọi `ShutdownGracefully()`
  giữa các map — luồng này phải giữ nguyên, không hồi quy.
- Backtest **headless/nhanh**: action-ack gate phải có timeout ngắn để không kéo dài mỗi map; ưu tiên agent
  *tiến triển* hơn là chờ tuyệt đối.
- **Fallback F1 (hybrid local 1-step):** nếu server trả `W`/invalid cho 1 agent N tick liên tục nhưng map
  reachable, agent tạm dùng greedy 1-cell về phía goal rồi trả lại cho TCP. Đảm bảo vẫn lấy được số liệu di chuyển
  thay vì hàng dữ liệu toàn 0. Phải log rõ "fallback" để không nhầm là kết quả thuần PIBT TCP khi viết ĐATN.
- Metric đã có trong agent (`btReplanCount`, `btCellsVisited`, `btShotCount`, `btRecoveryCount`) phải tiếp tục
  cập nhật đúng nghĩa sau khi đổi execution model (đặc biệt `btCellsVisited` không được tăng giả do dead-reckon).

---

## 6. Validation & acceptance

### Static
`validate_script`: `PIBTTcpClient.cs`, `MapScenarioBootstrapPIBT_TCP.cs`, `GridEnemyAgentPIBT_TCP.cs`
(+ `MapLoader.cs` nếu thêm clearance helper). Console 0 error.

### Runtime matrix (mỗi map ≥ 40s sau skip placement)
`random-32-32-10` (baseline), `ht_mansion_n`, `ht_chantry`, `lt_gallowstemplar_n`, `maze-128-128-10`.

Pass mỗi map khi:
- `_serverReady=true`, `_frame ≥ 10`, không parse/protocol error.
- ≥ 4/6 agent có `btCellsVisited ≥ 2` trong 20s (trừ agent đang bắn hợp lệ).
- ≥ 1 agent gây damage Eagle hoặc vào range bắn trong thời gian test.
- Không agent nào đứng > 10s mà `LastStallReason` rỗng.
- Server log không sanitize-loop vô hạn cho cùng agent.

### Backtest end-to-end
- Chạy backtest PIBT_TCP qua đủ 5 map, export ra `BacktestResults/` với các cột metric khác 0 và hợp lý.
- So sánh được với A*/local PIBT trong cùng schema (xác nhận với mapf-metrics nếu cần).

### Regression
- Alpha32 TCP không tệ hơn hiện tại.
- Local PIBT (`GridEnemyAgentPIBT`/`MapScenarioBootstrapPIBT`) và A* không đổi (không sửa file của chúng).

---

## 7. Files dự kiến sửa
- `Assets/Scripts/PIBTTcpClient.cs` (expose nextLoc + action counts/trace).
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs` (nextLoc apply, physical orientation, action-ack gate,
  staging goal, forced replan/quarantine, trace).
- `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs` (action state, in-place rotate, stuck detection, debug API).
- `Assets/Scripts/MapLoader.cs` *(chỉ nếu cần clearance helper)*.
- *(có điều kiện)* `~/projectY/Server-PIBT-TeamNoMan-sSky` — patch riêng, có evidence.

## 8. Rủi ro & rollback
- Action-ack gate quá chặt → chậm/deadlock: bắt buộc timeout ngắn + quarantine.
- Bỏ dead-reckon mà server không trả nextLoc ổn định → giữ fallback dead-reckon có log.
- Staging goal có thể làm agent không "lao thẳng" Eagle: chỉ dùng tới khi vào range thì chuyển goal=Eagle.
- Rollback nhanh: mọi thay đổi khu trú trong 3 file TCP; revert là về hành vi hiện tại.

## 9. Thứ tự thực hiện đề xuất
1. M0: instrumentation + chạy 3 map (random32/mansion/maze), lưu evidence vào
   `adds/fix/evidence/pibt_tcp_v3_2026-06-23/`.
2. Đọc bảng quyết định (Mục 3.3), chốt nhánh.
3. F-A + F-B (gần như chắc chắn cần): nextLoc + physical orientation + stuck recovery.
4. Chạy lại 3 map. Nếu map hẹp vẫn kẹt → F-C (action-ack) + F-D (staging goal).
5. Nếu vẫn đâm tường/đứng → F-E và/hoặc F-F (verify server map/heuristic).
6. Backtest 5/5 map, export, kiểm tra số liệu. Lưu artifact.

## 10. Quan hệ với plan cũ
Plan V3 **thay thế phần thực thi** của:
- `datn_pibt_tcp_mansion_enemy_standstill_root_cause_fix_plan_2026-06-22.md`
- `datn_pibt_tcp_non_alpha_maps_stall_root_cause_fix_plan_2026-06-22.md`
- `datn_pibt_tcp_5map_full_coverage_fix_plan_v2_2026-06-22.md`

Giữ lại từ chúng: tinh thần "invariant + evidence trước khi sửa", matrix 5 map, và ý tưởng action-ack/staging goal
(đúng nhưng từng được viết cho một bản code khác). Khác biệt cốt lõi của V3: chỉ ra rằng code hiện tại **đã bỏ
`nextLoc` và xoay-đứng-im**, nên fix rẻ nhất & chắc nhất là quay lại bám `nextLoc` + orientation vật lý + recovery,
trước khi nghĩ tới các lớp phức tạp hơn.
