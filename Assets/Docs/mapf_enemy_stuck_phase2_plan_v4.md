# Plan v4 — Bỏ smoothing + Hard physical inflate

> **Phiên bản v4** — kế thừa context từ v2/v3, NHƯNG **override một phần kết luận của v3** dựa trên feedback từ Play Mode test sau khi implement Phase A + B.
>
> **Status v3**: Phase A + B đã implement xong (verified runtime: navMask wired, clearance=0.4 active). Tuy nhiên tăng số cluster từ 1 lên 3 thì enemy **càng dumb hơn trước** — A\* vẫn tạo path qua khe mà tank vật lý không lọt, smoothing thêm vào càng gây bug khó debug.
>
> **Quyết định v4**: thay vì tinh chỉnh tiếp Phase C/D/E như v3 dự kiến, **đơn giản hóa drastically**:
> 1. **Bỏ path smoothing** (path raw 4-neighbor cell-to-cell, deterministic).
> 2. **Hard-block cell dựa trên `Physics2D.OverlapCircle`** thay vì chỉ Chebyshev cell-grid logic.
>
> Plan v3 (`mapf_enemy_stuck_phase2_plan_v3.md`) Phase C/D/E **defer/skip** cho đến khi v4 ổn — có thể không cần nữa nếu hard inflate giải quyết triệt để.

---

## 1. Triệu chứng mới (sau khi A + B implement)

User test với scene `MapF_TankTest` đã tăng số cluster barrier từ 1 lên 3 (densely placed):

- Enemy **vẫn cọ cluster** ngay cả khi A\* đã có soft cost +8 và smoothing đã có swept-capsule check.
- Một số path A\* vẫn đi xuyên khe giữa 2 cluster mà tank capsule (`0.696`) không lọt vào cell có clearance lý thuyết `0.65` (sau inflate=1).
- Smoothing đôi khi tạo diagonal cut "trông khôn" nhưng thực tế đi sát edge cluster → tank scuff khi đi theo.
- **Tổng kết**: enemy `dumb hon truoc` — visual chất lượng tệ hơn cả pre-Phase-A.

## 2. Re-diagnosis: tại sao Phase A + B không đủ

### 2.1. Soft cost không phải hard block

Phase A: cell Chebyshev=1 với hard-block region có cost `+8`. A\* vẫn chọn cell đó nếu alternative path dài hơn `8` step. Với 3 cluster gần nhau:

- Gap-through path: `~12 cells`, qua 2 cell có cost +8 = `12 + 16 = 28`.
- Around path: `~22 cells`, không cell nào có cost extra = `22`.
- **A\* chọn around** ✓ — nhưng nếu cluster nằm ở vị trí khác mà around path = `35 cells`:
  - Gap-through = `28`, around = `35` → **A\* vẫn chọn gap-through**.
  - Tank cọ.

→ Soft cost **không guarantee** tank không cọ. Chỉ shift preference. Với map đông cluster, gap-through có thể vẫn rẻ hơn around.

### 2.2. Cell walkable theo Chebyshev ≠ cell tank lọt vào

Inflate radius `1` mark cell non-walkable nếu **bất kỳ ô 8-neighbor** là `@`. Nhưng:

- Cell `(x, y)` walkable theo navMask khi 8-neighbor không có `@`.
- Tank capsule width `0.696`. Khi tank ở center cell `(x, y)`, capsule trải `±0.348` mỗi chiều.
- Nếu `(x-1, y-1)` là `@` (cluster góc) thì collider `@` extend tới `(x-0.5, y-0.5)`. Capsule edge tại `(x-0.348, y-0.348)`.
- Khoảng cách từ capsule edge tới collider edge = `0.5 - 0.348 = 0.152` unit.
- Lý thuyết không cọ — nhưng:
  - Rotation drift (tank xoay khi đi) làm capsule lệch.
  - Cluster 2x2 hoặc 3-cluster: nhiều `@` adjacent → chạm nhau ngay góc.
  - Inflate Chebyshev không tính `@` ở Chebyshev=2 hoặc xa hơn nhưng có thể vẫn chạm capsule khi tank di chuyển ngang qua.

→ Chebyshev cell-grid **không equivalent** với physical capsule check. Tank vẫn cọ ngay cả khi cell walkable.

### 2.3. Smoothing tạo thêm bug khó debug

Phase B `HasClearance` check khoảng cách analytical từ obstacle cell center tới line. Nhưng:

- Chỉ check **obstacle cells theo `IsWalkable`**, không check `IsAgentWalkable` (inflate cells).
- BBox iteration có thể miss cells gần edge nếu margin không đủ.
- Khi smoothing approve một diagonal collapse, tank đi quỹ đạo cong (vì `TankMover` accel + rotation), thực tế lệch khỏi line analytical → cọ ngay cả khi `HasClearance` returns true.

→ Smoothing thêm complexity mà không guarantee no-scuff. **Bỏ smoothing** giảm variable, giúp baseline deterministic.

### 2.4. Tại sao "3 cluster dumb hơn 1 cluster"

Với 1 cluster:
- A\* dễ tìm around path ngắn (cluster nhỏ).
- Soft cost nudge enough.

Với 3 cluster densely placed:
- Around path qua 3 cluster dài hơn nhiều.
- A\* tìm gap-through ngắn hơn → cọ.
- Mỗi cluster thêm vào = thêm cơ hội cọ.

→ Vấn đề scale theo số cluster. Plan A+B chỉ work cho map sparse.

## 3. Mục tiêu v4

Đơn giản hóa baseline:

1. **Determinism**: tank chỉ đi cell-to-cell theo path A\* raw. Không có pha smoothing nào tạo unexpected geometry.
2. **Physical safety**: nếu tank capsule không lọt vào cell, **navMask hard-block** cell đó. A\* không có cách tạo path qua cell không lọt.
3. **Đơn giản hơn v3**: bỏ Phase C/D/E nếu hard inflate đủ. Phase D (scuff detection) giữ làm safety net optional.

Sau v4, **single-enemy baseline phải production-ready** cho Milestone 3.

## 4. Phase v4.1 — Bỏ smoothing

### Vấn đề

Smoothing (Bresenham + swept capsule) làm tăng variable, tạo diagonal quỹ đạo cong gây cọ. Path raw 4-neighbor là **monotonic cell-to-cell** — tank đi thẳng giữa 2 cell tâm, không cong, không cắt góc.

### Thiết kế

#### Lựa chọn v4.1.A — Disable smoothing toàn bộ (Recommended)

Trong `GridAStarPathfinder.TryFindPath`:

```csharp
// Cũ:
if (path.Count > 2)
{
    SmoothPath(mapLoader, navMask, blocked, path, clearanceRadius);
}

// Mới:
// Smoothing disabled in v4 — path raw 4-neighbor for deterministic follow.
// SmoothPath kept in source as fallback for ablation studies.
```

Path raw từ A\* là chuỗi cell adjacent (4-neighbor), tank đi cell-by-cell theo `currentPath`.

**Ưu điểm**:
- Deterministic. Tank không bao giờ rẽ giữa 2 cell.
- Path luôn nằm ở cell center → no swept-volume issue.
- Code đơn giản. Bỏ ~50 lines `SmoothPath` + `HasLineOfSight` + `HasClearance` (giữ trong source nhưng không gọi).

**Nhược điểm**:
- Quỹ đạo nhiều rẽ 90° hơn (zigzag). Visual không "smooth" như trước.
- Số waypoint tăng (mỗi cell = 1 waypoint).

#### Lựa chọn v4.1.B — Toggle (Alternative)

Thêm `[SerializeField] bool enableSmoothing = false` vào `MapScenarioBootstrap`. Pass qua `GridEnemyAgent` → `TryFindPath` overload.

**Ưu điểm**: dễ ablation study cho thesis (so sánh smooth on/off).

**Nhược điểm**: thêm 1 param phải wire qua chain.

⇒ **Chọn v4.1.B** vì thesis cần ablation. Default `false`.

### Triển khai

| File | Thay đổi |
|---|---|
| `GridAStarPathfinder.cs` | Thêm overload `TryFindPath(..., bool enableSmoothing)`; gọi `SmoothPath` chỉ khi `enableSmoothing == true`. Default callers truyền `false`. Giữ `SmoothPath`, `HasLineOfSight`, `HasClearance` private methods (không xóa). |
| `GridEnemyAgent.cs` | Thêm field `[SerializeField] public bool enableSmoothing = false;`. Pass vào `TryFindPath` ở mọi call site. |
| `MapScenarioBootstrap.cs` | Inspector field `[SerializeField] bool agentEnableSmoothing = false;`. Set vào `agent.enableSmoothing` trong `AddGridEnemyAgent`. |

### Done khi

- Path A\* raw (no smoothing) hiển thị Gizmos là chuỗi line cell-to-cell, không có diagonal cut.
- Tank đi từng cell, không cong qua 2 cell.

## 5. Phase v4.2 — Physical-collision hard inflate

### Vấn đề

Chebyshev=1 inflate cho clearance lý thuyết `0.652`, nhưng tank capsule rotation drift + multi-cluster scenarios khiến tank cọ ngay cả khi cell walkable theo navMask.

Cần check **physical** xem tank capsule có overlap obstacle collider khi đứng tâm cell không.

### Thiết kế

#### Lựa chọn v4.2.A — `Physics2D.OverlapCircle` (Recommended cho radial check)

Dùng `Physics2D.OverlapCircle(cellCenterWorld, tankCheckRadius, obstacleLayerMask)`:

- `tankCheckRadius = tankHalfExtent + safetyMargin = 0.348 + 0.1 = 0.45` (default).
- `obstacleLayerMask = ObstaclesMovement` (layer của wall colliders).
- Nếu return non-null → cell **không lọt tank** → `agentWalkable = false` (HARD block).

Code trong `GridNavMask.RebuildPhysical()`:

```csharp
public void RebuildPhysical(float tankCheckRadius, LayerMask obstacleMask)
{
    for (int x = 0; x < width; x++)
    for (int y = 0; y < height; y++)
    {
        Vector2Int cell = new Vector2Int(x, y);
        if (!mapLoader.IsWalkable(cell))
        {
            agentWalkable[x, y] = false;
            continue;
        }
        Vector2 cellCenter = mapLoader.CellToWorld(cell);
        Collider2D hit = Physics2D.OverlapCircle(cellCenter, tankCheckRadius, obstacleMask);
        agentWalkable[x, y] = (hit == null);
    }
    BuildProximityCost(); // Phase A soft cost on top
}
```

**Ưu điểm**:
- **Chính xác physical**: cell chỉ walkable khi tank thật sự lọt.
- Hoạt động trong Edit Mode (Physics2D query không cần Play Mode).
- Tự động handle mọi shape obstacle (không chỉ `@` cells, kể cả custom collider).

**Nhược điểm**:
- 1024 calls `OverlapCircle` ở init — nhanh (`<10ms` total trên 32x32).
- Yêu cầu obstacle colliders đã spawn TRƯỚC khi `Rebuild` chạy. `MapLoader.LoadAndBuild` build collider trước `MapScenarioBootstrap.SpawnScenario` → OK.

#### Lựa chọn v4.2.B — `Physics2D.OverlapBox` (capsule-shaped)

Dùng `OverlapBox(cellCenter, capsuleSize, 0f, mask)`. Tương tự OverlapCircle nhưng box thay vì circle. Vì tank capsule có 1 chiều dài hơn (0.696) và 1 chiều ngắn (0.597), box conservative hơn circle.

**Ưu điểm**: chính xác hơn cho capsule asymmetric.

**Nhược điểm**: phụ thuộc rotation tank — tank xoay 90° thì box rotate. Ở rebuild time không biết tank rotation tương lai → dùng max dimension `0.696`.

⇒ **Chọn v4.2.A** với `tankCheckRadius = max(width, height)/2 + margin = 0.348 + 0.1 = 0.45`. Conservative đủ cho mọi rotation.

### Tham số mặc định

| Param | Default | Lý do |
|---|---|---|
| `tankPhysicalRadius` | `0.45f` | tank half-extent `0.348` + safety margin `0.1` |
| `obstacleLayerMask` | `ObstaclesMovement` | layer của wall colliders trong project |

Expose lên `MapScenarioBootstrap` Inspector.

### Tích hợp với Phase A soft cost

**Quan trọng**: physical hard inflate **thay thế** Chebyshev hard inflate, NHƯNG **giữ Phase A soft cost** layer. Lý do:

- Hard inflate (physical): cell tank không lọt → block.
- Soft cost (Phase A): cell tank lọt nhưng gần wall → prefer alternative.
- Cộng hưởng: A\* không bao giờ tạo path không lọt được, VÀ ưu tiên path xa wall khi có lựa chọn.

`GridNavMask.Rebuild` mới:

1. Build `agentWalkable` qua `Physics2D.OverlapCircle` (thay Chebyshev inflate cũ).
2. Build `proximityCost` qua Chebyshev distance từ vùng hard-block (như Phase A).
3. `IsAgentWalkable(cell)` return `agentWalkable[x,y]`.
4. `GetCellCost(cell)` return `proximityCost[x,y]`.

### Triển khai

| File | Thay đổi |
|---|---|
| `GridNavMask.cs` | Constructor mới `(MapLoader, float physicalRadius, LayerMask obstacleMask, int softRadius, int softCostNear, int softCostMid)`. `Rebuild` dùng `Physics2D.OverlapCircle` cho `agentWalkable`. Constructor cũ Chebyshev giữ làm fallback (deprecated, mark `[Obsolete]` optional). |
| `MapScenarioBootstrap.cs` | Inspector fields `tankPhysicalRadius=0.45f`, `obstacleLayerMask` (default `ObstaclesMovement`). Replace constructor call. Expose `agentInflateRadius` → deprecated/remove (có thể giữ làm Chebyshev fallback). |
| `GridAStarPathfinder.cs` | KHÔNG đụng (cost integration đã có từ Phase A). |
| `GridEnemyAgent.cs` | KHÔNG đụng (vẫn dùng `navMask.IsAgentWalkable` + `navMask.GetCellCost`). |

### Done khi

- Path A\* không bao giờ đi qua cell mà `Physics2D.OverlapCircle(cellCenter, 0.45)` overlap obstacle.
- Tank đứng tâm bất kỳ cell walkable nào không touch collider (`Rigidbody2D.IsTouchingLayers(obstacleMask) == false` ở rest).
- Visual: tank không cọ trong scene 3-cluster trong 30s observation.

## 6. Trade-offs

### Ưu điểm v4

- **Determinism**: path đơn giản, dễ debug. Quan trọng cho thesis.
- **Physical safety**: tank guaranteed lọt vào mỗi cell trên path.
- **Simplicity**: bỏ smoothing layer = ít bug hơn.
- **Compatible với LNS2**: discrete cell path là input chuẩn của LNS2 (Milestone 4).

### Nhược điểm v4

- **Path zigzag hơn**: nhiều rẽ 90°. Visual không "natural" như smoothed path.
  - Mitigation: nếu thực sự khó chịu, ablation study trong thesis ("with/without smoothing").
- **Một số map có thể isolated**: với `tankPhysicalRadius=0.45`, hành lang 1-cell (clearance `1.0 - 0.9 = 0.1`) hoàn toàn block. Map cần thiết kế lại nếu có hành lang hẹp.
  - Mitigation: expose `tankPhysicalRadius` lên Inspector — nếu map quá hẹp, giảm xuống `0.4` (margin `0.05`). Nếu vẫn isolated, map design issue, không phải v4 issue.
- **Computational cost ở init**: `OverlapCircle` 1024 lần. Trên 32x32 ~5-10ms. Không phải hot path. OK.
- **Physics2D dependency**: `Rebuild` cần obstacle colliders đã spawn. Đảm bảo thứ tự `MapLoader.LoadAndBuild` → `MapScenarioBootstrap.SpawnScenario`.

## 7. Test plan

### Test v4.1 — Path raw không smooth

1. Bật scene `MapF_TankTest`, observe Gizmos path.
2. Verify path là chuỗi line cell-to-cell, không có diagonal cut nào dài hơn 1 cell.
3. Capture screenshot `Assets/Screenshots/v4_path_raw.png`.

### Test v4.2 — Hard inflate physical

1. Build scenario có 3 cluster densely placed (giống user feedback).
2. Bật scene → check heatmap Gizmos: cell tank không lọt phải có X marker (hoặc tint đậm hơn cell soft).
3. Verify A\* không tạo path qua cell hard-block. Spawn enemy ở vị trí nếu chỉ có 1 path khả dĩ qua khe → A\* return no-path nếu khe không lọt.
4. Visual run scene 30s: tank không cọ cluster.

### Test v4.3 — Regression: open map

1. Map `random-32-32-10.map` không có cluster lớn (sparse 10%).
2. Verify enemy vẫn reach Eagle bình thường, FPS không drop, không có log error.

### Test v4.4 — Path isolation edge case

1. Set `tankPhysicalRadius = 0.5` (tight).
2. Spawn enemy ở góc map có hành lang 1-cell.
3. Verify A\* return no-path log + agent enter "no-path" state (recovery hoặc idle).
4. Restore `tankPhysicalRadius = 0.45`.

### Test v4.5 — 3-cluster scenario (regression user feedback)

1. Tạo scenario 3 cluster densely placed như user mô tả.
2. Spawn enemy trên 1 phía map, Eagle phía kia.
3. Verify enemy đi vòng (around path), không đi qua khe giữa cluster.
4. Capture screenshot `Assets/Screenshots/v4_3cluster_around.png`.

### Done khi

5/5 tests pass. Console clean. Visual không cọ trong all 5 scenarios.

## 8. Sub-agent assignment

### Activation matrix v4

| Phase | Owner (impl) | Verify | Visualization | Metrics | Docs |
|---|---|---|---|---|---|
| **v4.1 — Disable smoothing** | `mapf-gameplay-engineer` | `unity-mcp-operator` | `mapf-ui-visualizer` (path Gizmos confirm raw) | (defer) | `datn-docs-curator` |
| **v4.2 — Physical hard inflate** | `mapf-gameplay-engineer` | `unity-mcp-operator` | `mapf-ui-visualizer` (heatmap + hard-block X markers) | `mapf-metrics-architect` (hard-block cell count, path-length delta) | `datn-docs-curator` |

### Sub-agent assignment chi tiết

#### Phase v4.1 — Disable smoothing

**Owner**: `mapf-gameplay-engineer`
- Scope: thêm `enableSmoothing` flag vào `TryFindPath` overload + `GridEnemyAgent` field + `MapScenarioBootstrap` Inspector. Default `false`. Giữ `SmoothPath`/`HasLineOfSight`/`HasClearance` private methods trong source (không xóa, để ablation study).
- Output: file diff + verify validate_script clean.

**Verify**: `unity-mcp-operator`
- Compile + scene smoke. Check console không error.
- Capture Gizmos path screenshot trong Edit Mode.

**Visualization**: `mapf-ui-visualizer`
- Confirm Gizmos path là raw cell-to-cell, không diagonal collapse.
- Optional: thêm toggle Inspector `drawRawPathGizmos` để tách raw path (yellow) vs smoothed path (green) khi `enableSmoothing` toggle on.

**Docs**: `datn-docs-curator`
- Cập nhật v3 plan: mark Phase B "deprecated by v4 — smoothing default off, kept as ablation flag".
- Cập nhật `mapf_eagle_enemy_astar_plan.md` Milestone 2: ghi WHY off smoothing default (deterministic, LNS2-ready).

**KHÔNG dùng**: `unity-scene-prefab-wiring` (no scene/prefab), `mapf-metrics-architect` (defer to v4.2 batch).

#### Phase v4.2 — Physical hard inflate

**Owner**: `mapf-gameplay-engineer`
- Scope: 
  - `GridNavMask.cs`: constructor mới `(MapLoader, float physicalRadius, LayerMask obstacleMask, int softRadius, int softCostNear, int softCostMid)`. `Rebuild` dùng `Physics2D.OverlapCircle` cho `agentWalkable`. Soft cost layer giữ nguyên.
  - `MapScenarioBootstrap.cs`: Inspector fields `tankPhysicalRadius=0.45f`, `obstacleLayerMask` (default `ObstaclesMovement`). Replace constructor call. Mark `agentInflateRadius` deprecated (giữ trong source comment).
- Output: file diff + validate clean. **CRITICAL**: verify `MapLoader.LoadAndBuild` chạy TRƯỚC `MapScenarioBootstrap.SpawnScenario` (do navMask `Rebuild` cần colliders đã exist). Đọc `MapTankTestBootstrap.Start()` confirm thứ tự.

**Verify**: `unity-mcp-operator`
- Compile clean, scene smoke 30s in Play Mode.
- Inject scenario: spawn 3 cluster densely placed (qua `execute_code` hoặc edit `random-32-32-10.map` test).
- Capture: heatmap Gizmos screenshot, in-play enemy trajectory screenshot.
- Reflection check: confirm `navMask.agentWalkable` cell count giảm so với pre-v4.2 (vì hard inflate tighter).

**Visualization**: `mapf-ui-visualizer`
- Gizmos heatmap update: thêm tier `cost == int.MaxValue` (hard-block từ physical) — render với đỏ rất đậm hoặc X marker rõ ràng, khác biệt với `@` cells (blue X từ MapLoader).
- Toggle `drawHardBlockOverlay` trên `MapScenarioBootstrap`.

**Metrics**: `mapf-metrics-architect`
- Schema mới (cumulative với batch 1 Phase A+B):
  - `hardblock_cell_count`: số cell bị `Physics2D.OverlapCircle` block (tăng so với Chebyshev inflate).
  - `path_length_v4` vs `path_length_v3`: confirm v4 path không dài hơn quá `+30%`.
  - `tankPhysicalRadius_used`: param value cho experiment ablation.

**Docs**: `datn-docs-curator`
- Cập nhật:
  - `mapf_enemy_stuck_phase2_plan_v4.md` (file này): mark "Phase v4.1, v4.2 DONE @ <date>".
  - `mapf_enemy_stuck_phase2_plan_v3.md`: thêm header note "v3 superseded by v4 — Phase A soft cost retained, Phase B smoothing deprecated, Phase C/D/E deferred".
  - `mapf_eagle_enemy_astar_plan.md` Milestone 2: ghi WHY chuyển từ Chebyshev inflate sang Physics2D.OverlapCircle (chính xác hơn về physical clearance, simpler hơn cho ablation study).
  - `mapf_enemy_stuck_at_obstacle_plan.md`: mark Phase 1 (inflate) deprecated, replaced by physical hard inflate.

**KHÔNG dùng**: `unity-scene-prefab-wiring` (no prefab change, no layer split — `ObstaclesMovement` đã đủ vì single-agent test, layer split cho Milestone 3 vẫn cần như v3 đã note).

## 9. Cross-phase coordination

### Thứ tự dispatch

```
v4.1 (Disable smoothing) → verify → 
v4.2 (Physical hard inflate) → verify → 
docs update + cleanup
```

**v4.1 trước v4.2** vì:
- v4.1 là toggle change, ít risk. Confirm path raw OK trước.
- v4.2 thay đổi navMask cấu trúc, có thể isolation map. Cần baseline raw path để compare.

**KHÔNG chạy song song** v4.1 và v4.2 — cùng đụng `MapScenarioBootstrap.cs`.

### Conflict file

| File | Phase đụng | Quy tắc |
|---|---|---|
| `GridNavMask.cs` | v4.2 only | Single owner. |
| `GridAStarPathfinder.cs` | v4.1 only (overload) | Single owner. |
| `GridEnemyAgent.cs` | v4.1 only (`enableSmoothing` field) | Single owner. |
| `MapScenarioBootstrap.cs` | v4.1 (smoothing field) + v4.2 (physical params) | Serialize. v4.1 trước v4.2. |

### Cleanup task sau khi v4.2 verify pass

- Diagnostic logs trong `AddGridEnemyAgent` từ Phase B (đã add nhưng chưa cleanup do hit limit). Owner: `mapf-gameplay-engineer`.

## 10. Liên hệ Milestone tiếp theo

### Phase v3 status sau v4

- **Phase A (soft cost)**: KEEP. Vẫn hữu ích kết hợp với hard physical inflate.
- **Phase B (swept smoothing)**: DEPRECATED but retained as toggle. Ablation study material.
- **Phase C (4-tier rotate)**: DEFER. Có thể không cần nếu hard inflate đủ. Re-evaluate sau khi v4 test.
- **Phase D (scuff detection)**: DEFER as safety net. Implement nếu vẫn còn case scuff sót sau v4.
- **Phase E (force-center snap)**: DEFER. Có thể không cần.

### Milestone 3 (multi-enemy)

- v4 Physical hard inflate **scale tốt**: mỗi cell hard-block dựa trên physics, không phụ thuộc agent count.
- v4 Path raw **compatible với LNS2** (Milestone 4): LNS2 dùng discrete cell path là input chuẩn.
- Layer split `Walls` vs `AgentBlocker` vẫn cần cho Milestone 3 (như v3 note) — DEFER cho lúc đó.

### Risk to thesis report

- "Bỏ smoothing" có thể bị hỏi: tại sao? Trả lời: ablation study. Smoothing trong literature thường chỉ improve quỹ đạo aesthetic, không phải optimality. LNS2 baseline competition cũng dùng discrete path.
- "Physical inflate" hơi non-standard so với pure grid MAPF literature. Trả lời: thesis applied research, tank physical size là constraint thực, ignore là sai về spec.

## 11. Bước tiếp theo ngay

1. **Dispatch `mapf-gameplay-engineer`** implement v4.1 (disable smoothing default).
2. Verify qua `unity-mcp-operator` — test scenario 3 cluster.
3. **Nếu v4.1 chưa đủ** (tank vẫn cọ vì cell-grid logic chưa physical), dispatch v4.2 (physical hard inflate).
4. Visual + metrics check sau v4.2.
5. Cleanup diagnostic logs.
6. Docs update.

**Estimated effort**:
- v4.1: ~1 dispatch (gameplay-engineer) + ~1 verify (mcp-operator).
- v4.2: ~1 dispatch (gameplay-engineer) + ~1 verify (mcp-operator) + ~1 viz update (ui-visualizer).
- Cleanup + docs: ~1 dispatch each.

Total: ~6 sub-agent dispatches, expect <30 min real time.

---

## Tóm tắt v4

| Aspect | v3 (current) | v4 (proposed) |
|---|---|---|
| Smoothing | ON (swept-capsule) | OFF default (toggle for ablation) |
| Inflate | Chebyshev cell-grid | Physics2D.OverlapCircle (physical) |
| Soft cost | ON | ON (retained) |
| Path geometry | Smoothed diagonal | Raw 4-neighbor cell-to-cell |
| Scuff resistance | Soft penalty + smooth check | Hard-block + soft penalty |
| LNS2 compatibility | Smoothing might break | Compatible out-of-box |
| Code complexity | High (3 layers gating) | Low (1 layer hard, 1 layer soft) |
| Ablation flexibility | Low (smoothing always on) | High (toggle for thesis) |

**Recommendation**: dispatch v4.1 + v4.2 ngay. Phase D scuff detection giữ làm fallback nếu sau v4 vẫn còn case sót.
