# PLAN: Điều tra và fix bug Enemy đứng yên trong TCP PIBT ở Mansion/non-alpha maps

Ngày lập: 2026-06-22  
Phạm vi: Single Play, map `Assets/MapData/ht_mansion_n.map`, scene `Assets/Scenes/MapF_TankTest_PIBT.unity`, mode `PIBT_TCP`, server C++ chạy ở WSL `./build/pibt_tcp_server --host 0.0.0.0 --port 7777`.

## 1. Mục tiêu

Tìm nguyên nhân chính xác vì sao Enemy nhận plan từ server C++ đều đặn nhưng trong Unity vẫn đứng yên hoặc chỉ xoay/nhúc nhích rồi dừng trên các map ngoài Alpha32. Sau khi có bằng chứng, implement fix hẹp, kiểm chứng bằng MCP và không làm regression Alpha32.

Kết quả mong muốn:

- Mansion TCP PIBT: sau khi skip Placement UI, 6 Enemy spawn cùng component đi được tới Eagle hoặc chuyển sang bắn khi có line-of-sight.
- Server vẫn nhận `plan_step` đều, Unity không còn parse/protocol error.
- Ít nhất một Enemy có `FW -> hasTarget=true -> speed>0` trong 3 giây đầu nếu chưa ở trạng thái bắn.
- Không có Enemy chồng lên nhau hoặc bị vật lý khóa tại spawn.

## 2. Bằng chứng hiện có

- Unity đã chạy đúng nhánh TCP: console có `SpawnScenario algorithm='PIBT_TCP'`, `Connected to 127.0.0.1:7777`, `hello_ack`, `Connected and initialized`.
- WSL server nhận `plan_step` liên tục, ví dụ `requestId=106..108 agents=6 computeMs~80ms`, nên lỗi không còn nằm ở kết nối TCP cơ bản.
- Source hiện tại đã parse `plan_result` dạng object array và dùng `nextLoc` từ server:
  - `PIBTTcpClient.PlanStepDetailed()`.
  - `PIBTTcpClient.ParsePlanActions()`.
  - `MapScenarioBootstrapPIBT_TCP.ApplyStepActions()`.
- Điểm nghi vấn lớn trong Unity execution:
  - `MapScenarioBootstrapPIBT_TCP.ApplyAction()` cập nhật orientation logic cho `CR/CCR`.
  - `GridEnemyAgentPIBT_TCP.NotifyServerAction()` lại xử lý `CR/CCR/W` như idle: tăng wait count, `ClearMovementTarget()`, không ra lệnh tank xoay thân.
  - Nếu server C++ trả nhiều `CR/CCR` để align hướng trước khi `FW`, Unity logic có thể tin orientation đã đổi, nhưng tank vật lý không xoay. Từ đó request tiếp theo bị lệch giữa logical orientation và visual/physics orientation, tạo vòng lặp "server có plan nhưng tank đứng".
- Bằng chứng còn thiếu: snapshot runtime sau vài `plan_step`, action-count thực tế, `hasTarget`, `currentTarget`, speed, trạng thái shooting/recovery của từng agent.

## 3. Giả thuyết cần kiểm chứng theo thứ tự ưu tiên

1. **CR/CCR bị biến thành idle trong Unity agent.** Server trả rotate action, coordinator cập nhật orientation logic, nhưng agent không xoay tank. Đây là giả thuyết ưu tiên vì phù hợp triệu chứng "server plan đều nhưng Enemy đứng".
2. **`FW` được trả nhưng `nextCell == CurrentCell`.** Có thể do mapping `flat loc <-> build cell` hoặc map bounds khác Alpha32 làm target không đổi, dẫn đến `SetNextTarget()` đặt `_hasTarget=false`.
3. **`FW` có target nhưng movement bị khóa bởi shooting/recovery/physics.** Agent có `_hasTarget=true` nhưng `TankMover` không tạo vận tốc do collider/layer, obstacle mask, hoặc logic shooting clear target.
4. **Server trả đa số `W` vì request state sai.** Unity gửi loc/orientation/goal khiến C++ planner nghĩ không có bước hợp lệ, hoặc sanitize còn xảy ra nhưng chưa quan sát đúng log.
5. **Kết nối OK nhưng main-thread state chưa sẵn sàng ở tick đầu.** `_serverReady`, `_stepInFlight`, `_frame`, `_nextTickTime` cần được dump sau 3s/10s để loại trừ race.

## 4. Giai đoạn A: Tái hiện có kiểm soát bằng MCP

Thao tác chuẩn:

1. Đảm bảo WSL server đang listen `0.0.0.0:7777`.
2. Clear Unity console.
3. Set PlayerPrefs:
   - `SelectedMapFile = Assets/MapData/ht_mansion_n.map`
   - `SelectedAlgorithm = PIBT_TCP`
4. Load `Assets/Scenes/MapF_TankTest_PIBT.unity`.
5. Enter Play Mode.
6. Nếu `PlacementUI` xuất hiện, invoke `SkipBtn.onClick`.
7. Chụp snapshot tại 0s, 3s, 10s, 20s.

Snapshot MCP cần lấy:

- Bootstrap:
  - `_serverReady`
  - `_stepInFlight`
  - `_frame`
  - `_nextTickTime`
  - `_agents.Count`
  - Eagle cell/flat loc
- Từng agent:
  - `name`
  - `CurrentCell`
  - `_currentTarget`
  - `_hasTarget`
  - `_lastServerAction`
  - `_lastServerActionTime`
  - `_needsPlanNow`
  - `btWaitActionCount`
  - `btReplanCount`
  - `btRecoveryCount`
  - `_wasShootingLastFrame`
  - `Rigidbody2D.linearVelocity` hoặc velocity hiện dùng
  - body forward/up vector
- Screenshot game view hoặc scene view để đối chiếu vị trí thật.

## 5. Giai đoạn B: Instrumentation tối thiểu trước khi sửa

Không fix mù. Thêm log/debug có thể tắt được, ưu tiên giữ trong code với cờ nhỏ.

### B1. `PIBTTcpClient`

Thêm state debug:

- `LastPlanRawResponse`
- `LastPlanRequestJson`
- `LastParsedActionSummary`: count `FW/CR/CCR/W/UNKNOWN`

Mục tiêu: biết server thực sự trả action gì cho từng tick, không chỉ thấy server log `plan ok`.

### B2. `MapScenarioBootstrapPIBT_TCP`

Thêm trace bounded cho single-play TCP:

- Log 10 tick đầu, sau đó mỗi 30 tick hoặc khi không có agent di chuyển.
- Mỗi tick log:
  - requestId/timestep
  - per-agent `loc`, `ori`, `goalLoc`
  - response `action`, `nextLoc`
  - `nextCell`
  - `currentCell`
  - `hasNextLoc`
  - kết quả `ApplyAction`

Ví dụ format:

```text
[PIBT_TCP_TRACE] tick=12 counts FW=2 CR=3 CCR=0 W=1
[PIBT_TCP_TRACE] a=0 cell=(12,34) loc=4884 ori=1 goal=... action=CR nextLoc=4884 nextCell=(12,34)
```

### B3. `GridEnemyAgentPIBT_TCP`

Thêm API debug thay vì phụ thuộc reflection lâu dài:

- `public string DebugSnapshot()`
- `public bool HasMovementTarget`
- `public Vector2Int MovementTarget`
- `public bool IsShootingLastFrame`

Log transition quan trọng:

- `NotifyServerAction()` nhận action.
- `SetNextTarget()` bị reject vì `nextCell == CurrentCell`.
- `ClearMovementTarget()` kèm reason.
- `SteerTowardTarget()` thấy target nhưng không tăng speed trong N frame.

## 6. Giai đoạn C: Quyết định root cause từ evidence

Sau instrumentation, phân loại theo bảng sau:

| Evidence | Kết luận | Fix |
| --- | --- | --- |
| Action-count nhiều `CR/CCR`, `_hasTarget=false`, tank không xoay | Rotate action chưa được thực thi vật lý | Implement rotate-in-place execution cho TCP agent |
| Action-count nhiều `FW`, `nextCell != CurrentCell`, `_hasTarget=true`, velocity gần 0 | Movement/physics bị khóa | Inspect `TankController`, `TankMover`, collider/layer, obstacle mask |
| Action-count nhiều `FW`, nhưng `nextCell == CurrentCell` | Mapping loc/cell hoặc server `nextLoc` sai | Fix `FlatToBuildCell`, `AgentFlat`, hoặc request rows/cols/build offset |
| Action-count chủ yếu `W`, server sanitize nhiều | Request state sai hoặc planner không thấy đường | So sánh loc/ori/goal với map C++, fix orientation/obstacle map |
| `_serverReady=false` sau hello hoặc `_frame=0` lâu | Thread visibility/race hoặc tick gate | Fix `ConnectAndHelloAsync` state handoff/main-thread gate |

## 7. Giai đoạn D: Fix dự kiến nếu giả thuyết CR/CCR đúng

Thiết kế hẹp:

1. `MapScenarioBootstrapPIBT_TCP` vẫn giữ `_agentOrientations` làm logical orientation cho request kế tiếp.
2. `GridEnemyAgentPIBT_TCP.NotifyServerAction("CR"/"CCR")` không `ClearMovementTarget()` như idle nữa.
3. Agent TCP có trạng thái rotate-in-place:
   - `_rotationAction`
   - `_rotationTargetDegrees`
   - `_hasRotationTarget`
4. Trong `Update()` hoặc nhánh steering hiện có:
   - Nếu `_hasRotationTarget`, gọi input xoay thân/tank theo hướng gần nhất.
   - Khi sai số góc nhỏ hơn threshold, dừng xoay, clear rotation state, request plan kế tiếp.
5. `W` vẫn là wait thật:
   - clear movement.
   - không xoay.
   - request lại sau threshold hiện có.
6. `FW` vẫn dùng `nextLoc` từ server để set target cell.

Nguyên tắc quan trọng: không cập nhật orientation vật lý bằng teleport/rotation snap nếu tank đang dùng physics, trừ khi evidence cho thấy controller không hỗ trợ rotate command ổn định.

## 8. Giai đoạn E: Fix nếu nguyên nhân là mapping `nextLoc`

Nếu trace cho thấy `FW` nhưng target sai:

1. Dump `rows`, `cols`, `BuildStartX`, `BuildStartY`, `BuildEndX`, `BuildEndY`.
2. Với từng agent, log:
   - Unity `CurrentCell`
   - Unity `AgentFlat`
   - Server `nextLoc`
   - `FlatToBuildCell(nextLoc)`
3. Cross-check công thức:
   - `loc = localY * cols + localX`
   - `localX = loc % cols`
   - `localY = loc / cols`
   - `buildCell = (BuildStartX + localX, BuildStartY + localY)`
4. Nếu server dùng row/col theo chiều ngược Y so với Unity, fix bằng adapter duy nhất trong Unity hoặc C++, không patch rải rác.

## 9. Giai đoạn F: Fix nếu movement/physics bị khóa

Nếu `_hasTarget=true` nhưng tank không di chuyển:

1. Check component trên Enemy prefab/runtime:
   - `Rigidbody2D`
   - `BoxCollider2D` hoặc collider tương đương
   - `TankController`
   - `TankMover`
   - layer collision matrix
2. Xác nhận Enemy không spawn đè nhau:
   - dump occupied spawn cells.
   - Physics2D overlap quanh mỗi tank sau spawn.
3. Check body/turret obstacle masks:
   - Enemy có đang coi tank khác hoặc tile walkable là obstacle không.
4. So sánh với Alpha32 single-play đang chạy tốt:
   - component list.
   - prefab/layer.
   - controller flags.
   - collider size/offset.
   - movement speed/current input.

## 10. Validation bắt buộc sau khi implement

### Static/compile

- `validate_script`:
  - `Assets/Scripts/PIBTTcpClient.cs`
  - `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
  - `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs`
  - file khác nếu có sửa.
- Unity console: 0 error.

### Runtime MCP

Chạy 2 map:

- `Assets/MapData/ht_mansion_n.map`
- Alpha32 map đang chạy tốt hiện tại.

Mỗi map cần evidence:

- Console có `PIBT_TCP Connected and initialized`.
- WSL server có `plan_step` tăng đều.
- Unity trace có action-count không toàn `W`.
- Snapshot 3s/10s:
  - `_serverReady=true`
  - `_frame>0`
  - ít nhất một agent `FW` và `hasTarget=true`, hoặc agent ở trạng thái shooting hợp lệ.
  - velocity/speed khác 0 với agent đang di chuyển.
- Screenshot sau 10s cho thấy Enemy đã rời spawn hoặc đang engage Eagle.

### Regression

- Alpha32 TCP không giảm hành vi hiện có.
- Non-TCP PIBT single-play không bị ảnh hưởng.
- Backtest path vẫn compile và chạy được vì code shared trong `GridEnemyAgentPIBT_TCP`.

## 11. Tiêu chí hoàn thành

Bug được coi là fix khi:

- Mansion TCP PIBT không còn trạng thái 6 Enemy đứng yên bất động sau 10 giây play.
- Không còn vòng lặp `CR/CCR/W` mà không có execution tương ứng trong Unity.
- Server và Unity cùng thống nhất orientation/cell state qua ít nhất 20 tick.
- Plan/result trace đủ rõ để nếu bug tái diễn có thể xác định dừng ở server, mapping, movement hay physics.

## 12. Rủi ro và rollback

- Rủi ro lớn nhất là sửa rotate action làm lệch Alpha32. Vì vậy fix phải đặt trong `GridEnemyAgentPIBT_TCP`, không đụng `GridEnemyAgentPIBT` local nếu không có evidence.
- Instrumentation có thể spam console. Dùng bounded trace và/hoặc cờ `enableTcpDebugTrace`.
- Nếu phải sửa C++ server, cần tách patch Unity và patch server rõ ràng, mỗi bên có log before/after.
- Rollback nhanh: revert các thay đổi trong `GridEnemyAgentPIBT_TCP`, `MapScenarioBootstrapPIBT_TCP`, `PIBTTcpClient` liên quan trace/rotate execution.

## 13. Thứ tự thực hiện đề xuất

1. Chạy MCP reproduction Mansion và lấy snapshot sau 3s/10s.
2. Nếu snapshot chưa đủ, implement instrumentation B1-B3.
3. Chạy lại Mansion, đọc action-count và per-agent state.
4. Chọn một nhánh root cause trong mục 6.
5. Implement fix hẹp.
6. Validate static.
7. Validate runtime Mansion.
8. Validate regression Alpha32.
9. Lưu console/server evidence vào `adds/fix` hoặc `adds/output`.
