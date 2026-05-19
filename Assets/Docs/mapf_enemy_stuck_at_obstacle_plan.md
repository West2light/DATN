# Plan: Fix enemy tank kẹt vào tường khi follow A\*

Đây là plan kế tiếp của:

- `Assets/Docs/mapf_eagle_enemy_astar_plan.md` (Milestone 2 đã xong: single-agent A\*).
- `Assets/Docs/mapf_tank_test_scene_idea.md` (định nghĩa scene `MapF_TankTest` và rule grid).

Mục tiêu của plan này: enemy tank chạy A\* tới Eagle **không bị kẹt vào mép obstacle** trong scene `MapF_TankTest`, để có baseline ổn định trước khi sang Milestone 3 (multi-enemy + metrics) và Milestone 4 (cooperative MAPF / LNS2 simplified).

## Chẩn đoán nhanh

### Hiện tượng

- 1 enemy tank chạy A\* tới Eagle ở `Assets/Scenes/MapF_TankTest.unity`.
- Khi đường đi đi sát hoặc rẽ ở cạnh ô `@`, tank đâm vào collider tường, dừng lại, rồi replan ra đúng path cũ ⇒ kẹt loop.

### Đây có phải “local minimum” của A\*?

Không, đây **không phải local minimum** của A\* theo nghĩa chặt:

- A\* trên grid 4-neighbor là **complete + optimal**: nếu có path từ start tới goal nó luôn tìm ra path tối ưu.
- Cái đang bị stuck là **physical controller** (`TankMover` + `GridEnemyAgent.FollowPath`), không phải search.

Nhưng triệu chứng có vẻ giống local minimum:

- Replan ra **đúng path cũ** ⇒ tank lại đâm đúng chỗ cũ.
- Đây là pattern quen thuộc của **potential-field / greedy local controller** bị bẫy bởi U-shape obstacle.

Vì vậy đặt vấn đề chính xác là:

> Path A\* hợp lệ trên grid, nhưng **agent có kích thước vật lý** không vừa với path đó, và controller không có cơ chế recovery khi va chạm.

### Vì sao kẹt — số liệu cụ thể

Đo từ project hiện tại:

| Thông số | Giá trị | Nguồn |
|---|---|---|
| `tileSize` | `1.0` | `MapLoader.tileSize` |
| Obstacle collider | `BoxCollider2D 1×1` | `MapLoader.CreateTile` |
| Tank body collider | `CapsuleCollider2D size (0.597, 0.696)` | `Assets/Prefabs/Tank.prefab` |
| Clearance khi tank đứng đúng tâm cell sát tường | `(1.0 - 0.696) / 2 ≈ 0.15` unit | tính từ size capsule + tileSize |
| Replan interval | `0.75s` | `GridEnemyAgent.replanInterval` |
| Waypoint reach distance | `0.25` | `GridEnemyAgent.waypointReachDistance` |
| Enemy maxSpeed | `50` | `EnemyTankMovementData.maxSpeed` |
| Enemy rotationSpeed | `150` deg/s | `EnemyTankMovementData.rotationSpeed` |

Với clearance chỉ ~0.15 unit, mọi sai lệch nhỏ (xoay khi đang chạy, va đẩy lẫn nhau giữa 2 enemy, vận tốc cao) đều đủ để capsule chạm collider.

### Các nguyên nhân riêng biệt (cần fix riêng)

1. **A\* không “biết” kích thước tank** — `GridAStarPathfinder` coi mọi cell `IsWalkable` là đi qua được vô tư, kể cả ô sát tường ở cả 2 bên ⇒ path đi sát mép.
2. **Corner cutting khi rẽ** — A\* 4-neighbor cho phép path `(5,5) → (5,6) → (6,6)` ngay cả khi `(6,5)` là `@`. Tank rẽ trái sang phải tại đỉnh `(5,6)` quét qua góc obstacle.
3. **`FollowPath` xoay + tiến đồng thời** — `GridEnemyAgent.FollowPath()` gửi `HandleMoveBody(new Vector2(rotation, 1f))` khi `dot < 0.96`. Nghĩa là khi cần xoay nhiều, tank vẫn tiến. Quỹ đạo thật là cung tròn, không phải đường gấp khúc cell-to-cell.
4. **Không có stuck detection / recovery** — Khi capsule bị tường chặn `Rigidbody2D.linearVelocity` thực tế ≈ 0, nhưng agent vẫn gửi lệnh forward; replan ra cùng path; lặp vô hạn.
5. **Waypoint reach distance hơi lớn so với cell** — `0.25` trên `tileSize 1.0` nghĩa là tank chuyển sang waypoint kế tiếp khi còn cách tâm cell hiện tại 25%. Khi đi sát tường, nó bắt đầu cắt góc trước cả khi vào cell hiện tại.

## Mục tiêu plan này

Fix tất cả các nguyên nhân trên ở mức **đủ tốt cho baseline 1 enemy + 1 player**, không over-engineer cho LNS2 sớm. Thứ tự ưu tiên theo cost/benefit:

1. **Inflate obstacle theo radius tank** — fix nguyên nhân 1, 2 (rẻ, hiệu quả nhất, làm trước).
2. **Tách rotate-first vs drive** — fix nguyên nhân 3.
3. **Stuck detection + recovery** — fix nguyên nhân 4.
4. **Tinh chỉnh waypoint tolerance** — fix nguyên nhân 5.
5. (Optional) **Path smoothing / string-pulling** — chất lượng path, không phải để tránh kẹt.

Sau khi xong 1–4, milestone “1 enemy A\* tới Eagle ổn định” mới được coi là done.

## Phase 1 — Inflate obstacle (radius-aware walkability)

> **Cập nhật 2026-05-10 (mở rộng trong v3 Phase A)**: phase này nay là **lớp hard-block** trong cấu trúc soft cost. `GridNavMask` đã có thêm trục `proximityCost[,]` với `SoftRadius=2`, `SoftCostNear=8` (Chebyshev=1 từ vùng hard-block), `SoftCostMid=2` (Chebyshev=2). A* dùng `tentativeScore = g + 1 + GetCellCost(neighbor)`. Inflate radius=1 thuần (Phase 1 nguyên bản) chưa đủ với cluster barrier 2x2 vì A* vẫn chọn cell sát mép inflated; soft cost ép A* đi vòng khi có alternative ≤ 8 cell. Chi tiết: `Assets/Docs/mapf_enemy_stuck_phase2_plan_v3.md` mục "Phase A — DONE".

### Ý tưởng

Coi mỗi cell `c` là “walkable cho tank” chỉ khi tank đặt tâm tại `CellToWorld(c)` không chạm bất kỳ collider obstacle nào. Tương đương: cell `c` walkable khi **khoảng cách Chebyshev** từ `c` tới ô `@` gần nhất **≥ `inflateRadius`** cells.

Với capsule `(0.597, 0.696)` và `tileSize = 1`:

```text
tankHalfExtent ≈ 0.35 (lấy max của 0.597/2, 0.696/2)
inflateRadius (cells) = ceil(tankHalfExtent / tileSize) = 1
```

Nghĩa là: **bất kỳ cell `.` nào có hàng xóm 8-neighbor là `@` đều coi như non-walkable cho A\* của AI**.

Lưu ý: chỉ áp dụng cho **AI walkability**, không đụng vào `MapLoader.IsWalkable` gốc (player vẫn được quyền đi sát tường nếu họ muốn).

### Thiết kế

Thêm script:

```text
Assets/Scripts/Pathfinding/GridNavMask.cs
```

API tối thiểu:

```csharp
public class GridNavMask
{
    public GridNavMask(MapLoader map, int inflateRadius);  // build từ MapLoader
    public bool IsAgentWalkable(Vector2Int cell);           // dùng cho A*
    public int InflateRadius { get; }
    public void Rebuild(int newInflateRadius);              // cho debug/inspector
}
```

Build:

1. Lấy `width`, `height`, `IsWalkable` từ `MapLoader`.
2. Tạo `bool[,] agentWalkable`.
3. Với mỗi cell `(x, y)`:
   - Nếu `!map.IsWalkable((x,y))` ⇒ `agentWalkable[x,y] = false`.
   - Else, scan 8-neighbor trong bán kính `inflateRadius`. Nếu có bất kỳ neighbor nào là `@` ⇒ `false`.
   - Else ⇒ `true`.

Cost: O(W·H·k²), với 32×32 map và k=1 là vài nghìn ops, không cần optimize.

### Tích hợp với A\*

Sửa `GridAStarPathfinder.TryFindPath` để nhận thêm `GridNavMask`:

```csharp
public static bool TryFindPath(
    MapLoader mapLoader,
    GridNavMask navMask,    // optional, null = fallback IsWalkable
    Vector2Int start,
    Vector2Int goal,
    List<Vector2Int> path)
```

Bên trong:

```csharp
bool walkable = navMask != null
    ? navMask.IsAgentWalkable(cell)
    : mapLoader.IsWalkable(cell);
```

Vì có thể agent spawn lệch vào ô bị inflate (start/goal cell có thể đứng ngay sát tường), cần fallback:

- Nếu `start` không `IsAgentWalkable` ⇒ tìm `TryFindAgentWalkableNear(start)` trước khi A\*.
- Tương tự cho `goal` (Eagle có thể đang đứng cạnh tường).

Thêm helper `GridNavMask.TryFindAgentWalkableNear` mirror với `MapLoader.TryFindWalkableNear`.

### Anti corner-cut

Phase 1 đã giải quyết được phần lớn corner cutting vì cell `.` ở ngay đối diện 2 góc obstacle thường có `@` neighbor và bị inflate đè.

Trường hợp còn sót: 4-neighbor A\* không thể đi diagonal nên ít gặp clip góc thực sự. Vẫn còn 1 case là path đi qua hành lang 1 ô sát tường (cell `.` có obstacle ở đúng 1 phía). Chấp nhận case này nếu không có lựa chọn nào khác — nhưng inflate radius=1 đã loại bỏ luôn cả case đó (cell có 1 neighbor `@` cũng bị mark non-walkable).

⇒ Có thể cần expose `inflateRadius` trên Inspector để bật/tắt khi map quá hẹp:

```csharp
[SerializeField, Range(0, 3)] int agentInflateRadius = 1;
```

Map `random-32-32-10` có ~10% obstacle rải đều, radius=1 vẫn còn nhiều path. Nếu map dày hơn, hạ xuống 0 và xử lý kẹt bằng các phase sau.

### Wiring

Tạo `GridNavMask` 1 lần ở `MapScenarioBootstrap.SpawnScenario()` sau khi `MapLoader` build xong:

```csharp
navMask = new GridNavMask(mapLoader, agentInflateRadius);
```

Pass reference vào từng `GridEnemyAgent`:

```csharp
agent.navMask = navMask;
```

`GridEnemyAgent.ReplanPath` gọi `GridAStarPathfinder.TryFindPath(mapLoader, navMask, ...)`.

### Done khi

- Bật scene `MapF_TankTest`, enemy spawn xong → A\* trả path nằm cách `@` ít nhất 1 cell mọi điểm (debug bằng Gizmos).
- Tank không clip góc trong các test path đi qua khu vực đông obstacle.

## Phase 2 — Tách rotate-first vs drive trong `GridEnemyAgent.FollowPath`

### Vấn đề hiện tại

```csharp
if (dotProduct < 0.96f)
{
    int rotation = cross >= 0 ? -1 : 1;
    tankController.HandleMoveBody(new Vector2(rotation, 1f));  // tiến + xoay đồng thời
}
```

Khi cần xoay 90° (phổ biến với 4-neighbor path), `dot ≈ 0`, tank vừa xoay vừa tiến với speed cao ⇒ vẽ cung quét qua tường.

### Sửa

3-tier rule:

```csharp
const float DotAlignedThreshold = 0.97f;     // đi thẳng
const float DotMostlyAlignedThreshold = 0.6f; // tiến chậm + xoay

if (dot >= DotAlignedThreshold)
{
    HandleMoveBody(Vector2.up);                          // full forward
}
else if (dot >= DotMostlyAlignedThreshold)
{
    HandleMoveBody(new Vector2(rotation, 1f));           // xoay + tiến (như cũ)
}
else
{
    HandleMoveBody(new Vector2(rotation, 0f));           // CHỈ xoay tại chỗ
}
```

Lý do tách 3 tier thay vì 2: nếu chỉ rotate-or-drive thì tank giật giật ở góc rộng. Tier giữa cho phép vào cua mềm khi sai hướng vừa phải.

`TankMover` hiện tại có hành vi nhỏ cần lưu ý:

```csharp
if (movementVector.y > 0) currentForewardDirection = 1
else if (movementVector.y < 0) currentForewardDirection = -1
```

Khi `movementVector.y == 0`, `currentSpeed` giảm dần do `deacceleration` ⇒ rotate-only thực sự dừng tiến. OK, không cần đụng `TankMover`.

### Done khi

- Tank rẽ 90° tại waypoint giảm tốc rõ rệt, không quét cung qua tường.
- Đo bằng Gizmos: quỹ đạo thực gần với đường gấp khúc cell-to-cell.

## Phase 3 — Stuck detection + recovery

### Detect

Trong `GridEnemyAgent`:

```csharp
private Vector3 lastProgressPosition;
private float lastProgressTime;
private const float ProgressEpsilon = 0.05f;
private const float StuckTimeout = 1.5f;
```

Mỗi `Update`:

- Nếu `Vector3.Distance(transform.position, lastProgressPosition) > ProgressEpsilon`:
  - `lastProgressPosition = transform.position`
  - `lastProgressTime = Time.time`
- Nếu `Time.time - lastProgressTime > StuckTimeout`: trigger recovery.

### Recovery (escalating)

Tier 1 — Force replan ngay từ cell hiện tại:

```csharp
nextReplanTime = 0f;
currentPath.Clear();
```

Nếu A\* trả lại path cũ thì tier 2:

Tier 2 — Reverse 1 waypoint:

- `HandleMoveBody(new Vector2(0, -1))` trong `0.5s` (back-up).
- Sau đó replan.

Tier 3 — Tăng tạm `agentInflateRadius` lên `+1` cho riêng agent này, replan:

- Nếu vẫn bị kẹt sau tier 2.
- Cho phép path xa hơn, vòng tránh chỗ hẹp.
- Reset về radius mặc định khi đến waypoint kế tiếp.

Tier 3 chỉ là escape hatch, không nên trigger thường xuyên — log warning để soi sau.

### Lưu ý đa-agent

Phase này hoạt động ổn cho 1 enemy. Khi sang multi-enemy ở Milestone 3, “stuck” có thể do **enemy khác đang chặn**, không phải tường. Để xử lý đúng cần biết blocker là static (tường) hay dynamic (enemy khác). Không xử lý ở plan này — chỉ note để Milestone 3 xử lý: nếu blocker là enemy thì wait + replan với reservation, không reverse.

### Done khi

- Spawn enemy ở cell sát tường → tank tự thoát ra cell trống trong < 3s.
- Cho enemy đi qua hành lang vừa khít → nếu kẹt thì recovery, không loop forever.

## Phase 3.1 — Spatial stuck / loop detection

### Vì sao cần thêm

Phase 3 hiện tại chỉ bắt được case:

- tank gần như đứng yên,
- hoặc bị collider chặn cứng tại 1 chỗ.

Nhưng vẫn còn 1 class bug khác:

- tank **vẫn di chuyển**,
- nhưng chỉ loay quanh trong 1 cụm cell / 1 khu vực nhỏ,
- replan xong vẫn quay lại route cũ hoặc route rất gần route cũ,
- kết quả là **không tạo tiến triển thực sự** về Eagle.

Đây không phải “hard stuck” mà là **soft stuck / loop**.

### Lưu ý quan trọng: không nhầm với behavior đúng

Enemy có thể **đứng yên lại để bắn Eagle hoặc Player**. Trường hợp này là behavior hợp lệ, không được coi là stuck.

Vì vậy spatial-stuck detector chỉ được chạy khi:

- `shootingTarget == null`
- agent đang ở **navigation mode**
- mục tiêu là tiếp tục tới Eagle, không phải hold position để combat

### Detect

Thêm 1 cửa sổ quan sát ngắn, ví dụ:

```csharp
private const float SpatialStuckWindow = 3f;
private const float MinTravelDistanceInWindow = 1.5f;
private const float MaxDisplacementInWindow = 0.75f;
private const float GoalProgressEpsilon = 0.5f;
```

Theo dõi trong `GridEnemyAgent`:

- vị trí thực tế của tank trong `3s` gần nhất
- tổng quãng đường đã đi trong cửa sổ đó
- độ dời thẳng-line từ điểm đầu → điểm cuối
- khoảng cách tới `goalCell` hoặc waypoint hiện tại

Trigger spatial stuck khi:

1. Tổng quãng đường đã đi **lớn**, nhưng
2. Độ dời ròng từ điểm đầu → cuối **nhỏ**, hoặc
3. Khoảng cách tới goal/next waypoint **không giảm có ý nghĩa**

Nói ngắn gọn:

> tank cứ đi, nhưng không đi đâu cả.

### Recovery

Khi spatial stuck trigger, replan bình thường là chưa đủ, vì A\* thường trả lại cùng route cũ nếu cost map không đổi.

Vì vậy Phase 3.1 cần thay đổi input planner tạm thời:

#### Tier A — penalize / block recent cells

- Lưu `N` cell agent vừa đi qua (ví dụ 4–6 cell gần nhất)
- Khi replan, tạm coi các cell đó là:
  - non-walkable trong 1 lần replan, hoặc
  - có chi phí cao hơn nếu sau này A\* được nâng cấp có weighted cost

Mục tiêu: ép agent chọn nhánh khác thay vì quay lại cùng cell loop.

#### Tier B — temporary local avoidance zone

- Nếu loop xảy ra quanh 1 obstacle/bottleneck cụ thể
- Tạo 1 “temporary avoid zone” nhỏ quanh các cell vừa loop
- Replan với nav mask tạm thời đã loại vùng này

#### Tier C — fallback về baseline stuck recovery

- Nếu vẫn thất bại:
  - backup
  - expanded inflate mask
  - replan lại

### Thiết kế thực dụng

Không cần làm phức tạp như reservation table ở giai đoạn này. Đủ tốt cho baseline single-agent:

- thêm queue/circular buffer lưu recent positions
- thêm recent visited cells
- thêm 1 helper build “temporary blocked cells” cho 1 lần replan

API có thể theo kiểu:

```csharp
private bool IsSpatiallyStuck();
private void TriggerSpatialRecovery();
private bool TryFindPathAvoidingRecentCells(...);
```

### Done khi

- Enemy không còn quay vật / loop trong cùng 1 cụm cell quá `3s` khi không có line-of-sight bắn Eagle.
- Nếu enemy đang hold position để bắn Eagle thì **không** trigger spatial recovery.
- Khi spatial recovery xảy ra, path mới phải khác nhánh cũ trong ít nhất 1 đoạn local, không được replan ra y hệt loop cũ.

## Phase 4 — Tinh chỉnh waypoint tolerance

### Hiện tại

```csharp
public float waypointReachDistance = 0.25f;
```

Với `tileSize = 1`, agent skip waypoint khi còn cách tâm 0.25. Hợp lý cho khu vực rộng, nhưng khi rẽ ở góc thì tank cắt vào cell tiếp theo trước khi thực sự ở tâm cell hiện tại.

### Sửa nhẹ

Tách 2 tolerance theo loại waypoint:

```csharp
// nếu waypoint kế tiếp đổi hướng so với hiện tại → siết tolerance
float toleranceTurning = 0.12f;
float toleranceStraight = 0.30f;
```

Logic:

```csharp
bool isTurnAhead = pathIndex + 1 < currentPath.Count
    && (currentPath[pathIndex + 1] - currentPath[pathIndex]) !=
       (currentPath[pathIndex] - currentPath[pathIndex - 1]);

float tolerance = isTurnAhead ? toleranceTurning : toleranceStraight;
```

Tức là vào cua thì phải tới sát tâm cell mới snap, đi thẳng thì có thể skip sớm để di chuyển mượt.

### Done khi

- Quỹ đạo qua góc bám sát đường gấp khúc cell-to-cell.
- Đi đường thẳng vẫn không giật.

## Phase 5 (optional) — Path smoothing / string-pulling

### Khi nào làm

Chỉ làm sau khi Phase 1–4 ổn và chuẩn bị bước sang Milestone 3. Mục đích là **chất lượng quỹ đạo**, không phải tránh kẹt.

### Ý tưởng

Sau A\*, post-process path:

1. Bắt đầu từ `path[0]`, tìm `j` lớn nhất sao cho line từ `path[0]` đến `path[j]` không cắt obstacle (raycast hoặc Bresenham qua `IsAgentWalkable`).
2. Giữ `path[0]` và `path[j]`, lặp lại từ `j`.

Kết quả: ít waypoint hơn, đường ít zig-zag.

### Cẩn thận

- Vì đã inflate radius, line-of-sight check phải vẫn dùng `navMask.IsAgentWalkable` chứ không phải `mapLoader.IsWalkable` ⇒ giữ clearance.
- Khi sang Milestone 4 (LNS2 / cooperative) string-pulling trên timestep phức tạp hơn nhiều ⇒ khả năng phải bỏ Phase 5 này khi tích hợp reservation table. OK chấp nhận, đây là optional.

## Triển khai cụ thể (file/method)

| File | Thay đổi |
|---|---|
| `Assets/Scripts/Pathfinding/GridNavMask.cs` | **NEW** — radius-aware walkability mask |
| `Assets/Scripts/GridAStarPathfinder.cs` | Thêm overload nhận `GridNavMask`; default fallback `IsWalkable` |
| `Assets/Scripts/GridEnemyAgent.cs` | Field `navMask`; rotate-first 3-tier; stuck detection + recovery; spatial stuck / loop detection; waypoint tolerance theo turn/straight |
| `Assets/Scripts/MapScenarioBootstrap.cs` | Build `GridNavMask` 1 lần; pass vào mỗi `GridEnemyAgent`; expose `agentInflateRadius` lên Inspector |
| `Assets/Scripts/MapLoader.cs` | KHÔNG đụng — `IsWalkable` giữ nguyên cho player. |

## Test plan

### Test 1 — Inflate visualization (Phase 1)

- Thêm Gizmos trong `GridNavMask`: vẽ X đỏ trên mọi cell `IsAgentWalkable == false`.
- Mở scene `MapF_TankTest`, kiểm tra ô `.` cạnh `@` đã bị mark đỏ.

### Test 2 — Path không sát tường (Phase 1)

- Spawn 1 enemy ở cell xa Eagle.
- Verify path A\* qua Gizmos: không có đoạn nào nằm ngay cạnh `@`.

### Test 3 — Rẽ góc sạch (Phase 2)

- Tạo scenario: enemy spawn ở `(5, 5)`, eagle ở `(5, 28)`, có obstacle ở `(6, 14..18)`.
- Verify tank đi thẳng tới hàng obstacle, rẽ trái 90°, đi vòng, rẽ phải 90°, đi tiếp — không clip góc nào.

### Test 4 — Recovery (Phase 3)

- Force spawn enemy ở cell có 3 mặt là `@` (giả lập trap).
- Verify trong < 3s tank thoát ra cell trống và resume A\*.

### Test 5 — Hành lang hẹp (Phase 4)

- Tạo scenario có hành lang 1 cell rộng nối 2 vùng mở.
- Verify enemy qua được hành lang không reset, không kẹt mép.

### Test 6 — Spatial loop (Phase 3.1)

- Tạo scenario có obstacle/barrier khiến enemy không kẹt cứng, nhưng dễ chạy vòng trong cùng 1 cụm cell.
- Verify trong `<= 3s` agent detect spatial stuck và replan theo nhánh khác.
- Verify path mới không lặp lại y hệt chuỗi recent cells.

### Test 7 — Hold to shoot Eagle (Phase 3.1)

- Tạo scenario enemy có line-of-sight tới Eagle.
- Verify enemy được phép dừng để aim/shoot.
- Verify spatial-stuck detector **không** trigger trong khi combat hold.

### Done khi

Cả 7 test đều pass với cấu hình mặc định. Console sạch (chỉ log debug có ý nghĩa, không có error).

## Liên hệ với Milestone 3 và LNS2

Plan này **chỉ fix single-agent A\***. Khi qua Milestone 3 (multi-enemy A\* baseline):

- `GridNavMask` reuse y nguyên — vì đó là static obstacle inflation.
- Stuck recovery cần phân biệt blocker static vs dynamic (đã ghi note ở Phase 3).
- Path smoothing (Phase 5) có thể phải drop khi sang Milestone 4 vì reservation table dùng cell discrete.

Sau khi plan này hoàn tất, đi tiếp đúng theo `mapf_eagle_enemy_astar_plan.md` Milestone 3 → 4 → 5.

## Bước tiếp theo ngay

1. Implement `GridNavMask` + tích hợp vào `GridAStarPathfinder` (Phase 1) — riêng phase này đã đủ giải quyết 80% case kẹt mà user đang gặp.
2. Test bằng Gizmos trước khi đụng `GridEnemyAgent`.
3. Sau đó mới fix controller (Phase 2 + 3 + 4) trong cùng 1 commit hoặc tách 3 commit nhỏ.

Không nên gộp tất cả 5 phase vào 1 PR — Phase 1 và Phase 2 đã đủ visible diff.
