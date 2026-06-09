# PIBT TCP Implementation — Progress Notes

## Sprint 01 — TCP Server giả trong WSL ✅

**Hoàn thành:** Đã implement và build thành công.

### Files tạo/sửa (WSL)
- `/home/west2light/projectY/src/tcp_server_main.cpp` — entry point, parse `--host`, `--port`
- `/home/west2light/projectY/src/PibtTcpServer.h` — khai báo class
- `/home/west2light/projectY/src/PibtTcpServer.cpp` — logic serve với Boost.Asio + nlohmann::json
- `CMakeLists.txt` — thêm target `pibt_tcp_server`, tách khỏi target `lifelong`

### Chức năng server
- Protocol: JSON line (`\n`-delimited), host `127.0.0.1`, port `7777`
- Nhận `hello` → trả `hello_ack` (status ok, planner = FakeWaitPlanner)
- Nhận `plan_step` → trả `plan_result` với toàn bộ action = `W`, `nextLoc = loc`
- Nhận `shutdown` → trả `shutdown_ack` và đóng session
- Log: `sessionId`, `requestId`, `teamSize`, số agent nhận được

### Verify
- Build thành công: `build/pibt_tcp_server` tồn tại
- Server bind và listen port trong WSL

---

## Sprint 02 — Unity TCP Client và Grid Adapter ✅

**Hoàn thành:** 4 file tạo mới, compile 0 errors.

### Files tạo (Unity)
| File | Nội dung |
|------|---------|
| `Assets/Scripts/PibtTcp/PibtTcpProtocol.cs` | DTOs: `HelloRequest`, `HelloAck`, `PlanStepRequest`, `PlanResult`, `ActionDto`, `ShutdownRequest` — namespace `PibtTcp` |
| `Assets/Scripts/PibtTcp/PibtTcpGridAdapter.cs` | `CellToLoc` / `LocToCell` (row-major), `DirectionToOrientation` (0=east 1=south 2=west 3=north), `EncodeTileMap` (`'.'`/`'@'`), builder helpers |
| `Assets/Scripts/PibtTcp/PibtTcpClient.cs` | Async TCP client: `ConnectAndHelloAsync`, `PlanStepAsync` (timeout 100 ms), `DisconnectAsync`, log `latencyMs` / `computeMs` / `timeoutCount`. Dùng `Task.WhenAny` thay `WaitAsync` để tương thích .NET Standard 2.1 |
| `Assets/Scripts/PibtTcp/PibtTcpSessionState.cs` | Thread-safe state (lock), `SetActions` / `GetAction`, metadata: `SessionId`, `TeamSize`, `LatencyMsLast`, `ComputeMsLast`, `TimeoutCount`, `ResultReady` |

### Quyết định kỹ thuật
- `PibtTcpClient` chạy async, không block Unity main thread
- `PibtTcpSessionState` dùng `lock(_actions)` để an toàn giữa background thread (client) và main thread (agents)
- Timeout mặc định `100 ms` per step; connect timeout `500 ms`

---

## Sprint 03 — Bootstrap Scene Mode Mới ✅

**Hoàn thành:** Script tạo xong, scene duplicate và swap component, compile 0 errors.

### Files tạo/sửa (Unity)
| File | Thay đổi |
|------|---------|
| `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs` | **[NEW]** Bootstrap TCP: clone PIBT, tạo `PibtTcpClient` + `PibtTcpSessionState`, connect async sau spawn, placeholder `AddGridEnemyAgentPIBTTcp` (Sprint 4 sẽ implement) |
| `Assets/Scripts/MapTankTestBootstrap.cs` | **[MODIFY]** `SpawnScenario()` và `GetSpawnedEnemies()`: ưu tiên TCP bootstrap → PIBT bootstrap → A* bootstrap |
| `Assets/Scenes/MapF_TankTest_PIBT_TCP.unity` | **[NEW]** Duplicate từ `MapF_TankTest_PIBT`, swap `MapScenarioBootstrapPIBT` → `MapScenarioBootstrapPIBTTcp` |

### Hành vi hiện tại
- `MapScenarioBootstrapPIBTTcp.SpawnScenario()`: spawn Eagle, spawn enemies (theo pattern PIBT), sau đó coroutine `ConnectAndStartSession()` kết nối TCP server
- Mỗi enemy: `DefaultEnemyAI` bị disabled, không có `GridEnemyAgentPIBT` — chỉ có placeholder log agentId
- 3 scene độc lập: `MapF_TankTest`, `MapF_TankTest_PIBT`, `MapF_TankTest_PIBT_TCP`

### Pass condition
- Scene `MapF_TankTest_PIBT_TCP` load được, enemy spawn, log `[MapScenarioBootstrapPIBTTcp] Agent N placeholder`
- Khi server TCP đang chạy: log `[PibtTcpClient] Connected: server=pibt_tcp_server, planner=FakeWaitPlanner`
- Khi không có server: log error, game không crash

### Ghi chú sprint
- `AddGridEnemyAgentPIBTTcp` còn là placeholder — comment để lại hướng dẫn rõ ràng cho Sprint 4
- `MapTankTestBootstrap.SpawnScenario()` theo priority rõ ràng: TCP > PIBT > A*

---

## Sprint 04 — Enemy Runtime TCP ✅

**Hoàn thành:** File tạo xong, bootstrap wired, compile 0 errors.

### Files tạo/sửa (Unity)
| File | Thay đổi |
|------|---------|
| `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs` | **[NEW]** Enemy agent đọc action từ TCP session |
| `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs` | **[MODIFY]** `AddGridEnemyAgentPIBTTcp()` bỏ placeholder, gắn agent thật; expose `GetTcpClient()` |

### Thiết kế GridEnemyAgentPIBTTcp

**Pipeline mỗi tick:**
1. Shooting check (ưu tiên nhất — giữ nguyên từ PIBT)
2. Reverse recovery (scuff timeout)
3. `SendPlanStepAsync()` → gửi `plan_step` lên server qua `PibtTcpClient` (Task.Run, không block main thread)
4. Execute action từ `PibtTcpSessionState`: `FW` | `CR` | `CCR` | `W`

**Action mapping:**
| Action | Unity behavior |
|--------|----------------|
| `FW` | Steer đến `mapLoader.CellToWorld(nextLoc)` bằng steering logic giống PIBT |
| `CR` | Quay clockwise −90° trong `RotateDuration = 0.18s` |
| `CCR` | Quay counter-clockwise +90° |
| `W` | `HandleMoveBody(Vector2.zero)` |

**Cell commit:** chỉ update `currentCell` khi `dist <= cellCenterThreshold (0.25f)` để tránh lệch physics/server.

**Async safety:** dùng `CancellationTokenSource` per request; kết quả được viết vào `PibtTcpSessionState` từ background thread qua `SetActions()` (thread-safe lock).

**Backtest metrics thu thập:**
- `btShotCount`, `btCellsVisited`, `btTimeoutCount`
- `btLatencyMsTotal`, `btStepCount` (để tính avg latency)

### Pass condition
- Server fake `W`: enemy spawn, đứng yên, không crash, log `Agent N (GridEnemyAgentPIBTTcp) attached`
- Server trả `FW`: enemy steer về `nextLoc` trên grid, `btCellsVisited` tăng

---

## Tiếp theo: Sprint 05 — Nối Planner C++ Thật

Files cần tạo (WSL):
- `/home/west2light/projectY/src/PlannerSession.h` / `.cpp`
- `/home/west2light/projectY/src/UnityStartKitAdapter.h` / `.cpp`

Việc cần làm:
1. Convert `hello.map.symbols` → `SharedEnvironment.map`
2. Set env: `rows`, `cols`, `num_of_agents`, `curr_states`, `goal_locations`, `curr_timestep`, `plan_start_time`
3. Gọi `DefaultPlanner::initialize()` + `DefaultPlanner::plan()`
4. Convert action enum → `"FW"`, `"CR"`, `"CCR"`, `"W"`