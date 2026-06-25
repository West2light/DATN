# Plan: Vì sao "Total replans" của PIBT_TCP bị treo ở 0 (không cập nhật real-time)

Ngày: 2026-06-24
Phạm vi: Backtest `PIBT_TCP` (C++ PIBT qua TCP/WSL), HUD real-time + CSV/HTML report.
Liên quan: [[backtest_total_replans_cells_traveled_note_2026-06-24]] (note giải thích semantics hiện tại).

---

## 1. Triệu chứng

Khi chạy backtest thuật toán **PIBT_TCP** (Map: Chantry, 6 agents), panel real-time hiển thị:

```
PIBT_TCP
Map: Chantry
Agents: 6
Eagle HP: 500
Replans: 0      <-- treo ở 0 suốt cả run
Time: 8s
```

`Replans` không bao giờ tăng dù Unity Client **vẫn đang gọi server PIBT C++ trên WSL** mỗi tick (tank vẫn di chuyển, agents vẫn nhận target mới).

---

## 2. Kết luận sơ bộ (đã xác minh trong code)

**Đây KHÔNG phải lỗi kết nối server.** Unity vẫn nói chuyện với WSL bình thường.
Đây là **lỗi/nhập nhằng về ngữ nghĩa (semantics) của metric `Replans`** đối với PIBT_TCP.

### Luồng dữ liệu của panel

`BacktestRunner.UpdateRealtimePanel()` (`Assets/Scripts/Backtest/BacktestRunner.cs:364-367`):

```csharp
int totalReplans = 0;
foreach (var a in _agentsA) totalReplans += a.btReplanCount;
foreach (var a in _agentsL) totalReplans += a.btReplanCount;
foreach (var a in _agentsT) totalReplans += a.btReplanCount;   // _agentsT = PIBT_TCP agents
...
$"Replans: {totalReplans}\n"
```

Panel chỉ cộng `btReplanCount` của từng agent.

### Vì sao `btReplanCount` của PIBT_TCP đứng yên

`GridEnemyAgentPIBT_TCP` (`Assets/Scripts/GridEnemyAgentPIBT_TCP.cs`):

- Mỗi tick server, coordinator gọi `SetNextTarget()` (dòng 103-107) — **chỉ lưu cell đích, KHÔNG đụng tới `btReplanCount`**.
- `btReplanCount++` chỉ chạy đúng **1 chỗ duy nhất**: trong `TrackStuckAndRecover()` (dòng 127-133), khi agent bị watchdog phát hiện "no-progress":

```text
current cell không đổi
AND target cell khác current cell
AND không đang bắn
AND đứng yên > stuckTimeout (2.5s)
AND NeedsForcedReplan đang false
```

Đây là chủ đích của bản sửa 2026-06-24 (xem note): với PIBT_TCP, `btReplanCount` được định nghĩa lại là **"số lần forced replan do kẹt"**, KHÔNG phải số lần gọi `plan_step` của server C++.

### Hệ quả

- Server C++ thực sự được gọi mỗi `tcpTickInterval` (`DoStepAsync` → `client.PlanStep` → `ApplyStepActions` → `SetNextTarget`, `MapScenarioBootstrapPIBT_TCP.cs:323-382`), nhưng các tick "khỏe mạnh" này **không được đếm**.
- Khi agents di chuyển trơn tru (không kẹt), `btReplanCount` = 0 suốt run → HUD treo ở `Replans: 0` và không bao giờ cập nhật real-time. **Đúng theo thiết kế hiện tại, nhưng gây hiểu lầm.**

### Vấn đề khoa học (quan trọng cho thesis)

So sánh đang **không công bằng (apples-to-oranges)**:

| Thuật toán | `btReplanCount` đếm gì | Giá trị điển hình |
|---|---|---|
| AStar (`GridEnemyAgent`) | mọi lần gọi planner (replan định kỳ + emergency) | hàng trăm |
| PIBT C# (`GridEnemyAgentPIBT`) | mọi lần gọi planner local | hàng trăm |
| **PIBT_TCP** | **chỉ forced replan do kẹt** | **~0** |

→ Trên report HTML/CSV, PIBT_TCP "ăn gian" có rất ít replan, nhưng thực ra server C++ giải lại toàn bộ MAPF mỗi tick. Cần quyết định metric cho đúng và hiển thị real-time.

---

## 3. Mục tiêu của fix

1. HUD `Replans` của PIBT_TCP **cập nhật real-time** (phản ánh hoạt động thật của server).
2. Metric trong CSV/HTML **so sánh được/công bằng** với AStar & PIBT C#, hoặc nếu giữ ngữ nghĩa khác thì **đặt tên rõ ràng** để không gây hiểu lầm.
3. Không phá vỡ ý nghĩa "forced replan do kẹt" đang dùng cho chẩn đoán standstill.

---

## 4. Phương án đã chọn: Cách 1 + Chuẩn hóa nhịp (IMPLEMENTED 2026-06-24)

**Vấn đề của Cách 1 thô:** `tcpTickInterval = 0.25s` vs `replanInterval = 0.75s` → nếu đếm +N mỗi tick thô thì PIBT_TCP có số replan cao gấp ~3× so với A*/PIBT-C# không phải vì tốt hơn mà vì tick nhanh hơn — inflated.

**Giải pháp:** tích lũy thời gian TCP tick, chỉ tăng `btReplanCount` khi tích lũy vượt `enemyReplanInterval` (0.75s). Mỗi lần vượt ngưỡng → `+1` vào `btReplanCount` của từng agent và trừ lại tích lũy (sliding window).

```
Sau mỗi plan_step thành công:
  _replanWindowAccum += tcpTickInterval     // += 0.25s
  if _replanWindowAccum >= enemyReplanInterval (0.75s):
      foreach agent: btReplanCount++        // +1 per agent
      _replanWindowAccum -= 0.75s
```

**Kết quả về đơn vị so sánh** (ví dụ 6 agents, 30s run):
| Thuật toán | Nhịp | `btReplanCount` tổng |
|---|---|---|
| AStar / PIBT C# | +1/agent mỗi 0.75s | 6 × (30/0.75) = **240** |
| PIBT_TCP (sau fix) | +1/agent mỗi 0.75s (chuẩn hóa từ tick 0.25s) | 6 × (30/0.75) = **240** |

→ Cùng đơn vị "số lần một agent được planner xử lý mỗi 0.75s", so sánh trực tiếp được.

**Không cần thay đổi** `BacktestRunner`, `BacktestData`, CSV schema, `backtest_plot_report.py` — vì `btReplanCount` đã được đọc đúng ở tất cả các chỗ.

---

## 5. Các thay đổi đã implement (`MapScenarioBootstrapPIBT_TCP.cs`)

1. **Thêm field** `private float _replanWindowAccum;` (dòng ~322, cạnh `_geometryStallTicks`).
2. **Reset** `_replanWindowAccum = 0f;` trong block teardown/reset (dòng ~1029, sau `_geometryStallTicks = 0`).
3. **Thêm method** `AccumulateReplanWindow()` sau `ApplyStepActions` (dòng ~446):
   - Mỗi lần gọi: `_replanWindowAccum += tcpTickInterval`.
   - Nếu vượt `enemyReplanInterval`: `foreach agent btReplanCount++; _replanWindowAccum -= enemyReplanInterval`.
4. **Gọi `AccumulateReplanWindow()`** ngay sau `ApplyStepActions(...)` trong cả `DoStepAsync` (dòng ~381) và `DoStepWeb` (dòng ~313).

`btReplanCount` của `GridEnemyAgentPIBT_TCP` (forced-replan-do-kẹt) **vẫn giữ nguyên** — nó vẫn tăng trong `TrackStuckAndRecover()` và được cộng vào cùng biến `btReplanCount`. Kết quả cuối = (window replans) + (forced stuck replans), nhưng forced replans rất hiếm nên không ảnh hưởng đáng kể.

---

## 6. Ghi chú cho thesis

Khi trình bày bảng so sánh "Total replans":
> "PIBT_TCP: số lần bộ giải C++ tập trung được gọi, quy đổi về cùng cadence 0.75s/agent với A* và PIBT C# để so sánh công bằng. 1 lần `plan_step` giải cho tất cả N agent đồng thời (centralized), còn A* và PIBT C# mỗi agent replan độc lập."

---

## 7. File / dòng tham chiếu chính

- `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs:103-107` (`SetNextTarget`), `:127-133` (`btReplanCount++` khi kẹt).
- `Assets/Scripts/Backtest/BacktestRunner.cs:358-377` (HUD), `:401-454` (record per-agent), `:483-502` (CSV header).
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs:323-382` (`DoStepAsync`), `:409-419` (`ApplyStepActions`→`SetNextTarget`), `:268-300` (`DoStepWeb`).
- `Assets/Scripts/Backtest/BacktestData.cs:11` (schema record).
- `adds/note/backtest_total_replans_cells_traveled_note_2026-06-24.md` (semantics hiện tại).
