# PLAN: Điều tra và fix bug TCP PIBT C++ Enemy chỉ đi một đoạn rồi đứng trên non-alpha maps

Ngày lập: 2026-06-22  
Phạm vi: Single Play `PIBT_TCP` với server C++ WSL trên các map ngoài Alpha32: `ht_mansion_n.map`, `ht_chantry.map`, `lt_gallowstemplar_n.map`, `maze-128-128-10.map`. Scene chính: `Assets/Scenes/MapF_TankTest_PIBT.unity`.

## 1. Mục tiêu

Xác định vì sao TCP PIBT C++ vẫn nhận `plan_step` đều nhưng Enemy trên các map non-alpha chỉ di chuyển một chút rồi đứng hẳn. Plan này tách riêng với plan local PIBT trong `adds/PLAN/datn_pibt_non_alpha_maps_stall_fix_plan_2026-06-22.md` và mở rộng plan Mansion trong `adds/fix/datn_pibt_tcp_mansion_enemy_standstill_root_cause_fix_plan_2026-06-22.md`.

Kết quả cần đạt:

- Enemy TCP PIBT trên mỗi non-alpha map không còn đứng yên im lặng sau vài giây nếu server vẫn trả action hợp lệ.
- Khi tank không di chuyển, log phải chỉ ra nguyên nhân cụ thể: server toàn `W`, rotate chưa xong, `FW` target sai, physics kẹt, shooting clear movement, hoặc map unreachable.
- Không làm regression Alpha32 TCP đang là baseline tốt.

## 2. Tình trạng source hiện tại cần lưu ý

Qua kiểm tra source hiện tại:

- `MapTankTestBootstrap.ComputeEnemySpawnCells()` đã cộng `BuildStartX/BuildStartY`, nên lỗi offset spawn local/global không còn là giả thuyết chính.
- `MapLoader` đã có `TryFindWalkableNearInSameComponent(...)` và `AreWalkableCellsConnected(...)`.
- `MapScenarioBootstrapPIBT_TCP.SpawnEnemies()` đã dùng `TryFindWalkableNearInSameComponent(preferredCell, eagleCellWorld, ..., usedCells)`, tức spawn TCP đã cố ép cùng component với Eagle.
- `MapScenarioBootstrapPIBT_TCP.ApplyAction()` cập nhật `_agentOrientations` ngay khi nhận `CR/CCR`, trước khi tank vật lý xoay xong.
- `GridEnemyAgentPIBT_TCP.NotifyServerAction()` hiện đã có `_hasRotationTarget` cho `CR/CCR`, nhưng request tick tiếp theo vẫn có thể đi tiếp trong khi rotation vật lý chưa hoàn tất.
- `BuildStepData()` gửi orientation từ `_agentOrientations`, không đọc orientation vật lý từng tick. Điều này có thể tạo lệch state: server tin agent đã xoay, Unity tank chưa xoay xong.
- `Update()` ưu tiên shooting trước movement/rotation. Nếu line-of-sight hoặc friendly blocker logic bị kích hoạt sai trên map hẹp, movement target có thể bị clear liên tục.

Vì vậy trọng tâm mới không phải "chưa có collider" hay "chưa có rotate", mà là **đồng bộ action execution với planner tick** và **xác minh topology thực tế trên nhiều map**.

## 3. Giả thuyết root cause theo thứ tự ưu tiên

### H1 - Coordinator advance logical orientation quá sớm

Luồng hiện tại có thể là:

1. Server trả `CR` hoặc `CCR`.
2. `MapScenarioBootstrapPIBT_TCP.ApplyAction()` cập nhật `_agentOrientations[i]` ngay.
3. `GridEnemyAgentPIBT_TCP` bắt đầu xoay vật lý nhưng cần nhiều frame mới đạt hướng mới.
4. Tick TCP tiếp theo gửi orientation logical đã xoay sang server.
5. Server trả `FW` theo orientation mới.
6. Tank vật lý vẫn chưa xoay đủ nên `SteerTowardTarget()` phải tự sửa hướng, dễ thành nhúc nhích/xoay/kẹt trên map hẹp.

Alpha32 có thể che lỗi vì map mở; non-alpha map có hành lang/tường làm lệch hướng nhỏ cũng gây stuck.

### H2 - TCP tick không chờ agent execution ack

Server C++ là discrete planner, nhưng Unity execution là continuous physics. Nếu `tcpTickInterval` cứ gửi request đều dù agent chưa hoàn tất `CR/CCR/FW`, server sẽ plan trên state chưa commit. Trên Alpha32 vẫn chạy được vì không gian thoáng; trên Mansion/Chantry/Gallows/Maze hành lang hẹp sẽ tạo drift rồi đứng.

### H3 - `FW` target hợp lệ nhưng physical steering/collider kẹt ở corridor

Nếu trace cho thấy nhiều `FW`, `_hasTarget=true`, `nextCell != CurrentCell`, nhưng velocity gần 0 hoặc `btRecoveryCount` tăng, nguyên nhân nằm ở physical execution:

- steering xoay sai chiều hoặc quá yếu khi lệch nhiều;
- collider/tank đụng tường hoặc đụng friendly;
- `obstacleContactMask` kích hoạt recovery lặp;
- tank spawn đúng component nhưng corridor quá hẹp với collider.

### H4 - Map topology vẫn sai ở một số map dù đã same-component

`TryFindWalkableNearInSameComponent` đảm bảo cùng component walkable theo grid, nhưng chưa chắc đường đủ rộng cho tank physics hoặc server map symbols giống Unity collision. Cần so sánh:

- grid walkable component;
- C++ server map symbols;
- Unity tile/collider thật;
- vị trí tank/collider trong world.

### H5 - Shooting/friendly blocker clear movement liên tục

Trong `GridEnemyAgentPIBT_TCP.Update()`, `GetShootingTarget()` chạy trước rotate/move. Nếu map hẹp làm raycast gặp friendly blocker hoặc Eagle trong range nhưng turret chưa bắn được, agent có thể `ClearMovementTarget()` và request plan liên tục, nhìn như đứng.

### H6 - Server action bị sanitize/`W` sau khi đi một đoạn

Nếu WSL log xuất hiện `sanitize ... fw_row_wrap/fw_blocked` hoặc action-count nhiều `W`, lỗi nằm ở request state/mapping/obstacle model giữa Unity và C++.

## 4. Matrix tái hiện bắt buộc

Chạy cùng một quy trình MCP cho 5 map:

| Nhóm | Map | Mục đích |
| --- | --- | --- |
| Baseline | `random-32-32-10.map` hoặc map Alpha32 đang tốt | Xác nhận fix không phá baseline |
| Non-alpha 1 | `ht_mansion_n.map` | Bug đã tái hiện rõ |
| Non-alpha 2 | `ht_chantry.map` | Map nhiều phòng/hành lang |
| Non-alpha 3 | `lt_gallowstemplar_n.map` | Map lớn, topology khác |
| Non-alpha 4 | `maze-128-128-10.map` | Corridor hẹp, stress physics/rotation |

Quy trình:

1. Đảm bảo WSL server listen `0.0.0.0:7777`.
2. Clear Unity console.
3. Set `PlayerPrefs.SelectedMapFile`.
4. Set `PlayerPrefs.SelectedAlgorithm = PIBT_TCP`.
5. Load `Assets/Scenes/MapF_TankTest_PIBT.unity`.
6. Enter Play Mode.
7. Nếu có `PlacementUI`, invoke skip.
8. Ghi snapshot tại 3s, 10s, 20s, 40s.
9. Lưu screenshot và console/server log vào `adds/output` hoặc `adds/fix/evidence`.

## 5. Instrumentation cần thêm trước khi sửa

### 5.1. TCP action trace trong `PIBTTcpClient`

Thêm debug state:

- `LastPlanRawResponse`
- `LastPlanRequestJson`
- `LastActionCounts`
- `LastPlanRequestId`

Mục tiêu: biết mỗi tick server trả bao nhiêu `FW/CR/CCR/W/UNKNOWN` thay vì chỉ thấy `plan ok`.

### 5.2. Coordinator trace trong `MapScenarioBootstrapPIBT_TCP`

Thêm trace bounded:

- log 20 tick đầu;
- sau đó mỗi 30 tick;
- log ngay khi tất cả agent velocity thấp hoặc không ai có `FW`.

Mỗi tick cần có:

- map file, build bounds, requestId;
- agent `cell`, `loc`, `logicalOri`, `physicalOri`, `goalLoc`;
- response `action`, `nextLoc`, `nextCell`;
- `logicalOriAfter`;
- `agentExecutionState`: moving/rotating/waiting/shooting/recovery;
- action-count toàn team.

Điểm quan trọng: log cả `logicalOri` và `physicalOri`. Nếu hai giá trị lệch kéo dài hơn một tick, H1/H2 gần như đúng.

### 5.3. Agent execution trace trong `GridEnemyAgentPIBT_TCP`

Expose hoặc log:

- `HasMovementTarget`
- `MovementTarget`
- `HasRotationTarget`
- `RotationTargetForward`
- `LastServerAction`
- `IsShooting`
- `btWaitActionCount`
- `btRecoveryCount`
- `btSameCellSeconds`
- `btCellsVisited`
- `Rigidbody2D.linearVelocity`
- reason mới nhất khi clear movement hoặc request plan.

Thêm method debug:

```csharp
public string DebugSnapshot()
```

Không dùng reflection lâu dài cho các field private nếu có thể tránh.

### 5.4. Topology/physics trace

Một lần sau spawn:

- Eagle cell/flat/component.
- Mỗi enemy preferred cell/resolved cell/component.
- `AreWalkableCellsConnected(enemyCell, eagleCell, true)`.
- Physics overlap quanh enemy collider.
- kích thước collider và layer.

## 6. Decision tree sau khi có log

| Evidence | Kết luận | Hướng fix |
| --- | --- | --- |
| `logicalOri != physicalOri` kéo dài, sau đó `FW` sai hướng/stuck | Planner tick đang chạy trước execution | Thêm action-ack gate; chỉ gửi plan_step mới khi agent commit action |
| Nhiều `CR/CCR`, `HasRotationTarget=true`, velocity thấp, không đạt hướng | Rotate execution chưa ổn | Fix rotate-in-place threshold/direction/input và timeout |
| Nhiều `FW`, `nextCell != CurrentCell`, `HasMovementTarget=true`, velocity thấp | Physical steering/collider kẹt | Fix steering/collider/layer/recovery |
| Nhiều `FW`, nhưng `nextCell == CurrentCell` | `nextLoc` hoặc flat/cell mapping sai | Fix `FlatToBuildCell`, `AgentFlat`, orientation delta |
| Server trả chủ yếu `W` hoặc sanitize nhiều | Request/map model sai | So sánh Unity request với C++ map/orientation, fix adapter |
| Agent vào shooting liên tục nhưng không bắn/không đi | Shooting/friendly blocker override movement | Fix shooting gate/raycast mask/blocker behavior |
| Spawn cùng component nhưng physics overlap/collider kẹt ngay đầu | Spawn/physics chưa đủ clearance | Resolve spawn theo collider clearance, không chỉ grid walkable |

## 7. Fix hướng ưu tiên nếu H1/H2 đúng

Thiết kế cần chuyển từ "tick theo thời gian" sang "tick theo commit action".

### 7.1. Thêm trạng thái action execution per-agent

Trong `GridEnemyAgentPIBT_TCP`:

- `IsExecutingServerAction`
- `LastCommittedAction`
- `LastActionCompleteReason`
- `CanAcceptNextServerAction`

Quy ước:

- `CR/CCR` complete khi physical orientation đạt target trong threshold.
- `FW` complete khi `CurrentCell == nextCell` hoặc đến gần cell center.
- `W` complete ngay hoặc sau một interval rất ngắn.
- Shooting có thể pause planner hoặc gửi goal=loc rõ ràng, nhưng không được làm mất action state mơ hồ.

### 7.2. Gate `plan_step` trong coordinator

Trong `MapScenarioBootstrapPIBT_TCP.Update()`:

- Không gửi `plan_step` mới chỉ vì đến `tcpTickInterval` nếu còn agent đang execute action cũ.
- Gửi ngay khi tất cả agent đã complete action hoặc có recovery/protocol error cần replan.
- Nếu một agent timeout execution, mark diagnostic và request replan với state vật lý thật.

Pseudo logic:

```text
if (!AllAgentsReadyForNextPlan() && !HasForcedReplanRequest())
    return;
BuildStepDataFromCommittedPhysicalState();
SendPlanStep();
```

### 7.3. Đồng bộ orientation source of truth

Chọn một trong hai hướng, không trộn:

- Hướng A: physical orientation là source of truth. `BuildStepData()` đọc `ReadAgentOrientation()` khi action complete.
- Hướng B: logical orientation là source of truth, nhưng Unity phải snap/drive physical orientation tới đúng target trước khi tick tiếp.

Khuyến nghị: Hướng A an toàn hơn cho game physics. Chỉ update `_agentOrientations` khi `CR/CCR` complete, không update ngay trong `ApplyAction()`.

## 8. Fix nếu H3/H4 đúng

Nếu action sync đúng nhưng vẫn kẹt:

- Tạo `TryFindWalkableNearWithClearance()` dựa trên collider half-extents hoặc `Physics2D.OverlapBox`.
- Spawn enemy cùng component và đủ clearance quanh cell.
- Với corridor hẹp, giảm steering forward khi góc lệch lớn để tránh cọ tường.
- Log `IsScuffing()` mask hit object/layer thay vì chỉ boolean.
- Không ignore toàn bộ friendly collision nếu điều này làm tank overlap logic; giữ vật lý chống đè nhau nhưng planner phải tránh cùng cell.

## 9. Fix nếu H5 đúng

Nếu shooting/friendly blocker là nguyên nhân:

- `GetShootingTarget()` không được `ClearMovementTarget()` vô điều kiện khi turret chưa aligned hoặc line-of-sight bị friendly chặn.
- Khi friendly blocker xuất hiện, agent nên request replan nhưng vẫn giữ/complete action hiện tại nếu action đó không làm collision xấu hơn.
- Log target type: Eagle/player/friendly-blocked/obstacle-blocked.

## 10. Fix nếu H6 đúng

Nếu server sanitize hoặc trả `W`:

- So sánh orientation convention end-to-end:
  - C++ `UnityStartKitAdapter`: `0=east`, `1=south`, `2=west`, `3=north` theo comment.
  - Unity `ReadAgentOrientation()`.
  - Unity `OrientationToDelta()`.
  - `FlatToBuildCell()` và `AgentFlat()`.
- Kiểm tra `OrientationToDelta(1)` có khớp với C++ `NextLoc + cols` trong hệ tọa độ Unity hiện tại không.
- Nếu row/y convention lệch, sửa tại adapter duy nhất và thêm test/log invariant:
  - `Apply FW locally` phải ra đúng cell với `server nextLoc`.

## 11. Validation sau khi implement

### Static

- `validate_script` cho:
  - `Assets/Scripts/PIBTTcpClient.cs`
  - `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
  - `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs`
  - `Assets/Scripts/MapLoader.cs` nếu sửa topology/clearance.
- Unity console 0 error.

### Runtime

Mỗi map trong matrix phải có artifact:

- console extract;
- WSL server log extract;
- Unity trace action-count;
- snapshot per-agent ở 3s/20s;
- screenshot sau 20s.

Pass criteria mỗi map:

- `_serverReady=true`, `_frame` tăng.
- Không có parse error hoặc `Unexpected plan_step response`.
- Không có agent đứng yên > 10s mà thiếu reason.
- Ít nhất 4/6 agent có `btCellsVisited > 1` trong 20s, trừ agent đang bắn Eagle/player hợp lệ.
- Nếu agent bị kẹt, trace phải chỉ đúng branch: rotate timeout, physics overlap, unreachable, sanitize, shooting blocker.

### Regression

- Alpha32 TCP vẫn chạy tốt.
- Local PIBT không bị ảnh hưởng nếu chỉ sửa TCP class.
- Backtest compile và không vỡ export metrics.

## 12. Artifact đề xuất

Tạo thư mục:

```text
adds/fix/evidence/pibt_tcp_non_alpha_2026-06-22/
```

Mỗi run lưu:

- `unity_console_<map>.log`
- `server_<map>.log`
- `agent_snapshot_<map>_3s.json`
- `agent_snapshot_<map>_20s.json`
- `screenshot_<map>_20s.png`
- `summary_<map>.md`

## 13. Thứ tự triển khai đề xuất

1. Thêm instrumentation không đổi behavior.
2. Chạy matrix Alpha32 + Mansion + Chantry + Gallows + Maze.
3. Phân loại theo decision tree.
4. Nếu H1/H2 đúng, implement action-ack gate trước.
5. Nếu H3/H4 đúng, implement clearance/physics fix sau.
6. Nếu H5 đúng, sửa shooting blocker gate.
7. Nếu H6 đúng, sửa orientation/flat adapter và thêm invariant check.
8. Validate lại matrix và lưu evidence.

## 14. Kết luận kỹ thuật hiện tại

Vì source đã có same-component spawn và đã có rotate target cho `CR/CCR`, bug còn lại nhiều khả năng không phải lỗi "không spawn được" hay "thiếu BoxCollider" đơn giản. Hướng điều tra đúng là đo sự lệch giữa discrete planner state của C++ và continuous execution state trong Unity. Nếu server tick chạy trước khi tank thật sự hoàn tất `CR/CCR/FW`, các map hẹp ngoài Alpha32 sẽ đứng yên rất dễ dù server vẫn log `plan_step` đều.
