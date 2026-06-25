# PLAN (IMPLEMENT-READY): Backtest — Timeout 180s + Nhập số lượng Agent + Biểu đồ/CSV máu Base Eagle

Ngày lập: 2026-06-23
Nhánh: `feature/fix_backtest_PIBT_TCP`
Mục đích: 4 thay đổi cho module Backtest, viết sẵn code cụ thể để implement tuần tự (kể cả model năng lực thấp hơn vẫn làm được).

Yêu cầu gốc của user:
1. Tăng timeout mỗi lần backtest **120s → 180s**.
2. Thêm **UI + chức năng nhập số lượng agent** ngay trong panel `SELECT MAPS TO BACKTEST`, cạnh "Runs per map".
3. **Xuất biểu đồ so sánh máu Base Eagle** qua từng thuật toán (A* / PIBT / PIBT-C++).
4. **Xuất .csv có thêm máu Base Eagle**.

> Đọc mục B (bối cảnh) trước. Mỗi milestone tự chứa đủ chi tiết "sửa file nào, dòng nào, thay bằng gì".

---

## A. QUY ƯỚC CHUNG (đọc trước, bắt buộc)

1. **Làm tuần tự M1 → M4.** Mỗi milestone phải compile sạch (0 error) trước khi sang milestone sau.
2. **Sau mỗi lần sửa code, kiểm tra biên dịch:**
   - Có Unity MCP: `validate_script` cho file vừa sửa → `read_console` lọc error. Console phải 0 error.
   - Không có MCP: yêu cầu user mở Unity Editor để compile, rồi đọc Console.
3. **KHÔNG đổi behavior khi chơi thường** (ngoài backtest). Mọi thay đổi spawn-count chỉ kích hoạt khi `BacktestMode.IsActive == true`. Field `enemySpawnCells` của 3 bootstrap giữ nguyên làm mặc định cho chế độ chơi thường.
4. **Giữ nguyên** mọi log `[BacktestRunner]` / `[PIBT_TCP]` hiện có.
5. Mỗi đoạn code dưới đây là "final form" của một method/khối — tìm đúng method cùng tên và thay nguyên khối (trừ khi ghi "thêm mới"). Giữ nguyên `using`, namespace, các method khác.
6. **Tính công bằng (fairness) là bất biến của thesis:** với cùng 1 map + cùng số agent, **cả 3 thuật toán phải spawn ở các ô y hệt nhau**. Vì vậy bộ sinh ô spawn phải **deterministic** (chỉ phụ thuộc kích thước map + số agent, không dùng random thời gian thực).

### Các file sẽ đụng tới
| File | M1 | M2 | M3 | M4 |
|---|---|---|---|---|
| `Assets/Scripts/Backtest/BacktestRunner.cs` | ✅ | ✅ | ✅ | (✅ HTML fallback) |
| `Assets/Scripts/Backtest/BacktestConfigUI.cs` | | ✅ | | |
| `Assets/Scripts/Backtest/BacktestMode.cs` | | ✅ | | |
| `Assets/Scripts/Backtest/BacktestData.cs` | | | ✅ | |
| `Assets/Scripts/Backtest/BacktestResultChart.cs` | ✅ | | | ✅ |
| `Assets/Scripts/Backtest/BacktestSpawn.cs` (**tạo mới**) | | ✅ | | |
| `Assets/Scripts/MapScenarioBootstrap.cs` (A*) | | ✅ | | |
| `Assets/Scripts/MapScenarioBootstrapPIBT.cs` | | ✅ | | |
| `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs` | | ✅ | | |
| `Tools/backtest_plot_report.py` | ✅(maxHint)? | | | ✅ |

---

## B. BỐI CẢNH CODE HIỆN TẠI (số dòng tham chiếu)

**Luồng backtest:**
`MenuViewBootstrap.cs:288` → `BacktestConfigUI.Show()` (panel chọn map + reps + dynamic-obstacle)
→ `BacktestConfigUI.StartBacktest()` (`:310`) → `BacktestRunner.Launch(indices, _reps, dyn)` (`:316`)
→ `BacktestRunner.RunAll()` lặp từng job → `RunJob()` gọi `BacktestMode.Activate(...)` (`:168`) rồi `LoadScene(job.scene)`.

**Bootstrap (3 thuật toán) đọc `BacktestMode.IsActive`** để biết đang backtest:
- `MapScenarioBootstrap.cs` (A*) — `enemySpawnCells` `:32-38` (4 ô cố định), `SpawnEnemies()` `:340-375`, loop dùng `enemySpawnCells` `:358`.
- `MapScenarioBootstrapPIBT.cs` — `enemySpawnCells` `:43`, `SpawnEnemies()` `:281-313`, loop `:299`.
- `MapScenarioBootstrapPIBT_TCP.cs` — `enemySpawnCells` `:54-60`, `SpawnEnemies()` `:743-772`, loop `:757`. **Quan trọng:** `ConnectAndHello()` gửi `_agents.Count` cho server (`:160`) → spawn đúng số agent TRƯỚC khi connect thì server tự nhận đúng số (`SpawnScenario` gọi `SpawnEnemies()` rồi mới `ConnectAndHello()`, `:127→133`).

**MapLoader API (đã verify):**
- `BuildStartX/BuildStartY/BuildWidth/BuildHeight` (`:60-63`), `IsWalkable(cell)` (`:168`).
- `TryFindWalkableNear(Vector2Int preferred, out Vector2Int result)` (`:340`).
- `TryFindAvailableSpawnNear(Vector2Int preferred, ICollection<Vector2Int> reserved, int minSep, out Vector2Int result)` (`:190`) — nếu `preferred` không hợp lệ thì **tự quét toàn map** tìm ô walkable gần nhất + đủ giãn cách. Nghĩa là seed của bộ sinh không cần chính xác tuyệt đối, hàm này tự "nắn" về ô hợp lệ.

**Số liệu hiện có:**
- `BacktestRunRecord` (`BacktestData.cs`) **đã có** `eagleHpAtEnd`.
- Summary CSV (`BacktestRunner.cs:474`) **đã có cột `EagleHP`** (= `r.eagleHpAtEnd`). → Phần CSV "có máu eagle" đã tồn tại; M3 sẽ làm giàu thêm (HP max + % mất) và đảm bảo lên chart.
- Chart trong game `BacktestResultChart.cs` `Metrics` (`:63-68`) hiện có **3** metric: Duration, Replans, Shots — **chưa có Eagle HP**.
- Python report `Tools/backtest_plot_report.py` `METRICS` (`:20-25`) có **4** metric: Duration, Replans, Shots, Cells — lưới `subplots(2,2)` (`:77`) — **chưa có Eagle HP**.
- HTML fallback C# `BacktestRunner.BuildHTML()` (`:712`) có `NM=4` metric hardcode — chưa có Eagle HP.

---

## M1 — Timeout 120s → 180s

### M1.1 `BacktestRunner.cs` — hằng số timeout (`:23-25`)
Thay:
```csharp
    public const int   Reps          = 3;   // default; caller can override via Launch()
    public const float RunTimeoutSec = 120f;
```
bằng:
```csharp
    public const int   Reps          = 3;   // default; caller can override via Launch()
    public const float RunTimeoutSec = 180f;
```

### M1.2 `BacktestRunner.cs` — comment header (`:17`)
Thay `or timeout (120 s).` → `or timeout (180 s).`

### M1.3 `BacktestResultChart.cs` — trục Y biểu đồ thời gian (`:65`)
Metric "Average time (s)" đang ghim trần `maxHint=120f`. Thay:
```csharp
        new Metric { label="Average time (s)", get=r=>r.duration,    maxHint=120f, lowerBetter=true  },
```
bằng:
```csharp
        new Metric { label="Average time (s)", get=r=>r.duration,    maxHint=180f, lowerBetter=true  },
```

### M1.4 (KHÔNG cần sửa)
- Subtitle panel config (`BacktestConfigUI.cs:130`) đã dùng `BacktestRunner.RunTimeoutSec:F0` → tự đổi thành "Timeout 180s".
- `Tools/backtest_plot_report.py` không hardcode 120 (autoscale) → không cần đổi.

### M1.5 Test
- Mở panel backtest → subtitle hiển thị "Timeout 180s".
- Chạy 1 map (reps=1) đến khi Timeout → log `Run ... done — Timeout (180.0s)` (xấp xỉ 180).

---

## M2 — Nhập số lượng Agent (UI + plumbing + sinh ô spawn)

### Tổng quan luồng
`BacktestConfigUI` (stepper mới) → `BacktestRunner.Launch(..., agentCount)` → lưu `_agentCount`
→ `BacktestMode.Activate(..., agentCount)` → `BacktestMode.AgentCount`
→ mỗi `SpawnEnemies()` (3 bootstrap): nếu backtest thì lấy danh sách ô spawn từ `BacktestSpawn.GenerateSpawnCells(mapLoader, BacktestMode.AgentCount)` thay cho `enemySpawnCells`.

Phạm vi: **1–20**, mặc định **4** (giữ nguyên hành vi hiện tại khi để mặc định).

### M2.1 `BacktestMode.cs` — thêm `AgentCount` (thay nguyên file)
```csharp
public static class BacktestMode
{
    public static bool   IsActive             { get; private set; }
    public static string Algorithm            { get; private set; } // "AStar" | "PIBT" | "PIBT_TCP"
    public static string MapLabel             { get; private set; }
    public static bool   DynamicObstacleMode  { get; private set; }
    public static int    AgentCount           { get; private set; } // 0 = dùng enemySpawnCells mặc định

    public static void Activate(string algorithm, string mapLabel,
                                bool dynamicObstacles = false, int agentCount = 0)
    {
        IsActive            = true;
        Algorithm           = algorithm;
        MapLabel            = mapLabel;
        DynamicObstacleMode = dynamicObstacles;
        AgentCount          = agentCount;
    }

    public static void Deactivate()
    {
        IsActive            = false;
        Algorithm           = string.Empty;
        MapLabel            = string.Empty;
        DynamicObstacleMode = false;
        AgentCount          = 0;
    }
}
```

### M2.2 `BacktestSpawn.cs` — **TẠO FILE MỚI** (bộ sinh ô spawn deterministic)
Đường dẫn: `Assets/Scripts/Backtest/BacktestSpawn.cs`
```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sinh danh sách ô spawn cho backtest theo SỐ LƯỢNG agent yêu cầu.
/// Deterministic: chỉ phụ thuộc kích thước map + count → cả 3 thuật toán
/// (A*/PIBT/PIBT-C++) nhận đúng cùng một tập ô, đảm bảo công bằng.
/// Các điểm rải đều quanh chu vi map (enemy bao vây Eagle ở trung tâm).
/// Mỗi điểm được nắn về ô walkable gần nhất; loop spawn của bootstrap
/// (TryFindAvailableSpawnNear) sẽ lo phần tránh trùng/đủ giãn cách.
/// </summary>
public static class BacktestSpawn
{
    public static List<Vector2Int> GenerateSpawnCells(MapLoader map, int count)
    {
        var cells = new List<Vector2Int>();
        if (map == null || count <= 0) return cells;

        const int inset = 1; // tránh sát viền / boundary collider
        int x0 = map.BuildStartX + inset;
        int y0 = map.BuildStartY + inset;
        int x1 = map.BuildStartX + map.BuildWidth  - 1 - inset;
        int y1 = map.BuildStartY + map.BuildHeight - 1 - inset;
        if (x1 <= x0 || y1 <= y0)
        {
            // Map quá nhỏ: fallback về tâm map.
            var c = new Vector2Int(map.BuildStartX + map.BuildWidth / 2,
                                   map.BuildStartY + map.BuildHeight / 2);
            for (int i = 0; i < count; i++) cells.Add(c);
            return cells;
        }

        int w = x1 - x0;            // chiều ngang khả dụng
        int h = y1 - y0;            // chiều dọc khả dụng
        int perim = 2 * (w + h);    // số bước đi quanh chu vi

        var seen = new HashSet<Vector2Int>();
        for (int i = 0; i < count; i++)
        {
            // Rải đều quanh chu vi (cộng offset 0.5 để không dồn 2 điểm vào 1 góc).
            int d = Mathf.RoundToInt(((i + 0.5f) / count) * perim) % perim;
            Vector2Int edge = PerimeterToCell(d, x0, y0, x1, y1, w, h);

            // Nắn về ô walkable gần nhất để seed luôn hợp lệ.
            if (map.TryFindWalkableNear(edge, out Vector2Int walk)) edge = walk;

            // Nếu trùng ô đã có, nhích quanh chu vi vài bước cho tới khi khác.
            int guard = 0;
            while (seen.Contains(edge) && guard < perim)
            {
                d = (d + 1) % perim;
                edge = PerimeterToCell(d, x0, y0, x1, y1, w, h);
                if (map.TryFindWalkableNear(edge, out Vector2Int w2)) edge = w2;
                guard++;
            }
            seen.Add(edge);
            cells.Add(edge);
        }
        return cells;
    }

    // d ∈ [0, perim): đi quanh hình chữ nhật biên
    // cạnh dưới (L→R) → cạnh phải (B→T) → cạnh trên (R→L) → cạnh trái (T→B)
    private static Vector2Int PerimeterToCell(int d, int x0, int y0, int x1, int y1, int w, int h)
    {
        if (d < w)               return new Vector2Int(x0 + d, y0);            // bottom
        d -= w;
        if (d < h)               return new Vector2Int(x1, y0 + d);            // right
        d -= h;
        if (d < w)               return new Vector2Int(x1 - d, y1);            // top
        d -= w;
        return new Vector2Int(x0, y1 - d);                                     // left
    }
}
```
> Sau khi tạo: `validate_script` + `read_console` 0 error.

### M2.3 `BacktestRunner.cs` — nhận `agentCount` ở `Launch` (`:89-102`)
Thay:
```csharp
    public static void Launch(List<int> selectedMapIndices = null, int reps = Reps, bool dynamicObstacles = false)
    {
        if (_instance != null) return;
        var go = new GameObject("BacktestRunner");
        var runner = go.AddComponent<BacktestRunner>();
        runner._selectedMapIndices  = selectedMapIndices;
        runner._reps                = Mathf.Max(1, reps);
        runner._dynamicObstacles    = dynamicObstacles;
    }

    private List<int> _selectedMapIndices;
    private int       _reps = Reps;
    private bool      _dynamicObstacles;
    private DynamicObstacleSpawner _obstacleSpawner;
```
bằng:
```csharp
    public const int DefaultAgentCount = 4; // = số ô spawn cố định cũ

    public static void Launch(List<int> selectedMapIndices = null, int reps = Reps,
                              bool dynamicObstacles = false, int agentCount = DefaultAgentCount)
    {
        if (_instance != null) return;
        var go = new GameObject("BacktestRunner");
        var runner = go.AddComponent<BacktestRunner>();
        runner._selectedMapIndices  = selectedMapIndices;
        runner._reps                = Mathf.Max(1, reps);
        runner._dynamicObstacles    = dynamicObstacles;
        runner._agentCount          = Mathf.Clamp(agentCount, 1, 20);
    }

    private List<int> _selectedMapIndices;
    private int       _reps = Reps;
    private bool      _dynamicObstacles;
    private int       _agentCount = DefaultAgentCount;
    private DynamicObstacleSpawner _obstacleSpawner;
```

### M2.4 `BacktestRunner.cs` — truyền count vào `BacktestMode.Activate` (`:168`)
Thay:
```csharp
        BacktestMode.Activate(job.algorithm, job.mapLabel, _dynamicObstacles);
```
bằng:
```csharp
        BacktestMode.Activate(job.algorithm, job.mapLabel, _dynamicObstacles, _agentCount);
```

### M2.5 Ba bootstrap — dùng danh sách ô spawn theo count khi backtest
Trong **cả 3 file** `MapScenarioBootstrap.cs`, `MapScenarioBootstrapPIBT.cs`, `MapScenarioBootstrapPIBT_TCP.cs`:
ở đầu `SpawnEnemies()`, tính danh sách `spawnSeeds`, rồi đổi vòng `for` để lặp theo `spawnSeeds` thay vì `enemySpawnCells`.

**(a)** Ngay trước vòng `for (int i = 0; i < enemySpawnCells.Count; i++)`, thêm:
```csharp
        // Backtest: số agent do người dùng nhập → sinh ô spawn deterministic (công bằng giữa 3 thuật toán).
        // Chơi thường: giữ nguyên enemySpawnCells.
        List<Vector2Int> spawnSeeds =
            (BacktestMode.IsActive && BacktestMode.AgentCount > 0)
                ? BacktestSpawn.GenerateSpawnCells(mapLoader, BacktestMode.AgentCount)
                : enemySpawnCells;
```
**(b)** Đổi đúng dòng vòng lặp:
- `MapScenarioBootstrap.cs:358` và `MapScenarioBootstrapPIBT.cs:299` và `MapScenarioBootstrapPIBT_TCP.cs:757`:
  - `for (int i = 0; i < enemySpawnCells.Count; i++)` → `for (int i = 0; i < spawnSeeds.Count; i++)`
  - bên trong, `enemySpawnCells[i]` → `spawnSeeds[i]`
> 3 file đều `using System.Collections.Generic;` sẵn (đã có `List`). Không thêm using mới.
> Không đổi gì khác trong `SpawnEnemies`. Với PIBT-C++, `_agents.Count` sau spawn = số agent thực → `ConnectAndHello` gửi đúng số cho server (không cần sửa thêm).

### M2.6 `BacktestConfigUI.cs` — thêm stepper "Agents per map"
Mẫu y hệt `BuildRepsStepper`. Đặt ngay **dưới** "Runs per map", **trên** toggle dynamic-obstacle.

**(a)** GIỮ NGUYÊN `HeaderH = 124f` và GIỮ NGUYÊN vị trí "Runs per map" + toggle dynamic. Stepper "Agents" nằm **cùng hàng** với "Runs per map" (y = -72), đặt vào **vùng trống bên phải** (ô đỏ user khoanh) → không cần nới panel, không dời hàng nào.

**(b)** Thêm state (cạnh `_reps`/`_repsTxt`, khoảng `:38-39`):
```csharp
    private static int        _reps = BacktestRunner.Reps;
    private static Text       _repsTxt;
```
→ thêm 2 dòng:
```csharp
    private static int        _reps = BacktestRunner.Reps;
    private static Text       _repsTxt;
    private static int        _agentCount = BacktestRunner.DefaultAgentCount;
    private static Text       _agentTxt;
```

**(c)** Reset `_agentCount` trong `Show()` (cạnh `_reps = BacktestRunner.Reps;`, `:53`):
```csharp
        _reps    = BacktestRunner.Reps;
```
→
```csharp
        _reps        = BacktestRunner.Reps;
        _agentCount  = BacktestRunner.DefaultAgentCount;
```

**(d)** KHÔNG dời hàng nào (reps giữ y `-72` ở `BuildRepsStepper:327`, toggle dynamic giữ y `-98` ở `:368`). Chỉ **gọi thêm** `BuildAgentStepper` — nó tự đặt cùng hàng reps, lệch sang phải. Trong `BuildHeader`, sau `BuildRepsStepper(panel, layer);` (`:135`) thêm dòng:
  ```csharp
        BuildAgentStepper(panel, layer);
  ```

**(e)** Thêm method mới `BuildAgentStepper` (copy `BuildRepsStepper`, đổi label/biến/handler/vị trí y = -90). Dán ngay sau `BuildRepsStepper`:
```csharp
    // ── Agent-count stepper ─────────────────────────────────────────────────
    // Cùng hàng với "Runs per map" (y = -72), đặt vào vùng trống bên phải (ô đỏ).
    // Row neo theo tâm panel (anchor 0.5), pivot mép trái, đẩy 96px sang phải tâm:
    // mép trái row ≈ center+96, rộng 210 → nằm gọn ở 1/3 phải header.
    private static void BuildAgentStepper(GameObject panel, int layer)
    {
        var row  = Child(panel, "AgentsRow", layer);
        var rRt  = row.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(0.5f, 1f); rRt.anchorMax = new Vector2(0.5f, 1f);
        rRt.pivot = new Vector2(0f, 1f);                 // pivot mép trái
        rRt.anchoredPosition = new Vector2(96f, -72f);   // cùng hàng reps, lệch phải
        rRt.sizeDelta = new Vector2(210f, 22f);

        // Label ngắn "Agents:" cho vừa ô đỏ
        var lbl  = Child(row, "Lbl", layer);
        var lRt  = lbl.GetComponent<RectTransform>();
        lRt.anchorMin = new Vector2(0f, 0f); lRt.anchorMax = new Vector2(0f, 1f);
        lRt.pivot = new Vector2(0f, 0.5f);
        lRt.anchoredPosition = Vector2.zero; lRt.sizeDelta = new Vector2(64f, 0f);
        var lTxt = lbl.AddComponent<Text>();
        lTxt.text = "Agents:"; lTxt.font = Fnt(); lTxt.fontSize = 13;
        lTxt.color = TextMuted; lTxt.alignment = TextAnchor.MiddleLeft;

        StepBtn(row, layer, "AMinus", "−", new Vector2(66f, 0f), new Vector2(22f, 22f),
            () => ChangeAgents(-1));

        var cnt  = Child(row, "ACount", layer);
        var cRt  = cnt.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0f, 0f); cRt.anchorMax = new Vector2(0f, 1f);
        cRt.pivot = new Vector2(0f, 0.5f);
        cRt.anchoredPosition = new Vector2(92f, 0f); cRt.sizeDelta = new Vector2(34f, 0f);
        _agentTxt = cnt.AddComponent<Text>();
        _agentTxt.text = _agentCount.ToString(); _agentTxt.font = Fnt(); _agentTxt.fontSize = 15;
        _agentTxt.fontStyle = FontStyle.Bold; _agentTxt.color = AccentGold;
        _agentTxt.alignment = TextAnchor.MiddleCenter;

        StepBtn(row, layer, "APlus", "+", new Vector2(130f, 0f), new Vector2(22f, 22f),
            () => ChangeAgents(+1));
    }

    private static void ChangeAgents(int delta)
    {
        _agentCount = Mathf.Clamp(_agentCount + delta, 1, 20);
        if (_agentTxt != null) _agentTxt.text = _agentCount.ToString();
    }
```

**(f)** Truyền count khi start. `StartBacktest` (`:310-317`):
```csharp
        Close();
        BacktestRunner.Launch(indices, _reps, dyn);
```
→
```csharp
        int agents = _agentCount;
        Close();
        BacktestRunner.Launch(indices, _reps, dyn, agents);
```

### M2.7 Test M2
1. Mở panel → **cùng hàng** với "Runs per map", phía bên phải (đúng ô đỏ user khoanh) hiện "Agents: [−] 4 [+]". Bấm +/− chỉnh 1–20, không đè lên reps stepper, không tràn mép panel.
2. Đặt Agents = 6, chọn 1 map, reps = 1, START.
3. Quan sát panel realtime (góc phải) `Agents: N` và HUD `ENEMY: N` ≈ 6 cho **cả 3 thuật toán** (A*, PIBT, PIBT_TCP) trên cùng map.
4. Console: `[BacktestRunner] Injected: ... agentsA/agentsL/agentsT` khớp số đã nhập (theo từng scene).
5. PIBT-C++: log `[PIBT_TCP] ConnectAndHello start. ... agents=6` → server nhận đúng 6. (Lưu ý mục Rủi ro R2 nếu server giới hạn agent.)
6. Để Agents = 4 (mặc định) → kết quả phải giống hệt trước khi sửa (4 enemy quanh map).

---

## M3 — CSV thêm máu Base Eagle (HP cuối + HP max + % mất)

> Cột `EagleHP` (HP cuối) **đã có** trong summary CSV. M3 bổ sung `EagleHPMax` và `EagleHPLostPct` để CSV tự diễn giải và phục vụ biểu đồ ở M4. Lý do tách HP max: lúc `RecordRun`, GameObject Eagle có thể đã bị Destroy (HP=0) nên phải chụp max HP từ `InjectScene`.

### M3.1 `BacktestData.cs` — thêm field `eagleHpMax`
`:9`:
```csharp
    public int    rep, eagleHpAtEnd, enemiesAliveAtEnd, agentCount;
```
→
```csharp
    public int    rep, eagleHpAtEnd, eagleHpMax, enemiesAliveAtEnd, agentCount;
```

### M3.2 `BacktestRunner.cs` — chụp HP max ở `InjectScene`
Thêm field (cạnh `_eagleDamagable`, `:65`):
```csharp
    private Damagable              _eagleDamagable;
```
→
```csharp
    private Damagable              _eagleDamagable;
    private int                    _eagleMaxHp;
```
Trong `InjectScene`, ngay sau khối subscribe eagle death (sau dòng `:268` `}` đóng `else`), thêm:
```csharp
        _eagleMaxHp = _eagleDamagable != null ? Mathf.Max(0, _eagleDamagable.MaxHealth) : 0;
```
> Đặt sau khi đã tìm `_eagleDamagable` (sau block `if (_eagleDamagable != null) { ... } else { ... }`).

### M3.3 `BacktestRunner.cs` — ghi `eagleHpMax` vào record (`RecordRun`, `:374-383`)
Thêm dòng vào khởi tạo `rec`:
```csharp
            eagleHpAtEnd = _eagleDamagable != null ? Mathf.Max(0, _eagleDamagable.Health) : -1,
```
→
```csharp
            eagleHpAtEnd = _eagleDamagable != null ? Mathf.Max(0, _eagleDamagable.Health) : -1,
            eagleHpMax   = _eagleMaxHp,
```

### M3.4 `BacktestRunner.cs` — thêm cột vào summary CSV (`ExportCSV`, `:474` & `:478-482`)
Header (`:474`):
```csharp
        sb.AppendLine("Run,Map,Algorithm,Rep,Outcome,Duration_s,EagleHP,AgentCount,EnemiesAlive,TotalReplans,TotalRecoveries,TotalShots,TotalCells");
```
→
```csharp
        sb.AppendLine("Run,Map,Algorithm,Rep,Outcome,Duration_s,EagleHP,EagleHPMax,EagleHPLostPct,AgentCount,EnemiesAlive,TotalReplans,TotalRecoveries,TotalShots,TotalCells");
```
Dòng dữ liệu (`:478-482`):
```csharp
            sb.AppendLine(string.Join(",",
                i + 1, r.map, r.algorithm, r.rep, r.outcome,
                r.duration.ToString("F2"), r.eagleHpAtEnd, r.agentCount,
                r.enemiesAliveAtEnd, r.totalReplans, r.totalRecoveries,
                r.totalShots, r.totalCells));
```
→
```csharp
            float hpLostPct = (r.eagleHpMax > 0 && r.eagleHpAtEnd >= 0)
                ? (1f - (float)r.eagleHpAtEnd / r.eagleHpMax) * 100f
                : 0f;
            sb.AppendLine(string.Join(",",
                i + 1, r.map, r.algorithm, r.rep, r.outcome,
                r.duration.ToString("F2"), r.eagleHpAtEnd, r.eagleHpMax,
                hpLostPct.ToString("F1"), r.agentCount,
                r.enemiesAliveAtEnd, r.totalReplans, r.totalRecoveries,
                r.totalShots, r.totalCells));
```

### M3.5 Test M3
- Chạy backtest 1 map, mở `BacktestResults/backtest_summary_*.csv`:
  - Có đủ cột `EagleHP, EagleHPMax, EagleHPLostPct`.
  - `EagleHPMax` ≈ 500 (giá trị `eagleHealth`); outcome `EagleDestroyed` → `EagleHP=0`, `EagleHPLostPct=100.0`.

---

## M4 — Biểu đồ so sánh máu Base Eagle qua từng thuật toán

3 nơi vẽ chart; làm theo thứ tự ưu tiên: M4.1 (in-game, hiện ngay) → M4.2 (Python PNG/HTML, file xuất) → M4.3 (HTML fallback C#, chỉ khi không có Python).

> **Hướng "thắng" (best) của metric Eagle HP:** mặc định plan dùng **HP thấp = tốt hơn** (`lowerBetter=true`) — vì thuật toán điều khiển ENEMY tấn công Base, HP cuối càng thấp nghĩa là enemy gây nhiều damage hơn. Nếu muốn khung "phòng thủ" (HP cao = tốt) thì đổi `lowerBetter` → `false` (chỉ 1 chỗ ở M4.1, và đảo `reverse`/màu nếu cần ở Python). **Đây là chỗ duy nhất cần quyết định — đổi 1 dòng.**

### M4.1 `BacktestResultChart.cs` — thêm metric Eagle HP (in-game)
`Metrics` (`:63-68`):
```csharp
    private static readonly Metric[] Metrics =
    {
        new Metric { label="Average time (s)", get=r=>r.duration,    maxHint=180f, lowerBetter=true  },
        new Metric { label="Total replans",    get=r=>r.totalReplans,maxHint=0,    lowerBetter=false },
        new Metric { label="Total shots",      get=r=>r.totalShots,  maxHint=0,    lowerBetter=false },
    };
```
→ thêm 1 metric:
```csharp
    private static readonly Metric[] Metrics =
    {
        new Metric { label="Average time (s)", get=r=>r.duration,    maxHint=180f, lowerBetter=true  },
        new Metric { label="Total replans",    get=r=>r.totalReplans,maxHint=0,    lowerBetter=false },
        new Metric { label="Total shots",      get=r=>r.totalShots,  maxHint=0,    lowerBetter=false },
        new Metric { label="Eagle HP cuối",    get=r=>r.eagleHpAtEnd,maxHint=0,    lowerBetter=true  },
    };
```
> Layout tự co giãn theo `Metrics.Length` (đã có scroll). `eagleHpAtEnd` (int) tự ép sang float qua `get`. `maxHint=0` → auto-scale (HP max ~500). Bảng summary cuối chart cũng tự thêm cột (loop theo `Metrics.Length`). Không cần sửa layout cứng.
> (Tùy chọn nhỏ) Nếu muốn thêm cả "Cells traveled" cho khớp Python, thêm metric tương tự `get=r=>r.totalCells` — không bắt buộc.

### M4.2 `Tools/backtest_plot_report.py` — thêm Eagle HP + đổi lưới subplot
**(a)** `METRICS` (`:20-25`) — đọc trực tiếp cột CSV `EagleHP` (đã có sau M3):
```python
METRICS = [
    ("Duration_s", "Thoi gian TB (s)", True),
    ("TotalReplans", "Replan tong", False),
    ("TotalShots", "Tong so shot", False),
    ("TotalCells", "Cells da di", False),
    ("EagleHP", "Mau Base Eagle", True),
]
```
**(b)** `plot_png` — lưới `2x2` không đủ cho 5 metric → đổi sang `2x3` và ẩn ô thừa. Tại `:77`:
```python
    fig, axes = plt.subplots(2, 2, figsize=(14, 8), constrained_layout=True)
    fig.patch.set_facecolor("#0e1014")
    axes = axes.flatten()
```
→
```python
    ncols = 3
    nrows = (len(METRICS) + ncols - 1) // ncols
    fig, axes = plt.subplots(nrows, ncols, figsize=(6 * ncols, 4 * nrows), constrained_layout=True)
    fig.patch.set_facecolor("#0e1014")
    axes = axes.flatten()
    # Ẩn các ô subplot dư (khi số metric không lấp đầy lưới)
    for j in range(len(METRICS), len(axes)):
        axes[j].set_visible(False)
```
> Phần còn lại của `plot_png` lặp `for metric_idx, (...) in enumerate(METRICS)` nên tự xử lý 5 metric. `build_html` lặp `METRICS` → bảng tự thêm cột "Mau Base Eagle". Không cần sửa thêm.

### M4.3 `BacktestRunner.cs` — HTML fallback (chỉ chạy khi thiếu Python/matplotlib)
Trong `BuildHTML` (`:712`), mở rộng `NM=4` → `5` để có thêm Eagle HP.
- `:723`: `const int NM = 4;` → `const int NM = 5;`
- Khối cộng dồn (`:730-733`): thêm dòng cho index 4:
  ```csharp
            sums[k][0] += r.duration;      cnts[k][0]++;
            sums[k][1] += r.totalReplans;  cnts[k][1]++;
            sums[k][2] += r.totalShots;    cnts[k][2]++;
            sums[k][3] += r.totalCells;    cnts[k][3]++;
  ```
  →
  ```csharp
            sums[k][0] += r.duration;      cnts[k][0]++;
            sums[k][1] += r.totalReplans;  cnts[k][1]++;
            sums[k][2] += r.totalShots;    cnts[k][2]++;
            sums[k][3] += r.totalCells;    cnts[k][3]++;
            sums[k][4] += r.eagleHpAtEnd;  cnts[k][4]++;
  ```
- `:743-744`: thêm nhãn + hướng tốt:
  ```csharp
        string[] metLabels    = { "Average time (s)", "Total replans", "Total shots", "Cells traveled" };
        bool[]   lowerBetter  = { true, false, false, false };
  ```
  →
  ```csharp
        string[] metLabels    = { "Average time (s)", "Total replans", "Total shots", "Cells traveled", "Eagle HP cuối" };
        bool[]   lowerBetter  = { true, false, false, false, true };
  ```
> Các vòng vẽ/bảng trong `BuildHTML` chạy theo `NM` → tự thêm section + cột. Không cần sửa thêm.

### M4.4 Test M4
1. Chạy backtest ≥2 thuật toán trên ≥1 map, reps ≥1.
2. **In-game**: sau khi xong, chart hiện section thứ 4 "Eagle HP cuối" với 3 cột A*/PIBT/PIBT-C++; bảng summary có thêm cột này; cuộn được.
3. **Python**: `BacktestResults/backtest_chart_*.png` có 5 ô, ô "Mau Base Eagle" hiển thị; `*.html` bảng có cột tương ứng. (Cần `python` + `matplotlib`; nếu thiếu sẽ rơi về M4.3.)
4. **Fallback C#**: tạm đổi tên `Tools/backtest_plot_report.py` để ép fallback → mở `backtest_chart_*.html` thấy section "Eagle HP cuối". (Nhớ đổi tên lại.)

---

## C. THỨ TỰ THỰC HIỆN & CHECKPOINT
1. **M1** (timeout) — nhanh, ít rủi ro. Compile + chạy thử 1 run Timeout.
2. **M2** (agent count) — phần lớn nhất. Làm đúng thứ tự: M2.1 → M2.2 → M2.3 → M2.4 → M2.5 (3 file) → M2.6 → test M2.7.
3. **M3** (CSV) — phụ thuộc không gì, nhưng nên sau M2 để test chung.
4. **M4** (chart) — phụ thuộc M3 (cột `EagleHP`/dữ liệu). Làm M4.1 trước (thấy ngay), rồi M4.2, M4.3.
5. Chạy **1 backtest tổng** (2–3 map, 2–3 thuật toán, agents=6, reps=2) làm nghiệm thu cuối: kiểm UI, HUD số enemy, CSV 3 cột HP mới, chart 4 section in-game + PNG 5 ô.

---

## D. RỦI RO & LƯU Ý
- **R1 — Layout panel config:** stepper "Agents" đặt cùng hàng reps (y `-72`), neo phải (`anchoredPosition.x = 96`, rộng 210). Nếu đè lên nút [+] của reps hoặc tràn mép phải panel (PanelW=640), tinh chỉnh `x` (96±) và/hoặc `sizeDelta.x`. KHÔNG đổi `HeaderH`, KHÔNG dời reps/toggle.
- **R2 — Server PIBT-C++ với nhiều agent:** số agent gửi server = số thực spawn. Nếu server (`Server-PIBT-TeamNoMan-sSky`) giới hạn agent hoặc map dày khiến PIBT không giải được, đặt trần `_agentCount` thấp hơn (sửa clamp ở M2.3) hoặc test PIBT-C++ với count vừa phải trước. Map maze-128 + nhiều agent là case nặng nhất.
- **R3 — Spawn trên map maze/dày:** seed quanh chu vi có thể rơi vào tường; `TryFindWalkableNear` + `TryFindAvailableSpawnNear` (tự quét toàn map) sẽ nắn về ô hợp lệ, nên số spawn thực có thể < count nếu map quá chật. Chấp nhận được; record dùng số thực. Nếu cần đúng tuyệt đối count, tăng vùng tìm.
- **R4 — Công bằng:** vì `GenerateSpawnCells` deterministic theo (kích thước map, count) và cả 3 scene dùng cùng map → 3 thuật toán nhận cùng tập ô. **Không** đưa `Random`/thời gian vào bộ sinh.
- **R5 — Hướng "best" của Eagle HP:** xem ghi chú đầu M4. Quyết định 1 dòng `lowerBetter`. Mặc định plan: HP thấp = tốt (khung tấn công).
- **R6 — Chế độ chơi thường:** mọi nhánh count chỉ chạy khi `BacktestMode.IsActive && AgentCount > 0`. Khi chơi thường `AgentCount=0` → dùng `enemySpawnCells` cũ → không regression.

---

## E. TÓM TẮT THAY ĐỔI (checklist)
- [ ] M1: `RunTimeoutSec=180f`; comment; `maxHint=180f` (chart time).
- [ ] M2: `BacktestMode.AgentCount`; tạo `BacktestSpawn.cs`; `Launch(...,agentCount)`; `Activate(...,_agentCount)`; 3 bootstrap dùng `spawnSeeds`; stepper "Agents per map" trong config UI.
- [ ] M3: `BacktestRunRecord.eagleHpMax`; chụp `_eagleMaxHp`; ghi record; 3 cột CSV mới.
- [ ] M4: metric "Eagle HP cuối" in-game; Python METRICS + lưới 2x3; HTML fallback NM=5.
- [ ] Nghiệm thu tổng: UI + HUD + CSV + chart đủ 3 thuật toán.
