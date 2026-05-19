# Plan: Fix enemy tank cọ/kẹt vào tường - Pha 2 (v3 - sub-agent assignment)

> **Phiên bản v3** — kế thừa toàn bộ phân tích nguyên nhân, thiết kế và test plan của v2 (`mapf_enemy_stuck_phase2_plan.md`). v3 bổ sung **sub-agent ownership matrix**, **activation table** và **cross-phase coordination** để dispatch dễ hơn theo cấu trúc orchestrator. Không thay đổi logic kỹ thuật của v2.

Đây là plan kế tiếp của:

- `Assets/Docs/mapf_enemy_stuck_at_obstacle_plan.md` (đã implement xong Phase 1-5: inflate, rotate-first, stuck recovery, spatial loop, smoothing).
- `Assets/Docs/mapf_eagle_enemy_astar_plan.md` (Milestone 2 đã xong; Milestone 3 multi-enemy + metrics là điểm đến tiếp theo).

Mục tiêu plan này: sau khi đã implement xong plan kẹt-tường gốc, enemy tank vẫn còn 3 triệu chứng tồn đọng cần xử lý riêng biệt trước khi sang Milestone 3:

1. **Cọ vào tường (visual scuff)** — tank vẫn quệt body vào collider barrier khi đi qua, dù không bị kẹt cứng.
2. **Path smoothing kém clearance** — Phase 5 đang gộp waypoint dựa trên `IsAgentWalkable` của center cell, làm đoạn smooth chạy sát mép obstacle hơn cả path A\* gốc.
3. **Hard-stuck cạnh cluster barrier** — tank dán body vào cluster barrier 2x2/3-ô và không tiến tiếp được (xem `Assets/Screenshots/MapF_AStar_EnemyAgent_stuck_with_barries.png`, `MapF_AStar_EnemyAgent_stuck_with_barries_2.png`).

---

## Activation matrix (v3)

Bảng dispatch tổng cho toàn plan. Cột "Owner" là sub-agent chính chịu trách nhiệm implement; các cột còn lại là sub-agent được kích hoạt sau hoặc song song.

| Phase | Owner (impl) | Verify | Visualization | Metrics | Docs |
|---|---|---|---|---|---|
| **A — Soft cost map** | `mapf-gameplay-engineer` | `unity-mcp-operator` | `mapf-ui-visualizer` (heatmap Gizmos) | `mapf-metrics-architect` (cost histogram, path-length delta) | `datn-docs-curator` |
| **B — Swept-capsule smoothing** | `mapf-gameplay-engineer` | `unity-mcp-operator` | `mapf-ui-visualizer` (raw vs smoothed line, clearance margin) | `mapf-metrics-architect` (smoothed_segments, min_clearance) | `datn-docs-curator` |
| **C — Tighter rotate-first (4-tier)** | `mapf-gameplay-engineer` | `unity-mcp-operator` | `mapf-ui-visualizer` (trajectory recorder, dot-tier label) | `mapf-metrics-architect` (`rotation_drift_max`, `corner_deviation`) | `datn-docs-curator` |
| **D — Capsule-aware stuck (scuff)** | `mapf-gameplay-engineer` **+** `unity-scene-prefab-wiring` (layer split nếu cần) | `unity-mcp-operator` | `mapf-ui-visualizer` (scuff state HUD, contact tint) | `mapf-metrics-architect` (`scuff_recovery_count`, `scuff_total_duration`) | `datn-docs-curator` |
| **E — Force-center snap** | `mapf-gameplay-engineer` | `unity-mcp-operator` | `mapf-ui-visualizer` (effective tolerance ring) | `mapf-metrics-architect` (`waypoint_snap_radius_avg`) | `datn-docs-curator` |

**Đọc bảng**: mỗi phase đi qua đủ 5 cột. Nếu Verify fail (compile error / scene crash / regression test) ⇒ block Phase tiếp theo cho đến khi Owner fix xong.

---

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

### Sub-agent assignment cho Phase A

**Owner (implementation)**: `mapf-gameplay-engineer`
- Lý do: phase A đụng đúng 2 file core MAPF (`GridNavMask.cs`, `GridAStarPathfinder.cs`) + bootstrap field — đây chính là scope đặc trưng của agent này (algorithm + cost function + A\* relaxation rule). Soft-cost A\* là bài toán pathfinding thuần, không phải scene wiring hay UI.
- Scope: thêm `proximityCost[,]`, `SoftRadius/SoftCostNear/SoftCostMid` properties, `GetCellCost()` API trong `GridNavMask.Rebuild`. Sửa `GridAStarPathfinder.TryFindPath` để cộng `GetCellCost(neighbor)` vào `tentativeScore`. Expose params lên `MapScenarioBootstrap` Inspector và truyền constructor.

**Verification**: `unity-mcp-operator`
- Lý do: cần verify compile pass + scene smoke test (Editor mode → ContextMenu Load Map Now → Spawn Scenario Now). Agent này là tool router cho MCP, đọc `editor_state.isCompiling`, `read_console` để bắt error sau domain reload.
- Output: console log clean, không error/warning từ `GridNavMask` hay `GridAStarPathfinder`. Confirm A\* vẫn ra path khi `SoftRadius=0` (regression check).

**Visualization**: `mapf-ui-visualizer`
- Lý do: Test A.1 yêu cầu Gizmos heatmap cost — đây là debug overlay đặc trưng của agent này (Gizmos, blocked/reserved cell tints, heatmap cost visualization).
- Output: `OnDrawGizmosSelected` trên `MapScenarioBootstrap` (hoặc component riêng `NavMaskGizmos`) vẽ cell theo `GetCellCost`: đỏ đậm cho `SoftCostNear`, đỏ nhạt cho `SoftCostMid`, không vẽ khi `0`. Toggle bật/tắt bằng bool field.

**Metrics**: `mapf-metrics-architect`
- Lý do: Phase A đổi cost function nên path length có thể tăng — cần đo delta để báo cáo thesis. Đây là trách nhiệm "what to measure + how to measure fairly".
- Schema/log cần thêm:
  - `path_length_with_soft_cost` vs `path_length_baseline` (regenerate path với `SoftRadius=0` để so sánh).
  - `cell_cost_histogram` (số cell có cost 0/2/8) để verify rebuild đúng.
  - Log mỗi lần `TryFindPath` xong: `[metrics] path_len={N} avg_cost={C}`.

**Docs**: `datn-docs-curator`
- Cập nhật doc nào sau khi xong phase: `Assets/Docs/mapf_eagle_enemy_astar_plan.md` (thêm note ở Milestone 2 — soft-cost is now baseline cost) và `Assets/Docs/mapf_enemy_stuck_at_obstacle_plan.md` (đánh dấu Phase 1 inflate radius=1 đã được mở rộng thành soft cost).

**Không nên dùng**:
- `unity-scene-prefab-wiring`: Phase A không đụng prefab/scene/layer — chỉ thêm field Inspector trên `MapScenarioBootstrap` mà bootstrap auto-resolve. Không cần wiring agent.
- `unity-mcp-operator` cho implement: agent này KHÔNG sửa C# logic (theo định nghĩa), chỉ verify.

### Phase A — DONE @ 2026-05-10

**File diff summary**:

- `Assets/Scripts/Pathfinding/GridNavMask.cs` — thêm `proximityCost[,]`, `SoftRadius`, `SoftCostNear`, `SoftCostMid`. API mới `GetCellCost(Vector2Int) → int` (trả `int.MaxValue` cho cell hard-block, `0` deep interior, `SoftCostMid` ring Chebyshev=2 từ vùng hard-block, `SoftCostNear` ring Chebyshev=1). Constructor cũ `(map, inflateRadius)` giữ nguyên (backward-compat, soft layer = 0).
- `Assets/Scripts/GridAStarPathfinder.cs` — `tentativeScore = gScore[current] + 1 + navMask.GetCellCost(neighbor)` với null-guard; `int.MaxValue` ⇒ skip neighbor. Manhattan heuristic vẫn admissible.
- `Assets/Scripts/MapScenarioBootstrap.cs` — Inspector fields `agentSoftRadius=2`, `agentSoftCostNear=8`, `agentSoftCostMid=2`, `drawNavMaskHeatmap=true`. `OnDrawGizmosSelected` render heatmap (đỏ alpha 0.6 cho `SoftCostNear`, đỏ alpha 0.3 cho `SoftCostMid`).

**Quyết định thiết kế đáng chú ý**:

Khoảng cách Chebyshev được đo so với **vùng hard-block của agent** (out-of-bounds OR `@` OR cell bị `InflateRadius` loại) thay vì so với cell `@` thuần. Nếu đo theo `@` thuần, với default `InflateRadius=1` thì ring "Chebyshev=1 tới `@`" trùng đúng với vùng inflated nên `SoftCostNear=8` không có cell nào để áp dụng — heatmap đỏ đậm trống và acceptance criterion fail. Đo theo vùng hard-block giữ nguyên ý đồ "tránh cọ tường" của plan và cho heatmap đỏ đậm + đỏ nhạt phân biệt rõ trên scene.

**Smoke-test (verify by `unity-mcp-operator`)**:

- Compile sạch, console không error/warning.
- `MapLoader.LoadAndBuild` + `MapScenarioBootstrap.SpawnScenario` chạy clean trong Edit mode trên `random-32-32-10.map`.
- Histogram cost với `InflateRadius=1, SoftRadius=2, SoftCostNear=8, SoftCostMid=2`: 648 hard-block / 290 cell cost=8 / 81 cell cost=2 / 5 cell cost=0 (trên 1024 cell).
- Regression: gọi `TryFindPath` với `navMask` có `SoftRadius=0` (chỉ inflate) cho kết quả trùng với phiên bản pre-Phase-A — không vỡ path hiện hữu.
- Heatmap Gizmos render đúng 2 tier khi `MapRuntime` được select. Screenshot tham khảo: `Assets/Screenshots/phaseA_heatmap_only.png` (heatmap-only), `phaseA_heatmap_close.png` (heatmap + X markers).

**Còn sót cho Phase B**: Bresenham `HasLineOfSight` vẫn có thể smooth path đi sát edge cluster vì soft cost chỉ áp dụng cho cell A* expand, không cho line smoothing. Phase B sẽ swept-capsule check để fix scuff khi smoothing.

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
- **Test B.2** — Map trống: smoothed path từ `(0,0)` → `(31,31)` phải gộp về 2 waypoint.

### Done khi

- Smoothed path không bao giờ có line đi cách obstacle < 0.4 unit theo hình học.
- Visual: tank theo smoothed path không cọ.

### Sub-agent assignment cho Phase B

**Owner (implementation)**: `mapf-gameplay-engineer`
- Lý do: `HasClearance` là geometric primitive (point-to-segment distance, bbox iteration) thuộc layer pathfinding. Sửa `SmoothPath` trong `GridAStarPathfinder` cũng là core algorithm. Đúng scope agent này.
- Scope: thêm `HasClearance(Vector2 start, Vector2 end, float radius)` vào `GridNavMask.cs`. Sửa `GridAStarPathfinder.SmoothPath` thay `HasLineOfSight` bằng `HasClearance`. Thêm overload `TryFindPath(..., float clearanceRadius)`. Wire `tankClearanceRadius` qua `GridEnemyAgent` → pathfinder.

**Verification**: `unity-mcp-operator`
- Lý do: cần compile + smoke test sau khi thêm overload mới (có thể break call site cũ). Cũng cần regenerate path trong scene để confirm Test B.2 (map trống gộp 2 waypoint).
- Output: console clean, scene replay confirm path map trống vẫn smooth tốt, không regression.

**Visualization**: `mapf-ui-visualizer`
- Lý do: cần overlay so sánh **raw A\* path** vs **smoothed path** để debug visually — agent này owns path lines, waypoint markers.
- Output: 2 line series Gizmos: yellow cho raw path (`pathBeforeSmooth`), green cho smoothed path. Khi reject gộp do clearance fail, vẽ red dashed segment giữa pair candidate. Toggle bật/tắt.

**Metrics**: `mapf-metrics-architect`
- Lý do: cần đo hiệu quả smoothing (số segment giảm) và safety (min clearance) để báo cáo. Thuộc scope metrics architect.
- Schema/log cần thêm:
  - `smoothed_segments` (số waypoint sau smooth) vs `raw_segments` (trước smooth) → ratio reduction.
  - `min_clearance_along_path` (min distance từ smoothed line tới `@` gần nhất) — verify ≥ `tankClearanceRadius`.
  - Log: `[metrics] smooth_ratio={X.XX} min_clear={Y.YY}`.

**Docs**: `datn-docs-curator`
- Cập nhật `mapf_eagle_enemy_astar_plan.md` Milestone 2 (đoạn smoothing): note thay Bresenham bằng analytical capsule sweep, ghi WHY (Bresenham center-only miss swept-volume).

**Không nên dùng**:
- `unity-scene-prefab-wiring`: không đụng prefab/scene.
- `unity-mcp-operator` cho implement: chỉ verify, không sửa logic.

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

### Sub-agent assignment cho Phase C

**Owner (implementation)**: `mapf-gameplay-engineer`
- Lý do: chỉ sửa logic trong `GridEnemyAgent.FollowPath` (4-tier rotate + cycling drive accumulator) — đây là controller logic agent này owns. Quan trọng: chọn approach **cycling** thay vì sửa `TankMover` để KHÔNG ảnh hưởng player input pipeline (theo CLAUDE.md: "any control logic added must work for both human and AI input"). Cycling chỉ tác động AI side.
- Scope: thêm `partialDriveAccumulator`, `partialDrivePeriod`, `dotMostlyAlignedThreshold`, `dotPartialThreshold` vào `GridEnemyAgent`. Refactor block `if/else if` trong `FollowPath` thành 4-tier. KHÔNG đụng `TankMover.cs`.

**Verification**: `unity-mcp-operator`
- Lý do: Test C.1 cần record `transform.position` qua thời gian — agent này có thể chạy `execute_code` để dump trajectory hoặc capture screenshot khi enemy rẽ 90°. Cũng verify FPS không drop (Test C.2).
- Output: trajectory CSV hoặc screenshot trước/sau corner. Console log clean.

**Visualization**: `mapf-ui-visualizer`
- Lý do: cần record + vẽ trajectory enemy để so deviation với đường cell-to-cell. Cũng cần label dot-tier hiện tại lên enemy (debug).
- Output:
  - LineRenderer/Gizmos buffer N=100 vị trí gần nhất của enemy → vẽ trail.
  - Text overlay trên enemy: `[ALIGNED|MOSTLY|PARTIAL|ROTATE]` theo dot tier hiện tại.

**Metrics**: `mapf-metrics-architect`
- Lý do: cần đo `rotation_drift_max` và `corner_deviation` để chứng minh Phase C có effect. Thuộc thesis evaluation.
- Schema/log cần thêm:
  - `corner_deviation_max` per corner waypoint (max distance từ tank position tới đường gấp khúc cell-to-cell).
  - `rotation_drift_max` (max angular error trong khi tier=PARTIAL).
  - `tier_time_distribution` (% thời gian ở mỗi tier).

**Docs**: `datn-docs-curator`
- Cập nhật `mapf_enemy_stuck_at_obstacle_plan.md` Phase 2 (rotate-first): note đã upgrade từ 3-tier thành 4-tier với cycling drive. Ghi WHY: tránh sửa `TankMover` để không vỡ player input contract.

**Không nên dùng**:
- `unity-scene-prefab-wiring`: không đụng prefab.
- `mapf-gameplay-engineer` cho `TankMover.cs`: rule "không đụng TankMover" trong Phase C đã được chốt — agent phải tránh.

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

### Sub-agent assignment cho Phase D (PHỨC TẠP — 2 OWNER)

**Owner chính (implementation logic)**: `mapf-gameplay-engineer`
- Lý do: scuff detection là controller logic trong `GridEnemyAgent.UpdateProgressTracking` — đúng scope agent này (`Rigidbody2D.IsTouchingLayers`, recovery escalation reuse).
- Scope: thêm field `obstacleContactMask`, `scuffTimeout`, `scuffStartTime`, `wasTouchingLastFrame` vào `GridEnemyAgent`. Implement `IsScuffing()` + branch trong `UpdateProgressTracking`. Hook recovery vào pipeline existing (`ForcedReplan` → `Reverse` → `ExpandedMask`).

**Owner phụ (layer split — BẮT BUỘC trước khi Milestone 3)**: `unity-scene-prefab-wiring`
- Lý do: theo phân tích v2 (cuối "Liên hệ Milestone tiếp theo"): khi sang multi-agent, tank cọ tank (PlayerBlocker) cũng trigger `IsTouchingLayers(ObstaclesMovement)` vì PlayerBlocker đang dùng cùng layer. Cần TÁCH layer riêng `Walls` (chỉ wall thật) và `AgentBlocker` (per-tank blocker) để Phase D không nhầm. Layer split phải làm ở `MapScenarioBootstrap.AddPlayerBlocker` + `MapLoader.AddObstacleColliders` + Project Settings → Layers — đây là scope của wiring agent.
- Scope: thêm 2 layer mới `Walls` và `AgentBlocker` trong Tags & Layers; sửa `MapLoader` đặt obstacle layer = `Walls`; sửa `MapScenarioBootstrap.AddPlayerBlocker` đặt blocker layer = `AgentBlocker`; cập nhật `obstacleContactMask` default trên `GridEnemyAgent` chỉ chứa `Walls`. KHÔNG sửa logic trong `GridEnemyAgent.IsScuffing` — đó là việc của Owner chính.
- **Phối hợp**: layer split phải xong **TRƯỚC** khi mapf-gameplay-engineer wire `obstacleContactMask` default, nếu không tank cọ tank sẽ trigger scuff giả ngay khi single-agent gần player. Nếu tạm thời chỉ test single-enemy, có thể dùng workaround `obstacleContactMask = ObstaclesMovement` rồi đánh dấu TODO chuyển sang `Walls` khi Milestone 3 bắt đầu.

**Verification**: `unity-mcp-operator`
- Lý do: cần verify cả compile (sau layer enum đổi) và scene smoke test (Test D.1 + D.2). Đặc biệt Test D.2 yêu cầu spawn enemy có LOS với Eagle, body cọ collider — cần MCP để inject scenario.
- Output: console log có `[GridEnemyAgent] scuff recovery triggered after Xs` khi cọ > 0.4s; KHÔNG có log đó khi enemy đang shooting Eagle.

**Visualization**: `mapf-ui-visualizer`
- Lý do: scuff state khó debug nếu không có overlay — agent này owns "agent state label".
- Output:
  - Text overlay enemy: `[scuff: 0.32s/0.4s]` countdown khi `wasTouchingLastFrame=true`.
  - Body tint đỏ khi đang scuff, vàng khi vừa exit, default trắng.

**Metrics**: `mapf-metrics-architect`
- Lý do: scuff event là tín hiệu chất lượng pathfinding/control mà thesis cần báo cáo. Nếu Phase A/B làm tốt thì scuff_recovery_count phải giảm xuống gần 0.
- Schema/log cần thêm:
  - `scuff_recovery_count` (cumulative per session).
  - `scuff_total_duration` (tổng thời gian cọ).
  - `scuff_recovery_outcome` (sau recovery có thoát được không, sau bao lâu).
  - Log per event: `[metrics] scuff_event ts={T} duration={D} recovery_action={replan|reverse|expanded}`.

**Docs**: `datn-docs-curator`
- Cập nhật:
  - `mapf_enemy_stuck_at_obstacle_plan.md`: thêm symptom mới "scuff" (đã định nghĩa trong v2/v3) vào bảng triệu chứng.
  - `mapf_eagle_enemy_astar_plan.md` Milestone 2/3: ghi WHY phải tách layer `Walls` vs `AgentBlocker` trước Milestone 3 (multi-agent), trích quote phân tích trong plan v3.
  - DEMO_CHECKLIST.md (nếu có): thêm bước verify scuff recovery log xuất hiện đúng khi enemy bị force vào khe hẹp.

**Không nên dùng**:
- `unity-mcp-operator` cho implement: chỉ verify.
- Chỉ một mình `mapf-gameplay-engineer`: nếu agent này tự ý sửa layer trong Project Settings hoặc đụng `MapLoader.AddObstacleColliders` thì vi phạm scope (file đó là wiring/scene setup). Phải dispatch song song với `unity-scene-prefab-wiring`.

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

### Sub-agent assignment cho Phase E

**Owner (implementation)**: `mapf-gameplay-engineer`
- Lý do: chỉ sửa `GridEnemyAgent.GetWaypointReachDistance` để query `navMask.GetCellCost` — đụng đúng controller logic và API đã được Phase A thêm. Đây là phase đơn giản nhất.
- Scope: refactor `GetWaypointReachDistance` (hoặc inline tại call site) thành dynamic theo `proximityCost`. Phụ thuộc Phase A đã xong (cần `GetCellCost`).

**Verification**: `unity-mcp-operator`
- Lý do: smoke test sau khi tighten tolerance — risk: tank có thể stall ở cell sát cluster do tolerance quá chặt + drift thực tế lớn hơn. Cần verify enemy vẫn reach Eagle.
- Output: replay scene 60s, confirm enemy không stall, console clean.

**Visualization**: `mapf-ui-visualizer`
- Lý do: cần overlay vòng tròn tolerance hiện tại quanh enemy để debug — agent này owns waypoint markers.
- Output: Gizmos vòng tròn radius=`GetWaypointReachDistance()` quanh enemy, đổi màu theo tier (cyan = base, yellow = mid, red = near).

**Metrics**: `mapf-metrics-architect`
- Lý do: Phase E claim "drift < 0.1 unit" — phải đo để chứng minh.
- Schema/log cần thêm:
  - `waypoint_snap_radius_avg` (avg tolerance hiệu lực qua thời gian).
  - `cell_center_deviation_avg` (avg distance từ tank position tới center của cell hiện tại, khi cell có cost > 0).

**Docs**: `datn-docs-curator`
- Cập nhật `mapf_enemy_stuck_phase2_plan_v3.md` (chính file này) với note "Phase E completed at <date>" hoặc "Skipped — A+B+D đã đủ".

**Không nên dùng**:
- `unity-scene-prefab-wiring`: không đụng prefab/scene.
- Bất kỳ agent nào trước khi Phase A done: Phase E phụ thuộc cứng vào API `GetCellCost` của Phase A. Nếu A chưa xong, tuyệt đối không dispatch E.

## Triển khai cụ thể (file/method)

| File | Thay đổi |
|---|---|
| `Assets/Scripts/Pathfinding/GridNavMask.cs` | Thêm `proximityCost[,]`, `GetCellCost()`, `HasClearance()` (Phase A + B). Rebuild scan thêm SoftRadius. |
| `Assets/Scripts/GridAStarPathfinder.cs` | A\* dùng `gScore + 1 + GetCellCost(neighbor)` thay vì chỉ `+1`. `SmoothPath` thay `HasLineOfSight` bằng `navMask.HasClearance(world, world, radius)`. Thêm overload `TryFindPath(..., float clearanceRadius)`. |
| `Assets/Scripts/GridEnemyAgent.cs` | Phase C: 4-tier rotate với cycling partial drive. Phase D: `IsTouchingLayers` scuff detection + `scuffTimeout`. Phase E: tighten `GetWaypointReachDistance` theo proximity cost. Field mới: `obstacleContactMask`, `scuffTimeout`, `tankClearanceRadius`, `partialDrivePeriod`. |
| `Assets/Scripts/MapScenarioBootstrap.cs` | Expose `softRadius`, `softCostNear`, `softCostMid`, `tankClearanceRadius`, `scuffTimeout` lên Inspector. Wire vào `GridEnemyAgent`. Khi tạo `GridNavMask` truyền params mới. |
| `Assets/Scripts/TankMover.cs` | KHÔNG đụng (nếu chọn approach cycling thay vì partial-magnitude). |
| `Assets/Scripts/MapLoader.cs` | KHÔNG đụng (Phase A/B/C/E). Có thể đụng Phase D nếu split layer `Walls` vs `ObstaclesMovement` — owner: `unity-scene-prefab-wiring`. |
| `ProjectSettings/TagManager.asset` | Phase D layer split (chỉ owner `unity-scene-prefab-wiring`). |

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

---

## Cross-phase coordination (v3 mới)

### Thứ tự bắt buộc (dependency DAG)

```
Phase A (soft cost API)
   ├─→ Phase B (smoothing dùng HasClearance — cùng file GridNavMask, có thể song song nếu chia method khác nhau, nhưng SAFER là sequential)
   ├─→ Phase E (waypoint snap dùng GetCellCost — phụ thuộc cứng API Phase A)
   └─→ (independent) Phase C, Phase D không phụ thuộc Phase A về API
                          │
Phase D (scuff detection)
   ├─ depends: layer split (sub-task của unity-scene-prefab-wiring) HOẶC dùng workaround `ObstaclesMovement`
   └─→ độc lập với A/B/C/E về file
```

**Quy tắc dispatch**:

1. **Phase A trước hết** — vì A mở rộng API `GridNavMask` mà B/E sẽ consume. Nếu chạy song song, risk merge conflict trên `GridNavMask.cs`.
2. **Phase B sau Phase A** — cùng file `GridNavMask.cs` (thêm `HasClearance`) và cùng file `GridAStarPathfinder.cs` (sửa `SmoothPath`). Phải SERIALIZE.
3. **Phase C độc lập** — chỉ đụng `GridEnemyAgent.cs`. Có thể chạy song song A/B nếu chia file khác nhau, NHƯNG vì Phase D cũng đụng `GridEnemyAgent.cs` (thêm field scuff), cần serialize C ↔ D.
4. **Phase D sau Phase C** — tránh conflict trên `GridEnemyAgent.cs` (cả 2 đều thêm field + sửa `UpdateProgressTracking`/`FollowPath`).
5. **Phase E cuối cùng** — phụ thuộc Phase A (`GetCellCost`) và đụng `GridEnemyAgent.cs` (sau D).

**Thứ tự đề xuất (theo cost/benefit + DAG)**:

```
A → B → D (commit 1: A+B; commit 2: D)
  → C (chỉ nếu sau A+B+D vẫn còn drift)
  → E (chỉ khi cần polish)
```

### Conflict file (cần serialize)

| File | Phase đụng | Quy tắc |
|---|---|---|
| `GridNavMask.cs` | A, B | Serialize A trước B. Cùng owner `mapf-gameplay-engineer` ⇒ 1 PR/branch là OK, nhưng commit riêng cho rollback dễ. |
| `GridAStarPathfinder.cs` | A (cost in Dijkstra), B (`SmoothPath`) | Serialize. Cùng lý do. |
| `GridEnemyAgent.cs` | C (4-tier), D (scuff), E (snap) | Serialize C → D → E. KHÔNG dispatch song song. |
| `MapScenarioBootstrap.cs` | A (softRadius), B (tankClearanceRadius), D (obstacleContactMask, scuffTimeout) | Mỗi phase chỉ thêm field — risk conflict thấp. Vẫn nên serialize để code review sạch. |
| `MapLoader.cs` | (chỉ Phase D nếu layer split) | Owner = `unity-scene-prefab-wiring`. Phải xong trước khi D wire `obstacleContactMask` mặc định. |
| `ProjectSettings/TagManager.asset` | (chỉ Phase D nếu layer split) | Single owner `unity-scene-prefab-wiring`. Layer enum đổi ⇒ recompile toàn project. |

### Khi nào call `unity-mcp-operator`

`unity-mcp-operator` là MCP tool router — KHÔNG sửa code. Dispatch agent này khi:

1. **Sau mỗi phase implement xong** — chạy `read_console` + `editor_state.isCompiling` để confirm compile pass. Đây là gate bắt buộc trước khi dispatch phase tiếp theo.
2. **Trước khi Owner phase tiếp theo bắt đầu** — đọc `editor_state` để xác nhận domain reload xong, không có pending compile error.
3. **Trước scene smoke test** — `manage_scene` để load `MapF_TankTest.unity`, `manage_editor` enter Play mode, capture console log, screenshot key moment.
4. **Khi cần refresh Unity** sau khi `unity-scene-prefab-wiring` thêm layer mới — gọi `refresh_unity` để Unity reload `TagManager.asset`.
5. **Khi Owner cần inject scenario test** (Test D.1, D.2, C.1) — dùng `manage_gameobject` / `execute_code` để spawn enemy ở cell cụ thể, force LOS, record trajectory.
6. **Cuối mỗi commit** — chạy `run_tests` (nếu có test assembly) hoặc smoke test scene 30-60s, screenshot kết quả cho `datn-docs-curator`.

**Không gọi `unity-mcp-operator` khi**:
- Chỉ để đọc file C# tĩnh (dùng `Read` tool trực tiếp).
- Khi Owner đang viết logic — chỉ gọi sau khi Owner báo "compile-ready".
- Trong vòng lặp poll `isCompiling` lặp lại — chỉ poll 1-2 lần với delay hợp lý.

### Khi nào call `unity-scene-prefab-wiring`

Chỉ dispatch riêng cho:
- **Phase D layer split** (bắt buộc trước Milestone 3, có thể defer trong single-agent test).
- Bất kỳ thay đổi nào tới prefab `Tank.prefab`, layer matrix, sorting layer, collider layout (Phase A/B/C/E **không** có).

### Khi nào call `mapf-metrics-architect`

Mỗi phase đều có metrics cần — nhưng nên dispatch theo **batch** sau khi 2-3 phase implement xong:
- **Batch 1 (sau A + B)**: thêm `path_length_with_soft_cost`, `cell_cost_histogram`, `min_clearance_along_path`, `smoothed_segments`. Cùng schema CSV/JSON.
- **Batch 2 (sau D)**: thêm `scuff_recovery_count`, `scuff_total_duration`, `scuff_recovery_outcome`.
- **Batch 3 (sau C + E nếu làm)**: thêm trajectory metrics (`corner_deviation_max`, `tier_time_distribution`, `cell_center_deviation_avg`).

Tách batch để tránh thay đổi schema CSV nhiều lần.

### Khi nào call `datn-docs-curator`

Sau **mỗi** phase done (compile pass + verify pass). Doc updates không gộp batch vì mỗi phase có WHY riêng cần ghi ngay khi context còn fresh:
- Phase A done → update plan v3 (mark Phase A complete) + Milestone 2 doc.
- Phase B done → update Milestone 2 doc (smoothing → analytical).
- Phase C done → update plan stuck doc (rotate-first 3-tier → 4-tier).
- Phase D done → update plan stuck doc (thêm symptom scuff) + Milestone 3 doc (layer split note).
- Phase E done → update plan v3 (mark Phase E complete hoặc skipped).

---

## Tóm tắt phase phức tạp vs straightforward

- **Phase phức tạp**: **Phase D** — vì cần 2 owner (`mapf-gameplay-engineer` cho logic + `unity-scene-prefab-wiring` cho layer split `Walls` / `AgentBlocker`). Cũng có dependency mềm: nếu chỉ test single-agent thì có thể dùng workaround `ObstaclesMovement`, nhưng để lên multi-agent (Milestone 3) thì layer split bắt buộc xong trước.
- **Phase trung bình**: **Phase A** — chỉ 1 owner nhưng cần wire param qua nhiều file (`GridNavMask`, `GridAStarPathfinder`, `MapScenarioBootstrap`, `GridEnemyAgent` indirect). Visualization (heatmap Gizmos) là tính năng mới của `mapf-ui-visualizer` cần thiết kế.
- **Phase straightforward**:
  - **Phase B** — geometric primitive thuần, 1 owner, chỉ đụng pathfinding.
  - **Phase C** — chỉ trong `GridEnemyAgent.FollowPath`, đã chốt approach (cycling, không sửa TankMover).
  - **Phase E** — đơn giản nhất, chỉ refactor `GetWaypointReachDistance`, phụ thuộc Phase A đã xong.
