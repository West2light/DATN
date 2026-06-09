# Kế hoạch mode `PIBT_TCP` qua Client-Server

## Mục tiêu

Thêm một mode chơi thứ ba ngoài A* và PIBT C# hiện tại:

| Mode | Runtime trong Unity | Thuật toán |
|------|---------------------|------------|
| `AStar` | `GridEnemyAgent` | A* grid-based trong C# |
| `PIBT` | `GridEnemyAgentPIBT` | PIBT/LNS2 đã port sang C# |
| `PIBT_TCP` | Unity client + C++ server qua TCP | Planner C++ gốc từ start-kit trong WSL |

Mục tiêu của `PIBT_TCP` không phải thay thế `PIBTPlanner.cs`, mà là chứng minh hướng tích hợp thứ hai trong DATN: Unity gửi trạng thái game sang server C++ qua TCP, server chạy planner từ start-kit No Man's Sky/MAPF Competition, rồi trả action/path để enemy tank di chuyển trong scene.

Nguồn C++ hiện nằm trong WSL:

```text
/home/west2light/projectY
```

Các file cần giữ làm nguồn sự thật về format:

| File | Vai trò |
|------|---------|
| `/home/west2light/projectY/Input_Output_Format.md` | Format input/output chính thức của start-kit |
| `/home/west2light/projectY/src/driver.cpp` | CLI entrypoint hiện tại: đọc `--inputFile`, ghi `--output` |
| `/home/west2light/projectY/inc/SharedEnv.h` | State runtime mà planner C++ đọc |
| `/home/west2light/projectY/default_planner/planner.cpp` | Orchestration: traffic flow assignment + PIBT action selection |
| `/home/west2light/projectY/default_planner/pibt.cpp` | Causal PIBT action selection |
| `/home/west2light/projectY/default_planner/flow.cpp` | Frank-Wolfe / trajectory flow update |

Lưu ý quan trọng: start-kit hiện là chương trình CLI batch (`lifelong --inputFile ... -o ...`), chưa thấy TCP server có sẵn. Vì vậy cần thêm lớp TCP server mỏng, nhưng không được tự đổi format lõi của start-kit.

---

## Format dữ liệu start-kit cần tuân thủ

### Input problem JSON

Start-kit nhận một file JSON có các field chính:

```json
{
  "mapFile": "maps/random-32-32-20.map",
  "agentFile": "agents/random_32_32_20_100.agents",
  "teamSize": 100,
  "taskFile": "tasks/random_32_32_20.tasks",
  "numTasksReveal": 1.5,
  "version": "2024 LoRR"
}
```

Các path trong JSON là path tương đối so với vị trí của file JSON.

### Map file

Map dùng format benchmark `.map`:

```text
type octile
height 32
width 32
map
..........@......@...@.@........
...
```

Ký hiệu:

| Ký hiệu | Ý nghĩa |
|---------|---------|
| `.` | ô trống |
| `@` | hard obstacle |
| `T` | hard obstacle |
| `E` | traversable delivery/emitter |
| `S` | traversable service/pickup |

Unity hiện cũng dùng format `.map` trong `Assets/MapData`, nên có thể tái sử dụng trực tiếp hoặc export lại từ `MapLoader` khi có obstacle runtime.

### Agent file

Agent file:

```text
# version for LoRR 2024
100
360
636
886
...
```

Dòng đầu là comment version, dòng tiếp theo là số agent, các dòng sau là vị trí start dạng linear location.

### Task file

Task file:

```text
# version for LoRR 2024
100000
627,871
802,623
...
```

Dòng đầu là comment version, dòng tiếp theo là số task, các dòng sau là danh sách location của task. Với game tank hiện tại, MVP có thể tạo mỗi enemy một task đi đến `EagleBase`, tức mỗi task chỉ cần một location goal.

### Linear location

Start-kit linearize tọa độ theo công thức:

```text
location = row * width + column
```

Trong Unity, `Vector2Int cell` đang dùng:

```text
column = cell.x
row    = cell.y
```

Do đó adapter cần dùng:

```csharp
int ToStartKitLocation(Vector2Int cell, int width) => cell.y * width + cell.x;
Vector2Int FromStartKitLocation(int loc, int width) => new Vector2Int(loc % width, loc / width);
```

Quy ước này khớp với `MapLoader`: top-left origin, Y tăng xuống dưới.

### Output JSON của CLI

Output chính thức của `./build/lifelong` là JSON gồm các field:

| Field | Ý nghĩa |
|-------|---------|
| `actionModel` | Luôn là `MAPF_T` |
| `teamSize` | Số robot |
| `start` | Start location list |
| `actualPaths` | Chuỗi action thực tế từng robot |
| `plannerPaths` | Chuỗi action planner đề xuất |
| `plannerTimes` | Thời gian compute từng planning episode |
| `errors` | Lỗi action/collision |
| `actualSchedule`, `plannerSchedule` | Lịch task |
| `events`, `tasks` | Event/task runtime |
| `numTaskFinished`, `sumOfCost`, `makespan` | Metric |

Action symbols:

| Symbol | Ý nghĩa |
|--------|---------|
| `F` / `FW` | đi thẳng |
| `R` / `CR` | quay clockwise |
| `C` / `CCR` | quay counter-clockwise |
| `W` | wait |
| `T` | implicit wait do timeout/missing action |

Với TCP realtime, không nên bắt Unity parse toàn bộ `actualPaths` sau một simulation batch. Server nên gọi planner từng timestep và trả action list ngay.

---

## Kiến trúc đề xuất

```text
Unity Scene: MapF_TankTest_PIBT_TCP
  MapTankTestBootstrap
    └─ MapScenarioBootstrapPIBTTcp
         ├─ PibtTcpClient
         ├─ PibtTcpSessionState
         └─ GridEnemyAgentPIBTTcp[]
                │
                │ TCP JSON line / length-prefixed JSON
                ▼
WSL C++ Server: pibt_tcp_server
  TcpServer
    ├─ Protocol parser
    ├─ StartKitEnvironmentAdapter
    ├─ DefaultPlanner::initialize(...)
    └─ DefaultPlanner::plan(...)
```

### Nguyên tắc thiết kế

1. `PIBT_TCP` là mode riêng, không sửa behavior của `AStar` và `PIBT` hiện tại.
2. Contract dữ liệu phải dùng linear location, map symbols, action model của start-kit.
3. Unity client phải có timeout/fallback rõ ràng: nếu server không trả lời, enemy `Wait` hoặc fallback về `PIBTPlanner.cs` tùy cấu hình.
4. Server giữ planner state qua nhiều timestep để `trajLNS`, flow grid, heuristic cache và PIBT priority có ý nghĩa. Không spawn CLI process mỗi frame.
5. Backtest phải xem `PIBT_TCP` là algorithm thứ ba để so sánh công bằng.

---

## TCP protocol nội bộ

Start-kit có file format chính thức, nhưng không có network format. Vì vậy thêm protocol TCP mỏng phía trên, vẫn giữ nội dung theo start-kit.

Đề xuất dùng JSON một dòng kết thúc bằng `\n` cho MVP. Sau khi ổn định có thể đổi sang length-prefixed JSON để tránh lỗi khi payload lớn.

### Message `hello`

Unity gửi khi vừa connect:

```json
{
  "type": "hello",
  "protocol": "projecty-pibt-tcp-v1",
  "sessionId": "mapf-tanktest-001",
  "map": {
    "width": 32,
    "height": 32,
    "symbols": [
      "..........@......@...@.@........",
      "@...@.@@...........@.@...@......"
    ]
  },
  "teamSize": 6,
  "planTimeLimitMs": 30,
  "preprocessTimeLimitMs": 30000
}
```

Server trả:

```json
{
  "type": "hello_ack",
  "sessionId": "mapf-tanktest-001",
  "status": "ok",
  "server": "pibt_tcp_server",
  "planner": "DefaultPlanner"
}
```

### Message `plan_step`

Unity gửi mỗi planning tick:

```json
{
  "type": "plan_step",
  "sessionId": "mapf-tanktest-001",
  "requestId": 42,
  "timestep": 123,
  "agents": [
    { "id": 0, "loc": 360, "orientation": 0, "goal": 528, "alive": true },
    { "id": 1, "loc": 636, "orientation": 2, "goal": 528, "alive": true }
  ],
  "dynamicObstacles": []
}
```

Field mapping:

| Field | Mapping |
|-------|---------|
| `loc` | `cell.y * width + cell.x` |
| `orientation` | Start-kit orientation: `0=east`, `1=south`, `2=west`, `3=north` |
| `goal` | Eagle cell hoặc target cell hiện tại |
| `alive=false` | Agent bị destroy; server nên cho `W` hoặc loại khỏi active set |

Server trả:

```json
{
  "type": "plan_result",
  "sessionId": "mapf-tanktest-001",
  "requestId": 42,
  "timestep": 123,
  "computeMs": 8.41,
  "actions": [
    { "id": 0, "action": "FW", "nextLoc": 361 },
    { "id": 1, "action": "CR", "nextLoc": 636 }
  ],
  "errors": []
}
```

### Message `shutdown`

Unity gửi khi scene unload hoặc dừng Play Mode:

```json
{
  "type": "shutdown",
  "sessionId": "mapf-tanktest-001"
}
```

---

## C++ server cần xây dựng trong WSL

### File/target mới

Đề xuất thêm trong `/home/west2light/projectY`:

| File | Vai trò |
|------|---------|
| `src/tcp_server_main.cpp` | Entry point server |
| `src/PibtTcpServer.cpp/.h` | Listen socket, accept client, read/write JSON |
| `src/UnityStartKitAdapter.cpp/.h` | Convert Unity JSON sang `SharedEnvironment` |
| `src/PlannerSession.cpp/.h` | Giữ `SharedEnvironment`, planner state, timestep |
| `CMakeLists.txt` | Thêm target `pibt_tcp_server` |

Boost đã là dependency của start-kit, nên MVP có thể dùng `boost::asio` để tránh thêm thư viện mới.

### Planner integration

Không nên gọi:

```text
./build/lifelong --inputFile ... -o ...
```

cho mỗi tick, vì cách đó tốn file I/O, mất planner state và output chỉ có ý nghĩa sau simulation batch.

Nên tạo wrapper gọi trực tiếp:

```cpp
DefaultPlanner::initialize(preprocess_time_limit, &env);
DefaultPlanner::plan(plan_time_limit, actions, &env);
```

`PlannerSession` chịu trách nhiệm:

1. Khởi tạo `SharedEnvironment` từ `hello`.
2. Set `env.num_of_agents`, `env.rows`, `env.cols`, `env.map`.
3. Set `env.curr_states` từ `plan_step.agents`.
4. Set `env.goal_locations[i] = {{goalLoc, timestep}}`.
5. Set `env.curr_timestep`.
6. Set `env.plan_start_time = std::chrono::steady_clock::now()`.
7. Gọi `DefaultPlanner::plan(...)`.
8. Convert `Action` enum sang string `FW`, `CR`, `CCR`, `W`.

### Rủi ro C++ cần xử lý sớm

| Rủi ro | Cách xử lý |
|--------|------------|
| `DefaultPlanner` dùng biến global trong namespace | MVP chỉ phục vụ một Unity session/server process; multi-session để sau |
| Agent chết làm `num_of_agents` thay đổi | Giữ team size cố định, agent chết nhận action `W` và goal = current loc |
| Dynamic obstacle chưa có trong `SharedEnvironment` | Phase đầu bỏ dynamic obstacle; Phase sau rebuild map hoặc thêm blocked list vào adapter |
| Orientation Unity lệch start-kit | Viết test mapping orientation riêng trước khi nối gameplay |
| Planner timeout | Server trả `W` cho toàn bộ agent và ghi `timeout=true` |

---

## Unity client cần xây dựng

### File mới trong Unity

| File | Vai trò |
|------|---------|
| `Assets/Scripts/PibtTcp/PibtTcpClient.cs` | TCP connect, send/receive JSON, timeout |
| `Assets/Scripts/PibtTcp/PibtTcpProtocol.cs` | DTO request/response |
| `Assets/Scripts/PibtTcp/PibtTcpGridAdapter.cs` | Convert `Vector2Int` <-> linear location, orientation |
| `Assets/Scripts/PibtTcp/PibtTcpSessionState.cs` | Cache latest action/nextLoc theo agent |
| `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs` | Enemy runtime nhận action từ server |
| `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs` | Spawner cho scene TCP |

### Scene mới

Tạo scene:

```text
Assets/Scenes/MapF_TankTest_PIBT_TCP.unity
```

Setup giống `MapF_TankTest_PIBT`, nhưng:

1. Dùng `MapScenarioBootstrapPIBTTcp` thay `MapScenarioBootstrapPIBT`.
2. Enemy thêm `GridEnemyAgentPIBTTcp`.
3. Không gắn `GridEnemyAgent` và `GridEnemyAgentPIBT` trên cùng enemy.
4. Inspector có:
   - `serverHost = 127.0.0.1`
   - `serverPort = 7777`
   - `planTickInterval = 0.25s` hoặc `0.5s`
   - `requestTimeoutMs = 50-100`
   - `fallbackMode = Wait` trong MVP

### Mapping action sang gameplay

C++ action model là discrete grid timestep, còn Unity tank di chuyển vật lý liên tục. Không nên điều khiển rigidbody bằng teleport.

MVP:

| Server action | Unity behavior |
|---------------|----------------|
| `FW` | set target waypoint = `nextLoc`, dùng steering hiện có để đi tới center cell |
| `CR` | quay tank theo chiều clockwise, không tiến |
| `CCR` | quay tank counter-clockwise, không tiến |
| `W` | dừng move body, vẫn có thể bắn nếu target trong range |

Khi enemy chưa tới center của `nextLoc`, client không gửi current loc mới. Timestep tiếp theo nên lấy `WorldToCell(transform.position)`, nhưng chỉ commit cell khi tank đủ gần center để tránh server nghĩ agent đã qua ô trong khi physics còn mắc ở cạnh.

### Shooting logic

Giữ shooting logic giống `GridEnemyAgentPIBT`:

1. Nếu player hoặc Eagle trong range và line-of-sight sạch, enemy ưu tiên bắn.
2. Khi đang bắn, gửi trạng thái loc hiện tại nhưng action server có thể bị bỏ qua tạm thời.
3. Sau khi hết điều kiện bắn, client tiếp tục nhận action từ server.

Điều này giữ gameplay tank tự nhiên, còn server chỉ phụ trách điều hướng.

---

## Các phase triển khai

### Phase 0 - Xác nhận build start-kit

Mục tiêu: đảm bảo C++ repo WSL build được trước khi thêm TCP.

Lệnh kiểm tra:

```bash
cd /home/west2light/projectY
./compile.sh
./build/lifelong --inputFile ./example_problems/random.domain/random_32_32_20_100.json -o /tmp/projecty_lifelong_test.json --simulationTime 20 --planTimeLimit 30
```

Pass khi:

1. Build thành công.
2. Output JSON có `actualPaths`, `plannerPaths`, `plannerTimes`.
3. Không có `errors` nghiêm trọng trong output/log.

### Phase 1 - Data adapter và protocol test

Mục tiêu: chưa đụng gameplay, chỉ chứng minh Unity cell map đúng format start-kit.

Việc cần làm:

1. Viết `PibtTcpGridAdapter.cs`.
2. Viết unit/dev test nhỏ cho:
   - `Vector2Int(0,0) -> 0`
   - `Vector2Int(1,0) -> 1`
   - `Vector2Int(0,1) -> width`
   - `loc -> cell -> loc` roundtrip
3. Export map từ `MapLoader` thành `symbols[]`.
4. Export enemy spawn/eagle goal thành `agents[]`.
5. Tạo request JSON sample lưu log để so sánh với start-kit format.

Pass khi:

1. JSON `hello` và `plan_step` có đủ width/height/map/agent/goal.
2. Linear location đúng với công thức start-kit.
3. Không có lệch origin giữa Unity và C++.

### Phase 2 - C++ TCP server MVP

Mục tiêu: server nhận `hello`, nhận `plan_step`, trả action hợp lệ.

Việc cần làm:

1. Thêm target `pibt_tcp_server`.
2. Dùng `boost::asio` listen `127.0.0.1:7777`.
3. Parse JSON bằng `nlohmann::json` có sẵn.
4. Build `SharedEnvironment` từ `hello`.
5. Gọi `DefaultPlanner::initialize`.
6. Với mỗi `plan_step`, cập nhật `env.curr_states`, `env.goal_locations`, `env.curr_timestep`, gọi `DefaultPlanner::plan`.
7. Trả `plan_result`.

Pass khi chạy thủ công:

```bash
cd /home/west2light/projectY
./build/pibt_tcp_server --host 127.0.0.1 --port 7777
```

và gửi một request test thì nhận được action list có số lượng bằng `teamSize`.

### Phase 3 - Unity client nối server

Mục tiêu: Unity connect được tới server trong Play Mode.

Việc cần làm:

1. Viết `PibtTcpClient.cs` async/non-blocking.
2. Scene start gửi `hello`.
3. Mỗi `planTickInterval`, gửi `plan_step`.
4. Cache response vào `PibtTcpSessionState`.
5. Log `computeMs`, `latencyMs`, `timeoutCount`.
6. Khi disconnect hoặc timeout, agent `Wait`.

Pass khi:

1. Play Mode không freeze khi server chậm hoặc mất kết nối.
2. Console có connect/hello/plan_result rõ ràng.
3. Khi tắt server, Unity không crash.

### Phase 4 - Enemy runtime `GridEnemyAgentPIBTTcp`

Mục tiêu: enemy di chuyển theo action/path từ C++ server.

Việc cần làm:

1. Copy có chọn lọc từ `GridEnemyAgentPIBT`: shooting, stuck detection, metrics.
2. Thay `GridPIBTPathfinder.TryFindPath` bằng action từ `PibtTcpSessionState`.
3. Implement action-to-steering:
   - `FW`: follow next cell center.
   - `CR`/`CCR`: rotate in place.
   - `W`: stop.
4. Giữ `btReplanCount`, `btRecoveryCount`, `btShotCount`, `btCellsVisited`.
5. Thêm gizmo/debug line cho next cell/action.

Pass khi:

1. Enemy trong `MapF_TankTest_PIBT_TCP` đi được về Eagle.
2. Enemy vẫn bắn player/Eagle như mode PIBT C#.
3. Không có double-control từ legacy `DefaultEnemyAI`.

### Phase 5 - Scene/bootstrap mode riêng

Mục tiêu: mode TCP có scene và bootstrap độc lập.

Việc cần làm:

1. Duplicate `MapF_TankTest_PIBT` thành `MapF_TankTest_PIBT_TCP`.
2. Tạo `MapScenarioBootstrapPIBTTcp`.
3. `MapTankTestBootstrap.SpawnScenario()` nhận diện bootstrap TCP trước PIBT C# nếu scene có component TCP.
4. Add scene vào Build Settings nếu cần.
5. Thêm config Inspector cho host/port/timeout/fallback.

Pass khi:

1. Ba scene/mode chạy độc lập: `MapF_TankTest`, `MapF_TankTest_PIBT`, `MapF_TankTest_PIBT_TCP`.
2. Tắt TCP scene không ảnh hưởng scene A* và PIBT C#.

### Phase 6 - Backtest và báo cáo so sánh

Mục tiêu: `PIBT_TCP` tham gia cùng hệ backtest hiện có.

Việc cần làm:

1. Sửa `BacktestMode.Algorithm` comment thành `"AStar" | "PIBT" | "PIBT_TCP"`.
2. Sửa `BacktestRunner.BuildJobs()`:
   - scenes: `MapF_TankTest`, `MapF_TankTest_PIBT`, `MapF_TankTest_PIBT_TCP`
   - algos: `AStar`, `PIBT`, `PIBT_TCP`
3. Sửa collection agent để nhận thêm `GridEnemyAgentPIBTTcp`.
4. Sửa chart màu/legend cho thuật toán thứ ba.
5. Ghi thêm metric:
   - `ServerLatencyMsAvg`
   - `ServerTimeoutCount`
   - `PlannerComputeMsAvg`

Pass khi:

1. Backtest chạy được A*, PIBT, PIBT_TCP trên cùng map set.
2. CSV/HTML có đủ ba thuật toán.
3. Có thể dùng số liệu để viết DATN: local C# migration vs client-server C++ integration.

---

## MVP nên làm trước

Thứ tự thực tế để tránh rủi ro:

1. Build start-kit CLI trong WSL.
2. Viết C++ TCP server trả action giả `W` cho mọi agent.
3. Viết Unity TCP client connect và hiển thị latency.
4. Thay action giả bằng `DefaultPlanner::plan`.
5. Làm `GridEnemyAgentPIBTTcp` di chuyển theo `FW/W` trước, sau đó mới xử lý `CR/CCR` chính xác.
6. Tạo scene `MapF_TankTest_PIBT_TCP`.
7. Tích hợp backtest.

Nếu kẹt ở C++ planner integration, fallback tạm thời là server gọi CLI `lifelong` với input file sinh từ request và trả `plannerPaths`. Cách này chỉ dùng để debug format, không nên là bản cuối vì chậm và mất state.

---

## Checklist nghiệm thu

### Kỹ thuật

- [ ] Server WSL build được bằng CMake/compile script.
- [ ] Unity connect TCP tới `127.0.0.1:7777`.
- [ ] `hello` gửi đúng map width/height/symbols.
- [ ] `plan_step` gửi đúng `loc`, `orientation`, `goal`.
- [ ] Server trả action list đúng số agent.
- [ ] Enemy di chuyển tới Eagle trong scene TCP.
- [ ] Timeout/disconnect không làm Unity freeze.
- [ ] Mode A* và PIBT C# không bị regression.

### DATN/thesis

- [ ] Có bảng so sánh A* vs PIBT C# vs PIBT TCP.
- [ ] Có metric server latency và compute time.
- [ ] Có mô tả rõ hai hướng tích hợp:
  - migration C++ -> C# (`PIBTPlanner.cs`)
  - client-server gọi C++ start-kit (`PIBT_TCP`)
- [ ] Không ghi quá mức rằng toàn bộ kiến trúc No Man's Sky đã port sang Unity; mode TCP là bằng chứng tích hợp trực tiếp với C++ planner.

---

## File dự kiến sau khi hoàn thành

### Unity

```text
Assets/Scripts/PibtTcp/PibtTcpClient.cs
Assets/Scripts/PibtTcp/PibtTcpProtocol.cs
Assets/Scripts/PibtTcp/PibtTcpGridAdapter.cs
Assets/Scripts/PibtTcp/PibtTcpSessionState.cs
Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs
Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs
Assets/Scenes/MapF_TankTest_PIBT_TCP.unity
```

### WSL C++

```text
/home/west2light/projectY/src/tcp_server_main.cpp
/home/west2light/projectY/src/PibtTcpServer.cpp
/home/west2light/projectY/src/PibtTcpServer.h
/home/west2light/projectY/src/UnityStartKitAdapter.cpp
/home/west2light/projectY/src/UnityStartKitAdapter.h
/home/west2light/projectY/src/PlannerSession.cpp
/home/west2light/projectY/src/PlannerSession.h
/home/west2light/projectY/CMakeLists.txt
```

---

## Quyết định cần chốt trước khi code

1. TCP payload dùng JSON line cho MVP hay length-prefixed JSON ngay từ đầu.
2. Khi server timeout: enemy `Wait` hay fallback sang `PIBTPlanner.cs`.
3. `PIBT_TCP` dùng goal cố định là Eagle hay đổi goal theo target combat gần nhất.
4. Dynamic obstacle có đưa vào Phase đầu không. Khuyến nghị: chưa đưa vào, để ổn định static map trước.
5. C++ server chỉ hỗ trợ một Unity session hay nhiều session. Khuyến nghị: một session cho DATN/backtest, multi-session để sau.
