# PLAN V2: Fix TCP PIBT C++ đạt 5/5 map trong hệ thống

Ngày lập: 2026-06-22  
Phạm vi: Single Play `PIBT_TCP` với server C++ WSL, scene `Assets/Scenes/MapF_TankTest_PIBT.unity`.  
Mục tiêu map: `random-32-32-10.map`/Alpha32 baseline, `ht_mansion_n.map`, `ht_chantry.map`, `lt_gallowstemplar_n.map`, `maze-128-128-10.map`.

## 1. Mục tiêu V2

V2 không chỉ điều tra nguyên nhân mà định nghĩa kiến trúc fix đủ bao phủ 5/5 map. Cách tiếp cận là đặt invariant bắt buộc giữa C++ planner và Unity execution, rồi thêm các lớp fallback để từng loại map đều có đường xử lý rõ.

Kết quả cần đạt:

- 5/5 map chạy TCP PIBT mà Enemy không đứng yên im lặng.
- Alpha32 giữ hành vi tốt hiện tại.
- Mansion/Chantry/Gallows/Maze đều có Enemy tiếp cận hoặc bắn Eagle trong thời gian test.
- Nếu một agent không di chuyển, phải có reason cụ thể trong log và không được làm toàn team deadlock.

## 2. Nguyên tắc sửa

1. **Không fix theo map name.** Mọi fix phải dựa trên invariant runtime: action completion, reachable component, clearance, sanitize, shooting state.
2. **Server C++ là discrete planner, Unity là continuous executor.** Không gửi state mới sang server khi Unity chưa commit action cũ.
3. **Physical state là source of truth cuối cùng.** Nếu logical orientation/cell khác body thật, build request bằng body thật hoặc chờ commit.
4. **Không để một agent block cả team vô hạn.** Team gate cần timeout và recovery rõ, không deadlock vì một tank kẹt.
5. **Mọi fallback phải có log.** Không tự sửa im lặng vì sẽ khó chứng minh 5/5 map.

## 3. Invariant bắt buộc để pass 5/5 map

### I1 - Request state hợp lệ

Trước mỗi `plan_step`, với từng agent:

- `loc` phải nằm trong build bounds.
- `orientation` phải thuộc `0..3`.
- `goalLoc` phải nằm trong build bounds.
- `loc` phải khớp `CurrentCell` sau chuyển đổi `AgentFlat -> FlatToBuildCell`.
- Nếu agent không shooting, `goalLoc` phải là Eagle hoặc staging goal reachable.

Nếu fail: không gửi request mù; log `[PIBT_TCP_INVARIANT]` và re-resolve state.

### I2 - Action completion

Mỗi action từ server phải có trạng thái:

- `Pending`: vừa nhận.
- `Executing`: Unity đang xoay/chạy/chờ.
- `Committed`: action đã hoàn tất trong world.
- `Failed`: timeout, blocked, invalid target, hoặc physics stuck.

Không gửi `plan_step` tick mới theo thời gian nếu phần lớn agent vẫn đang `Executing`, trừ khi có forced replan.

### I3 - Orientation sync

Với `CR/CCR`:

- Không update `_agentOrientations` như đã commit ngay khi nhận action.
- Chỉ commit orientation khi tank body đạt hướng mới trong threshold.
- Nếu rotation timeout, dùng physical orientation hiện tại để request replan.

### I4 - Cell sync

Với `FW`:

- Target cell phải là `nextLoc` từ server nếu có.
- Action complete khi `CurrentCell == targetCell` hoặc tank ở gần center target trong threshold.
- Nếu sau timeout vẫn chưa tới target, action failed và request replan bằng physical cell hiện tại.

### I5 - Team progress

Trong 20 giây đầu:

- Ít nhất 4/6 agent phải tăng `btCellsVisited`, hoặc
- agent không tăng cell phải ở một trong các state hợp lệ: shooting, blocked-by-friendly-with-replan, unreachable-with-diagnostic, disabled-by-spawn-failure.

## 4. Kiến trúc fix V2

### Layer A - Instrumentation và evidence

Thêm trace có thể bật/tắt:

- `PIBTTcpClient`: raw request/response, action counts, request id.
- `MapScenarioBootstrapPIBT_TCP`: per-tick team state, logical vs physical orientation, action commit status.
- `GridEnemyAgentPIBT_TCP`: per-agent execution state, target, velocity, rotation status, last clear/replan reason.
- `MapLoader`: component id/size, connectivity, clearance result.

Output đề xuất:

```text
adds/fix/evidence/pibt_tcp_5map_v2_2026-06-22/
```

### Layer B - Action-ack gate

Thêm gate trong coordinator:

```text
if (!AllAgentsReadyForNextPlan() && !HasForcedReplanRequest())
    return;
BuildStepDataFromPhysicalCommittedState();
SendPlanStep();
```

Quy tắc:

- `W` complete nhanh.
- `CR/CCR` complete sau khi body xoay xong.
- `FW` complete sau khi body tới target cell.
- Shooting không được xóa action execution mơ hồ; shooting phải là state riêng.
- Forced replan được phép phá gate khi agent timeout/stuck.

### Layer C - Physical-state request builder

`BuildStepData()` phải ưu tiên state đã commit:

- `loc = AgentFlatFromCurrentCell()`.
- `orientation = ReadAgentOrientation()` hoặc committed orientation mới nhất đã sync.
- Nếu agent đang shooting hợp lệ: `goalLoc = loc`.
- Nếu agent đang failed/recovery: dùng physical cell và physical orientation hiện tại.

Mục tiêu: server không bao giờ plan trên orientation/cell mà Unity chưa thật sự đạt được.

### Layer D - Map topology and clearance

Spawn và goal phải có hai tiêu chuẩn:

- cùng connected component với Eagle;
- đủ physical clearance cho collider tank.

Nếu map hẹp:

- tìm spawn gần preferred nhưng có clearance.
- nếu Eagle component hẹp hoặc Eagle nằm ở pocket xấu, chọn staging goal quanh Eagle trong cùng component.
- nếu không có đường vật lý đủ rộng, log fail map-specific nhưng không để agent silent stall.

### Layer E - Movement/rotation executor hardening

For `CR/CCR`:

- dùng target angle/forward cố định theo action.
- không cộng dồn rotation target sai nếu nhận action mới khi action cũ chưa commit.
- timeout rotation có reason.

For `FW`:

- chỉ drive forward khi alignment đủ.
- khi lệch lớn, xoay tại chỗ hoặc forward rất nhỏ để tránh cọ tường.
- detect scuff bằng object/layer cụ thể.
- nếu stuck, recovery không chỉ reverse mù; replan bằng physical state.

### Layer F - Shooting and friendly blocker

Tách shooting khỏi navigation:

- Agent chỉ vào shooting state khi target thật sự shootable hoặc đang align turret trong range hợp lệ.
- Friendly blocker không được clear movement vĩnh viễn.
- Nếu friendly blocker chặn Eagle, request replan nhưng action hiện tại phải complete/fail rõ.
- Nếu turret không aligned trong timeout, thoát shooting và replan.

### Layer G - Server adapter invariant

Kiểm tra end-to-end:

- C++ comment: `0=east`, `1=south`, `2=west`, `3=north`.
- Unity `ReadAgentOrientation()`.
- Unity `OrientationToDelta()`.
- Server `nextLoc`.

Invariant:

```text
FlatToBuildCell(serverNextLoc) == CurrentCell + OrientationToDelta(sentOrientation)
```

Nếu invariant fail, log và không áp action đó. Fix phải nằm ở một adapter duy nhất, không sửa rải rác.

## 5. Coverage matrix 5/5

| Map | Rủi ro chính | Gate bắt buộc |
| --- | --- | --- |
| Alpha32/random32 | Regression baseline | Action-ack không làm chậm quá mức, không phá existing movement |
| Mansion | Lệch orientation/tick, phòng/tường | logical vs physical orientation phải sync |
| Chantry | Nhiều room/hành lang | topology + staging goal + friendly blocker |
| Gallows | Map lớn, component/clearance | spawn/goal same component + progress timeout |
| Maze128 | corridor hẹp | action-ack + steering/collider clearance + stuck recovery |

Pass 5/5 chỉ khi từng map có artifact và đạt criteria. Không được pass bằng cảm giác nhìn game.

## 6. Acceptance criteria định lượng

Mỗi map chạy tối thiểu 40 giây sau skip placement.

Pass map khi:

- `_serverReady=true`.
- `_frame >= 10`.
- không có parse/protocol error.
- server log không có sanitize lặp vô hạn cho cùng agent.
- ít nhất 4/6 agent có `btCellsVisited >= 2` trong 20 giây, trừ agent đang shooting hợp lệ.
- ít nhất 1 agent gây sát thương Eagle hoặc vào range bắn hợp lệ trong thời gian test phù hợp map.
- không agent nào đứng yên quá 10 giây mà `LastActionCompleteReason` rỗng.
- không có overlap physics rõ ràng giữa Enemy tanks tại spawn.

Fail map khi:

- team planner vẫn tick đều nhưng toàn bộ agents idle không reason.
- một agent executing action quá timeout và block cả team.
- `logicalOri != physicalOri` kéo dài hơn 2 plan cycles.
- `FW` nhận `nextCell == CurrentCell` lặp mà không có diagnostic.

## 7. Phased implementation để đạt 5/5

### M0 - Baseline evidence

Không sửa behavior. Thêm hoặc dùng dump MCP để lấy:

- action counts;
- per-agent state;
- logical/physical orientation;
- current/target cell;
- velocity;
- shooting/recovery;
- server sanitize log.

Chạy 5 map và lưu summary.

### M1 - Action execution state model

Thêm state model vào `GridEnemyAgentPIBT_TCP`:

- `ServerActionState`
- `CurrentServerAction`
- `ActionTargetCell`
- `ActionTargetForward`
- `ActionStartedAt`
- `LastActionCompleteReason`

Không đổi planner gate lớn ở M1, chỉ chuẩn hóa state và logs.

### M2 - Coordinator action-ack gate

Sửa `MapScenarioBootstrapPIBT_TCP.Update()`:

- chờ `AllAgentsReadyForNextPlan()`;
- cho phép forced replan;
- không advance `_agentOrientations` trước commit;
- build request bằng physical/committed state.

Đây là fix chính có khả năng sửa đồng loạt non-alpha maps.

### M3 - Adapter invariant and nextLoc validation

Thêm invariant check:

- sent orientation;
- expected next cell;
- server `nextLoc`;
- actual applied next cell.

Nếu fail, mark protocol/adapter diagnostic. Fix orientation mapping nếu evidence chỉ ra mismatch.

### M4 - Clearance-safe spawn and goal

Nâng spawn/goal từ walkable-only lên walkable + clearance:

- enemy spawn unique;
- same component;
- no immediate overlap;
- enough collider clearance;
- staging goal quanh Eagle nếu Eagle cell hẹp.

### M5 - Corridor movement hardening

Sửa executor nếu Maze/Chantry/Gallows vẫn fail:

- rotate tại chỗ khi angle lớn;
- không forward vào tường khi chưa align;
- stuck timeout replan bằng physical state;
- reverse recovery chỉ là fallback ngắn, không loop vô hạn.

### M6 - Shooting/friendly blocker hardening

Sửa nếu trace cho thấy shooting clear movement:

- shooting state có timeout;
- friendly blocker không clear target vĩnh viễn;
- replan quanh blocker;
- nếu player/Eagle raycast fail, quay về navigation.

### M7 - Full 5-map validation

Chạy lại matrix đầy đủ:

- Alpha32/random32;
- Mansion;
- Chantry;
- Gallows;
- Maze128.

Lưu artifact, tổng hợp pass/fail, chỉ đóng bug khi 5/5 pass theo tiêu chí định lượng.

## 8. Fallback cuối nếu vẫn chưa 5/5

Nếu sau M2-M6 vẫn còn map fail, áp fallback có kiểm soát:

### F1 - Hybrid local fallback per-agent

Nếu TCP server trả `W`/invalid liên tục cho một agent trong N tick nhưng map reachable, agent đó tạm dùng local single-step greedy/PIBT-lite trong 1-2 cell rồi quay lại TCP.

Chỉ bật khi:

- server action không giúp tiến triển;
- agent không shooting;
- fallback step không conflict occupied cells.

### F2 - Staging waypoints

Với map lớn/hẹp, không dùng Eagle làm goal trực tiếp mỗi tick. Tạo staging goals:

- waypoint cùng component;
- giảm khoảng cách tới Eagle;
- có clearance;
- không trùng agent khác.

TCP vẫn plan tới `goalLoc`, nhưng `goalLoc` có thể là staging goal thay vì Eagle nếu direct Eagle goal làm team nghẽn.

### F3 - Agent quarantine

Nếu một agent kẹt vật lý quá lâu:

- exclude agent khỏi team gate trong thời gian ngắn;
- giữ agent ở `W/recovery`;
- các agent khác tiếp tục plan.

Mục tiêu: không để 1 tank làm 5 tank còn lại đứng.

### F4 - Map-specific diagnostics, not map-specific behavior

Nếu map fail do dữ liệu map/collider không thống nhất, tạo diagnostic riêng cho map đó. Không hard-code behavior theo tên map trừ khi user chấp nhận workaround.

## 9. Files dự kiến sẽ sửa khi implement

Ưu tiên sửa hẹp:

- `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs`
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
- `Assets/Scripts/PIBTTcpClient.cs`
- `Assets/Scripts/MapLoader.cs` nếu cần clearance/topology helper

Không sửa local PIBT (`GridEnemyAgentPIBT.cs`, `MapScenarioBootstrapPIBT.cs`) nếu evidence không yêu cầu.

## 10. Verification commands/checks

Static:

```text
validate_script Assets/Scripts/GridEnemyAgentPIBT_TCP.cs
validate_script Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs
validate_script Assets/Scripts/PIBTTcpClient.cs
validate_script Assets/Scripts/MapLoader.cs
read_console errors
```

Runtime:

```text
WSL server: ./build/pibt_tcp_server --host 0.0.0.0 --port 7777
Unity PlayerPrefs SelectedAlgorithm=PIBT_TCP
Unity PlayerPrefs SelectedMapFile=<map>
Load Assets/Scenes/MapF_TankTest_PIBT.unity
Play, skip PlacementUI, capture 3s/20s/40s
```

Evidence summary per map:

```text
map=<name>
serverReady=true
frames=<n>
actions FW/CR/CCR/W=<counts>
agentsMoved=<n>/6
shootingValid=<n>
stallsWithReason=<n>
protocolErrors=0
sanitizeLoop=false
pass=true|false
```

## 11. Rủi ro

- Action-ack gate có thể làm game chậm nếu timeout quá dài. Cần timeout ngắn và forced replan.
- Nếu gate chờ all agents tuyệt đối, một tank kẹt có thể deadlock team. Vì vậy phải có quarantine/timeout.
- Clearance quá chặt có thể làm một số map không spawn đủ 6 enemy. Khi đó cần staging/relax có log, không im lặng.
- Hybrid fallback có thể làm kết quả không còn thuần TCP PIBT. Chỉ dùng như fallback cuối và phải log rõ.

## 12. Kết luận V2

Để đạt 5/5 map, fix phải xử lý cả ba lớp:

- **Protocol/execution sync:** action-ack gate và physical state source of truth.
- **Map robustness:** same-component + clearance + staging goal.
- **Runtime resilience:** stuck recovery, shooting blocker, quarantine, diagnostic reason.

Nếu chỉ sửa một nhánh như `CR/CCR` hoặc collider, có thể pass Mansion nhưng fail Maze/Chantry. V2 buộc mọi map đi qua cùng invariant và có fallback khi invariant không đủ, nên đây là kế hoạch phù hợp để đạt 5/5 map thay vì fix cục bộ.
