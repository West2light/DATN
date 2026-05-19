# Plan: Fix enemy tank cọ/kẹt vào tường - Pha 2

Đây là plan kế tiếp của:

- `Assets/Docs/mapf_enemy_stuck_at_obstacle_plan.md` (đã implement xong Phase 1-5: inflate, rotate-first, stuck recovery, spatial loop, smoothing).
- `Assets/Docs/mapf_eagle_enemy_astar_plan.md` (Milestone 2 đã xong; Milestone 3 multi-enemy + metrics là điểm đến tiếp theo).

Mục tiêu plan này: sau khi đã implement xong plan kẹt-tường gốc, enemy tank vẫn còn 3 triệu chứng tồn đọng cần xử lý riêng biệt trước khi sang Milestone 3:

1. **Cọ vào tường (visual scuff)** — tank vẫn quệt body vào collider barrier khi đi qua, dù không bị kẹt cứng.
2. **Path smoothing kém clearance** — Phase 5 đang gộp waypoint dựa trên `IsAgentWalkable` của center cell, làm đoạn smooth chạy sát mép obstacle hơn cả path A\* gốc.
3. **Hard-stuck cạnh cluster barrier** — tank dán body vào cluster barrier 2x2/3-ô và không tiến tiếp được (xem `Assets/Screenshots/MapF_AStar_EnemyAgent_stuck_with_barries.png`, `MapF_AStar_EnemyAgent_stuck_with_barries_2.png`).

## Phân biệt 3 triệu chứng (rất quan trọng)

Trước khi đề xuất fix, cần phân biệt rõ 3 lớp lỗi vì cách xử lý khác nhau:

| Triệu chứng | Tank còn di chuyển? | Body có chạm collider? | Có tiến tới Eagle? | Hiện tại detect được không? |
|---|---|---|---|---|
| **CỌ (scuff)** | Có, chậm | Có (liên tục) | Có (chậm) | KHÔNG. `progressEpsilon = 0.05` quá lỏng — tank cọ tường vẫn trượt > 0.05 unit / frame nên `lastProgressTime` reset đều, không trigger stuck. |
| **STUCK (hard)** | Không / gần 0 | Có (đè cứng) | Không | Có. `stuckTimeout = 1.5s` + displacement check sẽ trigger. |
| **LOOP (soft-stuck)** | Có, đáng kể | Không nhất thiết | Không | Có. Phase 3.1 spatial detector đã xử lý. |

3 triệu chứng cần 3 nhóm fix riêng. Plan gốc đã giải quyết tốt (2) và (3); plan này tập trung (1) **cọ** và phần còn sót của (2) **stuck cạnh cluster** mà inflate radius=1 chưa đủ chặn.

## Chẩn đoán nguyên nhân gốc

### Hiện trạng code đã implement

Đã verify qua đọc code:

- `GridNavMask.cs` — Chebyshev radius `inflateRadius` (default `1`). `HasBlockedNeighborWithinRadius` quét cell `(x±r, y±r)` và mark non-walkable nếu **bất kỳ** ô trong vùng đó là `@`.
- `GridAStarPathfinder.cs` — A\* 4-neighbor + Manhattan + smoothing Bresenham qua `IsWalkable(map, navMask, blocked, cell)`. Smoothing **dùng `navMask.IsAgentWalkable`** chứ không phải `mapLoader.IsWalkable` — đây không phải nguyên nhân smoothing kém.
- `GridEnemyAgent.cs` — đầy đủ: rotate-first 3-tier (`0.97 / 0.6`), stuck timer, reverse, expanded mask, spatial detector + recent-cell blocking, waypoint tolerance turn/straight (`0.12 / 0.30`).
- `MapScenarioBootstrap.cs` — build `navMask = new GridNavMask(mapLoader, 1)` và pass vào agent.

### Số liệu vật lý quan trọng

| Thông số | Giá trị | Nguồn |
|---|---|---|
| `tileSize` | `1.0` | `MapLoader.tileSize` |
| Tank capsule | `(0.597, 0.696)` | `Tank.prefab` |
| Tank half-extent (max) | `0.348` | tính từ size capsule |
| Inflate radius hiện tại | `1` cell (Chebyshev) | `MapScenarioBootstrap.agentInflateRadius` |
| Khoảng cách an toàn lý thuyết | `≥ 1.0 - 0.348 = 0.652` (cell tâm-tới-tâm) | sau inflate |
| `progressEpsilon` | `0.05` unit | `GridEnemyAgent` |
| `forwardAlignmentThreshold` | `0.97` | `GridEnemyAgent` |
| `turningDriveAlignmentThreshold` | `0.6` | `GridEnemyAgent` |
| `EnemyTankMovementData.maxSpeed` | `50` | asset |
| `Time.fixedDeltaTime` | `0.02s` (default) | Unity |
| Vận tốc thực mỗi frame | `50 * 0.02 = 1.0` unit | `TankMover.FixedUpdate` (rb2d.linearVelocity nhân Time.fixedDeltaTime) |

### Nguyên nhân gốc theo từng triệu chứng

#### Triệu chứng 1 — CỌ vào tường

Có 4 nguyên nhân cộng hợp:

**1.1. Inflate radius=1 chưa đủ với cluster 2x2 barrier**

Inflate Chebyshev=1 chỉ đảm bảo cell A\* path không nằm cạnh `@` trực tiếp. NHƯNG khi 2 cluster đứng chéo nhau hoặc 1 cluster 2x2 thì cell `.` ở **giữa 2 cluster** vẫn là agent-walkable nếu khoảng cách Chebyshev tới `@` gần nhất là 2. Tank đi qua khe đó với clearance `(2 * 1.0 - 0.696) / 2 ≈ 0.65` unit — cộng với rotation drift khi rẽ thì capsule scuff vào edge của cluster.

Cụ thể với screenshot 1: cluster 4 barrier ở grid `(x, y..y+1) × (z, z+1)`. Cell tâm tank đứng có Chebyshev distance = 1 tới cluster bên trái VÀ Chebyshev = 1 tới cluster bên dưới-phải. Inflate=1 mark cell đó non-walkable nhưng **adjacent cell** (Chebyshev=2 với 1 cluster, =1 với cluster kia) vẫn walkable — path A\* xuyên qua đó, tank cọ.

**1.2. Smoothing có thể sinh đường chéo cắt sát góc cluster**

`SmoothPath` ở `GridAStarPathfinder.SmoothPath` gộp waypoint khi `HasLineOfSight` qua Bresenham trả `true`. Bresenham chỉ check **center cell** dọc đường thẳng. Khi line đi cheo từ `(5, 5)` → `(8, 7)` qua khe cluster, các cell trung gian đều agent-walkable theo navMask, nhưng **đường thẳng vật lý** từ tâm `(5,5)` tới tâm `(8,7)` chạy lệch khỏi tâm các cell trung gian, cách edge cluster có khi chỉ `0.1-0.2` unit — nhỏ hơn tank half-extent `0.348`. Tank đi đường này chắc chắn cọ.

Đây là **swept-volume problem**: smoothing chỉ check center, không check tank capsule khi sweep dọc line.

**1.3. `turningDriveAlignmentThreshold = 0.6` quá lỏng**

Với `dot ∈ [0.6, 0.97]` tank vừa xoay vừa tiến full speed. `dot = 0.6` tương đương lệch ~53°. Tại waypoint corner, tank quét cung tròn rộng. Cung này quét qua góc obstacle khi rẽ tại cell sát cluster.

`TankMover.Move()` reset `currentSpeed = 0` chỉ khi đảo dấu `currentForewardDirection`. Khi `dot = 0.6` và `movementVector = (rot, 1)`, tank vẫn tiến với `currentSpeed = maxSpeed = 50` ⇒ vận tốc thực 1 unit/frame ⇒ trong 0.5s xoay 75° tank đã trượt 25 unit lệch khỏi cell tâm — quá nhiều với clearance `0.65`.

**1.4. `progressEpsilon = 0.05` quá lỏng để bắt cọ**

Tank cọ tường vẫn trượt slow nhưng đều, displacement/frame ~ 0.1-0.3. `progressEpsilon = 0.05` < cọ-displacement ⇒ `lastProgressTime` reset đều, **stuck timer không bao giờ trigger** trong khi visual rất xấu.

#### Triệu chứng 2 — Smoothing đẩy path sát tường

Đã cover ở 1.2. Smoothing dùng Bresenham center-cell check mà không sweep tank capsule. Khi cell trung gian agent-walkable nhưng line vật lý đi sát edge cluster, smoothing vẫn approve và rút path → quỹ đạo final tệ hơn path A\* gốc trong các khu cluster.

**Lưu ý**: Smoothing **không** dùng `mapLoader.IsWalkable` (đã verify). Nó dùng `navMask.IsAgentWalkable`. Vấn đề là *cell-center check* chứ không phải *raw obstacle check*.

#### Triệu chứng 3 — Hard-stuck cạnh cluster (screenshot 1, 2)

Quan sát screenshot 2: tank xen giữa cluster, có **red line LOS dài** chạy ra ngoài frame. Dấu hiệu cho thấy `GetShootingTarget()` trả về non-null ⇒ `HandleMoveBody(Vector2.zero)` ⇒ tank dừng hẳn để aim. Đây **không phải bug stuck**, đây là **aim/shoot legit** — nhưng vị trí dừng lại là vị trí đang cọ collider (kế thừa từ pha approach trước đó).

Còn screenshot 1: tank dán body vào cluster, dashed line là path A\* hiện tại đi xuyên cluster. Đây có thể là:
- Path mới sau replan đi qua khe hẹp, tank rẽ vào và bị cọ quá đà ⇒ velocity = 0 thực sự (Rigidbody bị collider cản hoàn toàn);
- HOẶC tank đang ở chế độ shoot (LOS hợp lệ) và dừng hẳn — body đã cọ sẵn từ trước.

Cả 2 case đều có chung pattern: **tank đã trượt tới vị trí body chạm collider trước khi reach waypoint**. Inflate=1 không che hết case này vì path đi qua khe Chebyshev=1 từ cluster đối diện.

### Tóm tắt nguyên nhân

| Triệu chứng | Nguyên nhân chính | Phase fix |
|---|---|---|
| Cọ tường visual | Inflate radius không đủ + smoothing không sweep + alignment threshold lỏng | Phase A + B + C |
| Smoothing kém clearance | Bresenham check center cell, không check swept capsule | Phase B |
| Hard-stuck cạnh cluster | Cộng hưởng từ cọ → body cản hoàn toàn → progressEpsilon quá lỏng để detect | Phase A + D |

## Mục tiêu plan

Theo thứ tự cost/benefit (rẻ + nhiều benefit nhất trước):

- **Phase A** — Soft cost map (cell gần obstacle có cost cao hơn, không block hẳn) thay cho binary inflate radius=2.
- **Phase B** — Swept-capsule line-of-sight cho smoothing.
- **Phase C** — Tighter rotate-first (`turningDriveAlignmentThreshold` lên `0.85`, slow drive khi cọ).
- **Phase D** — Capsule-aware stuck detection (detect contact với collider thay vì chỉ displacement).
- **Phase E** (optional) — Tighter waypoint reach distance ở khu cluster + force-center snap.

Sau khi xong A + B + D, baseline single-enemy có thể coi là production-ready cho Milestone 3.

## Phase A — Soft cost map (ưu tiên cao nhất)

### Vấn đề

Binary inflate radius=1 không đủ; inflate radius=2 chặn quá nhiều, có thể làm map 32x32 với 10% obstacle bị **cô lập** (no path từ một số corner tới Eagle).

### Ý tưởng

Thay vì binary `agentWalkable[x,y] = bool`, dùng `int agentCost[x,y]` với scale:

| Chebyshev distance tới `@` gần nhất | Cost cộng thêm |
|---|---|
| 0 (cell `@`) | `∞` (block) |
| 1 (sát tường) | `+8` (vẫn đi được nhưng tránh nếu có lựa chọn) |
| 2 (Chebyshev 2) | `+2` |
| ≥ 3 | `0` |

A\* sẽ ưu tiên path đi cell xa tường. Khi không còn lựa chọn (cell hẹp), nó vẫn dùng cell sát tường, chỉ phải trả thêm cost. Tham số `+8` nghĩa là tank thà đi vòng 8 cell còn hơn cọ 1 cell sát tường.

### Thiết kế

Sửa `GridNavMask.cs`:

```csharp
public class GridNavMask
{
    private bool[,] agentWalkable;       // hard-block (cell @ hoặc trong inflateRadius)
    private int[,] proximityCost;         // soft cost (≥ 0)
    public int InflateRadius { get; }     // hard inflate (default 1)
    public int SoftRadius { get; }        // soft inflate (default 2)
    public int SoftCostNear { get; }      // cost cho cell Chebyshev=1 (default 8)
    public int SoftCostMid { get; }       // cost cho cell Chebyshev=2 (default 2)

    public bool IsAgentWalkable(Vector2Int cell);
    public int GetCellCost(Vector2Int cell);  // 0 nếu không có cost; trả int.MaxValue nếu !walkable
}
```

`Rebuild`:
1. Build `agentWalkable` như cũ với `InflateRadius`.
2. Build `proximityCost`: với mỗi cell walkable, scan trong `SoftRadius` cells; nếu Chebyshev=1 tới `@` ⇒ cost = max(cost, `SoftCostNear`); nếu Chebyshev=2 ⇒ cost = max(cost, `SoftCostMid`).

Sửa `GridAStarPathfinder.cs`:

```csharp
int tentativeScore = gScore[current]
    + 1                                       // step cost
    + (navMask != null ? navMask.GetCellCost(neighbor) : 0);
```

Heuristic Manhattan vẫn admissible vì cost ≥ 1.

### Tham số mặc định

| Param | Default | Lý do |
|---|---|---|
| `InflateRadius` | `1` | giữ nguyên |
| `SoftRadius` | `2` | đủ tránh cọ với cluster 2x2 |
| `SoftCostNear` | `8` | thà đi vòng 8 cell |
| `SoftCostMid` | `2` | nhẹ, chỉ nudge |

Expose lên `MapScenarioBootstrap` Inspector.

### Test plan

- **Test A.1** — Visualize cost: thêm Gizmos vẽ cell theo cost (đỏ đậm = cao). Verify cell sát cluster có cost `8`.
- **Test A.2** — Path qua khe Chebyshev=1: spawn enemy ở cell phải đi qua khe hẹp → verify path chỉ đi khe khi không có alternative. Khi có alternative xa hơn 5 cell, A\* vẫn chọn alternative.
- **Test A.3** — Cluster 2x2 barriers: spawn enemy đối diện cluster → path đi vòng cluster với khoảng cách ≥ 2 cell, không xuyên khe.

### Done khi

- Path A\* hiển thị qua Gizmos không còn đi qua khe Chebyshev=1 giữa 2 cluster trừ khi đó là lựa chọn duy nhất.
- Tank chạy trong scene `MapF_TankTest` không còn cọ cluster trong 30s observation.

## Phase B — Swept-capsule smoothing

### Vấn đề

`GridAStarPathfinder.HasLineOfSight` (Bresenham) chỉ check center của các cell dọc line. Khi line `(5,5) → (8,7)` chạy chéo, các cell trung gian là agent-walkable nhưng **line vật lý** lệch khỏi tâm cell và có thể đi sát edge cluster.

### Ý tưởng

Trước khi gộp waypoint, simulate sweep tank capsule (`Physics2D.CapsuleCast` hoặc check analytical) dọc line. Nếu cast hit `ObstaclesMovement` ⇒ không gộp.

### Thiết kế 2 lựa chọn

#### Lựa chọn B.1 — Analytical center-distance check (rẻ, ưu tiên)

Với mỗi pair `(start, end)` candidate gộp:
1. Tính line `start → end` trong world coord.
2. Với mỗi cell `@` trong **bounding box** của line (mở rộng `tankHalfExtent + epsilon`):
   - Tính khoảng cách từ tâm cell `@` tới line.
   - Nếu khoảng cách < `tankHalfExtent + clearanceMargin` (default `0.4`) ⇒ reject gộp.

Cost: O(cells_in_bbox * 1) cho mỗi candidate gộp; total ~ O(path_length²) trong worst case nhưng path 32-cell thì tổng ops vài nghìn — OK.

#### Lựa chọn B.2 — Physics2D.CapsuleCastAll (chính xác nhưng đắt hơn)

```csharp
RaycastHit2D[] hits = Physics2D.CapsuleCastAll(
    startWorld, capsuleSize, capsuleDirection, angle,
    direction, distance, obstacleLayerMask);
```

Yêu cầu chạy ở runtime (không Editor-time). Không phù hợp với `MapLoader.LoadAndBuild` ở Edit Mode (ContextMenu).

⇒ **Chọn B.1**.

### API

Thêm `GridNavMask` (hoặc class helper riêng `NavMaskGeometry`):

```csharp
public bool HasClearance(Vector2 worldStart, Vector2 worldEnd, float radius);
```

`GridAStarPathfinder.SmoothPath` thay `HasLineOfSight(cells)` bằng:

```csharp
Vector2 startWorld = mapLoader.CellToWorld(path[anchorIndex]);
Vector2 endWorld = mapLoader.CellToWorld(path[candidateIndex]);
if (!navMask.HasClearance(startWorld, endWorld, tankClearanceRadius)) break;
```

`tankClearanceRadius` default = `0.4` (tank half-extent `0.348` + margin `0.05`).

Pass `tankClearanceRadius` từ `MapScenarioBootstrap` qua `GridEnemyAgent` qua `GridAStarPathfinder.TryFindPath` (cần thêm overload).

### Test plan

- **Test B.1** — Map có cluster 2x2 ở `(10,10)`, path từ `(5,10)` → `(15,10)`: verify smoothed path KHÔNG gộp `(5,10)-(15,10)` thành 1 line nếu line đi sát cluster. Phải giữ ít nhất 1 waypoint trung gian né lên/xuống.
- **Test B.2** — Map trống: smoothed path từ `(0,0)` → `(31,31)` phải gộp về 2 waypoint (start, end).

### Done khi

- Smoothed path không bao giờ có line đi cách obstacle < 0.4 unit theo hình học.
- Visual: tank theo smoothed path không cọ.

## Phase C — Tighter rotate-first

### Vấn đề

`turningDriveAlignmentThreshold = 0.6` (≈ 53° lệch) cho phép tank tiến full-speed trong khi xoay. Cung quét rộng ở waypoint góc ⇒ scuff cluster cạnh corner.

### Sửa

Tách 4-tier thay vì 3:

```csharp
const float DotAlignedThreshold = 0.97f;          // đi thẳng full-speed
const float DotMostlyAlignedThreshold = 0.85f;    // đi thẳng + nudge xoay (cos 32°)
const float DotPartialThreshold = 0.5f;           // tiến chậm + xoay (cos 60°)
// dưới 0.5 ⇒ rotate-only

if (dot >= DotAlignedThreshold)        HandleMoveBody(Vector2.up);
else if (dot >= DotMostlyAlignedThreshold) HandleMoveBody(new Vector2(rotation * 0.3f, 1f));   // nhẹ xoay
else if (dot >= DotPartialThreshold)   HandleMoveBody(new Vector2(rotation, 0.3f));            // tiến chậm
else                                    HandleMoveBody(new Vector2(rotation, 0f));             // chỉ xoay
```

Lưu ý `TankMover` không support `movementVector.x` < 1 trực tiếp (rotationSpeed nhân `movementVector.x`), chứ `currentSpeed` không phụ thuộc magnitude `y` — chỉ phụ thuộc `Mathf.Abs(y) > 0`. Nên `y = 0.3f` vẫn = full-speed accel. Cần sửa `TankMover.CalculateSpeed` chấp nhận magnitude `y`:

```csharp
float yMagnitude = Mathf.Clamp01(Mathf.Abs(movementVector.y));
if (yMagnitude > 0)
    currentSpeed += movementData.acceleration * Time.deltaTime * yMagnitude;
else
    currentSpeed -= movementData.deacceleration * Time.deltaTime;
currentSpeed = Mathf.Clamp(currentSpeed, 0, movementData.maxSpeed * yMagnitude > 0 ? 1 : 1);
```

**Cảnh báo**: sửa `TankMover` ảnh hưởng cả player. Phải kiểm tra player input pipeline (`PlayerInput.HandleMoveBody`) không gửi `y` partial. Nếu player chỉ gửi `±1` hoặc `0` thì vẫn OK.

⇒ **Cách an toàn hơn**: Không sửa `TankMover`. Trong `GridEnemyAgent.FollowPath`, khi cần "tiến chậm + xoay", **cycle** giữa `(rot, 0)` và `(rot, 1)` mỗi frame (rate ~ 50-50 hoặc 30-70). Speed thực giảm vì `currentSpeed` decay khi `y = 0`.

Implement bằng:

```csharp
private float partialDriveAccumulator;  // [0..1)
// trong FollowPath:
else if (dot >= DotPartialThreshold)
{
    partialDriveAccumulator += Time.deltaTime / partialDrivePeriod;  // period 0.1s
    bool drive = (partialDriveAccumulator % 1f) < 0.4f;  // 40% drive, 60% pure rotate
    HandleMoveBody(new Vector2(rotation, drive ? 1f : 0f));
}
```

### Test plan

- **Test C.1** — Spawn enemy phải rẽ 90° tại waypoint cạnh cluster → quỹ đạo (record `transform.position`) không vượt quá `0.2` unit ra ngoài đường gấp khúc cell-to-cell.
- **Test C.2** — Đường thẳng dài: enemy không bị giật do cycling.

### Done khi

- Quỹ đạo qua corner gần đường gấp khúc.
- FPS không bị ảnh hưởng (cycling chỉ thêm 1 modulo).

## Phase D — Capsule-aware stuck detection

### Vấn đề

`progressEpsilon = 0.05` quá lỏng. Tank cọ tường vẫn trượt > 0.05 unit/frame ⇒ stuck timer không trigger ⇒ visual cọ kéo dài vô hạn.

Hơn nữa khi tank dán cứng vào cluster (screenshot 1), nếu `linearVelocity ≈ 0` thì stuck timer trigger sau 1.5s — OK. Nhưng nếu tank vừa cọ vừa trượt (không bị cản hoàn toàn) thì timer không bao giờ trigger.

### Ý tưởng

Detect contact với obstacle layer thay vì chỉ displacement.

### Thiết kế

Thêm vào `GridEnemyAgent`:

```csharp
public LayerMask obstacleContactMask;   // = ObstaclesMovement
public float scuffTimeout = 0.4f;        // cọ liên tục 0.4s ⇒ trigger

private float scuffStartTime = -1f;
private bool wasTouchingLastFrame;

private bool IsScuffing()
{
    Rigidbody2D rb = tankController.tankMover.rb2d;
    if (rb == null) return false;
    return rb.IsTouchingLayers(obstacleContactMask);
}

// trong UpdateProgressTracking:
bool touching = IsScuffing();
if (touching)
{
    if (!wasTouchingLastFrame) scuffStartTime = Time.time;
    if (Time.time - scuffStartTime > scuffTimeout)
    {
        // cọ quá lâu → treat như stuck
        TriggerRecovery();
        scuffStartTime = -1f;
    }
}
else
{
    scuffStartTime = -1f;
}
wasTouchingLastFrame = touching;
```

Reset khi reach waypoint mới (path index đổi).

### Lưu ý

- Không trigger trong combat hold (`shootingTarget != null`) — body có thể đang scuff khi dừng để aim, nhưng đó là legit. Đã cover sẵn vì `Update()` early-return khi shooting.
- Recovery escalation reuse Phase 3 escalating (`ForcedReplan` → `Reverse` → `ExpandedMask`). Khi `Reverse` chạy, tank lùi 0.5s ra khỏi contact ⇒ replan với inflate radius +1 → tránh cluster.

### Test plan

- **Test D.1** — Force enemy spawn ở cell có path đi qua khe hẹp giữa 2 cluster: nếu enemy cọ > 0.4s, recovery trigger và replan ra path xa hơn (do `ExpandedMask`).
- **Test D.2** — Combat hold: enemy có LOS với Eagle, body cọ collider khi dừng aim. Verify recovery KHÔNG trigger trong khi shooting.

### Done khi

- Console log mỗi lần scuff recovery: `"[GridEnemyAgent] {name} scuff recovery triggered after {N}s"`.
- Enemy không cọ liên tục quá 0.4s.

## Phase E (optional) — Force-center snap khi gần cluster

Chỉ làm sau khi A + B + D ổn. Mục đích: giảm rotation drift khi tank đi cell sát cluster.

### Ý tưởng

Khi tank đang trong cell mà `proximityCost > 0` (gần obstacle), tăng độ chặt waypoint reach:

```csharp
private float GetWaypointReachDistance()
{
    float baseTolerance = isTurnAhead ? waypointReachDistanceTurning : waypointReachDistanceStraight;
    Vector2Int currentCell = mapLoader.WorldToCell(GetAgentPosition());
    int proximity = navMask.GetCellCost(currentCell);
    if (proximity >= 8) return Mathf.Min(baseTolerance, 0.05f);
    if (proximity >= 2) return Mathf.Min(baseTolerance, 0.10f);
    return baseTolerance;
}
```

Khi gần cluster, tank phải gần như ở tâm cell mới snap waypoint kế tiếp ⇒ ít drift.

### Done khi

- Quỹ đạo qua khu cluster bám tâm cell trong < 0.1 unit error.

## Triển khai cụ thể (file/method)

| File | Thay đổi |
|---|---|
| `Assets/Scripts/Pathfinding/GridNavMask.cs` | Thêm `proximityCost[,]`, `GetCellCost()`, `HasClearance()` (Phase A + B). Rebuild scan thêm SoftRadius. |
| `Assets/Scripts/GridAStarPathfinder.cs` | A\* dùng `gScore + 1 + GetCellCost(neighbor)` thay vì chỉ `+1`. `SmoothPath` thay `HasLineOfSight` bằng `navMask.HasClearance(world, world, radius)`. Thêm overload `TryFindPath(..., float clearanceRadius)`. |
| `Assets/Scripts/GridEnemyAgent.cs` | Phase C: 4-tier rotate với cycling partial drive. Phase D: `IsTouchingLayers` scuff detection + `scuffTimeout`. Phase E: tighten `GetWaypointReachDistance` theo proximity cost. Field mới: `obstacleContactMask`, `scuffTimeout`, `tankClearanceRadius`, `partialDrivePeriod`. |
| `Assets/Scripts/MapScenarioBootstrap.cs` | Expose `softRadius`, `softCostNear`, `softCostMid`, `tankClearanceRadius`, `scuffTimeout` lên Inspector. Wire vào `GridEnemyAgent`. Khi tạo `GridNavMask` truyền params mới. |
| `Assets/Scripts/TankMover.cs` | KHÔNG đụng (nếu chọn approach cycling thay vì partial-magnitude). |
| `Assets/Scripts/MapLoader.cs` | KHÔNG đụng. |

## Test plan tổng

### Test 1 — Soft cost visualization (Phase A)

- Bật scene `MapF_TankTest` → Editor mode → select `MapScenarioBootstrap` → Gizmos vẽ heatmap cost.
- Verify: cell Chebyshev=1 với `@` có chấm đỏ đậm; Chebyshev=2 chấm đỏ nhạt; Chebyshev≥3 không chấm.

### Test 2 — Path tránh cluster (Phase A)

- Spawn enemy ở `(5, 30)`, eagle ở `(28, 5)`. Verify path đi vòng cluster ở giữa, không xuyên khe Chebyshev=1.
- Đo path length. So với baseline (radius=1 binary): path mới có thể dài hơn ≤ 15%, chấp nhận.

### Test 3 — Smoothing có clearance (Phase B)

- Test path `(5,5) → (15,5)` với cluster `(8,4)-(9,5)` chắn ngang dưới: smoothed path KHÔNG gộp `(5,5)-(15,5)` thành line trực tiếp. Phải có waypoint vòng lên `y=3` hoặc dưới.
- Test path map trống: gộp về 2 waypoint.

### Test 4 — Cọ tường < 0.4s (Phase D)

- Force scenario: spawn enemy ngay sát cluster (cell Chebyshev=1), Eagle bên kia cluster.
- Verify tank cọ ≤ 0.4s rồi recovery, không cọ liên tục 5s+.

### Test 5 — Rotation drift giảm (Phase C)

- Record `transform.position` của enemy trong 10s khi rẽ 90° tại waypoint cạnh cluster.
- Verify max deviation từ đường gấp khúc cell-to-cell < `0.2` unit (so với baseline ~ `0.5`).

### Test 6 — Combat hold không trigger scuff recovery (Phase D)

- Spawn enemy có LOS thẳng tới Eagle, body chạm collider.
- Verify enemy hold position aim/shoot, KHÔNG bị scuff recovery interrupt.

### Test 7 — Regression spatial loop (Phase 3.1 cũ)

- Chạy lại Test 6 (spatial loop) của plan gốc → verify không regress.

### Test 8 — Regression hard-stuck recovery (Phase 3 cũ)

- Spawn enemy bị 3 mặt là `@` → verify thoát trong 3s.

## Liên hệ Milestone tiếp theo

Plan này vẫn chỉ là single-agent. Khi sang Milestone 3 (multi-enemy):

- **Phase A (soft cost)** scale tốt sang multi-agent — đơn giản là static cost. Có thể dùng làm base cho **dynamic cost** ở Milestone 4 (cell có agent reservation = +cost cao).
- **Phase B (swept clearance)** không cần đụng vì cluster geometry không đổi giữa agents.
- **Phase C (4-tier rotate)** per-agent, không conflict.
- **Phase D (scuff detection)** Cảnh báo: ở multi-agent, tank cọ tank khác cũng gây `IsTouchingLayers(ObstaclesMovement)` nếu PlayerBlocker để layer này. Phải kiểm tra `obstacleContactMask` chỉ chứa **wall layer**, KHÔNG chứa Agent layer. Nếu cần, tách layer wall riêng `Walls` thay vì `ObstaclesMovement` chung.

⇒ Trước khi sang Milestone 3, ghi note đổi `MapScenarioBootstrap.AddPlayerBlocker` từ `ObstaclesMovement` sang layer riêng `AgentBlocker` để Phase D không nhầm.

## Bước tiếp theo ngay

1. **Phase A trước** — soft cost map fix 70-80% case cọ. Implement riêng PR/commit.
2. Test scene → nếu vẫn cọ ở smoothed path (dự đoán: có), implement **Phase B**.
3. Sau đó **Phase D** để có safety net cho mọi case còn sót.
4. **Phase C** chỉ cần nếu sau A+B+D vẫn còn drift visible.
5. **Phase E** chỉ cần khi user/feedback yêu cầu polish thêm.

Ưu tiên Phase A + B trong commit đầu, Phase D commit thứ 2. Phase C, E để sau.
