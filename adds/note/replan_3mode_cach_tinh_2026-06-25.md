# Cách tính Replan cho 3 mode backtest (AStar / PIBT C# / PIBT_TCP)

Ngày đọc code: 2026-06-25
Nhánh nguồn: `feature/fix_backtest_PIBT_TCP` @ `6dba2de`
Phạm vi: cột **Total replans** trong báo cáo backtest (`BacktestResults/*.csv`, `*.html`).

## Tổng quan: replan được cộng dồn ở đâu

Mỗi agent giữ một biến đếm `btReplanCount` (`[NonSerialized] public int`). Khi một lượt backtest kết thúc, `BacktestRunner.RecordResult()` gom tất cả agent đang hoạt động rồi cộng lại:

```text
run.totalReplans = Σ agent.btReplanCount   (cho mọi agent trong lượt chạy)
```

Trong `BacktestRunner` ba nhóm agent tách riêng theo mode: `_agentsA` (AStar), `_agentsL` (PIBT C#), `_agentsT` (PIBT_TCP) — và đều cộng vào cùng `totalReplans` (BacktestRunner.cs:364–367).

Bảng summary trong HTML **không cộng dồn các lần lặp lại** mà lấy **trung bình** `totalReplans` theo nhóm `(Map, Algorithm)` qua các repetition.

Điểm mấu chốt của bản sửa hôm 24/06: chuẩn hoá cadence để cả 3 mode đếm trên **cùng một đơn vị** — "số lần planner được gọi cho 1 agent, đo theo cửa sổ `enemyReplanInterval` = 0.75s". Nhờ vậy cột Total replans giữa 3 thuật toán mới so sánh được với nhau.

## 1. AStar — `GridEnemyAgent`

`btReplanCount++` nằm trong hai hàm:

- `ReplanPath()` (GridEnemyAgent.cs:287)
- `ReplanToDestructible()` (GridEnemyAgent.cs:358)

Mỗi lần tăng đều đặt lại `nextReplanTime = Time.time + replanInterval`.

Các nguồn kích hoạt replan:

- Định kỳ: khi `Time.time >= nextReplanTime` hoặc đường đi rỗng (`currentPath.Count == 0`).
- Gặp blocker động/đồng minh: xoá path và đặt `nextReplanTime = 0` ⇒ frame sau replan ngay.
- Luồng recovery (kẹt): xoá path rồi gọi `ReplanPath()`.
- Gặp vật cản phá được: `ReplanToDestructible()`.

Cadence mặc định: `replanInterval` (đặt qua `MapScenarioBootstrap.enemyReplanInterval`, mặc định 0.75s). Mỗi agent ≈ +1 mỗi 0.75s ⇒ ~40 lần / 30s.

## 2. PIBT C# — `GridEnemyAgentPIBT`

Cơ chế **giống hệt AStar** về mặt đếm:

- `btReplanCount++` trong `ReplanPath()` (GridEnemyAgentPIBT.cs:320) và `ReplanToDestructible()` (line 392).
- Cùng các trigger: định kỳ theo `nextReplanTime`, path rỗng, blocker đồng minh (`nextReplanTime = 0`), recovery, vật cản phá được.
- Cadence từ `MapScenarioBootstrapPIBT.enemyReplanInterval` (0.75s).

Diễn giải đúng: đây là "số lần gọi planner cục bộ phía C#", **không phải** chỉ replan khẩn cấp. Chạy lâu với nhiều agent sẽ tự nhiên có nhiều replan ngay cả khi di chuyển bình thường.

## 3. PIBT_TCP — `GridEnemyAgentPIBT_TCP` + `MapScenarioBootstrapPIBT_TCP`

Đây là mode được sửa hôm 24/06. `btReplanCount` được cộng từ **hai nguồn** vào cùng một biến.

### Nguồn 1 — cadence chuẩn hoá theo cửa sổ (chính)

Server giải tập trung: mỗi `tcpTickInterval` = 0.25s gửi một `plan_step` giải cho **tất cả** agent cùng lúc. Nếu đếm thô mỗi tick thì PIBT_TCP bị thổi phồng 3× so với A*/PIBT (0.25s vs 0.75s).

Vì vậy `AccumulateReplanWindow()` (MapScenarioBootstrapPIBT_TCP.cs:450) gom thời gian tick lại và chỉ +1 cho mỗi agent khi đủ một cửa sổ `enemyReplanInterval` (0.75s):

```text
_replanWindowAccum += tcpTickInterval          // +0.25 mỗi plan_step
if _replanWindowAccum < enemyReplanInterval: return
_replanWindowAccum -= enemyReplanInterval      // tiêu một cửa sổ 0.75s
foreach agent: agent.btReplanCount++           // +1 cho mỗi agent
```

`AccumulateReplanWindow()` được gọi sau mỗi `plan_step` thành công (line 314 và 386). Kết quả: +1/agent mỗi 0.75s — đúng nhịp với A*/PIBT C#.

### Nguồn 2 — replan ép buộc do kẹt (phụ)

Trong `TrackStuckAndRecover()` (GridEnemyAgentPIBT_TCP.cs:129): khi watchdog phát hiện không tiến triển quá `stuckTimeout`, `btReplanCount++` một lần và bật `NeedsForcedReplan`. Hiếm khi xảy ra lúc di chuyển bình thường nên gần như không ảnh hưởng tổng.

## So sánh tính hợp lệ

| Thuật toán | Cách tăng | btReplanCount / agent / 30s |
|---|---|---|
| AStar | +1 mỗi `ReplanPath()` ≈ 0.75s | ~40 |
| PIBT C# | +1 mỗi `ReplanPath()` ≈ 0.75s | ~40 |
| PIBT_TCP | +1 mỗi cửa sổ 0.75s (gom từ tick 0.25s) | ~40 |

Lưu ý cho báo cáo: một `plan_step` của PIBT_TCP giải cho toàn bộ N agent (tập trung), còn A* và PIBT C# replan độc lập từng agent. Chuẩn hoá cadence làm con số so sánh được, nhưng khác biệt kiến trúc này vẫn cần nêu rõ.

## Cảnh báo về dữ liệu cũ

Các file CSV/HTML sinh **trước** bản sửa cadence ngày 24/06 có PIBT_TCP `Replans = 0` (chỉ đếm stuck-replan) — **không dùng để so sánh thuật toán**.

## Phân biệt với "Cells traveled" (để khỏi nhầm)

- AStar / PIBT C#: `btCellsVisited++` khi tới waypoint và `pathIndex++` (tiến độ theo waypoint của path đã hoạch định).
- PIBT_TCP: `btCellsVisited++` khi `CurrentCell` thực sự đổi (vị trí world quy qua `MapLoader.WorldToCell`) — chặt hơn, nhận target mới mà chưa sang ô mới thì không tính.
