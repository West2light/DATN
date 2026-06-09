# Kế hoạch xử lý Enemy di chuyển lạ trong `PIBT_TCP`

Ngày lập: 2026-06-07
Repo: `D:\2025.2\DATN\projectY`

## Mục tiêu

Tìm và sửa nguyên nhân khiến Enemy tank trong scene `MapF_TankTest_PIBT_TCP` đi được một đoạn rồi xoay vòng hoặc xoay 360 độ quanh một điểm, không tiếp tục áp sát `EagleBase`.

Phạm vi thực thi ưu tiên:

- `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`
- `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs`
- `Assets/Scripts/PibtTcp/PibtTcpGridAdapter.cs`
- Chỉ chạm server C++ trong `/home/west2light/projectY` nếu Unity-side evidence chứng minh cần đổi protocol.

Không sửa lung tung scene/prefab trước khi có log chứng minh, vì worktree hiện đang có nhiều file untracked và thay đổi local.

## Kết luận nghiên cứu hiện tại

### 1. Full static map đã được gửi cho server

Scene TCP đang dùng:

- `mapFileName = random-32-32-10.map`
- `useSelectedMapFileOverride = false`
- `maxBuildWidth = 0`
- `maxBuildHeight = 0`
- `mapOffsetX = 0`
- `mapOffsetY = 0`

Map file có:

- `width = 32`
- `height = 32`
- `32 * 32 = 1024` ô
- `922` ô trống, `102` ô obstacle

Unity gửi map qua:

- `MapScenarioBootstrapPIBTTcp.ConnectAndStartSession()`
- `BuildWalkableArray()`
- `PibtTcpGridAdapter.BuildMapDto()`
- `PibtTcpGridAdapter.EncodeTileMap()`

Probe TCP thủ công đã gửi `width=32`, `height=32`, `symbols=1024`; server trả `hello_ack status=ok`.

C++ server `UnityStartKitAdapter.ApplyMap()` reject nếu `symbols.size() != width * height`, nên server đã nhận đủ 1024 ký tự map.

Kết luận: lỗi hiện tại không phải do thiếu full static map.

### 2. Server không nhận obstacle động

Protocol hiện tại:

- `hello`: gửi map tĩnh một lần.
- `plan_step`: chỉ gửi `agents` gồm `id`, `loc`, `orientation`, `goalLoc`.

Không có trường:

- player/current player tank cells
- Eagle collider footprint
- dynamic obstacles
- runtime blockers
- physical footprint/radius của enemy tank

Server chỉ biết các Enemy như MAPF agents. Unity physics lại có collider thật: player, enemy body, Eagle, wall collider, agent blockers. Vì vậy planner có thể trả action hợp lệ trên grid nhưng tank thật vẫn bị chặn, cạ collider hoặc phải xoay/recover trong Unity.

### 3. Movement bridge là nghi phạm chính

Trace hiện tại cho thấy:

- `plan_step` vẫn chạy đều.
- `timeout=0`.
- `plan_result` có action hợp lệ.
- Nhiều request trả `FW` và `distEagle` giảm.
- Khi có `CR/CCR`, đó là một phần của MAPF-T orientation model, không tự động là lỗi. Tuy nhiên nếu `CR/CCR` lặp quá lâu ở cùng cell thì mới là lỗi.

Các điểm rủi ro trong `GridEnemyAgentPIBTTcp`:

1. `StartRotation()` gọi `SnapBodyToCurrentCellCenter()` trước mỗi rotate. Nếu physics body đang lệch nhẹ hoặc đang kẹt cạnh collider, việc snap về center rồi xoay có thể tạo cảm giác quay quanh một tâm và phá nhịp movement.
2. `SnapBodyToCurrentCellCenter()` set cả `tankController.tankMover.transform.position` và `tankController.tankMover.rb2d.position`. Nếu `TankMover` nằm trên child nhưng `Rigidbody2D` nằm ở parent hoặc ngược lại, hai transform này có thể lệch nhau.
3. `GetAgentPosition()` dùng `tankController.tankMover.transform.position`; trong khi server-state code trước đây từng phải đọc vị trí từ `TankMover.rb2d.transform.position`. Cần xác nhận pivot/body source of truth trong prefab.
4. `ContinueFwMove()` dùng `HandleMoveWorldDirection()`, còn rotate/stop lại dùng `HandleMoveBody(Vector2.zero)`. Cần bảo đảm `useWorldMovement` reset đúng sau mỗi transition.
5. Server không biết player collider, nên action `FW` có thể đưa enemy vào vùng Unity physics không cho đi. Khi đó scuff/reverse có thể tạo vòng lặp.

## Plan thực thi

### Phase 0 - Snapshot và invariant

1. Đảm bảo Unity không ở Play Mode trước khi sửa script.
2. Giữ `debugTcpTrace = true` và `debugValidateOrientationOnSpawn = true` trong scene TCP nếu đã bật.
3. Chạy server:

```bash
cd /home/west2light/projectY
./build/pibt_tcp_server --host 127.0.0.1 --port 7777
```

4. Chạy scene `Assets/Scenes/MapF_TankTest_PIBT_TCP.unity`.
5. Lấy console trace 15-20 request đầu.

Pass của Phase 0:

- Có log `hello_ack ok`.
- Có `req=... timeout=0`.
- Không có `PlanStep exception`.
- Biết được agent nào bắt đầu xoay loop và tại cell nào.

### Phase 1 - Thêm instrumentation cần thiết

Trong `GridEnemyAgentPIBTTcp.cs`, thêm log có giới hạn khi một agent:

- nhận `CR/CCR` liên tiếp ở cùng `currentCell` quá 3 lần
- `hasPendingMove == true` quá 1.5 giây mà chưa arrive
- `dist` tới `targetCell` không giảm trong 0.75 giây
- `IsScuffing()` true quá `scuffTimeout`

Log bắt buộc:

- agent id
- current cell
- target cell
- action
- rb2d position
- tankMover transform position
- root transform position
- rb2d velocity
- distance tới target cell
- touching layers
- current `pendingAction`, `isRotating`, `hasPendingMove`

Mục tiêu: phân biệt 3 lỗi:

1. server trả turn loop thật
2. Unity đang nhận `FW` nhưng body không đi tới center
3. body/root/tankMover pivot lệch làm commit cell sai

### Phase 2 - Chuẩn hóa body position source of truth

Sửa `GridEnemyAgentPIBTTcp` để có helper rõ ràng:

- `GetBodyPosition()`: ưu tiên `tankController.tankMover.rb2d.position`, fallback `tankController.tankMover.transform.position`
- `SetBodyPosition(Vector3 center)`: set `rb2d.position` là chính; chỉ set transform nếu không có rb2d hoặc nếu `tankMover.transform == rb2d.transform`
- `SetBodyRotationAngle(float angle)`: set `rb2d.rotation` là chính, đồng bộ transform rotation cẩn thận

Sau đó thay:

- `GetAgentPosition()`
- `SnapBodyToCurrentCellCenter()`
- `SetBodyRotationAngle()`
- debug position logs

Mục tiêu: tránh tình trạng child transform và rigidbody parent bị set chéo gây quay quanh pivot lạ.

### Phase 3 - Không snap khi chỉ rotate

Thay đổi chính:

- `StartRotation()` không gọi `SnapBodyToCurrentCellCenter()` nữa.
- Khi nhận `CR/CCR`, chỉ zero velocity và rotate tại vị trí body hiện tại.
- Chỉ snap về center khi vừa hoàn tất một `FW` và khoảng cách tới center đã nằm trong `cellCenterThreshold`.
- Nếu đang lệch khỏi center nhiều hơn threshold mà server trả rotate, log warning thay vì snap cưỡng bức.

Mục tiêu: loại bỏ biểu hiện “xoay quanh một tâm” do snap/rotate lặp lại tại cùng cell.

### Phase 4 - Xử lý `FW` bị kẹt

Nếu log Phase 1 cho thấy `FW` đang pending lâu:

1. Thêm timeout cho một `FW` step, ví dụ `maxMoveStepDuration = 1.2f`.
2. Nếu timeout:
   - không commit cell
   - clear `hasPendingMove`
   - recompute `currentCell` từ body position nếu đủ gần center
   - request immediate plan step
   - ghi log `[PIBT_TCP_STUCK_FW]`
3. Nếu đang touching wall/player/enemy, kích hoạt reverse recovery nhưng không tự sửa action sang greedy trừ khi có flag debug.

Giữ `goalBiasCorrectBadFw = false` mặc định. Client không nên tự bẻ action server thành greedy move khi đang debug protocol.

### Phase 5 - Dynamic obstacle decision

Sau khi Phase 2-4 sạch, nếu enemy vẫn bị kẹt vì collider động:

1. Ghi log player cell và các friendly/enemy occupied cells trong `BuildAgentStates()`.
2. Nếu player/Eagle collider đang chặn đường mà server không biết, chọn một trong hai hướng:
   - Unity-side: agent tránh collider động bằng local blocked-cell fallback trước khi execute `FW`.
   - Protocol-side: mở rộng `plan_step` thêm `blockedLocs` hoặc `dynamicObstacles` và update C++ server.

Ưu tiên Unity-side trước nếu cần nhanh, vì server C++ hiện chưa parse `dynamicObstacles`.

### Phase 6 - Validation

Pass criteria:

- Trong 10 giây đầu, tối thiểu 4/6 enemy giảm `distEagle` liên tục.
- Không có agent nào `CR/CCR` ở cùng cell quá 4 lần liên tiếp.
- Không có `FW` pending quá 1.5 giây mà không có recovery log.
- Không có teleport/snap lớn hơn 0.35 world-unit khi đang rotate.
- Console không có compile error, `PlanStep exception`, hoặc timeout spam.
- Screenshot/gameplay không còn biểu hiện xoay 360 độ tại một điểm trong khi không bị tường/enemy chặn.

## Gợi ý thứ tự sửa cho agent thực thi

1. Implement Phase 1 instrumentation trước.
2. Implement Phase 2 body source of truth.
3. Implement Phase 3 no-snap rotation.
4. Compile Unity và kiểm console.
5. Chạy Play 10 giây với server TCP, đọc trace.
6. Chỉ khi còn kẹt `FW`, làm Phase 4.
7. Chỉ khi chứng minh do player/runtime collider, làm Phase 5.

## Prompt giao cho agent thực thi

Bạn đang ở repo `D:\2025.2\DATN\projectY`. Hãy thực thi kế hoạch trong file này. Ưu tiên sửa `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`; chỉ sửa file khác khi thật sự cần. Không revert thay đổi local/untracked hiện có. Sau mỗi sửa script, refresh/compile Unity và kiểm console error. Khi hoàn tất, báo rõ file đã sửa, nguyên nhân đã xác nhận, và log validation.
