# Plan: Spawn Eagle Base, Enemies và baseline A\* cho `MapF_TankTest`

## Kết luận nhanh

`MapF_TankTest` **đã đủ nền tảng để bắt đầu spawn Eagle Base và enemies**, nhưng **chưa nên nhảy ngay sang LNS2 hoặc Unity NavMesh**.

Thứ tự hợp lý nhất theo `DATN.md` và hiện trạng project:

1. Hoàn thiện objective gameplay tối thiểu: spawn Eagle Base, spawn enemies, win/lose condition.
2. Implement **A\* trên grid từ `MapLoader`** làm baseline.
3. Cho enemy dùng A\* để đi tới Eagle/Base và bắn phá.
4. Sau khi A\* chạy ổn, mới làm multi-agent layer: reservation table / prioritized planning / LNS2 simplified.
5. Unity NavMesh chỉ nên dùng như benchmark phụ hoặc fallback tham khảo, không nên là baseline chính cho đồ án MAPF grid.

## Vì sao nên A\* grid trước, không phải Unity NavMesh trước?

Trong `DATN.md`, mục tiêu là MAPF trên môi trường grid/map, có pipeline:

```text
Input: trạng thái game
Process: tính toán đường đi
Output: hành động agent
```

Map test hiện tại cũng đang dùng MAPF benchmark `.map`:

```text
Assets/MapData/random-32-32-10.map
```

`MapLoader` đã đọc map dạng cell và có các hàm nền tảng:

- `IsWalkable(Vector2Int cell)`
- `CellToWorld(Vector2Int cell)`
- `WorldToCell(Vector3 position)`
- `TryFindWalkableNear(...)`

Vì vậy A\* grid là baseline khớp trực tiếp với đề tài hơn Unity NavMesh.

Unity NavMesh trong project 2D top-down này có vài điểm bất lợi:

- NavMesh mặc định thiên về 3D surface, cần setup/bridge thêm cho 2D.
- MAPF benchmark map là grid discrete, còn NavMesh là continuous mesh.
- Khó đo các metric kiểu cell path length, timestep collision, reservation table.
- Khi sang LNS2/Prioritized Planning vẫn phải quay lại biểu diễn grid/timestep.

NavMesh vẫn có ích nếu sau này muốn so sánh “Unity built-in navigation vs custom grid A\*”, nhưng không nên là bước đầu.

## Hiện trạng scene test

Scene:

```text
Assets/Scenes/MapF_TankTest.unity
```

Đã có:

- `Main Camera`
- `EventSystem`
- `MapRuntime`
  - `MapLoader`
  - `MapTankTestBootstrap`
- Runtime spawn player tank từ `Assets/Prefabs/Tank.prefab`
- Runtime generate map 32x32 từ `random-32-32-10.map`
- Tile obstacle `@` có collider layer `ObstaclesMovement`
- Obstacle có hitbox cho bullet layer `Hittable`
- Boundary quanh map để tank không đi ra ngoài
- Bullet đã có linecast chống xuyên obstacle khi tốc độ cao

Chưa có:

- Eagle/Base objective prefab hoặc script riêng
- Enemy spawner theo cell
- A\* solver trên grid
- AI follow path từ cell này sang cell khác
- Game state win/lose trong scene test
- Metrics/debug visualization cho pathfinding

## Có nên spawn Eagle Base và enemies ngay không?

Có, nhưng chỉ nên spawn theo dạng **static test target** trước.

Mục tiêu của bước này không phải AI thông minh ngay, mà là tạo vòng gameplay tối thiểu:

```text
Player bảo vệ Eagle
Enemies xuất hiện ở các cell walkable
Enemies có target là Eagle
Enemy bắn Eagle hoặc đi theo path tới Eagle
Eagle chết => lose
Enemies chết hết => win / wave clear
```

Bước spawn này giúp kiểm tra:

- Map cell -> world position có đúng không.
- Enemy prefab có hoạt động trong map runtime không.
- Bullet/collision/damage giữa enemy, player, Eagle có ổn không.
- A\* sau này có target cụ thể để đi tới.

## Spawn Eagle Base trước như thế nào?

### Cách tối thiểu

Tạo một prefab hoặc runtime object `EagleBase`:

```text
EagleBase
├── SpriteRenderer
├── BoxCollider2D hoặc CircleCollider2D
└── Damagable
```

Layer nên là:

```text
Hittable
```

Vì bullet hiện tìm `Damagable` khi trigger hit.

Spawn cell gợi ý:

```text
eagleCell = gần giữa map, ví dụ (16, 16), nếu obstacle thì tìm walkable gần nhất
```

Dùng:

```csharp
mapLoader.TryFindWalkableNear(eagleCell, out actualEagleCell)
eagle.transform.position = mapLoader.CellToWorld(actualEagleCell)
```

### Visual tạm

Nếu chưa có sprite Eagle, có thể dùng:

- sprite UI/icon tạm nếu có
- sprite crate/base khác trong Kenny assets
- hoặc runtime colored square vàng/trắng với sorting order cao

Quan trọng là `Damagable` + collider hoạt động trước, visual polish để sau.

## Spawn enemies trước như thế nào?

Project đã có prefab enemy:

```text
Assets/Prefabs/StaticEnemy.prefab
Assets/Prefabs/PatrolingEnemy Variant.prefab
Assets/Prefabs/EnemyTank Variant.prefab
```

Nhưng AI hiện tại chủ yếu là:

- `DefaultEnemyAI`: nếu thấy target thì shoot, không thấy thì patrol.
- `AIPatrolStaticBehaviour`: xoay turret random.
- `AIPatrolPathBehaviour`: đi theo patrol points có sẵn.
- `AIDetector`: detect player bằng layer/raycast.

Các prefab này dùng tốt cho gameplay cũ, nhưng **chưa phải MAPF enemy** vì chưa follow path từ `MapLoader`.

### Bước spawn enemy tối thiểu

Tạo `MapScenarioBootstrap` hoặc mở rộng `MapTankTestBootstrap`:

```text
- reference MapLoader
- reference EagleBase prefab
- reference enemy prefab
- eagleCell
- enemySpawnCells[]
- Spawn Eagle trước
- Spawn N enemies ở walkable cells
- Gán target Eagle cho enemy path agent sau này
```

Spawn cells gợi ý:

```text
Eagle: center-ish
Enemies: 4 góc map hoặc biên map
Player: gần Eagle hoặc góc đối diện
```

Ví dụ:

```text
playerSpawnCell = (1, 1)
eagleCell = (16, 16)
enemySpawnCells = [(30, 1), (1, 30), (30, 30), (16, 1)]
```

Nếu cell bị `@`, dùng `TryFindWalkableNear`.

## A\* baseline nên thiết kế thế nào?

### Module đề xuất

```text
Assets/Scripts/Pathfinding/
├── GridAStarPathfinder.cs
├── GridPathAgent.cs
├── GridPathFollower.cs
└── PathDebugRenderer.cs
```

### `GridAStarPathfinder`

Input:

```csharp
MapLoader map
Vector2Int start
Vector2Int goal
```

Output:

```csharp
List<Vector2Int> path
```

Rule bước đầu:

- 4-neighbor trước: up/down/left/right.
- Cost mỗi bước = 1.
- Heuristic = Manhattan distance.
- Chỉ đi qua `map.IsWalkable(cell)`.

Lý do chọn 4-neighbor trước:

- Tank movement hiện xoay/tiến theo hướng, phù hợp grid cardinal hơn diagonal.
- Tránh corner-cutting qua góc obstacle.
- Dễ mở rộng sang reservation table theo timestep.

Sau khi ổn có thể thử 8-neighbor/octile vì file map type là `octile`.

### `GridPathAgent`

Gắn lên enemy tank hoặc wrapper enemy:

```text
- MapLoader map
- Transform target
- float replanInterval = 0.5f hoặc 1.0f
- List<Vector2Int> currentPath
- currentPathIndex
```

Behavior:

1. Lấy `start = map.WorldToCell(enemy.position)`.
2. Lấy `goal = map.WorldToCell(target.position)`.
3. A\* tìm path.
4. Follow từng waypoint bằng `TankController.HandleMoveBody(...)`.
5. Nếu đến gần Eagle thì dừng và bắn.

### Tích hợp với enemy hiện có

Không nên sửa mạnh `DefaultEnemyAI` ngay. Nên tạo một behavior mới:

```text
AStarChaseEagleBehaviour : AIBehaviour
```

Hoặc script độc lập:

```text
GridEnemyAgent
```

Trong bản test, có thể disable `DefaultEnemyAI` cũ và dùng `GridEnemyAgent` trực tiếp để tránh xung đột giữa patrol cũ và pathfinding mới.

## Multi-agent/MAPF layer sau A\*

Sau khi single-agent A\* tới Eagle ổn, mới làm multi-agent:

### Phase 1: Independent A\*

Mỗi enemy tự A\* tới Eagle, không tránh nhau.

Metric:

- Time to reach Eagle.
- Số lần enemy overlap/va chạm nhau.
- Số enemy bị kẹt.

### Phase 2: Prioritized Planning đơn giản

Từng enemy lập path theo thứ tự:

```text
enemy 1: A* bình thường
enemy 2: A* tránh path/timestep của enemy 1
enemy 3: tránh enemy 1 + enemy 2
...
```

Cần thêm reservation table:

```text
(cell, timestep) -> reserved by enemyId
(edge from cell A to B, timestep) -> tránh swap collision
```

Đây có thể được trình bày như “LNS2 simplified / cooperative baseline” trước khi tích hợp LNS2 thật.

### Phase 3: LNS2 thật hoặc server bridge

Theo `DATN.md`, có 2 hướng:

- Migration C++ sang C#.
- Server-client gọi LNS2 từ start-kit.

Chỉ nên bắt đầu khi:

- Grid state export/import đã rõ.
- A\* baseline có metric.
- Scenario enemy/base đã ổn.

## Unity NavMesh nên nằm ở đâu?

Đề xuất: không test NavMesh trước A\*.

Nếu vẫn muốn dùng NavMesh, đặt nó ở nhánh phụ:

```text
Experiment: Unity NavMesh baseline
```

Mục đích:

- So sánh tốc độ setup và behavior với custom grid A\*.
- Không dùng làm nền cho LNS2/MAPF chính.

Điều kiện nếu làm NavMesh:

- Cần xác nhận package/2D NavMesh workflow.
- Chuyển obstacle grid sang NavMeshObstacle hoặc build surface runtime.
- Mapping agent radius/tank size sang NavMeshAgent.

Chi phí này cao hơn lợi ích ở thời điểm hiện tại.

## Plan triển khai cụ thể

### Milestone 1 — Objective + spawn scenario

File/script:

```text
Assets/Scripts/MapScenarioBootstrap.cs
Assets/Scripts/EagleBase.cs hoặc dùng Damagable trực tiếp
```

Việc cần làm:

1. Tạo/spawn Eagle Base ở walkable cell.
2. Spawn 2–4 enemy từ prefab hiện có.
3. Đặt player gần base hoặc giữ spawn hiện tại.
4. Gán layer/collider/Damagable cho Eagle.
5. Kiểm tra bullet bắn Eagle mất máu.
6. Kiểm tra enemies không xuyên obstacle/boundary.

Done khi:

- Play `MapF_TankTest` có Player + Eagle + enemies.
- Console sạch.
- Eagle có thể nhận damage.

### Milestone 2 — Single-agent A\*

File/script:

```text
Assets/Scripts/Pathfinding/GridAStarPathfinder.cs
Assets/Scripts/Pathfinding/GridEnemyAgent.cs
```

Việc cần làm:

1. Implement A\* 4-neighbor từ `MapLoader`.
2. Enemy tìm đường từ cell hiện tại tới Eagle cell.
3. Enemy follow waypoint bằng `TankController`.
4. Replan mỗi 0.5–1 giây.
5. Khi gần Eagle, dừng và bắn.

Done khi:

- Một enemy đi vòng qua obstacle tới Eagle.
- Không đi xuyên `@`.
- Có thể visualize path bằng Gizmos hoặc LineRenderer.

> **Cập nhật 2026-05-10 (`mapf_enemy_stuck_phase2_plan_v3.md` Phase A)**: cost function của A\* không còn là `1` mỗi step. Baseline mới là **soft cost map** trong `GridNavMask`: cell Chebyshev=1 từ vùng hard-block (`@` + inflated) nhận `+8`, Chebyshev=2 nhận `+2`, deep interior `+0`. Manhattan heuristic vẫn admissible (cost ≥ 1). Inflate radius=1 thuần không đủ chặn cọ với cluster 2x2 — soft cost map là cách tiếp cận thay thế giữ map không bị cô lập. Tham khảo `Assets/Scripts/Pathfinding/GridNavMask.GetCellCost`.

### Milestone 3 — Multi-enemy A\* baseline

Việc cần làm:

1. Spawn 4 enemies.
2. Mỗi enemy independent A\* tới Eagle.
3. Ghi metric đơn giản:
   - path length
   - replan count
   - time to reach Eagle range
   - collision/blocked count
4. Tạo debug overlay hoặc log summary khi wave kết thúc.

Done khi:

- Có demo “A\* baseline chưa phối hợp”.
- Có số liệu để so sánh với MAPF layer.

### Milestone 4 — Cooperative / MAPF simplified

Việc cần làm:

1. Thêm reservation table theo timestep.
2. Prioritized planner tạo path cho từng enemy.
3. Tránh vertex collision và edge swap.
4. So sánh với independent A\*.

Done khi:

- Nhiều enemy ít chen nhau hơn.
- Có mode switch `Independent A*` / `Prioritized`.

### Milestone 5 — LNS2 integration research

Việc cần làm:

1. Định nghĩa format export grid/agents:
   - width, height
   - obstacle cells
   - starts
   - goals
2. Chạy solver ngoài hoặc port C# thử trên scenario nhỏ.
3. Import path vào Unity để enemy follow.
4. So sánh metrics.

## Rủi ro cần tránh

- Không nên để enemy AI cũ (`DefaultEnemyAI` + patrol) chạy đồng thời với `GridEnemyAgent`, vì sẽ có hai hệ điều khiển cùng gọi `TankController`.
- Không nên phụ thuộc Unity NavMesh nếu mục tiêu là MAPF benchmark grid.
- Không nên spawn enemy trên obstacle cell; luôn dùng `TryFindWalkableNear`.
- Đạn tốc độ cao cần giữ linecast hiện tại để không xuyên obstacle/Eagle.
- Tank movement hiện là continuous physics, còn A\* là grid; path follower cần waypoint tolerance đủ rộng để không rung/lệch cell.

## Đề xuất bước tiếp theo ngay

Bước tiếp theo nên làm:

```texts
Milestone 1: MapScenarioBootstrap + EagleBase runtime object + spawn 2 enemies static
```

Chưa cần A\* ngay trong commit đầu tiên. Mục tiêu trước là có scenario gameplay đúng:

```text
Map + Player + Eagle + Enemy + Collision + Bullet damage
```

Sau đó mới thêm `GridAStarPathfinder` và cho enemy đi tới Eagle.
