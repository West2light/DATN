# Plan: Chế độ "Mixed" — kết hợp A* và PIBT

> Mục tiêu: thêm mode thứ 4 **Mixed** bên cạnh 3 mode hiện có (A*, PIBT C#, PIBT-TCP).
> Ý tưởng của người dùng: *"tận dụng planning của PIBT lúc đầu, gần đến nơi thì switch
> sang A*"*. Tài liệu này (1) nghiên cứu **khi nào** và **tại sao** switch cho đúng với
> project Eagle-defense, (2) thiết kế **switch như thế nào**, (3) liệt kê **đầy đủ điểm
> chạm** để wiring vào kiến trúc hiện tại.

---

## 1. Tình trạng hiện tại (đọc từ code)

### 1.1. Ba mode và cách dispatch

Mode được chọn qua **chuỗi algorithm** `"AStar" | "PIBT" | "PIBT_TCP"`, chạy qua các lớp:

| Lớp | File | Vai trò |
|---|---|---|
| Agent A* | `GridEnemyAgent.cs` | A* độc lập (`GridAStarPathfinder`) + navMask + recovery |
| Agent PIBT | `GridEnemyAgentPIBT.cs` | PIBT flow-aware (`GridPIBTPathfinder` + `PIBTPlanner`) |
| Agent PIBT-TCP | `GridEnemyAgentPIBT_TCP.cs` | nhận action từ server C++ |
| Planner PIBT | `PIBTPlanner.cs` | flow grid tĩnh chia sẻ + flow-aware A* + Frank-Wolfe |
| Bootstrap A* | `MapScenarioBootstrap.cs` | spawn Eagle + enemies gắn `GridEnemyAgent` |
| Bootstrap PIBT | `MapScenarioBootstrapPIBT.cs` | gắn `GridEnemyAgentPIBT` |
| Bootstrap TCP | `MapScenarioBootstrapPIBT_TCP.cs` | gắn `GridEnemyAgentPIBT_TCP` |
| Chọn bootstrap | `MapTankTestBootstrap.SpawnScenario()` | đọc `SelectedAlgorithm`/`BacktestMode`/`LAN` → `AddComponent` đúng bootstrap |
| Menu | `MenuViewBootstrap.cs` | 3 nút mode (mảng `modeLabels/modeAlgos/modeScenes`) |
| Backtest | `BacktestRunner.cs` | mảng `scenes[]`/`algos[]` sinh job; gom 3 loại agent; report |
| Static state | `BacktestMode.cs` | giữ `Algorithm` hiện hành |
| Report | `Tools/backtest_plot_report.py` | `ALGOS` hardcode 3 dòng + `WIN_CLASS` |

**Scene:** A* dùng `MapF_TankTest`; PIBT và PIBT-TCP **dùng chung** `MapF_TankTest_PIBT`
(phân biệt bằng bootstrap được `MapTankTestBootstrap` add lúc runtime; TCP branch
`return` sớm trước PIBT branch nên chỉ 1 bootstrap chạy).

### 1.2. Khác biệt thực chất giữa A* agent và PIBT agent

Hai agent gần **trùng 90%** — cùng FollowPath (dot-product rotate-then-drive), cùng
recovery (scuff/spatial/reverse), cùng shooting, cùng shoot-to-clear destructible.
**Điểm khác duy nhất về thuật toán nằm trong `ReplanPath()`:**

```csharp
// A* (GridEnemyAgent.ReplanPath):
GridAStarPathfinder.TryFindPath(mapLoader, activeMask, start, goal, path,
                                blockedCells, tankClearanceRadius, smoothing);
//   → đường ngắn nhất ĐỘC LẬP, không biết agent khác → tất cả chọn cùng cửa → kẹt

// PIBT (GridEnemyAgentPIBT.ReplanPath):
GridPIBTPathfinder.TryFindPath(mapLoader, agentId, start, goal, path, frankWolfeMs);
//   → flow-aware, phối hợp qua PIBTPlanner._flow → tản luồng qua nhiều cửa
```

> **Hệ quả cho Mixed:** chỉ cần một agent kiểu PIBT (đã đăng ký `PIBTPlanner`, có
> `agentId`, chia sẻ flow), rồi trong `ReplanPath()` **chọn pathfinder theo tình huống**.
> Không phải viết lại follow/recovery/shooting.

### 1.3. Cơ chế PIBT quan trọng cần nhớ khi làm Mixed

- `PIBTPlanner._flow[cell*4+d]` = số trajectory dùng cạnh `cell→d`. `AddFlow`/`RemoveFlow`
  cộng/trừ khi agent đặt/hủy traj.
- `FrankWolfe(id, start, goal, budgetMs)`: replan agent `id`, rồi dùng ngân sách thời gian
  còn lại **replan round-robin các agent khác trong `_currPos.Keys`**.
- `GetOrBuildH(goal)`: **reverse-BFS từ goal**, cache theo goal → cho **khoảng cách lưới
  chính xác** tới goal (chính là thứ ta cần cho tiêu chí switch, gần như free).
- Một agent còn nằm trong `_currPos`/`_goals`/`_trajs` sẽ (a) tiếp tục bơm flow và (b) bị
  FrankWolfe của agent khác replan hộ. → Khi Mixed chuyển sang A*, phải **rút agent khỏi
  tập active** để không tạo flow ma và không bị replan hộ.

---

## 2. Nghiên cứu: KHI NÀO switch và TẠI SAO (phần lõi)

### 2.1. Đặc thù kịch bản Eagle-defense

Tất cả enemy **chung đúng một goal = Eagle**. Map có bottleneck (Rooms-32: 51 cửa rộng
1 ô; Maze-128: hành lang hẹp). Điều này tạo ra hai vùng có tính chất trái ngược:

**Vùng xa (còn nhiều tuyến thay thế).** Ra ngoài map, giữa các cửa/hành lang **có** lựa
chọn khác nhau. `op_flow` (chống đối đầu) + `vertex_flow` (chống ô đông) của PIBT **thực
sự** đẩy agent tản sang các cửa khác nhau → ít kẹt đối đầu, ít recovery. **Đây là nơi
PIBT đáng đồng tiền** (dù trả giá: Frank-Wolfe replan cả đàn mỗi lần, đường vòng dài hơn).

**Vùng gần (tiếp cận Eagle).** Sát base, **mọi** trajectory **bắt buộc** hội tụ về đúng ô
Eagle. `vertex_flow` quanh Eagle **tất yếu cực đại** — PIBT tốn công phạt những ô **không
thể né**, dễ sinh đường vòng/dao động ngay sát base. Ở đây A* = lao thẳng vào, **rẻ và
chính xác**. Vả lại đây cũng là vùng `eagleShootingRange` — agent dừng lại bắn.

> **Kết luận nghiên cứu:** dùng **PIBT khi còn xa** (phân bố toàn cục qua bottleneck),
> **A* khi đã gần** (tiếp cận cục bộ chính xác). Slogan: **"Phối hợp ở xa, tham lam ở
> gần"** (*coordinated far, greedy near*). Đây đúng trực giác người dùng và có lý do kỹ
> thuật vững cho báo cáo.

### 2.2. Tiêu chí switch — chọn "khoảng cách tới goal", không chọn "phần trăm đường đi"

Hai ứng viên tiêu chí:

- **(A) Khoảng cách lưới còn lại tới Eagle** — `dist = h[goal][start]` (reverse-BFS, đã
  cache trong `PIBTPlanner`). PIBT khi `dist > R`, A* khi `dist ≤ R`.
- **(B) Phần trăm quãng đường đã đi** (vd đi hết 70% path thì switch). **Bị loại:** độ dài
  path biến động rất mạnh theo map (Alpha-32 vs Gallows 251×180) → cùng % cho hành vi
  không nhất quán; không nhắm trúng "vùng phễu" quanh Eagle.

→ Chọn **(A)**. Ưu điểm: **bất biến theo map** — "vùng phễu" quanh Eagle có kích thước
gần như cố định (vài ô), nên **bán kính ô cố định `R`** là đúng bản chất, không phải theo
tỉ lệ map. Chi phí: một lần tra dict `h` — gần như free (BFS đã cache sẵn cho goal).

### 2.3. Chọn `R` (switchRadiusCells)

`R` nên phủ "vùng phễu" nơi hội tụ là bắt buộc, và nên lớn hơn tầm bắn để đoạn cuối đã là
A* trước khi agent dừng bắn:

```
R = ceil(eagleShootingRange / tileSize) + biên  ≈ 5 + 3 = 8 ô   (mặc định)
```

Để lộ ra Inspector (`[Min(1)] int switchRadiusCells = 8`). Lưu ý: trên map nhỏ 32×32 thì
vùng A* chiếm phần lớn; trên map lớn thì `R=8` chỉ là "chóp cuối" — **đúng như mong muốn**
vì vùng phễu không nở theo map.

### 2.4. Hysteresis (chống rung ở ranh giới)

Dùng enum `Phase { PIBT, AStar }`, đánh giá lại **mỗi lần replan** (rẻ):

- `PIBT → AStar` khi `dist ≤ R`.
- `AStar → PIBT` chỉ khi `dist ≥ R + margin` (vd `margin = 3`).

Vì agent chủ yếu tiến đơn điệu về goal, vào A* rồi thường ở lại A*; hysteresis chỉ kích
khi agent bị **đẩy lùi** (vật cản động, va chạm) ra khỏi vùng phễu → khi đó nó **tái nhập
phối hợp PIBT**. Không latch một chiều (một chiều sẽ hỏng khi agent bị đẩy ra xa).

### 2.5. (Tùy chọn, KHÔNG bật mặc định) tiêu chí phụ theo mật độ

Ngay cả khi còn xa, nếu quanh agent **trống** (không có đồng đội gần **và** `vertex_flow`
local ≈ 0) thì PIBT ≈ A* → có thể xài A* để tiết kiệm Frank-Wolfe. **Đề xuất TẮT** cho
bản so sánh luận văn: giữ **một biến duy nhất** (khoảng cách) cho câu chuyện sạch, tránh
flip-flop. Ghi lại như một hướng mở rộng có kiểm soát (thêm cờ `congestionAwareSwitch`).

---

## 3. Thiết kế switch NHƯ THẾ NÀO (mechanics)

Toàn bộ thay đổi gói trong `ReplanPath()` của agent Mixed; mọi thứ khác **giữ y nguyên**
agent PIBT.

```csharp
private void ReplanPath()
{
    btReplanCount++;
    nextReplanTime = Time.time + replanInterval;
    RefreshFactionCache();

    Vector2Int start = mapLoader.WorldToCell(GetAgentPosition());
    HashSet<Vector2Int> blocked = MergeBlockedCells(pendingBlockedCells, BuildDynamicBlockedCells(start));
    pendingBlockedCells = null;
    Vector2Int goal = ResolveGoalCell(mapLoader.WorldToCell(eagleTarget.position), blocked, start);

    // ── Tiêu chí switch: khoảng cách lưới còn lại (reverse-BFS đã cache) ──
    int dist = PIBTPlanner.GetHeuristicDistance(start, goal);   // API mới, xem §4.1
    _phase = UpdatePhase(_phase, dist);                          // hysteresis §2.4
    if (_phase != _lastPhase) { btSwitchCount++; _lastPhase = _phase; }

    bool ok;
    if (_phase == Phase.PIBT)
    {
        // Phối hợp: bơm flow, đồng thời FrankWolfe replan hộ đồng đội
        ok = GridPIBTPathfinder.TryFindPath(mapLoader, agentId, start, goal, currentPath, frankWolfeMs);
    }
    else // Phase.AStar — tiếp cận cục bộ
    {
        // Rút khỏi tập active PIBT: bỏ flow ma + không bị replan hộ (API mới §4.1)
        PIBTPlanner.SuspendAgent(agentId);
        // A* "vật lý" (navMask = null) giống nhánh destructible của PIBT agent
        ok = GridAStarPathfinder.TryFindPath(mapLoader, null, start, goal, currentPath, blocked);
    }

    if (ok) { pathIndex = currentPath.Count > 1 ? 1 : 0; lastTrackedPathIndex = pathIndex;
              if (btInitialPathLength == 0) btInitialPathLength = currentPath.Count; _destructibleTarget = null; }
    else    { /* fallback destructible y hệt PIBT agent */ }
}
```

Ghi chú:
- **Per-replan re-evaluation** (không latch): mỗi replan 1 lần tra dict → rẻ, luôn phản
  ánh vị trí thật, giữ flow grid trung thực.
- Khi ở A*, gọi `SuspendAgent` **mỗi replan** là idempotent (nếu đã suspend thì no-op).
- Khi quay lại PIBT, `GridPIBTPathfinder.TryFindPath` gọi `SetCurrentPos` + `FrankWolfe`
  → agent **tự động tái nhập** `_currPos`/`_goals`/`_trajs`. Không cần re-register id.
- Gizmo/HUD: đổi màu path theo phase (vd **cyan = PIBT**, **đỏ = A***) để demo lộ rõ
  thời điểm switch. Thêm `btSwitchCount` để đo tần suất switch.

---

## 4. Điểm chạm để wiring (checklist đầy đủ, không sót)

### 4.1. `PIBTPlanner.cs` — thêm 2 API nhỏ (chỉ đọc/nhẹ)

```csharp
/// Khoảng cách lưới chính xác start→goal (reverse-BFS, cache theo goal).
/// Trả int.MaxValue/2 nếu không tới được. Dùng cho tiêu chí switch của Mixed.
public static int GetHeuristicDistance(Vector2Int start, Vector2Int goal)
{
    if (!IsReady) return int.MaxValue / 2;
    int gf = ToFlat(goal), sf = ToFlat(start);
    if (gf < 0 || gf >= _size || sf < 0 || sf >= _size) return int.MaxValue / 2;
    return GetOrBuildH(gf)[sf];        // GetOrBuildH đang private — dùng nội bộ, OK
}

/// Rút agent khỏi tập active: xóa flow của traj, bỏ khỏi _trajs/_goals/_currPos.
/// GIỮ id (id cấp phát tăng dần, không tái dùng) → gọi PIBT lại sẽ tự tái nhập.
public static void SuspendAgent(int id)
{
    if (_trajs.TryGetValue(id, out var t)) { RemoveFlow(t); _trajs.Remove(id); }
    _goals.Remove(id);
    _currPos.Remove(id);
}
```

> Khác `Unregister` ở chỗ: `Unregister` dành cho `OnDestroy` (agent chết hẳn). `SuspendAgent`
> để tạm dừng phối hợp mà vẫn giữ id sống. (Có thể refactor `Unregister` gọi lại
> `SuspendAgent` cho gọn.)

### 4.2. `GridEnemyAgentMixed.cs` — **mới** (copy từ `GridEnemyAgentPIBT.cs`)

- Copy nguyên `GridEnemyAgentPIBT.cs` → đổi tên class.
- Thêm `[Header("Mixed")] [Min(1)] public int switchRadiusCells = 8;`
  và `public int switchMargin = 3;` (+ tùy chọn `bool congestionAwareSwitch = false;`).
- Thêm `enum Phase { PIBT, AStar }`, field `_phase`, `_lastPhase`, `UpdatePhase(...)`.
- Sửa `ReplanPath()` theo §3. Giữ nguyên phần còn lại (Follow/recovery/shoot/destructible).
- Thêm metric `[System.NonSerialized] public int btSwitchCount;`.
- `OnDrawGizmos`: màu theo `_phase`.

> Lý do chọn **copy-and-modify** thay vì trừu tượng hóa: đúng convention hiện có (A*/PIBT/TCP
> là 3 file agent song song, dễ đọc cho luận văn), rủi ro thấp cho nhánh `final-demo`. Nếu
> muốn gọn hơn: có thể tách `IReplanStrategy` dùng chung — nêu như phương án thay thế, **không**
> khuyến nghị làm ngay trước demo.

### 4.3. Bootstrap — chọn 1 trong 2 cách

**Cách A (khuyến nghị, đồng bộ với TCP): `MapScenarioBootstrapMixed.cs` mới.**
Copy `MapScenarioBootstrapPIBT.cs`, đổi `AddGridEnemyAgentPIBT` → gắn `GridEnemyAgentMixed`,
thêm truyền `switchRadiusCells`. Dùng chung scene `MapF_TankTest_PIBT` (Mixed không cần
navMask, giống PIBT). `PIBTFlowVisualizer` vẫn hoạt động (Mixed vẫn bơm flow ở pha PIBT).

**Cách B (ít file hơn): cờ trên `MapScenarioBootstrapPIBT`.**
Thêm `bool mixedMode` + `switchRadiusCells`; trong `AddGridEnemyAgentPIBT` nếu `mixedMode`
thì gắn `GridEnemyAgentMixed`. `mixedMode` set từ `BacktestMode.Algorithm == "Mixed"`.
Tiết kiệm 1 file nhưng lệch pattern (TCP có bootstrap riêng). → Chọn **A** cho nhất quán.

### 4.4. `MapTankTestBootstrap.SpawnScenario()` — thêm nhánh Mixed

Thêm **trước** nhánh PIBT (mirror nhánh TCP, `return` sớm):

```csharp
bool isMixed = selectedAlgorithm == "Mixed"
    || (LanSessionManager.IsActive && LanSessionManager.Algorithm == "Mixed")
    || (BacktestMode.IsActive && BacktestMode.Algorithm == "Mixed");

var mixedBootstrap = GetComponent<MapScenarioBootstrapMixed>();
if (isMixed && mixedBootstrap == null) mixedBootstrap = gameObject.AddComponent<MapScenarioBootstrapMixed>();
if (isMixed && mixedBootstrap != null)
{
    mixedBootstrap.mapLoader = mapLoader;
    if (spawnCells != null) mixedBootstrap.enemySpawnCells = spawnCells;
    mixedBootstrap.SpawnScenario();
    return;
}
```

Cũng cập nhật khối cleanup quanh dòng ~496–499 (đang `GetComponent` TCP/PIBT) để dọn cả
Mixed nếu có.

### 4.5. `BacktestRunner.cs` — thêm mode thứ 4

- `BuildJobs()`: mở rộng thành 4 phần tử:
  `scenes = { "MapF_TankTest", "MapF_TankTest_PIBT", "MapF_TankTest_PIBT", "MapF_TankTest_PIBT" }`,
  `algos = { "AStar", "PIBT", "PIBT_TCP", "Mixed" }`.
- Thêm list `_agentsM` kiểu `GridEnemyAgentMixed`.
- `InjectScene()`: `FindObjectsByType<GridEnemyAgentMixed>()`, subscribe death, set `btSpawnTime`,
  thêm scenario `MapScenarioBootstrapMixed` vào chuỗi tìm eagle.
- `UpdateRealtimePanel`/`RecordRun`: cộng metric của `_agentsM` (thêm `CollectAgentMixed`).
- `BuildHTML()` (fallback C#): thêm `"Mixed"` vào `algos[]`, thêm class `bar-m`/`win-m` + màu.

### 4.6. `MenuViewBootstrap.cs` — thêm nút mode thứ 4

- Mở rộng `modeLabels = { "A*", "PIBT", "PIBT-TCP", "Mixed" }`,
  `modeAlgos = { "AStar", "PIBT", "PIBT_TCP", "Mixed" }`,
  `modeScenes = { sceneAStar, scenePIBT, scenePIBT, scenePIBT }` (2 chỗ: single ~591, LAN ~908).
- Tăng `CardH` (comment ~509 nói đang cỡ cho 3 nút) để chứa 4 nút.
- Mixed load bằng `PlayerPrefs.SetString(PrefKeyAlgorithm,"Mixed")` + `LoadScene(scenePIBT)`
  (không cần nhánh đặc biệt như TCP vì Mixed chạy client-side).

### 4.7. `Tools/backtest_plot_report.py` — thêm dòng algo

- `ALGOS`: thêm `("Mixed", "Mixed", "<hex màu>")` (vd tím `#b07cff`).
- `WIN_CLASS`: thêm `"Mixed": "win-m"`.
- CSS `.win-m{color:#b07cff;...}` + tiêu đề đổi thành "A* vs PIBT vs PIBT-C++ vs Mixed".

### 4.8. Scene / prefab

- **Không cần scene mới** — dùng lại `MapF_TankTest_PIBT`.
- Cách A cần `MapScenarioBootstrapMixed` **được add runtime** bởi `MapTankTestBootstrap`
  (giống TCP) → không phải sửa file scene. Kiểm chứng: scene không tự chạy bootstrap trong
  `Start()` của nó (đã xác nhận PIBT/TCP bootstrap chỉ chạy khi `SpawnScenario()` được gọi).

---

## 5. Đo lường & kỳ vọng (đưa vào báo cáo)

Chạy backtest cùng kịch bản (đặc biệt **Rooms-32** và **Maze-128** — nơi bottleneck lộ
khác biệt) với 4 mode, so:

| Metric | Nguồn | Kỳ vọng của Mixed |
|---|---|---|
| Thời gian tới đích / clear | summary | ≈ PIBT ở vùng xa, nhanh hơn PIBT ở đoạn cuối (bỏ dao động phễu) |
| `btReplanCount` | agent | ≤ PIBT (đoạn cuối A* không gọi Frank-Wolfe) |
| `btRecoveryCount` | agent | ≈ PIBT (thấp hơn A* thuần nhờ tản luồng ở xa) |
| Chi phí CPU/Frank-Wolfe | (đo thêm) | thấp hơn PIBT thuần (ít agent gọi FW khi đông agent đã vào vùng A*) |
| `btSwitchCount` | agent (mới) | ~1–2/agent (đơn điệu tiến); tăng nếu bị đẩy lùi |
| Số cạnh đối đầu quanh Eagle | `PIBTFlowVisualizer` | thấp hơn PIBT thuần (không bơm flow ma sát base) |

Câu chuyện luận văn: Mixed **giữ lợi ích chống-kẹt của PIBT ở bottleneck** mà **bỏ được
chi phí + dao động của PIBT ở vùng phễu Eagle** → cân bằng chất lượng/chi phí.

---

## 6. Rủi ro & lưu ý

- **Flow ma khi ở A*:** nếu quên `SuspendAgent`, agent A* vẫn bơm flow theo đường nó không
  đi → làm PIBT của đồng đội lệch. Bắt buộc gọi `SuspendAgent` ở pha A*. (Kiểm thử: log
  tổng `Σ vertex_flow` phải giảm khi agent vào A*.)
- **Rung phase:** thiếu hysteresis → replan liên tục đổi planner, path giật. Bắt buộc
  `margin ≥ 2–3`.
- **`GetOrBuildH` cho goal động:** Eagle cố định nên goal ổn định → cache hiệu quả. Nếu
  `ResolveGoalCell` đổi goal (Eagle bị chặn) sang ô khác → BFS mới cho goal đó (chấp nhận
  được, hiếm).
- **Không chạy 2 agent trên 1 prefab:** giữ nguyên nguyên tắc — Mixed thay thế hoàn toàn
  PIBT/A* agent, `disableLegacyEnemyAI` vẫn bật.
- **navMask = null cho A* branch:** Mixed dùng A* "vật lý" (không inflate) như nhánh
  destructible của PIBT agent. Nhất quán với việc scene PIBT không build navMask. Nếu về
  sau muốn inflate cho đẹp góc, cân nhắc build navMask nhẹ — **không cần cho demo**.
- **Report Python hardcode 3 algo:** nếu quên §4.7, cột Mixed sẽ bị bỏ khỏi biểu đồ dù CSV
  có dữ liệu. C# fallback `BuildHTML` cũng vậy (§4.5).

---

## 7. Thứ tự làm (checklist)

- [ ] **B1** `PIBTPlanner`: thêm `GetHeuristicDistance` + `SuspendAgent` (§4.1). Biên dịch sạch.
- [ ] **B2** Copy `GridEnemyAgentPIBT.cs` → `GridEnemyAgentMixed.cs`; thêm `Phase`, fields,
      sửa `ReplanPath` (§3), thêm `btSwitchCount`, gizmo theo phase.
- [ ] **B3** `MapScenarioBootstrapMixed.cs` (copy PIBT bootstrap, gắn agent Mixed, truyền
      `switchRadiusCells`).
- [ ] **B4** `MapTankTestBootstrap.SpawnScenario`: nhánh Mixed + cleanup (§4.4).
- [ ] **B5** `BacktestRunner`: `algos/scenes` 4 phần tử, `_agentsM`, inject/record/report (§4.5).
- [ ] **B6** `MenuViewBootstrap`: nút mode thứ 4 + `CardH` (§4.6).
- [ ] **B7** `Tools/backtest_plot_report.py`: thêm dòng `Mixed` + `WIN_CLASS` + CSS (§4.7).
- [ ] **B8** Chạy thử **1 map nhỏ (Rooms-32)** cả 4 mode; xác nhận Mixed switch đúng (đổi
      màu path quanh Eagle), `btSwitchCount` hợp lý, flow giảm ở pha A*. *(cần mở Unity)*
- [ ] **B9** Backtest đầy đủ + chụp so sánh 4 mode cho báo cáo; cập nhật docs
      (`Assets/Docs/*`) qua `datn-docs-curator`.

> Ước lượng: B1–B4 là phần lõi (~1 buổi). B5–B7 là wiring cơ học đếm-điểm-chạm. B8–B9 cần
> Editor + backtest. Toàn bộ giữ nguyên FollowPath/recovery/shooting nên rủi ro tập trung
> ở `ReplanPath` + 2 API planner — dễ kiểm thử độc lập.

---

## 8. ĐÃ TRIỂN KHAI (2026-07-11) — full wiring

Mode Mixed đã được code xong (chưa mở Unity để verify runtime). Files chạm:

| File | Thay đổi |
|---|---|
| `Assets/Scripts/PIBTPlanner.cs` | + `GetHeuristicDistance(start,goal)`, `SuspendAgent(id)` |
| `Assets/Scripts/GridEnemyAgentMixed.cs` | **Mới** — agent Mixed (Phase enum + UpdatePhase hysteresis + ReplanPath switch + `btSwitchCount` + gizmo màu theo pha) |
| `Assets/Scripts/MapScenarioBootstrapMixed.cs` | **Mới** — bootstrap gắn agent Mixed, `switchRadiusCells`/`switchMargin` |
| `Assets/Scripts/MapTankTestBootstrap.cs` | + nhánh dispatch `isMixedMode` (trước PIBT) + `GetSpawnedEnemies` |
| `Assets/Scripts/Backtest/BacktestRunner.cs` | 4 algo/scene, `_agentsM`, inject/record/report, `AlgorithmCount=4`, HTML fallback + Mixed |
| `Assets/Scripts/Backtest/BacktestResultChart.cs` | cột + legend + win-tint + summary cell Mixed (bar thứ 4, màu tím) |
| `Assets/Scripts/Backtest/BacktestConfigUI.cs` | run-count `×AlgorithmCount` thay `×3`, nhãn liệt kê thêm Mixed |
| `Assets/Scripts/Backtest/DynamicObstacleSpawner.cs` | `SetAgents(...agentsM)` + né path/ô của agent Mixed |
| `Assets/Scripts/MenuViewBootstrap.cs` | nút "Mixed" (single + LAN), `CardH` 300→340 |
| `Tools/backtest_plot_report.py` | `ALGOS`+`WIN_CLASS`+CSS+title Mixed, offsets cột tự co theo N algo |

Thông số mặc định: `switchRadiusCells=8`, `switchMargin=3`, màu Mixed `#b07cff` (tím).
Verify Python: `py_compile` PASS. Verify C#: **cần mở Unity** (B8).

**Còn lại:** B8 (test Rooms-32 4 mode, xác nhận đổi màu path quanh Eagle + flow giảm ở
pha A*), B9 (backtest đầy đủ + docs). `.meta` của 2 script mới do Unity tự sinh khi focus.
