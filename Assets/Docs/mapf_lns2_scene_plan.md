# Màn LNS2 — `MapF_TankTest_LNS2`

## Tổng quan

Scene `Assets/Scenes/MapF_TankTest_LNS2.unity` là bản nâng cấp của `MapF_TankTest` dùng thuật toán **LNS2 (Large Neighborhood Search 2)** thay thế A\* thuần.

LNS2 được port từ mã nguồn C++ của team **No Man's Sky** tham dự **League of Robot Runners 2024** (kho lưu trữ: `Team_No_Man's_Sky/`). Thuật toán này giải bài toán **MAPF (Multi-Agent Pathfinding)** bằng cách tối ưu luồng giao thông chung giữa nhiều agent thay vì tìm đường độc lập cho từng agent.

---

## Kiến trúc thuật toán LNS2

### Ý tưởng cốt lõi

Thay vì mỗi agent tự tìm đường tốt nhất cho mình (có thể gây tắc nghẽn tập thể), LNS2 xây dựng một **flow grid chia sẻ** và tối ưu hóa đường đi của tất cả agent cùng nhau qua **Frank-Wolfe iterations**.

```
[Khởi tạo]
  Mỗi agent chạy A* flow-aware → tạo trajectory (đường đi)
  Trajectory đóng góp vào flow grid chung

[Mỗi timestep — Frank-Wolfe]
  for agent a (ưu tiên agent lệch đường nhiều nhất):
    1. Xóa trajectory của a khỏi flow grid
    2. Chạy lại A* với flow grid hiện tại (không có a)
    3. Thêm trajectory mới của a vào flow grid
  → Lặp cho đến hết time budget
```

### Flow Grid

Flow grid là cấu trúc dữ liệu trung tâm, lưu **số lượng trajectory đang đi qua mỗi cạnh** của lưới:

```
flow[cell * 4 + d]  =  số trajectory dùng cạnh cell → hướng d
d: 0=east(+x), 1=south(+y), 2=west(-x), 3=north(-y)
```

### Flow-aware A\*

Hàm ưu tiên của A\* được tăng cường với chi phí tắc nghẽn:

```
priority = g + h + op_flow + vertex_flow

op_flow per cạnh   = (flow[from→d] + 1) × flow[to→ngược(d)]
                   → phạt đi ngược chiều traffic
                   → khuyến khích agent đi cùng chiều nhau (lane formation)

vertex_flow tại to = Σflow[to→*] / 2
                   → phạt đi qua cell đang đông
```

Cả `op_flow` và `vertex_flow` được **tích lũy dọc theo đường đi** (cumulative), không phải chỉ tính tại từng bước riêng lẻ.

### Heuristic Table

Thay vì Manhattan distance, LNS2 dùng **BFS ngược từ goal** để có heuristic chính xác hơn trên map có vật cản:

```
h[goal][source] = số bước tối thiểu từ source đến goal
                  (tính theo đường đi thực sự, không qua vật cản)
```

Bảng được tính **lazy** (chỉ khi cần) và **cache** theo goal. Với map 32×32, chi phí tính toán là O(1024) mỗi goal mới.

---

## Các file source

| File | Vị trí | Vai trò |
|------|--------|---------|
| `LNS2Planner.cs` | `Assets/Scripts/` | Static singleton — flow grid, A\*, Frank-Wolfe, heuristic |
| `GridLNS2Pathfinder.cs` | `Assets/Scripts/` | Wrapper gọi `LNS2Planner`, trả về `List<Vector2Int>` |
| `GridEnemyAgentLNS2.cs` | `Assets/Scripts/` | MonoBehaviour điều khiển enemy tank (thay `GridEnemyAgent`) |
| `MapScenarioBootstrapLNS2.cs` | `Assets/Scripts/` | Spawner cho scene LNS2 (thay `MapScenarioBootstrap`) |
| `AIPatrolLNS2PathBehaviour.cs` | `Assets/Scripts/Ai/` | `AIBehaviour` dùng LNS2 cho legacy `DefaultEnemyAI` |

### Dependency graph

```
LNS2Planner.cs                          ← compile độc lập
├── GridLNS2Pathfinder.cs               ← wrapper tiện dụng
│    └── GridEnemyAgentLNS2.cs          ← MAPF agent chính
│         └── MapScenarioBootstrapLNS2  ← spawner scene
└── AIPatrolLNS2PathBehaviour.cs        ← legacy AI path
```

`LNS2Planner` là **static class** — tất cả agent trong cùng một session dùng chung một flow grid. Khi nhiều `GridEnemyAgentLNS2` cùng chạy, trajectory của mỗi agent ảnh hưởng lẫn nhau và Frank-Wolfe giúp tối ưu phối hợp.

---

## Khởi tạo màn LNS2

### Boot flow

```
MapTankTestBootstrap.Start()
 ├─ mapLoader.LoadAndBuild()
 ├─ SpawnPlayer()
 ├─ SetupCamera()
 └─ SpawnScenario()         ← tìm MapScenarioBootstrapLNS2 trước,
                               fallback về MapScenarioBootstrap nếu không có
      └─ MapScenarioBootstrapLNS2.SpawnScenario()
           ├─ LNS2Planner.Init(mapLoader)   ← build flow grid + neighbor list
           ├─ SpawnEagleBase()
           └─ SpawnEnemies()
                └─ ConfigureEnemy()
                     ├─ AddPlayerBlocker()
                     └─ AddGridEnemyAgentLNS2()
                          └─ GridEnemyAgentLNS2 (component)
                               ├─ Awake: lấy TankController
                               ├─ Update → EnsureLNS2Ready()
                               │    ├─ LNS2Planner.Init nếu chưa
                               │    └─ LNS2Planner.Register() → agentId
                               ├─ ReplanPath() mỗi replanInterval
                               │    └─ GridLNS2Pathfinder.TryFindPath()
                               │         └─ LNS2Planner.FrankWolfe()
                               └─ FollowPath() mỗi frame
```

### Setup trong Unity Editor

1. Duplicate scene `MapF_TankTest` → đặt tên `MapF_TankTest_LNS2`
2. Chọn GameObject `MapRuntime` (hoặc chứa `MapTankTestBootstrap`)
3. **Xóa** component `MapScenarioBootstrap`
4. **Thêm** component `MapScenarioBootstrapLNS2`
5. Gán Inspector:

| Field | Giá trị |
|-------|---------|
| `Enemy Prefab` | `Assets/Prefabs/StaticEnemy.prefab` |
| `Enemy Spawn Cells` | `(30,1)`, `(1,30)`, `(30,30)`, `(16,1)` |
| `Eagle Cell` | `(16,16)` |
| `Frank Wolfe Ms` | `15` |
| `Enemy Replan Interval` | `0.75` |

---

## So sánh A\* vs LNS2

| Tiêu chí | A\* (`GridEnemyAgent`) | LNS2 (`GridEnemyAgentLNS2`) |
|----------|----------------------|-----------------------------|
| Thuật toán | A\* + Manhattan/BFS heuristic | Flow-aware A\* + Frank-Wolfe |
| Phối hợp agent | Không (độc lập) | Có (flow grid chia sẻ) |
| Tránh tắc nghẽn | Qua `GridNavMask` soft cost | Qua flow opposition penalty |
| Chi phí tính toán | O(N log N) mỗi agent | O(N log N) + FW iterations |
| Thời gian replan | `replanInterval` giây | `replanInterval` + `frankWolfeMs` budget |
| Kết quả kỳ vọng | Agent có thể chặn lẫn nhau | Agent tự hình thành "lane" |

### Metric đo lường (thesis)

Để so sánh hai màn, theo dõi:

- **Throughput**: số enemy đến được Eagle / tổng số enemy
- **Sum of Costs (SoC)**: tổng số bước tất cả agent đi
- **Makespan**: bước cuối cùng enemy nào còn di chuyển
- **Collision rate**: số lần hai agent cùng vào một cell trong cùng timestep
- **Replan count**: số lần `ReplanPath()` được gọi tổng cộng

---

## Tham số điều chỉnh

### `MapScenarioBootstrapLNS2`

| Tham số | Mô tả | Giá trị gợi ý |
|---------|-------|--------------|
| `frankWolfeMs` | Time budget (ms) cho Frank-Wolfe mỗi replan | `10–30` |
| `enemyReplanInterval` | Giây giữa các lần replan | `0.5–1.0` |

### `GridEnemyAgentLNS2`

| Tham số | Mô tả | Ghi chú |
|---------|-------|---------|
| `frankWolfeMs` | Budget riêng cho agent này | Kế thừa từ bootstrap |
| `replanInterval` | Tần suất gọi LNS2 | Giống A\* để so sánh công bằng |
| `forwardAlignmentThreshold` | Ngưỡng dot product để đi thẳng | `0.97` |
| `stuckTimeout` | Giây trước khi bắt đầu recovery | `1.5` |

### Tuning Frank-Wolfe budget

```
frankWolfeMs tăng  → đường đi phối hợp tốt hơn, nhưng lag frame nhiều hơn
frankWolfeMs giảm  → gần giống A* đơn thuần, nhanh hơn
```

Với 4 agent, `frankWolfeMs = 15` cho phép mỗi agent được replan ít nhất 1 round FW trong hầu hết frame (map 32×32, A\* ~0.5ms/agent).

---

## Luồng dữ liệu LNS2Planner

```
Khởi tạo (Init):
  _flow[size * 4]     = 0          // tất cả cạnh chưa có traffic
  _nbrs[cell]         = [neighbors] // precomputed từ MapLoader.IsWalkable
  _h = {}                           // cache heuristic rỗng

Register(agentId):
  _trajs[agentId] = null
  _currPos[agentId] = ?

FrankWolfe(agentId, start, goal, budgetMs):
  ReplanOne(agentId, start, goal)
  for other agents trong budget:
    ReplanOne(other, _currPos[other], _goals[other])

ReplanOne(id, start, goal):
  RemoveFlow(_trajs[id])    // trừ flow của trajectory cũ
  _trajs[id] = AStarFlow(start, goal)
  AddFlow(_trajs[id])       // cộng flow trajectory mới
```

---

## Nguồn tham khảo

- **Thuật toán gốc C++**: `Team_No_Man's_Sky/1f654b03.../default_planner/`
  - `TrajLNS.h` — cấu trúc trajectory + flow grid
  - `flow.cpp` — `frank_wolfe`, `remove_traj`, `add_traj`, `update_traj`
  - `search.cpp` — A\* flow-aware
  - `heuristics.cpp` — reverse BFS heuristic
  - `pibt.cpp` — PIBT action selection (không port, dùng steering trực tiếp)
- **League of Robot Runners 2024**: https://www.leagueofrobotrunners.org/
- `DATN.md` — mục tiêu đồ án
- `mapf_eagle_enemy_astar_plan.md` — baseline A\* đã implement
