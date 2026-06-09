# Kế hoạch xử lý Enemy jitter và đi ra ngoài map trong `PIBT_TCP`

Ngày lập: 2026-06-07
Repo Unity: `D:\2025.2\DATN\projectY`
Server TCP: `/home/west2light/projectY`

## Mục tiêu

Xử lý hai triệu chứng còn lại trong scene `MapF_TankTest_PIBT_TCP`:

- Enemy tank di chuyển giật, cảm giác nhảy từ vị trí này sang vị trí khác.
- Enemy tank có xu hướng đi ra ngoài biên map, kẹt ở ngoài map và không còn ưu tiên tiến về `EagleBase`.

Plan này chỉ lập hướng triển khai. Chưa sửa code trong lượt lập plan.

## Kết luận nghiên cứu hiện tại

### 1. Static map không phải điểm nghẽn chính

Scene đang dùng `random-32-32-10.map`, full build window:

- `maxBuildWidth = 0`
- `maxBuildHeight = 0`
- `mapOffsetX = 0`
- `mapOffsetY = 0`
- `eagleCell = (16, 16)`
- enemy spawn ở gần các góc/biên: `(30,1)`, `(1,30)`, `(30,30)`, `(16,1)`

Vì enemy spawn gần biên, mọi lỗi `nextLoc`/bounds sẽ lộ rất nhanh.

### 2. Unity hiện chưa hard-reject `FW` ra ngoài map

Trong `GridEnemyAgentPIBTTcp.ApplyAction(FW)`:

- `targetCell = LocToCell(dto.nextLoc, mapLoader.BuildWidth)`
- nếu `goalBiasCorrectBadFw == false` thì `TryCorrectBadForwardTarget(...)` không kiểm tra gì.
- code chỉ reject `targetCell == currentCell` và `!IsAdjacent(currentCell, targetCell)`.
- chưa reject bắt buộc `!IsCellInMap(targetCell)` hoặc `!mapLoader.IsWalkable(targetCell)`.

Điều này nguy hiểm ở biên map. Ví dụ với map width 32:

- agent tại hàng cuối `y=31`, server trả `FW` hướng south.
- `nextLoc = loc + 32`, decode thành cell `(x,32)`.
- `(x,32)` vẫn adjacent với `(x,31)`.
- Unity chấp nhận và `CellToWorld((x,32))` tạo world position nằm ngoài map.

Khi agent đã ra ngoài map, `TryCommitCurrentCellFromPosition()` có thể commit cell ngoài biên vì hiện không kiểm tra `IsCellInMap(observedCell)` trước khi `currentCell = observedCell`. Lượt sau Unity gửi `loc` ngoài range cho server, làm planner càng dễ sinh action sai tiếp.

### 3. Server trả `nextLoc` bằng phép cộng delta, chưa post-filter output

Trong server C++:

- `UnityStartKitAdapter::NextLoc(...)` tính `FW` bằng `s.location + delta[s.orientation]`.
- `PlannerSession::Plan(...)` đưa `NextLoc(...)` thẳng vào response.
- Không có lớp post-filter cuối cùng để đảm bảo action trả về Unity là in-bounds, walkable, không wrap hàng, không đi ra ngoài map.

Action model trong server có hàm validate, nhưng response adapter vẫn cần guard cuối vì Unity là runtime vật lý. Một action sai duy nhất cũng đủ đẩy tank ra ngoài biên.

### 4. Jitter có khả năng đến từ velocity quá nhỏ cộng với snap/correction

Trong `TankMover.MoveWorldDirection()` path, `FixedUpdate()` đang set:

```csharp
rb2d.linearVelocity = worldMovementVector * movementData.maxSpeed * Time.fixedDeltaTime;
```

`Rigidbody2D.linearVelocity` là vận tốc theo đơn vị/giây. Nhân thêm `Time.fixedDeltaTime` làm vận tốc thực tế nhỏ hơn nhiều lần. Hệ quả có thể xảy ra:

- Enemy mất rất lâu để tới center cell.
- pending `FW` bị kéo dài, dễ kích hoạt recovery/correction hoặc plan step chờ lâu.
- khi tới threshold, code `SnapBodyToCurrentCellCenter()` kéo tank về đúng center, nhìn như giật/nhảy.

Ngoài ra, nếu agent drift ra gần biên/outside, `WorldToCell()` dùng `RoundToInt` có thể đổi cell đột ngột, làm `currentCell` và vị trí vật lý lệch nhau.

## Nguyên tắc sửa

1. Unity client không được drive tới cell ngoài map dù server trả gì.
2. Server không được trả `FW nextLoc` invalid cho Unity.
3. Nếu trạng thái Unity đã bẩn, phải recover về cell walkable hợp lệ trước khi gửi `plan_step`.
4. Giảm snap cưỡng bức trong lúc di chuyển; chỉ snap khi đã gần center và sau khi action hợp lệ.
5. Không dùng `goalBiasCorrectBadFw` làm cơ chế an toàn chính. Bounds/walkable validation phải luôn bật; goal-bias chỉ là recovery phụ.

## Phase 0 - Reproduce và log tối thiểu

Mục tiêu: bắt được bằng chứng `nextLoc`, `targetCell`, `currentCell`, `goalCell`, `bodyPos` tại thời điểm tank đi ra ngoài hoặc jitter.

Việc cần làm:

- Bật `debugTcpTrace` cho `MapScenarioBootstrapPIBTTcp`.
- Giữ diagnostic `PIBT_TCP_DIAG` hiện có.
- Thêm log có cooldown cho các case:
  - `server_fw_invalid_bounds`
  - `server_fw_blocked_cell`
  - `observed_cell_outside`
  - `agent_state_outside_before_send`
  - `goal_cell_invalid`
  - `snap_distance_large`
- Log phải có: `agentId`, `req`, `currentCell`, `targetCell`, `goalCell`, `nextLoc`, `loc`, `bodyPos`, `rbPos`, `distToCenter`, `action`.

Tiêu chí xong:

- Có thể nhìn console và biết lỗi xuất phát từ server action, Unity state drift, hay snap/movement.

## Phase 1 - Hard guard ở Unity trước khi execute `FW`

File chính: `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`

Việc cần làm:

- Tạo helper:
  - `IsCellInBuildBounds(Vector2Int cell)`
  - `IsCellValidForMovement(Vector2Int cell)` = in build bounds và `mapLoader.IsWalkable(cell)`.
  - `TryDecodeServerTarget(ActionDto dto, out Vector2Int cell, out string reason)`.
- Trong `ApplyAction(FW)`, sau khi decode `targetCell`, bắt buộc reject nếu:
  - `nextLoc < 0 || nextLoc >= BuildWidth * BuildHeight`
  - `!IsCellInBuildBounds(targetCell)`
  - `!mapLoader.IsWalkable(targetCell)`
  - `!IsAdjacent(currentCell, targetCell)`
- Nếu invalid:
  - set action về `W`
  - `StopMovement()`
  - `ClearForwardMoveTracking()`
  - log `server_fw_invalid_*`
  - gọi `RequestImmediatePlanStep()` để xin action mới.
- Chỉ cho `BeginForwardMove()` khi target hợp lệ tuyệt đối.

Lưu ý:

- `IsCellInMap()` hiện chỉ đúng khi build window bắt đầu ở `(0,0)`. Nên đặt tên rõ là build bounds hoặc dùng `BuildStartX/BuildStartY` nếu muốn robust cho map offset.
- Với scene hiện tại offset bằng 0, guard `0 <= x < BuildWidth`, `0 <= y < BuildHeight` là đủ để chặn đi ra ngoài.

Tiêu chí xong:

- Dù server trả `nextLoc` ngoài range, Unity không drive tank ra ngoài map.

## Phase 2 - Recover state nếu body/currentCell đã ra ngoài map

File chính: `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`

Việc cần làm:

- Sửa `TryCommitCurrentCellFromPosition()`:
  - nếu `observedCell` ngoài build bounds hoặc không walkable, không commit.
  - log `observed_cell_outside`.
  - thử recover về cell hợp lệ gần nhất.
- Thêm helper `TryRecoverToNearestValidCell()`:
  - lấy `currentCell` nếu còn hợp lệ.
  - nếu không, lấy `mapLoader.TryFindWalkableNear(observedCell, out recoveredCell)`.
  - set body về `CellToWorld(recoveredCell)`.
  - set `currentCell = recoveredCell`, `targetCell = recoveredCell`.
  - clear pending movement/rotation/recovery.
  - request immediate plan step.
- Trước khi `BuildAgentState()` gửi lên server, assert `currentCell` và `goalCell` hợp lệ:
  - nếu `currentCell` invalid thì recover.
  - nếu `goalCell` invalid thì resolve lại bằng `TryFindWalkableNear(eagleTarget.position cell)`.

Tiêu chí xong:

- Agent không bao giờ gửi `loc` ngoài range cho server.
- Nếu một tank đã lỡ ra ngoài map từ frame trước, nó được kéo về cell walkable hợp lệ thay vì tiếp tục gửi state bẩn.

## Phase 3 - Server-side action post-filter

File chính bên server:

- `/home/west2light/projectY/src/UnityStartKitAdapter.cpp`
- `/home/west2light/projectY/src/PlannerSession.cpp`

Việc cần làm:

- Thêm helper server:
  - `bool IsLocInMap(loc, env)`
  - `bool IsLocWalkable(loc, env)`
  - `bool IsAdjacentNoWrap(from, to, cols)`
  - `Action SanitizeAction(State s, Action a, SharedEnvironment env, string* reason)`
- Trước khi build response trong `PlannerSession::Plan`, filter từng action:
  - nếu `FW` tạo `nextLoc` invalid/outside/wall/row-wrap thì đổi action thành `W`, `nextLoc = s.location`.
  - log warning một dòng: session, request, agent, loc, orientation, rawAction, rawNextLoc, reason.
- Validate input từ Unity trong `ApplyAgents(...)`:
  - reject hoặc clamp/log nếu `loc`/`goalLoc` ngoài range.
  - tốt nhất: nếu invalid input, trả all `W` kèm error để Unity recover, không để planner chạy với state ngoài map.

Tiêu chí xong:

- Server response không còn `FW nextLoc` ngoài map.
- Nếu Unity gửi state invalid, server không im lặng chạy planner với dữ liệu hỏng.

## Phase 4 - Sửa velocity/jitter trong `TankMover`

File chính: `Assets/Scripts/TankMover.cs`

Việc cần làm:

- Kiểm tra `TankMovementData.maxSpeed` đang được hiểu là unit/second.
- Với `MoveWorldDirection`, đổi:

```csharp
rb2d.linearVelocity = worldMovementVector * movementData.maxSpeed * Time.fixedDeltaTime;
```

thành:

```csharp
rb2d.linearVelocity = worldMovementVector * movementData.maxSpeed;
```

- Kiểm tra path `Move(...)` truyền thống cũng đang nhân `Time.fixedDeltaTime` vào `linearVelocity`. Nếu player/enemy legacy đang dựa vào bug này, không sửa toàn bộ ngay. Có thể thêm nhánh riêng hoặc flag:
  - `MoveWorldDirection` dùng velocity chuẩn.
  - legacy `Move` giữ nguyên trong phase đầu để tránh làm thay đổi gameplay khác.
- Nếu tank chạy quá nhanh sau khi sửa, giảm `EnemyTankMovementData.maxSpeed` thay vì nhân lại `fixedDeltaTime`.

Tiêu chí xong:

- Enemy đi mượt giữa hai cell, không cần snap lớn.
- `FW` một cell hoàn thành trong thời gian hợp lý so với `enemyReplanInterval`.

## Phase 5 - Giảm snap lớn và chống teleport cảm giác

File chính: `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`

Việc cần làm:

- Trong `SnapBodyToCurrentCellCenter()`, đo khoảng cách từ body tới center trước khi snap.
- Nếu `snapDistance > 0.5 tile`, không snap im lặng:
  - log `snap_distance_large`.
  - chỉ snap nếu đang recover invalid state hoặc vừa arrive hợp lệ.
- Khi arrive `FW`, nếu distance tới center nhỏ hơn threshold thì snap OK.
- Không snap trong rotate, không snap khi nhận action invalid.
- Nếu body bị đẩy bởi collider và lệch khỏi cell center, ưu tiên stop/replan/recover thay vì teleport qua cell mới.

Tiêu chí xong:

- Console không còn log `snap_distance_large` trong đường đi bình thường.
- Quan sát bằng Scene/Game view không còn cảm giác tank nhảy cell.

## Phase 6 - Kiểm tra goal `EagleBase` và hành vi đi về base

File chính:

- `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs`
- `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`

Việc cần làm:

- Log ở mỗi `plan_step`: `eagleCell`, `goalLoc` của từng agent, Manhattan distance current-to-goal.
- Assert `goalCell` luôn in-bounds và walkable.
- Nếu `EagleBase` collider chiếm hơn 1 cell nhưng server chỉ biết center cell là walkable, quyết định rõ:
  - goal là cell center của Eagle.
  - hoặc goal là nearest walkable cell quanh Eagle để tank tới sát và bắn.
- Khi enemy đã trong `eagleShootingRange`, cho phép dừng path và ưu tiên aim/shoot thay vì tiếp tục xin path đi xuyên vào Eagle collider.

Tiêu chí xong:

- Distance tới Eagle giảm theo thời gian hoặc enemy dừng lại để bắn khi đủ range.
- Không có agent nào tiếp tục đi xa khỏi Eagle nhiều tick liên tiếp mà không có obstacle lý do.

## Phase 7 - Validation bắt buộc

### Unit/text checks

- Test decode/guard cho các loc:
  - `-1`
  - `-BuildWidth`
  - `BuildWidth * BuildHeight`
  - bottom row + south
  - top row + north
  - row wrap east/west
- Test `CellToLoc`/`LocToCell` chỉ dùng với cell hợp lệ.

### Unity smoke test

- Scene: `Assets/Scenes/MapF_TankTest_PIBT_TCP.unity`.
- Run TCP server ở `127.0.0.1:7777`.
- Play ít nhất 60 giây.
- Pass criteria:
  - Console không có `server_fw_invalid_bounds` lặp lại sau vài tick đầu.
  - Không có `observed_cell_outside`.
  - Không có tank nào ra ngoài vùng 32x32.
  - Enemy di chuyển mượt giữa cell, không snap lớn.
  - Ít nhất một enemy tiến gần `EagleBase` hoặc chuyển sang bắn khi đủ range.

### Backtest/log review

- Capture trace request/result cho 20 plan steps đầu.
- Với mỗi agent:
  - `loc` nằm trong `[0, 1023]`.
  - `goalLoc` nằm trong `[0, 1023]`.
  - `FW nextLoc` nằm trong `[0, 1023]`.
  - `targetCell` in-bounds và walkable.

## Thứ tự ưu tiên triển khai

1. Phase 1: hard guard Unity `FW` target.
2. Phase 2: recover invalid Unity state trước khi gửi server.
3. Phase 3: server post-filter để không trả action invalid.
4. Phase 4: sửa velocity `MoveWorldDirection` và tune speed nếu cần.
5. Phase 5: giảm snap lớn.
6. Phase 6-7: goal/shooting validation và smoke test dài.

## Files dự kiến thay đổi

Unity:

- `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`
- `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs`
- `Assets/Scripts/PibtTcp/PibtTcpGridAdapter.cs`
- `Assets/Scripts/TankMover.cs`
- có thể cần `Assets/Data/TankData/EnemyTankMovementData.asset` nếu sửa velocity làm speed thực tế tăng quá cao.

Server:

- `/home/west2light/projectY/src/UnityStartKitAdapter.cpp`
- `/home/west2light/projectY/src/UnityStartKitAdapter.h`
- `/home/west2light/projectY/src/PlannerSession.cpp`

## Prompt gợi ý cho agent thực thi

Đọc `adds/pibt_tcp_enemy_jitter_outside_map_plan.md` và triển khai theo thứ tự Phase 1 -> Phase 4 trước. Không sửa scene/prefab nếu chưa có evidence. Ưu tiên chặn mọi `FW` invalid ở Unity, recover state ngoài map trước khi gửi `plan_step`, thêm server post-filter cho action invalid, rồi sửa `MoveWorldDirection` bỏ nhân `Time.fixedDeltaTime` khỏi `linearVelocity`. Sau mỗi bước compile Unity/check console; nếu sửa server thì rebuild hoặc chạy smoke test TCP. Báo rõ file đã sửa, log validation, và còn case nào chưa pass.
