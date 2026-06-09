# Kế hoạch nghiên cứu và fix Enemy PIBT_TCP di chuyển giật, dừng không ổn định

Ngày lập: 2026-06-09
Repo Unity: `F:\DATN`
Scene: `Assets/Scenes/MapF_TankTest_PIBT_TCP.unity`
Server TCP: `/home/lenovo/projectY/Server-PIBT-TeamNoMan-sSky`

## Hiện trạng đã kiểm tra

- Unity Editor đang mở scene `MapF_TankTest_PIBT_TCP`; console hiện không có error/warning.
- Server path mới đã được kiểm tra và có binary `build/pibt_tcp_server`.
- Server hiện đã có guard cơ bản:
  - `UnityStartKitAdapter::ApplyAgents()` reject `loc`/`goalLoc` invalid hoặc không walkable.
  - `UnityStartKitAdapter::SanitizeAction()` đổi `FW` invalid/out-of-bounds/row-wrap/blocked thành `W`.
  - `PlannerSession::Plan()` gọi sanitize trước khi trả action cho Unity.
- Unity client hiện cũng đã có guard:
  - `GridEnemyAgentPIBTTcp.TryDecodeServerForwardTarget()` reject `nextLoc` ngoài bounds, blocked, non-adjacent.
  - `TryRecoverToNearestValidCell()` recover state trước khi gửi server.
  - `StartRotation()` không còn snap về cell center trước khi rotate.

## Quy tắc thử nghiệm an toàn

- Nếu cần test tank, move, hoặc component chung, tạo bản copy trước rồi sửa trên bản copy.
- Tên file bản thử nghiệm phải có chữ `Copy` trong tên.
- File gốc chỉ sửa sau khi bản copy đã chứng minh đúng nguyên nhân và không phá local maps.
- Quy tắc này áp dụng cho script, asset data, và component chung.

## Giả thuyết nguyên nhân chính

### 1. `TankMover` đang set `Rigidbody2D.linearVelocity` sai đơn vị

File thử nghiệm: `Assets/Scripts/TankMoverCopy.cs`

File gốc chỉ sửa sau khi test xong: `Assets/Scripts/TankMover.cs`

Hiện tại cả movement thường và movement theo world direction đều gán:

```csharp
rb2d.linearVelocity = ... * movementData.maxSpeed * Time.fixedDeltaTime;
```

`linearVelocity` đã là vận tốc theo unit/giây, không nên nhân thêm `Time.fixedDeltaTime`. Với `EnemyTankMovementData.maxSpeed = 50` và fixed timestep thường là `0.02`, vận tốc thực tế chỉ còn khoảng `1 unit/s`. Hệ quả:

- Một bước `FW` mất lâu hơn nhịp `enemyReplanInterval = 0.75s`.
- `AreAgentsReadyForPlanStep()` phải chờ tất cả enemy không còn `IsExecutingTcpAction`, nên một vài tank đi chậm/kẹt sẽ làm cả team không gửi plan mới.
- Khi agent tới gần center, code snap về center để commit cell, nhìn thành giật/nhảy.
- Nếu đang bị collider cản, pending `FW` kéo dài và các tank khác có thể đứng bắn trong khi tank còn lại không nhận plan mới.

Đây là nguyên nhân đáng fix trước tiên.

### 2. Barrier "chờ cả team" làm 3 tank còn lại dễ đứng im

File: `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs`

`AreAgentsReadyForPlanStep()` trả `false` nếu chỉ một alive enemy còn execute action. Đây đúng với MAPF step đồng bộ, nhưng khi movement vật lý chậm/kẹt, nó gây nghẽn toàn team:

- 3 tank đã tới range Eagle thì `GetShootingTarget()` ưu tiên bắn và `StopMovement()`.
- 3 tank còn lại nếu đang pending `FW`, scuff/recovery, hoặc chờ action mới thì team plan bị trì hoãn.
- Cảm giác quan sát là "một nhóm tìm được Eagle và bắn, nhóm còn lại có di chuyển rồi dừng".

Barrier không nhất thiết là bug độc lập, nhưng nó khuếch đại lỗi velocity/collision.

### 3. Steering hiện dùng movement local dạng arcade, không phải drive mượt theo cell target

File thử nghiệm: `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcpCopy.cs`

File gốc chỉ sửa sau khi test xong: `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`

`SteerTowardTargetCell()` gọi `tankController.HandleMoveBody(...)`, tức đi theo forward local + rotate dần. Với grid/MAPF, mỗi action `FW` chỉ là một cell, nên nếu tank lệch hướng nhiều:

- tank xoay rồi nhích từng đoạn;
- dễ scuff vào collider;
- khó đảm bảo đi thẳng tới center cell;
- khi commit cell lại snap.

Hướng mượt hơn là dùng `HandleMoveWorldDirection(directionToTarget.normalized)` riêng cho `PIBT_TCP`, sau khi đã sửa velocity unit và tune speed.

## Plan fix đề xuất

## Trạng thái triển khai

- Phase 1 đã triển khai trên bản `Copy`, không sửa `TankMover.cs` hoặc `EnemyTankMovementData.asset` gốc.
- Smoke test ngắn trong Play Mode đã xác nhận 6/6 enemy dùng `GridEnemyAgentPIBTTcpCopy`, 6/6 dùng `TankMoverCopy`, TCP client connect được tới server.
- Console sau smoke test không có error/warning runtime.
- Phase 2 đã triển khai trên `GridEnemyAgentPIBTTcpCopy`: `FW` drive trực tiếp bằng `HandleMoveWorldDirection(directionToTarget.normalized)` thay vì arcade local steering. Chưa chạy gameplay test theo yêu cầu.
- Phase 3 đã triển khai trên `GridEnemyAgentPIBTTcpCopy`: thêm `maxMoveStepDuration = 1.1f`; nếu một `FW` quá hạn thì log `fw_step_timeout`, clear pending move, stop body, recover nếu state/cell không hợp lệ, rồi request plan ngay. Chưa chạy gameplay test theo yêu cầu.
- Phase 4 đã triển khai trên copy route: `BuildRequestTrace()` có summary `shootEagle/shootPlayer/pendingMove/reverse/scuffing/idle`, và agent copy có `motionState` per-agent để phân biệt đứng bắn hợp lệ với đứng do kẹt. Chưa chạy gameplay test theo yêu cầu.

### Phase 1 - Fix velocity unit và tune lại speed

Mục tiêu: bỏ jitter do vận tốc bị scale sai.

Việc làm:

1. Tạo `TankMoverCopy.cs` từ `TankMover.cs`.
2. Sửa `TankMoverCopy.FixedUpdate()`:
   - `MoveWorldDirection`: bỏ `* Time.fixedDeltaTime`.
   - Legacy `Move`: cân nhắc bỏ `* Time.fixedDeltaTime` hoặc giữ legacy và thêm mode riêng cho TCP để tránh phá gameplay cũ.
3. Tạo `EnemyTankMovementDataCopy.asset` từ `EnemyTankMovementData.asset`.
4. Tune speed trên bản copy trước, không đụng asset gốc.
5. Giữ `rotationSpeed = 150` trước, chỉ tune nếu tank còn quay quá chậm.

Pass:

- Một cell `FW` hoàn thành ổn định dưới `0.75s`.
- `fw_pending_long` và `fw_pending_no_progress` không còn xuất hiện thường xuyên.
- Movement không còn snap lớn khi arrive.

### Phase 2 - Chuyển TCP enemy sang world-direction drive

Mục tiêu: di chuyển từ cell center tới target cell center mượt, ít zig-zag.

Việc làm:

1. Tạo `GridEnemyAgentPIBTTcpCopy.cs` từ `GridEnemyAgentPIBTTcp.cs`.
2. Trong `GridEnemyAgentPIBTTcpCopy.SteerTowardTargetCell()`, dùng `HandleMoveWorldDirection(directionToTarget.normalized)` cho phase TCP.
3. Chỉ dùng rotation action `CR/CCR` để cập nhật `committedFacing` theo server, không để nó ép tank arcade xoay/nhích quá nhiều trong lúc thực thi `FW`.
4. Khi arrive, chỉ snap nếu `dist <= cellCenterThreshold`; nếu `snapDistance > 0.35`, log warning và không snap im lặng.

Pass:

- Tank đi thành đoạn thẳng giữa hai cell.
- Không còn cảm giác "giật cục" do vừa xoay vừa bò.

### Phase 3 - Giảm tác động của barrier chờ toàn team

Mục tiêu: tránh một agent chậm/kẹt làm toàn bộ team ngừng nhận plan.

Việc làm:

1. Thêm hard timeout cho một `FW`, ví dụ `maxMoveStepDuration = 1.1s`.
2. Nếu timeout:
   - không commit `targetCell`;
   - stop + clear pending move;
   - recover về cell hợp lệ gần nhất nếu cần;
   - request plan ngay;
   - log `[PIBT_TCP_DIAG] reason=fw_step_timeout`.
3. Trong `AreAgentsReadyForPlanStep()`, vẫn giữ barrier đồng bộ, nhưng timeout ở agent phải đảm bảo không có agent pending vô hạn.

Pass:

- Không agent nào pending `FW` quá `1.1-1.3s`.
- Team vẫn gửi `plan_step` đều sau khi có kẹt nhẹ.

### Phase 4 - Phân biệt "đứng bắn đúng" và "đứng do kẹt"

Mục tiêu: khi thấy 3 tank đứng, biết chúng đang bắn Eagle hợp lệ hay bị block.

Việc làm:

1. Mở `debugTcpTrace` trong scene.
2. Log thêm mỗi request:
   - số agent `shootTarget=Eagle`;
   - số agent `pendingMove=true`;
   - số agent bị `fw_step_timeout`, `scuff_recovery`, `server_fw_invalid_*`.
3. Với tank đang bắn Eagle, đây là hành vi hợp lệ nếu:
   - `canShootEagle=true`;
   - line-of-sight tới Eagle;
   - Eagle HP giảm.
4. Nếu tank dừng nhưng `shootTarget=-`, xem là stuck và xử lý theo Phase 3.

Pass:

- 3 tank đầu dừng chỉ khi đang thật sự bắn Eagle.
- 3 tank còn lại vẫn tiếp tục nhận plan hoặc timeout/recover rõ ràng.

### Phase 5 - Smoke test server + Unity

Chạy server:

```bash
cd /home/lenovo/projectY/Server-PIBT-TeamNoMan-sSky
./build/pibt_tcp_server --host 127.0.0.1 --port 7777
```

Chạy Unity scene `MapF_TankTest_PIBT_TCP` trong 60 giây.

Pass bắt buộc:

- Không có compile error.
- Không có `PlanStep exception`.
- Không có timeout spam.
- 6/6 enemy không ra ngoài map.
- Không có enemy pending `FW` quá timeout mà không recover.
- Tối thiểu 4/6 enemy tiếp tục giảm khoảng cách tới assigned Eagle slot hoặc chuyển sang `shootTarget=Eagle`.
- Quan sát Game view: movement giữa cell mượt, không nhảy cell/snap lớn.

## Thứ tự triển khai ngắn gọn

1. Sửa velocity unit và tune `EnemyTankMovementData.maxSpeed`.
2. Cho TCP enemy dùng `HandleMoveWorldDirection()` trong `FW`.
3. Thêm timeout/recover cho pending `FW`.
4. Bật trace để xác nhận tank đứng là do bắn Eagle hay bị stuck.
5. Chạy smoke test với server WSL đúng path.

## File dự kiến sửa

- `Assets/Scripts/TankMoverCopy.cs`
- `Assets/Scripts/TankControllerCopy.cs`
- `Assets/Data/TankData/EnemyTankMovementDataCopy.asset`
- `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcpCopy.cs`
- `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs` để route riêng scene TCP sang copy components
- Có thể chỉ đọc/không sửa server nếu log không còn sanitize/invalid action.

## Ghi chú runtime Play Mode - 2026-06-09

- Khi quan sát scene đang chạy với server TCP, chỉ 3 tank tiếp tục bắn Eagle vì một tank đã vào trạng thái `shoot_eagle` nhưng vẫn giữ `hasPendingMove=true`.
- `MapScenarioBootstrapPIBTTcp.AreAgentsReadyForPlanStep()` xem agent đó là còn đang execute action TCP, nên toàn team không được gửi `plan_step` mới. Kết quả nhìn từ Game view là 3 tank bắn được Eagle, 3 tank còn lại đứng im hoặc không nhận chuyển động mới.
- Fix đã áp dụng trên bản copy: `GridEnemyAgentPIBTTcpCopy.Update()` gọi `ClearExecutingActionForShooting()` trước `AimAndShoot()`. Helper này clear `pendingAction`, `hasPendingMove`, `isRotating`, recovery state, dừng Rigidbody và request plan ngay nếu agent vừa chuyển từ trạng thái đang execute sang bắn.
- File gốc không bị sửa: `TankMover.cs`, `TankController.cs`, `GridEnemyAgentPIBTTcp.cs`.
