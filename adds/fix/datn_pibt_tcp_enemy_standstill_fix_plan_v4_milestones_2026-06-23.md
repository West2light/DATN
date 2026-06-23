# PLAN V4 (MILESTONES, IMPLEMENT-READY): Fix Enemy đứng yên / đi sai hướng — PIBT TCP

Ngày lập: 2026-06-23
Mục đích: chia nhỏ thành các milestone độc lập, có code cụ thể, để **model năng lực thấp hơn
(Claude Sonnet 4.6, Gemini 3.1 Pro) implement được từng bước mà không cần suy luận nhiều.**
Tham chiếu phân tích gốc: `datn_pibt_tcp_enemy_standstill_consolidated_root_cause_fix_plan_v3_2026-06-23.md`.

> Đọc V3 để hiểu "tại sao". Đọc V4 để biết "làm gì, sửa dòng nào". V4 này tự chứa đủ chi tiết.

---

## A. QUY ƯỚC CHUNG CHO NGƯỜI IMPLEMENT (đọc trước, bắt buộc)

1. **CHỈ được sửa 3 file TCP** (và `MapLoader.cs` nếu một milestone nói rõ):
   - `Assets/Scripts/PIBTTcpClient.cs`
   - `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
   - `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs`
   - **KHÔNG** đụng `GridEnemyAgentPIBT.cs`, `MapScenarioBootstrapPIBT.cs`, `GridEnemyAgent.cs`,
     `GridAStarPathfinder.cs` (đó là mode A* / local PIBT, không liên quan và dễ gây regression).
2. **Làm tuần tự từng milestone.** Mỗi milestone phải compile sạch + test pass trước khi sang milestone sau.
3. **Sau mỗi lần sửa code phải kiểm tra biên dịch:**
   - Nếu có Unity MCP: `validate_script` cho file vừa sửa, rồi `read_console` lọc error. Console phải 0 error.
   - Nếu không có MCP: yêu cầu user mở Unity Editor để Unity compile, rồi đọc Console.
4. **Không đổi behavior của map A\*** và **không xoá log `[PIBT_TCP]` hiện có**.
5. **Mỗi đoạn code dưới đây là "final form" của một method** — tìm method cùng tên trong file và thay nguyên
   khối, trừ khi ghi rõ "thêm mới". Giữ nguyên `using`, namespace, các method khác.
6. **Convention đã đúng, đừng đổi:** orientation `0=east, 1=south, 2=west, 3=north`; flat index
   `loc = localRow * cols + localCol`, `localRow = cell.y - BuildStartY`, `localCol = cell.x - BuildStartX`.
   Đã verify `CellToWorld`/`OrientationToDelta`/`AgentFlat`/`EagleFlat`/`FlatToCell` nhất quán. **Không "sửa" toạ độ.**

---

## B. BỐI CẢNH CODE HIỆN TẠI (tóm tắt, có số dòng tham chiếu)

`MapScenarioBootstrapPIBT_TCP.cs`:
- `Update()` (≈200–213): mỗi `tcpTickInterval` gọi `DoStepAsync()`, gate bằng `_serverReady/_stepInFlight`.
- `DoStepAsync()` (≈309–366): chạy `PlanStep` trên thread nền, rồi `ApplyStepActions(actions, rows, cols)`.
- `BuildStepData()` (≈368–375): gửi `loc=AgentFlat`, `orientation=AgentOrientation` (logic), `goalLoc=EagleFlat` (chung).
- `ApplyStepActions()` (≈377–385) → `ApplyAction()` (≈425–450): **dead-reckon** cell kế tiếp từ orientation; CR/CCR
  chỉ tăng counter và đứng im; **không dùng `nextLoc` của server**.
- Helpers có sẵn: `FlatToCell(flat, cols)` (≈504), `IsInsideBuild` (≈452), `OrientationToDelta` (≈459),
  `ReadAgentOrientation(agent)` (≈470), `AgentFlat` (≈483), `EagleFlat` (≈495).

`PIBTTcpClient.cs`:
- `PlanStep(...)` (≈141–179) gọi `ParseActions(resp, n)` (≈304) → trả `string[]` action, **bỏ `nextLoc`** dù DTO
  `PlanActionDto` (≈296–302) có field `nextLoc`.

`GridEnemyAgentPIBT_TCP.cs`:
- `Update()` (≈59–83): shooting trước; nếu `_hasTarget` → `SteerTowardTarget()`.
- `SetNextTarget(cell)` (≈91–100), `CurrentCell` (≈102), `SteerTowardTarget()` (≈107–147).

---

## C. QUY TRÌNH TEST CHUNG (mỗi milestone sẽ tham chiếu mục này)

**C.1 — Khởi động server (user chạy trong WSL, 1 lần):**
```
cd ~/projectY/Server-PIBT-TeamNoMan-sSky
./build/pibt_tcp_server --host 0.0.0.0 --port 7777
```
(client Unity đã trỏ `127.0.0.1:7777`). Giữ terminal để đọc log server.

**C.2 — Chạy Single Play 1 map (ưu tiên qua Unity MCP nếu Editor đang mở):**
1. `read_console` clear.
2. Set PlayerPrefs: `SelectedAlgorithm = "PIBT_TCP"`, `SelectedMapFile = "<map>"`.
3. Load scene `Assets/Scenes/MapF_TankTest_PIBT.unity`.
4. Enter Play Mode. Nếu có Placement UI → bấm Skip.
5. Quan sát 20–40s, chụp screenshot, đọc Console + log server.

**C.3 — Danh sách map test (mã PlayerPrefs `SelectedMapFile`):**
| Tên | File |
| --- | --- |
| Alpha-32 (baseline) | `Assets/MapData/random-32-32-10.map` |
| Mansion | `Assets/MapData/ht_mansion_n.map` |
| Maze-128 | `Assets/MapData/maze-128-128-10.map` |
| Chantry | `Assets/MapData/ht_chantry.map` |
| Gallows | `Assets/MapData/lt_gallowstemplar_n.map` |

**C.4 — Đọc evidence:** Console Unity (lọc `[PIBT_TCP]`, `[PIBT_TCP_TRACE]`), terminal server (tìm
`sanitize`, `wrap`, `blocked`, `W`), screenshot vị trí tank.

---

## D. MILESTONES

> Thứ tự bắt buộc: **M0 → M1 → M2 → M3** (lõi). Sau đó test; nếu map hẹp vẫn kẹt thì làm **M4 → M5**.
> **M6** chỉ khi evidence chỉ ra lỗi server. **M7** = validation tổng + xuất số liệu backtest.

---

### M0 — Instrumentation (thêm log, KHÔNG đổi behavior)

**Mục tiêu:** có log per-tick để biết server trả `FW/CR/CCR/W` bao nhiêu, `nextLoc` là gì, so với cell hiện tại.
Đây là cơ sở để xác nhận các milestone sau thật sự có tác dụng.

**Files:** `MapScenarioBootstrapPIBT_TCP.cs`.

**Thay đổi 1 — thêm field cờ trace.** Tìm block `[Header("Reconnect")]` (≈72) và thêm NGAY TRÊN nó:
```csharp
    [Header("Debug")]
    [Tooltip("Bật log chi tiết action/nextLoc mỗi tick cho TCP PIBT.")]
    public bool enableTcpTrace = false;
    private int _traceTickCount;
```

**Thay đổi 2 — log action-counts mỗi tick.** Trong `DoStepAsync()`, ngay SAU dòng
`_reconnectAttempts = 0;` (gần cuối, trước `ApplyStepActions(...)`), thêm:
```csharp
        if (enableTcpTrace) LogTickTrace(actions, client != null ? client.LastNextLocs : null, rows, cols);
```
> Lưu ý: `client.LastNextLocs` chỉ tồn tại sau M1. Ở M0, tạm log không có nextLoc:
> dùng `LogTickTrace(actions, null, rows, cols);` rồi M1 sẽ nâng cấp. (Để tránh phải sửa 2 lần, có thể làm
> M0 và M1 liền nhau — xem ghi chú cuối M1.)

**Thay đổi 3 — thêm method `LogTickTrace` (thêm mới, đặt cạnh `ApplyStepActions`):**
```csharp
    private void LogTickTrace(string[] actions, int[] nextLocs, int rows, int cols)
    {
        _traceTickCount++;
        bool firstTen = _traceTickCount <= 10;
        bool every30  = _traceTickCount % 30 == 0;
        if (!firstTen && !every30) return;

        int fw = 0, cr = 0, ccr = 0, w = 0, other = 0;
        for (int i = 0; i < actions.Length; i++)
        {
            switch (actions[i])
            {
                case "FW": fw++; break;
                case "CR": cr++; break;
                case "CCR": ccr++; break;
                case "W": w++; break;
                default: other++; break;
            }
        }
        Debug.Log($"[PIBT_TCP_TRACE] tick={_traceTickCount} FW={fw} CR={cr} CCR={ccr} W={w} other={other}");

        int n = Mathf.Min(_agents.Count, actions.Length);
        for (int i = 0; i < n; i++)
        {
            if (_agents[i] == null) continue;
            Vector2Int cell = _agents[i].CurrentCell;
            int nl = (nextLocs != null && i < nextLocs.Length) ? nextLocs[i] : -1;
            string nlCell = nl >= 0 ? FlatToCell(nl, cols).ToString() : "n/a";
            Debug.Log($"[PIBT_TCP_TRACE] a={i} cell={cell} ori={AgentOrientation(i)} " +
                      $"action={actions[i]} nextLoc={nl} nextCell={nlCell}");
        }
    }
```

**Test (C.2):** chạy Alpha-32 với `enableTcpTrace=true`. Kỳ vọng Console có `[PIBT_TCP_TRACE]` đếm action.

**Acceptance M0:** compile 0 error; có log action-counts; behavior game KHÔNG đổi (vẫn như trước).

**Rollback:** xoá field + 2 chỗ gọi + method `LogTickTrace`.

---

### M1 — Bám `nextLoc` của server (bỏ dead-reckon cho bước đi) — ƯU TIÊN CAO NHẤT

**Mục tiêu:** dùng cell kế tiếp **server chỉ định** (`nextLoc`) làm target, thay vì Unity tự suy ra. Loại bỏ
sai lệch giữa plan server và execution Unity (RC2). Vẫn giữ logic orientation để server tiếp tục phát rotation.

**Files:** `PIBTTcpClient.cs`, `MapScenarioBootstrapPIBT_TCP.cs`.

#### M1.1 — `PIBTTcpClient.cs`: expose `nextLoc`

**(a) Thêm property.** Ngay dưới `public string LastError { get; private set; }` (≈30) thêm:
```csharp
    // Server-authoritative next cell (build-local flat index) per agent id from the last plan_result.
    // -1 means the server did not provide a usable nextLoc for that agent.
    public int[] LastNextLocs { get; private set; }
```

**(b) Set nó trong `PlanStep`.** Trong `PlanStep(...)`, tìm:
```csharp
        string[] actions = ParseActions(resp, agents.Length);
```
và thêm NGAY DƯỚI:
```csharp
        LastNextLocs = ParseNextLocs(resp, agents.Length);
```

**(c) Thêm method `ParseNextLocs` (thêm mới, đặt ngay dưới `ParseActions`, ≈336):**
```csharp
    internal static int[] ParseNextLocs(string json, int expectedLength)
    {
        var result = new int[expectedLength];
        for (int i = 0; i < expectedLength; i++) result[i] = -1;
        try
        {
            PlanResultDto dto = JsonUtility.FromJson<PlanResultDto>(json);
            if (dto?.actions != null)
            {
                for (int i = 0; i < dto.actions.Length; i++)
                {
                    PlanActionDto entry = dto.actions[i];
                    if (entry == null) continue;
                    int idx = entry.id >= 0 && entry.id < expectedLength ? entry.id : i;
                    if (idx >= 0 && idx < expectedLength) result[idx] = entry.nextLoc;
                }
            }
        }
        catch (System.Exception) { /* leave as -1 → coordinator falls back to dead-reckon */ }
        return result;
    }
```
> Ghi chú: nếu server không gửi `nextLoc`, `JsonUtility` để `nextLoc=0`. Bước đi tới cell 0 hầu như luôn
> không kề cell hiện tại nên sẽ bị guard ở M1.2 loại bỏ → tự fallback dead-reckon. An toàn.

#### M1.2 — `MapScenarioBootstrapPIBT_TCP.cs`: dùng `nextLoc`

**(a) Truyền `nextLocs` xuống.** Trong `DoStepAsync()`, thay:
```csharp
        _reconnectAttempts = 0;
        ApplyStepActions(actions, rows, cols);
        _stepInFlight = false;
```
bằng:
```csharp
        _reconnectAttempts = 0;
        int[] nextLocs = client != null ? client.LastNextLocs : null;
        if (enableTcpTrace) LogTickTrace(actions, nextLocs, rows, cols);
        ApplyStepActions(actions, nextLocs, rows, cols);
        _stepInFlight = false;
```
> Nếu đã thêm `LogTickTrace(...)` ở M0 với `null`, hãy đổi nó thành dùng `nextLocs` như trên (gộp M0+M1).

**(b) Web path.** Trong `DoStepWeb()` (#if UNITY_WEBGL) tìm `ApplyStepActions(actions, rows, cols);`
và đổi thành `ApplyStepActions(actions, null, rows, cols);` (web giữ dead-reckon, không thuộc phạm vi user).

**(c) Thay `ApplyStepActions` (final form):**
```csharp
    private void ApplyStepActions(string[] actions, int[] nextLocs, int rows, int cols)
    {
        for (int i = 0; i < _agents.Count && i < actions.Length; i++)
        {
            if (_agents[i] == null) continue;
            int nextLoc = (nextLocs != null && i < nextLocs.Length) ? nextLocs[i] : -1;
            Vector2Int nextCell = ApplyAction(i, actions[i], nextLoc, rows, cols);
            _agents[i].SetNextTarget(nextCell);
        }
    }
```

**(d) Thay `ApplyAction` (final form, thêm tham số `serverNextLoc`):**
```csharp
    private Vector2Int ApplyAction(int agentIdx, string action, int serverNextLoc, int rows, int cols)
    {
        int orientation = AgentOrientation(agentIdx);
        Vector2Int cell = _agents[agentIdx].CurrentCell;

        // Keep LOGICAL orientation in sync so the server keeps emitting rotate-then-FW correctly.
        switch (action)
        {
            case "CR":  _agentOrientations[agentIdx] = (orientation + 1) & 3; break;
            case "CCR": _agentOrientations[agentIdx] = (orientation + 3) & 3; break;
            case "FW": case "W": break;
            default:
                Debug.LogWarning($"[PIBT_TCP] Unknown action '{action}' for agent {agentIdx}; waiting.");
                break;
        }

        // Prefer the server-authoritative next cell when valid (same cell or a single 4-neighbour step).
        if (serverNextLoc >= 0)
        {
            Vector2Int serverCell = FlatToCell(serverNextLoc, cols);
            if (IsInsideBuild(serverCell, rows, cols) && IsStepValid(cell, serverCell)
                && (serverCell == cell || mapLoader.IsWalkable(serverCell)))
            {
                return serverCell;
            }
        }

        // Fallback: previous dead-reckon behaviour.
        if (action == "FW")
        {
            Vector2Int next = cell + OrientationToDelta(_agentOrientations[agentIdx]);
            if (IsInsideBuild(next, rows, cols) && mapLoader.IsWalkable(next)) return next;
        }
        return cell; // CR/CCR/W/unknown → stay (rotation handled logically above)
    }
```

**(e) Thêm helper `IsStepValid` (thêm mới, đặt cạnh `IsInsideBuild`):**
```csharp
    private static bool IsStepValid(Vector2Int from, Vector2Int to)
    {
        int d = Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
        return d <= 1; // same cell (rotate/wait) or one cardinal step (FW)
    }
```

**Test (C.2) trên Alpha-32 + Mansion**, `enableTcpTrace=true`. So sánh trong trace:
`nextCell` (từ server) vs `cell`. Kỳ vọng FW: `nextCell` kề `cell`; CR/CCR/W: `nextCell == cell`.

**Acceptance M1:**
- Compile 0 error.
- Alpha-32: ≥ 3 enemy tiếp cận/bắn Eagle (không tệ hơn hiện tại).
- Trace cho thấy khi `action=FW`, target dùng `nextLoc` (nextCell kề cell), không còn lệ thuộc dead-reckon.
- Không xuất hiện agent lao thẳng vào tường do target sai (nếu còn, ghi lại evidence cho M6).

**Rollback:** revert `PlanStep`/`ParseNextLocs`/`LastNextLocs` và đưa `ApplyAction`/`ApplyStepActions` về bản cũ.

---

### M2 — Stuck detection + recovery + diagnostic (cứu backtest khỏi đứng im không lý do)

**Mục tiêu:** không bao giờ để agent đứng im vĩnh viễn không log. Phát hiện kẹt → đánh dấu `NeedsForcedReplan`
(M4 dùng) + last-resort greedy nudge để vẫn sinh dữ liệu di chuyển cho backtest (có log rõ "fallback").

**Files:** `GridEnemyAgentPIBT_TCP.cs`, `MapScenarioBootstrapPIBT_TCP.cs`.

#### M2.1 — `GridEnemyAgentPIBT_TCP.cs`

**(a) Thêm fields.** Dưới block `// ── Runtime state ──` (≈36–40) thêm:
```csharp
    [Header("Stuck recovery")]
    [Min(0.5f)] public float stuckTimeout = 2.5f;     // no cell change while target differs → stuck
    [Min(0.5f)] public float fallbackTimeout = 4.0f;  // longer → do a local greedy nudge
    private Vector2Int _lastCell;
    private float _lastCellChangeTime;
    private bool  _stuckInit;
    [System.NonSerialized] public bool   NeedsForcedReplan;
    [System.NonSerialized] public string LastStallReason = "";
    private static readonly Vector2Int[] Neighbors4 =
        { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
```

**(b) Gọi tracking trong `Update()`.** Ngay sau dòng `if (mapLoader == null || tankController == null) return;`
(đầu `Update`) thêm:
```csharp
        TrackStuckAndRecover();
```

**(c) Thêm các method (thêm mới, đặt dưới `SetNextTarget`):**
```csharp
    private void TrackStuckAndRecover()
    {
        Vector2Int c = CurrentCell;
        if (!_stuckInit) { _stuckInit = true; _lastCell = c; _lastCellChangeTime = Time.time; }
        if (c != _lastCell)
        {
            _lastCell = c;
            _lastCellChangeTime = Time.time;
            btCellsVisited++;                 // count ACTUAL cell changes (was: target changes)
            NeedsForcedReplan = false;
            LastStallReason = "";
            return;
        }

        // Stationary. Only "stuck" if we are *supposed* to be moving (target != current cell) and not shooting.
        if (!_hasTarget || _currentTarget == c || GetShootingTarget() != null) return;

        float still = Time.time - _lastCellChangeTime;
        if (still > stuckTimeout && !NeedsForcedReplan)
        {
            NeedsForcedReplan = true;
            LastStallReason = $"no-progress {still:F1}s (target={_currentTarget}, cell={c})";
            Debug.LogWarning($"[PIBT_TCP] {name} stuck: {LastStallReason} → forced replan");
        }
        if (still > fallbackTimeout)
        {
            // Last-resort: local 1-step greedy toward the eagle so backtest still gets movement data.
            // NOTE: this is a NON-PIBT fallback; logged so it is not mistaken for pure TCP-PIBT behaviour.
            Vector2Int nudged = GreedyStepTowardEagle();
            if (nudged != c)
            {
                _currentTarget = nudged;
                _hasTarget = true;
                btRecoveryCount++;
                _lastCellChangeTime = Time.time; // give the nudge time to execute
                Debug.LogWarning($"[PIBT_TCP] {name} FALLBACK greedy nudge → {nudged}");
            }
        }
    }

    private Vector2Int GreedyStepTowardEagle()
    {
        if (eagleTarget == null || mapLoader == null) return CurrentCell;
        Vector2Int c = CurrentCell;
        Vector2Int goal = mapLoader.WorldToCell(eagleTarget.position);
        Vector2Int best = c;
        int bestDist = int.MaxValue;
        foreach (var d in Neighbors4)
        {
            Vector2Int nb = c + d;
            if (!mapLoader.IsWalkable(nb)) continue;
            int dist = Mathf.Abs(nb.x - goal.x) + Mathf.Abs(nb.y - goal.y);
            if (dist < bestDist) { bestDist = dist; best = nb; }
        }
        return best;
    }

    // Used by the coordinator (M4) and for debugging.
    public bool HasMovementTarget => _hasTarget;
    public Vector2Int MovementTarget => _currentTarget;
```
> Lưu ý: đã chuyển `btCellsVisited++` sang đếm cell thật trong `TrackStuckAndRecover`. Để tránh đếm đôi,
> **xoá** đoạn `if (cell != _currentTarget) { btCellsVisited++; }` trong `SetNextTarget` (giữ lại `btReplanCount++`).
> `SetNextTarget` final form:
```csharp
    public void SetNextTarget(Vector2Int cell)
    {
        btReplanCount++;
        _currentTarget = cell;
        _hasTarget = true;
    }
```

#### M2.2 — `MapScenarioBootstrapPIBT_TCP.cs`: clear cờ sau khi replan

Trong `ApplyStepActions` (đã sửa ở M1), sau `_agents[i].SetNextTarget(nextCell);` thêm:
```csharp
            _agents[i].NeedsForcedReplan = false;
```

**Test (C.2) trên Mansion + Maze-128.** Kỳ vọng: nếu có agent kẹt, Console có `stuck: ... → forced replan`
và/hoặc `FALLBACK greedy nudge`. Không còn agent đứng im > ~4s mà im lặng.

**Acceptance M2:**
- Compile 0 error.
- Không agent nào đứng > 10s mà `LastStallReason` rỗng.
- Có ít nhất vài dòng log stuck/fallback trên map hẹp (chứng tỏ cơ chế chạy).
- Backtest: cột `btCellsVisited`/`btRecoveryCount` phản ánh chuyển động thật.

**Rollback:** xoá fields + `TrackStuckAndRecover`/`GreedyStepTowardEagle`, khôi phục `SetNextTarget` cũ.

---

### M3 — Phân tán goal quanh Eagle (chống PIBT đẩy agent vào tường → fix "3/6 đi sai")

**Mục tiêu:** mỗi agent có **staging goal riêng** (1 ô walkable quanh Eagle), thay vì cả 6 cùng goal = ô Eagle.
Giảm tắc nghẽn PIBT khiến agent ưu tiên thấp bị đẩy ra tường/mép map.

**Files:** `MapScenarioBootstrapPIBT_TCP.cs`.

**(a) Thêm field.** Cạnh `_agentOrientations` (≈87) thêm:
```csharp
    private readonly List<int> _agentGoalFlats = new();
```

**(b) Thêm helper `CellToFlat` + `ComputeStagingGoals` (thêm mới, cạnh `EagleFlat`):**
```csharp
    private int CellToFlat(Vector2Int cell, int rows, int cols)
    {
        int localR = Mathf.Clamp(cell.y - mapLoader.BuildStartY, 0, rows - 1);
        int localC = Mathf.Clamp(cell.x - mapLoader.BuildStartX, 0, cols - 1);
        return localR * cols + localC;
    }

    // One distinct walkable goal cell per agent, clustered around the eagle (same component preferred).
    private void ComputeStagingGoals(int rows, int cols)
    {
        _agentGoalFlats.Clear();
        if (eagleBase == null) return;
        Vector2Int eagleCellGrid = mapLoader.WorldToCell(eagleBase.transform.position);
        var reserved = new HashSet<Vector2Int> { eagleCellGrid };
        for (int i = 0; i < _agents.Count; i++)
        {
            if (mapLoader.TryFindAvailableSpawnNear(eagleCellGrid, reserved, 1, out Vector2Int g))
            {
                reserved.Add(g);
                _agentGoalFlats.Add(CellToFlat(g, rows, cols));
            }
            else
            {
                _agentGoalFlats.Add(EagleFlat(rows, cols));
            }
        }
    }
```

**(c) Gọi `ComputeStagingGoals` trong `ConnectAndHello()`.** Tìm:
```csharp
        string symbols = BuildMapSymbols(width, height);
```
và thêm NGAY DƯỚI:
```csharp
        ComputeStagingGoals(height, width);   // rows=height, cols=width
```

**(d) Dùng goal riêng trong `BuildStepData` (final form):**
```csharp
    private (int id, int loc, int orientation, int goalLoc)[] BuildStepData(int rows, int cols)
    {
        var data = new (int id, int loc, int orientation, int goalLoc)[_agents.Count];
        for (int i = 0; i < _agents.Count; i++)
        {
            int goalFlat = (i < _agentGoalFlats.Count) ? _agentGoalFlats[i] : EagleFlat(rows, cols);
            data[i] = (i, AgentFlat(i, rows, cols), AgentOrientation(i), goalFlat);
        }
        return data;
    }
```

**Test (C.2) trên Alpha-32 + Mansion.** Kỳ vọng: enemy tản đều quanh Eagle thay vì chen 1 ô; số agent tiếp cận
Eagle tăng (mục tiêu ≥ 4/6 trên Alpha-32).

**Acceptance M3:**
- Compile 0 error.
- Alpha-32: ≥ 4/6 enemy tiếp cận/bắn Eagle (cải thiện so với 3/6).
- Không còn cụm agent đâm tường ở mép map do tranh 1 goal (so screenshot trước/sau).

**Rollback:** xoá field + 2 helper + lời gọi; khôi phục `BuildStepData` về `goalLoc=EagleFlat` chung.

---

### M4 — Action-ack gate (tick theo commit, không theo đồng hồ) — bỏ tick xoay phí, đồng bộ map hẹp

**Mục tiêu:** thay vì bắn `plan_step` cứng mỗi `tcpTickInterval`, **bắn ngay khi tất cả agent đã commit action
cũ** (rotation/wait commit tức thì → các tick xoay không còn đứng im 0.5s; FW thì chờ tới nơi). Có time-gate +
forced replan làm trần an toàn, không deadlock.

**Files:** `GridEnemyAgentPIBT_TCP.cs`, `MapScenarioBootstrapPIBT_TCP.cs`.

#### M4.1 — `GridEnemyAgentPIBT_TCP.cs`: thêm `HasCommittedAction()`
Thêm method (cạnh `HasMovementTarget`):
```csharp
    // True when this agent has finished executing the last assigned action:
    //  - shooting (not a movement action), or no target, or
    //  - target == current cell (rotate/wait → instantly committed), or
    //  - movement target reached (arrived within waypoint distance).
    public bool HasCommittedAction()
    {
        if (GetShootingTarget() != null) return true;
        if (!_hasTarget) return true;
        if (_currentTarget == CurrentCell) return true;
        Vector3 tw = mapLoader.CellToWorld(_currentTarget);
        float dist = ((Vector2)tw - (Vector2)tankController.tankMover.transform.position).magnitude;
        return dist <= waypointReachDistanceStraight;
    }
```

#### M4.2 — `MapScenarioBootstrapPIBT_TCP.cs`: gate trong `Update()`
Trong nhánh `#else` (non-WEBGL) của `Update()`, thay:
```csharp
        if (!_serverReady || _client == null || !_client.IsConnected || _stepInFlight) return;
        if (Time.time < _nextTickTime) return;
        _nextTickTime = Time.time + tcpTickInterval;
        StartCoroutine(DoStepAsync());
```
bằng:
```csharp
        if (!_serverReady || _client == null || !_client.IsConnected || _stepInFlight) return;
        bool ready = Time.time >= _nextTickTime || AllAgentsCommitted() || AnyAgentNeedsForcedReplan();
        if (!ready) return;
        _nextTickTime = Time.time + tcpTickInterval;
        StartCoroutine(DoStepAsync());
```
Thêm 2 helper (cạnh `ApplyStepActions`):
```csharp
    private bool AllAgentsCommitted()
    {
        foreach (var a in _agents)
            if (a != null && !a.HasCommittedAction()) return false;
        return true;
    }

    private bool AnyAgentNeedsForcedReplan()
    {
        foreach (var a in _agents)
            if (a != null && a.NeedsForcedReplan) return true;
        return false;
    }
```
> Cơ chế: `_stepInFlight` đảm bảo mỗi lúc chỉ 1 request đang chạy (≈ 1 round-trip mạng). Khi đứng/xoay,
> `AllAgentsCommitted()=true` mỗi frame → tick chạy nhanh theo tốc độ mạng (xoay xong gần như tức thì).
> Khi FW, agent đang đi *chưa* committed → chờ tới nơi hoặc tới time-gate `tcpTickInterval` (trần an toàn).
> Đây là semantics lock-step đúng cho MAPF.

**Khuyến nghị config kèm M4:** đặt `tcpTickInterval` = 0.25 (Inspector hoặc field default) làm trần an toàn
hợp lý; action-ack sẽ tự chạy nhanh hơn khi có thể.

**Test (C.2) trên cả 5 map.** Kỳ vọng: enemy di chuyển mượt/nhanh hơn rõ; trên map hẹp không còn "đi 1 chút
rồi đứng" kéo dài; trace cho thấy tick chạy dày khi xoay, thưa khi đang băng FW.

**Acceptance M4:**
- Compile 0 error.
- Alpha-32 không regression (vẫn ≥ 4/6 tiếp cận).
- Mansion/Maze: ≥ 4/6 agent có `btCellsVisited ≥ 2` trong 20s.
- Không deadlock: không có trạng thái cả team đứng im chờ 1 tank vô hạn (nhờ time-gate + forced replan).

**Rollback:** khôi phục `Update()` cũ; xoá `HasCommittedAction`/`AllAgentsCommitted`/`AnyAgentNeedsForcedReplan`.

---

### M5 — Corridor/clearance hardening (CHỈ làm nếu Maze/Chantry/Gallows vẫn kẹt sau M4)

**Mục tiêu:** xử lý kẹt vật lý ở hành lang hẹp.

**Files:** `MapScenarioBootstrapPIBT_TCP.cs`, `GridEnemyAgentPIBT_TCP.cs`.

**Ý chính (làm theo evidence, không làm mù):**
1. **Spawn clearance:** sau spawn, nếu `Physics2D.OverlapBox` quanh tank chạm tank khác/tường → tìm cell khác
   trong cùng component có chỗ trống. (Thêm guard trong `SpawnEnemies`.)
2. **Steering ở góc lệch lớn:** trong `SteerTowardTarget`, nhánh `dot < partialDriveAlignmentThreshold` hiện đẩy
   `forward=0.1f` (vẫn tiến nhẹ) → đổi thành xoay tại chỗ `forward=0f` để khỏi cọ tường:
   ```csharp
   else
   {
       _partialDriveAcc = 0f;
       tankController.HandleMoveBody(new Vector2(rotation, 0f)); // rotate in place when badly misaligned
   }
   ```
3. **Scuff diagnostic:** nếu cần, log layer của collider chạm để biết kẹt do tường hay do friendly.

**Acceptance M5:** Maze-128/Chantry/Gallows đạt ≥ 4/6 agent `btCellsVisited ≥ 2` trong 20s; không loop recovery vô hạn.

**Rollback:** revert từng thay đổi nhỏ (mỗi cái độc lập).

---

### M6 — Verify/sửa server C++ (CHỈ khi evidence chỉ ra lỗi server)

**Kích hoạt M6 khi:** trace cho thấy `nextLoc` của server (qua `FlatToCell`) trỏ vào **tường của Unity**, hoặc
log server lặp `sanitize/wrap/blocked`, hoặc agent vẫn bị đẩy vào tường dù M3/M4 đã chạy (heuristic không né tường).

**Việc cần làm (đọc trước, sửa sau, tách patch khỏi Unity):**
- Mở `~/projectY/Server-PIBT-TeamNoMan-sSky`. Tìm chỗ:
  - parse `symbols` từ hello (indexing `loc = row*width + col`, row 0 ở trên cùng?);
  - convention orientation (`0=east,1=south,2=west,3=north`?);
  - heuristic của `DefaultPlanner` (BFS né tường hay Manhattan/Euclid?).
- **Invariant kiểm chứng end-to-end:** với 1 agent FW, kiểm tra
  `FlatToCell(serverNextLoc) == CurrentCell + OrientationToDelta(sentOrientation)`. Nếu sai hệ thống → map của
  server bị lật/transpose so với Unity → sửa **một adapter duy nhất** (ưu tiên sửa phía server khi build symbols,
  hoặc phía Unity khi build `symbols`/`AgentFlat`), thêm log before/after.
- Nếu heuristic không né tường → đó là gốc local-minima; cân nhắc bật/đổi planner có BFS-heuristic, hoặc dựa
  M3 (staging) + M2 (fallback) để vẫn lấy được số liệu, ghi rõ trong ĐATN giới hạn của planner.

**Acceptance M6:** invariant pass trên ≥ 3 map; không sanitize-loop vô hạn; agent đi theo `nextLoc` không đâm tường.

---

### M7 — Validation tổng + xuất số liệu Backtest (đóng bug)

**Mục tiêu:** chứng minh 5/5 map chạy được và backtest xuất số liệu hợp lệ cho ĐATN.

**B1 — Static:** `validate_script` cho 3 file TCP (+`MapLoader.cs` nếu sửa). `read_console` errors = 0.

**B2 — Single Play matrix (C.2 cho cả 5 map):** lưu cho mỗi map:
- screenshot 20s,
- extract Console `[PIBT_TCP_TRACE]` (action counts, vài agent),
- extract log server.
Pass mỗi map: `_serverReady=true`, `_frame≥10`, 0 parse/protocol error, ≥4/6 agent `btCellsVisited≥2` trong 20s
(trừ agent đang bắn hợp lệ), ≥1 agent gây damage Eagle/vào range, không agent đứng >10s thiếu `LastStallReason`.

**B3 — Backtest end-to-end:** chạy mode Backtest với algorithm `PIBT_TCP` qua 5 map; kiểm tra `BacktestResults/`:
các cột (path/replan/shots/cells/recovery/win-loss…) khác 0 và hợp lý; schema khớp A*/local PIBT để so sánh được.

**B4 — Regression:** Alpha-32 TCP không tệ hơn baseline; mode A* và local PIBT KHÔNG đổi (không sửa file của chúng).

**B5 — Lưu artifact:** `adds/fix/evidence/pibt_tcp_v4_2026-06-23/` gồm screenshot, console, server log, summary mỗi map.

---

## E. BẢNG TỔNG KẾT THỨ TỰ & PHỤ THUỘC

| MS | Nội dung | Bắt buộc? | File chính | Phụ thuộc |
| --- | --- | --- | --- | --- |
| M0 | Instrumentation (log) | Nên có | Bootstrap | — |
| M1 | Dùng `nextLoc` server | **Bắt buộc** | Client + Bootstrap | M0 (khuyến nghị gộp) |
| M2 | Stuck detect + recovery | **Bắt buộc** | Agent + Bootstrap | M1 |
| M3 | Staging goals quanh Eagle | **Bắt buộc** | Bootstrap | M1 |
| M4 | Action-ack gate | Nên có (tốc độ/sync) | Agent + Bootstrap | M1, M2 |
| M5 | Corridor/clearance | Có điều kiện | Bootstrap + Agent | M4 |
| M6 | Verify/sửa server | Có điều kiện | Server C++ | evidence |
| M7 | Validation + backtest | **Bắt buộc** | — | tất cả |

**Lộ trình tối thiểu để backtest chạy:** M0+M1+M2+M3 → test 5 map → nếu map hẹp còn kẹt thì M4 (+M5) → M7.

---

## F. PHỤ LỤC

**F.1 — Lệnh server (WSL):** `./build/pibt_tcp_server --host 0.0.0.0 --port 7777` (client trỏ `127.0.0.1:7777`).

**F.2 — PlayerPrefs:** `SelectedAlgorithm="PIBT_TCP"`, `SelectedMapFile="Assets/MapData/<file>.map"`.

**F.3 — Scene:** `Assets/Scenes/MapF_TankTest_PIBT.unity`.

**F.4 — Hằng số quan trọng (đừng đổi nếu không có evidence):**
orientation `0=E,1=S,2=W,3=N`; `OrientationToDelta(1)=Vector2Int.up` (grid south); flat
`loc=localRow*cols+localCol`; reach distance `waypointReachDistanceStraight=0.3`.

**F.5 — Rủi ro & nguyên tắc an toàn:**
- Mọi thay đổi khu trú trong 3 file TCP → rollback = revert file.
- `tcpTickInterval` là trần an toàn của M4 — không đặt 0 (sẽ spam). 0.2–0.5 hợp lý.
- Fallback greedy (M2) là NON-PIBT, phải log; khi viết ĐATN ghi rõ tỉ lệ tick dùng fallback để số liệu trung thực.
- Nếu một milestone làm Alpha-32 regression → dừng, revert milestone đó, ghi evidence trước khi đi tiếp.

---

## G. QUAN HỆ VỚI CÁC PLAN KHÁC
- Phân tích root-cause đầy đủ: `..._consolidated_root_cause_fix_plan_v3_2026-06-23.md`.
- V4 này là bản **thực thi theo milestone** của V3. Ba plan ngày 2026-06-22 (mansion / non_alpha / 5map_v2)
  mô tả một bản code cũ đã không còn (có `NotifyServerAction`, `_hasRotationTarget`, `FlatToBuildCell`…),
  **không dùng để implement**; chỉ tham khảo ý tưởng invariant/action-ack/staging goal.
