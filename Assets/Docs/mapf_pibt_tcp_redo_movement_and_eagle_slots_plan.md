# Plan làm lại chuyển động PIBT_TCP và tiếp cận Eagle

## 1. Mục tiêu

Plan này dùng để làm lại 2 lỗi còn lại sau khi mode `MapF_TankTest_PIBT_TCP` đã chạy được:

1. Enemy di chuyển bị giật hơn scene `MapF_TankTest_PIBT`; vết xích trong ảnh chỉ còn một đường thẳng, không còn cảm giác hai vệt xích ổn định như tank thường.
2. Khi một Enemy tiếp cận được `EagleBase`, các Enemy còn lại đứng yên tại chỗ thay vì tiếp tục tìm vị trí tiếp cận để bắn phá mục tiêu.

Phạm vi plan:

- So sánh trực tiếp với `Assets/Scenes/MapF_TankTest_PIBT.unity`.
- Giữ map `random-32-32-10.map`, `tileSize = 1`, `enemyScale = 0.72`, `eagleCell = (16,16)`.
- Không thêm obstacle/layout mới trong lần sửa này.
- Ưu tiên sửa runtime bridge Unity trước, sau đó mới sửa planner/server nếu trace chứng minh server trả action sai.

## 2. Kết luận kiểm tra source hiện tại

### 2.1. Điểm khác biệt gây nghi ngờ cho lỗi di chuyển giật

`GridEnemyAgentPIBT` đi theo path bằng steering giống tank thường:

- `FollowPath()` tính `directionToTarget`.
- So dot/cross với `tankMover.transform.up`.
- Gọi `tankController.HandleMoveBody(...)`.
- `TankMover.Move()` tiếp tục xử lý tăng tốc, giảm tốc, quay thân xe.

`GridEnemyAgentPIBTTcp` hiện đang khác đáng kể:

- Server trả action rời rạc `FW/CR/CCR/W`.
- `FW` gọi `tankController.HandleMoveWorldDirection(dirToTarget.normalized)`.
- `MoveWorldDirection()` gán thẳng `rb2d.linearVelocity = worldMovementVector * maxSpeed`.
- Khi đến cell, agent `SnapBodyToCurrentCellCenter()` và `SnapBodyRotationToCommittedFacing()`.
- `CR/CCR` dùng rotation phase riêng và thường xuyên `ZeroBodyVelocity()`.

Nhận định: TCP đang kéo Rigidbody theo vector world và snap cell/rotation theo action discrete. Cách này không cùng "cảm giác lái" với PIBT C# và có khả năng tạo chuyển động nhấp nhả, vết xích bị gom thành một đường hoặc mất nhịp spawn track.

### 2.2. Điểm khác biệt cần kiểm tra trong `TankMover`

`TankMover.cs` hiện có 2 nhánh vận tốc khác nhau:

- Nhánh `MoveWorldDirection()` đã bỏ nhân `Time.fixedDeltaTime` khi gán `linearVelocity`.
- Nhánh `Move()` truyền thống vẫn đang gán `linearVelocity = transform.up * currentSpeed * currentForewardDirection * Time.fixedDeltaTime`.

Việc này tạo độ lệch rất lớn giữa:

- PIBT C# dùng `HandleMoveBody()` -> nhánh `Move()`.
- PIBT TCP dùng `HandleMoveWorldDirection()` -> nhánh `MoveWorldDirection()`.

Plan sửa không nên vá cảm giác lái bằng cách tăng/giảm tốc tùy tiện. Phải chuẩn hóa semantic của `TankMover`: `Rigidbody2D.linearVelocity` là đơn vị world-unit/second, không phải delta-position/frame.

### 2.3. Điểm gây nghi ngờ cho lỗi các tank còn lại đứng yên

`GridEnemyAgentPIBTTcp.BuildAgentStateDto()` hiện resolve goal như sau:

- Nếu Eagle ở ô walkable thì mọi agent gửi cùng một `goalLoc`.
- Nếu Eagle không walkable thì mọi agent dùng cùng fallback `TryFindWalkableNear(eagleCell)`.

Với action planner kiểu lifelong/start-kit, nhiều agent cùng một `goalLoc` dễ tạo trạng thái:

- Agent đầu tiên đã đứng gần hoặc ở goal slot.
- Planner coi goal/cell đó là bị chiếm.
- Các agent còn lại nhận `W`, hoặc quay/chờ vì không có slot hợp lệ để tiến vào.

PIBT C# ít lộ lỗi này hơn vì nó nhận path waypoint đến goal/fallback và runtime có logic tránh friendly blocker bằng cách replan. TCP đang phụ thuộc nhiều hơn vào action model của server, nên cần tách "Eagle target" khỏi "goal cell cụ thể".

## 3. Phase A - Baseline lại scene PIBT C#

Mục tiêu: có số đo/quan sát chuẩn trước khi sửa TCP.

Thực hiện:

1. Chạy `Assets/Scenes/MapF_TankTest_PIBT.unity`.
2. Ghi lại trong 10-15 giây đầu:
   - Enemy đi có bị giật không.
   - Vết xích có đều và nhìn giống hai vệt không.
   - Số enemy tiếp tục tiến về Eagle sau khi một enemy đã bắt đầu bắn.
3. Bật gizmo path nếu cần để so path với hướng thân xe.

Pass criteria:

- Enemy không bị snap cell rõ bằng mắt.
- Vết xích sinh đều theo thân xe, không bị gom một đường do teleport/snap/sideways movement.
- Ít nhất 4/6 enemy tiếp tục giảm khoảng cách đến Eagle trong 10 giây đầu, trừ khi bị tường chặn thật.

Ghi chú: MCP Unity chưa khả dụng trong phiên Codex hiện tại dù Editor đã kết nối phía người dùng. Khi MCP tool hiện lại, dùng Play Mode + screenshot/console để xác nhận Phase A.

## 4. Phase B - Sửa movement parity cho PIBT_TCP

### 4.1. Quyết định kỹ thuật

Không để TCP agent kéo Rigidbody bằng `HandleMoveWorldDirection()` làm đường đi chính nữa. TCP `FW` chỉ nên chọn `targetCell`; phần lái đến cell phải dùng steering giống `GridEnemyAgentPIBT`.

Thiết kế mới:

- Giữ action model `FW/CR/CCR/W` để tương thích server.
- Khi nhận `FW`, set `targetCell = nextLoc`.
- Trong `ContinueFwMove()`, gọi helper mới kiểu `SteerTowardTargetCell()` thay vì `HandleMoveWorldDirection()`.
- Helper này copy logic từ `GridEnemyAgentPIBT.FollowPath()`:
  - Tính `directionToTarget`.
  - Tính `dotProduct` và `cross`.
  - Dùng `forwardAlignmentThreshold`, `turningDriveAlignmentThreshold`, `partialDriveAlignmentThreshold`.
  - Gọi `tankController.HandleMoveBody(Vector2.up/new Vector2(...))`.
- Không snap rotation khi bắt đầu `FW`, trừ trường hợp debug/recovery thật sự cần.
- Chỉ snap position khi lệch lớn bất thường, không snap mỗi lần đến cell nếu khoảng cách đã nhỏ.

Lý do: phải giữ thân xe là nguồn sự thật của hướng di chuyển. Khi tank vừa di chuyển theo vector world vừa rotate riêng, vết xích/trail không còn khớp với thân xe.

### 4.2. Việc cần sửa

File chính:

- `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`

Thay đổi đề xuất:

1. Thêm `SteerTowardTargetCell(Vector3 targetWorld)` tương đương logic `GridEnemyAgentPIBT.FollowPath()`.
2. Đổi `ContinueFwMove()`:
   - Bỏ `tankController.HandleMoveWorldDirection(dirToTarget.normalized)`.
   - Dùng `SteerTowardTargetCell(targetWorld)`.
   - Chỉ commit cell khi `dist <= cellCenterThreshold`.
3. Hạn chế `SnapBodyRotationToCommittedFacing()`:
   - Không gọi ngay trước `BeginForwardMove()`.
   - Chỉ dùng sau rotate action hoàn tất hoặc recovery.
4. Hạn chế `SnapBodyToCurrentCellCenter()`:
   - Nếu `dist <= cellCenterThreshold`, chỉ commit logical cell.
   - Chỉ set body position nếu `snapDistance > 0.5f` hoặc diagnostic recovery.
5. Chuẩn hóa `TankMover`:
   - Kiểm tra lại cả 2 nhánh `Move()` và `MoveWorldDirection()` để cùng dùng đơn vị `linearVelocity` đúng.
   - Không giữ trạng thái một nhánh nhân `fixedDeltaTime`, nhánh kia không nhân.

Pass criteria:

- TCP Enemy không còn bị kéo ngang theo vector world trong khi thân xe chưa xoay kịp.
- Vết xích không bị gom thành một đường do snap/teleport liên tục.
- Trong trace, số lần `snap_distance_large`, `fw_pending_no_progress`, `rotate_off_center` giảm rõ.

## 5. Phase C - Tách goal quanh Eagle thành nhiều attack slots

### 5.1. Quyết định kỹ thuật

Không gửi cùng một `goalLoc` cho toàn bộ agent nữa. Mỗi agent cần một `attackSlot` walkable quanh `EagleBase`.

Attack slot là ô walkable trong bán kính đủ để bắn Eagle:

- Cách Eagle trong `enemyEagleShootingRange`.
- Có line of sight tới Eagle nếu có thể kiểm tra được bằng raycast.
- Không trùng cell đang bị friendly agent chiếm.
- Ưu tiên phân bổ slot theo khoảng cách từ agent hiện tại và độ phân tán quanh Eagle.

### 5.2. Thiết kế phân bổ slot

Thêm vào `MapScenarioBootstrapPIBTTcp`:

- Tính `List<Vector2Int> eagleAttackSlots` sau khi spawn Eagle.
- Slot candidates lấy từ vòng quanh Eagle:
  - Manhattan radius 1..`floor(enemyEagleShootingRange)`.
  - Chỉ lấy `mapLoader.IsWalkable(cell)`.
  - Loại slot nằm sát wall nếu raycast bị tường che.
- Mỗi lần build `plan_step`, bootstrap gán slot cho từng agent:
  - Agent đang bắn Eagle giữ slot hiện tại.
  - Agent chưa bắn chọn slot chưa bị agent khác chiếm.
  - Nếu hết slot, cho phép slot cùng vòng nhưng khác hướng tiếp cận.

Thêm vào `GridEnemyAgentPIBTTcp`:

- Field runtime `assignedGoalCell`.
- `BuildAgentStateDto()` dùng `assignedGoalCell`, không tự resolve thẳng về Eagle cell.
- `GetShootingTarget()` vẫn bắn `eagleTarget`, không bắn attack slot.
- Khi đã `CanShootTarget(eagleTarget)`, agent dừng lái và bắn, nhưng không làm các agent khác đổi goal thành `currentCell`.

### 5.3. Fallback

Nếu chưa muốn implement phân bổ động ngay:

- Dùng slot cố định theo agentId quanh Eagle: north/east/south/west và các ô phụ.
- Nếu slot không walkable, dùng `TryFindWalkableNear(slot)`.
- Không dùng `TryFindWalkableNear(eagleCell)` chung cho mọi agent.

Pass criteria:

- Trace `send req=... agents=[...]` phải cho thấy nhiều `goalLoc` khác nhau.
- Khi một agent đã bắn Eagle, các agent còn lại vẫn nhận goal slot khác và tiếp tục có action `FW/CR/CCR`, không đồng loạt `W`.
- Trong 10-15 giây, ít nhất 3 enemy có thể vào vùng bắn hoặc tiếp tục giảm khoảng cách tới Eagle.

## 6. Phase D - Trace để phân biệt lỗi Unity và lỗi server

Bật `debugTcpTrace` trong `MapScenarioBootstrapPIBTTcp`.

Log bắt buộc:

- `requestId`, `timestep`.
- Mỗi agent: `loc`, `orientation`, `goalLoc`, `assignedGoalCell`, `currentCell`, `bodyPos`.
- Mỗi action: `action`, `nextLoc`, `targetCell`.
- Trạng thái bắn: `canShootEagle`, `distEagle`, `lineOfSightBlockedBy`.
- Trạng thái movement: `hasPendingMove`, `rotating`, `distToCell`, `velocity`, `snapDistance`.

Kết luận từ trace:

- Nếu server trả `FW` hợp lý nhưng Unity không tiến: lỗi nằm ở execution/physics.
- Nếu server trả `W` cho agent còn xa Eagle trong nhiều request liên tiếp: lỗi nằm ở goal assignment hoặc planner.
- Nếu server trả `CR/CCR` liên tục cùng cell: lỗi orientation/action model.
- Nếu agent đang bắn và các agent khác vẫn có goal slot nhưng bị friendly raycast/blocker chặn: cần sửa collision/line-of-sight/friendly occupancy riêng.

## 7. Phase E - Kiểm tra line-of-sight và friendly blocker

Vì một enemy đã đứng gần Eagle có thể nằm giữa turret của các enemy khác và Eagle, cần kiểm tra riêng:

- `lineOfSightMask` hiện gồm `"Agent", "Enemy", "Player", "Hittable", "Walls", "ObstaclesMovement"`.
- `CanShootTarget()` bỏ qua collider friendly bằng `FactionMember.AreFriendly(...)`.
- Tuy nhiên planner movement vẫn có thể coi occupied/friendly blocker là không đi được nếu goal chung.

Việc cần làm:

1. Log collider đầu tiên chặn raycast tới Eagle.
2. Xác nhận `FactionMember` của enemy gần Eagle là `Enemy`.
3. Xác nhận collider Eagle thuộc target hoặc child của target để `CanShootTarget()` trả true.
4. Nếu friendly enemy đứng chắn đường đạn, bắn vẫn nên bỏ qua friendly nhưng movement không nên đứng chờ vô hạn.

Pass criteria:

- Agent khác không đứng yên chỉ vì một friendly enemy đang bắn Eagle.
- Nếu line-of-sight bị wall thật chặn, agent phải tiếp tục đi tới slot khác.

## 8. Phase F - Validation cuối

Chạy theo thứ tự:

1. `Assets/Scenes/MapF_TankTest_PIBT.unity` làm baseline.
2. `Assets/Scenes/MapF_TankTest_PIBT_TCP.unity` với `debugTcpTrace = true`.
3. Chụp screenshot/GIF 10-15 giây đầu.
4. So log `goalLoc` và action sequence.

Pass criteria cuối:

- Enemy TCP đi mượt gần với PIBT C#; không còn cảm giác snap từng cell.
- Vết xích không bị gom thành một đường do thân xe và vận tốc lệch nhau.
- Khi một enemy đã tiếp cận Eagle và bắn, các enemy còn lại vẫn tiếp tục di chuyển tới các attack slot khác.
- Không có chuỗi `W` kéo dài cho agent còn xa Eagle nếu vẫn còn slot hợp lệ.
- Không có chuỗi `CR/CCR` cùng cell quá 2 giây trừ khi bị obstacle thật.

## 9. Thứ tự implement đề xuất

1. Sửa movement parity trong `GridEnemyAgentPIBTTcp`: bỏ world-velocity làm đường đi chính, dùng steering giống PIBT C#.
2. Chuẩn hóa `TankMover.linearVelocity` để nhánh `Move()` và `MoveWorldDirection()` cùng đơn vị.
3. Thêm trace `assignedGoalCell`, `canShootEagle`, `goalLoc` cho TCP.
4. Implement attack slots quanh Eagle trong `MapScenarioBootstrapPIBTTcp`.
5. Đổi `GridEnemyAgentPIBTTcp.BuildAgentStateDto()` dùng assigned slot.
6. Chạy baseline PIBT C# và TCP để so cảm giác lái + behavior nhiều enemy.
7. Chỉ khi trace chứng minh server trả action sai sau khi đã có multi-goal slot, mới sửa C++ server/planner.

## 10. Không nên làm

- Không sửa bằng cách bật `goalBiasCorrectBadFw` làm giải pháp chính. Đây chỉ là diagnostic/temporary correction, có thể che lỗi planner.
- Không tăng timeout/replanInterval để che lỗi đứng yên. Timeout hiện không phải nghi phạm chính.
- Không snap Rigidbody mỗi cell nếu mục tiêu là cảm giác lái mượt.
- Không ép tất cả enemy cùng đi vào đúng cell Eagle.
