# Tách việc nhanh để implement mode `PIBT_TCP`

Tài liệu này rút gọn từ `mapf_pibt_tcp_client_server_plan.md` thành checklist triển khai theo thứ tự ít rủi ro nhất. Mục tiêu là tạo được mode chơi mới chạy qua TCP trước, sau đó mới tối ưu planner và backtest.

## Quyết định mặc định

| Hạng mục | Chọn cho MVP |
|----------|--------------|
| Protocol | JSON line, mỗi message kết thúc bằng `\n` |
| Host/port | `127.0.0.1:7777` |
| Server timeout | Unity cho enemy `Wait` |
| Planner timeout | Server trả `W` cho toàn bộ agent |
| Dynamic obstacle | Chưa đưa vào MVP |
| Session | Một Unity session cho một server process |
| Goal | Enemy đi về Eagle cell |

## Sprint 1 - TCP server giả trong WSL

Mục tiêu: chứng minh Unity có thể gọi server qua TCP mà chưa cần planner thật.

### WSL files

```text
/home/west2light/projectY/src/tcp_server_main.cpp
/home/west2light/projectY/src/PibtTcpServer.h
/home/west2light/projectY/src/PibtTcpServer.cpp
```

### Việc cần làm

1. Thêm CMake target `pibt_tcp_server`.
2. Server listen `127.0.0.1:7777`.
3. Nhận `hello`, trả `hello_ack`.
4. Nhận `plan_step`, trả `plan_result` với toàn bộ action là `W`.
5. Log `sessionId`, `requestId`, `teamSize`, số agent nhận được.

### Pass condition

```bash
cd /home/west2light/projectY
./build/pibt_tcp_server --host 127.0.0.1 --port 7777
```

Server không crash khi nhận nhiều request liên tiếp.

## Sprint 2 - Unity TCP client và grid adapter

Mục tiêu: Unity connect được, gửi map/agent/goal đúng format start-kit.

### Unity files

```text
Assets/Scripts/PibtTcp/PibtTcpProtocol.cs
Assets/Scripts/PibtTcp/PibtTcpGridAdapter.cs
Assets/Scripts/PibtTcp/PibtTcpClient.cs
Assets/Scripts/PibtTcp/PibtTcpSessionState.cs
```

### Việc cần làm

1. Tạo DTO cho `hello`, `hello_ack`, `plan_step`, `plan_result`.
2. Implement mapping:

```csharp
loc = cell.y * width + cell.x
cell = new Vector2Int(loc % width, loc / width)
orientation: 0=east, 1=south, 2=west, 3=north
```

3. `PibtTcpClient` chạy async, không block Unity main thread.
4. Timeout mặc định `100 ms`.
5. Log `latencyMs`, `computeMs`, `timeoutCount`.

### Pass condition

Unity Play Mode gửi được `hello` và `plan_step`, server fake trả `W`, Unity không freeze khi tắt server.

## Sprint 3 - Bootstrap scene mode mới

Mục tiêu: tạo mode riêng, không làm hỏng A* và PIBT C#.

### Unity files

```text
Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs
Assets/Scenes/MapF_TankTest_PIBT_TCP.unity
```

### Việc cần làm

1. Duplicate `MapF_TankTest_PIBT` thành `MapF_TankTest_PIBT_TCP`.
2. Thay `MapScenarioBootstrapPIBT` bằng `MapScenarioBootstrapPIBTTcp`.
3. `MapScenarioBootstrapPIBTTcp` tạo `PibtTcpClient`, spawn enemy và gán agent id.
4. Sửa `MapTankTestBootstrap.SpawnScenario()` ưu tiên bootstrap TCP nếu scene có component này.
5. Đảm bảo mỗi enemy chỉ có một AI runtime: không chạy `GridEnemyAgent`, `GridEnemyAgentPIBT`, `DefaultEnemyAI` cùng lúc.

### Pass condition

Ba scene chạy độc lập:

```text
MapF_TankTest
MapF_TankTest_PIBT
MapF_TankTest_PIBT_TCP
```

## Sprint 4 - Enemy runtime TCP

Mục tiêu: enemy nhận action từ server và phản ứng trong gameplay.

### Unity files

```text
Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs
```

### Việc cần làm

1. Copy có chọn lọc từ `GridEnemyAgentPIBT`: shooting, metrics, recovery cơ bản.
2. Thay path planning bằng action từ `PibtTcpSessionState`.
3. Mapping action:

| Action | Unity behavior |
|--------|----------------|
| `FW` | đi tới center của `nextLoc` |
| `CR` | quay clockwise tại chỗ |
| `CCR` | quay counter-clockwise tại chỗ |
| `W` | dừng body movement |

4. Khi đang bắn player/Eagle, giữ shooting logic ưu tiên như PIBT C#.
5. Chỉ commit cell mới khi tank đủ gần center cell để tránh lệch physics/server.

### Pass condition

Với server fake `W`, enemy đứng yên nhưng vẫn không crash. Sau đó khi server trả `FW`, enemy đi được giữa các cell.

## Sprint 5 - Nối planner C++ thật

Mục tiêu: server gọi `DefaultPlanner::plan(...)` thay vì trả action giả.

### WSL files

```text
/home/west2light/projectY/src/PlannerSession.h
/home/west2light/projectY/src/PlannerSession.cpp
/home/west2light/projectY/src/UnityStartKitAdapter.h
/home/west2light/projectY/src/UnityStartKitAdapter.cpp
```

### Việc cần làm

1. Convert `hello.map.symbols` sang `SharedEnvironment.map`.
2. Set `env.rows`, `env.cols`, `env.num_of_agents`.
3. Set `env.curr_states` từ `plan_step.agents`.
4. Set `env.goal_locations[i] = {{goalLoc, timestep}}`.
5. Set `env.curr_timestep` và `env.plan_start_time`.
6. Gọi:

```cpp
DefaultPlanner::initialize(preprocess_time_limit, &env);
DefaultPlanner::plan(plan_time_limit, actions, &env);
```

7. Convert action enum sang `FW`, `CR`, `CCR`, `W`.

### Pass condition

Server trả action thật, enemy TCP đi về Eagle trên static map.

## Sprint 6 - Backtest thuật toán thứ ba

Mục tiêu: đưa `PIBT_TCP` vào report so sánh.

### Unity files

```text
Assets/Scripts/Backtest/BacktestMode.cs
Assets/Scripts/Backtest/BacktestRunner.cs
Assets/Scripts/Backtest/BacktestData.cs
Assets/Scripts/Backtest/BacktestResultChart.cs
```

### Việc cần làm

1. Thêm algorithm `"PIBT_TCP"`.
2. Thêm scene `MapF_TankTest_PIBT_TCP` vào job list.
3. Collect metrics từ `GridEnemyAgentPIBTTcp`.
4. Thêm metric:
   - `ServerLatencyMsAvg`
   - `PlannerComputeMsAvg`
   - `ServerTimeoutCount`
5. Chart/CSV hiển thị đủ A*, PIBT, PIBT_TCP.

### Pass condition

Backtest xuất CSV/HTML có ba thuật toán trên cùng map set.

## Thứ tự code khuyến nghị

1. `pibt_tcp_server` fake `W`.
2. `PibtTcpGridAdapter`.
3. `PibtTcpClient`.
4. `MapScenarioBootstrapPIBTTcp`.
5. `GridEnemyAgentPIBTTcp` với fake server.
6. `PlannerSession` gọi planner thật.
7. `BacktestRunner` mở rộng.

## Ranh giới không làm trong MVP

- Không thêm dynamic obstacle vào request.
- Không support nhiều Unity client cùng lúc.
- Không gọi CLI `lifelong` mỗi frame làm bản chính thức.
- Không sửa logic của `GridEnemyAgent` và `GridEnemyAgentPIBT` ngoài phần bootstrap/backtest cần thiết.
- Không teleport tank theo `nextLoc`; vẫn dùng steering/physics của game.
