# Kế hoạch R3 - Chọn số agent và repetition cho backtest report

Ngày lập: 2026-06-22  
Phạm vi: dữ liệu thực nghiệm cho Chương 5 / R3 của báo cáo DATN  
Repo/nhánh khi khảo sát: `F:\DATN`, branch `report`

## 1. Kết luận ngắn

Với project hiện tại, bộ agent hợp lý nhất để đưa vào report là:

```text
agent_counts = 6, 12, 24, 36, 72
repetitions  = 6 cho bộ chính
```

Lý do:

- Code LAN hiện dùng `EnemyCount = 6 * PlayerCount`, nên các mức `6,12,24,36,72` tương ứng tự nhiên với quy mô 1, 2, 4, 6, 12 người chơi/nhóm spawn giả lập.
- Mốc `6` là baseline gameplay hiện có.
- Mốc `12,24,36` cho thấy xu hướng tăng tải vừa phải.
- Mốc `72` là stress test đủ mạnh nhưng vẫn còn hợp lý hơn mốc 100 vì bám theo logic spawn 6 enemy/player.
- `3 repetition` đủ cho smoke test, nhưng hơi mỏng cho report vì tỷ lệ success/timeout chỉ có các mức 0%, 33%, 67%, 100%.
- `6 repetition` là cân bằng tốt: đủ tính trung bình, độ lệch chuẩn, success rate với bước 16.7%, mà tổng thời gian vẫn có thể chạy qua đêm.
- `12 repetition` tốt hơn về thống kê nhưng không nên chạy full matrix ngay vì số run tăng gấp đôi.

Khuyến nghị chính:

```text
5 map x 3 thuật toán x 5 mức agent x 6 repetition = 450 run
```

Nếu bị giới hạn thời gian, dùng phương án rút gọn:

```text
Core:   5 map x 3 thuật toán x 4 mức agent (6,12,24,36) x 6 rep = 360 run
Stress: 5 map x 3 thuật toán x 1 mức agent (72) x 3 rep       = 45 run
Total: 405 run
```

## 2. Vì sao không chọn 3 rep cho report chính

`3 rep` phù hợp để:

- kiểm tra pipeline chạy được;
- phát hiện lỗi server `PIBT_TCP`;
- xem xu hướng rất sơ bộ;
- tạo ảnh/chart nháp.

Nhưng `3 rep` chưa tốt để kết luận trong báo cáo vì:

- Nếu có 1 run timeout, tỷ lệ timeout nhảy ngay lên 33.3%.
- Nếu seed/spawn hơi lệch, mean duration dễ bị kéo mạnh.
- Khó viết câu kiểu "PIBT ổn định hơn A*" nếu mỗi tổ hợp chỉ có 3 mẫu.

Với `6 rep`:

- Success/timeout có bước 16.7%, dễ diễn giải hơn.
- Có thể báo cáo `mean ± std`.
- Vẫn không quá nặng như `12 rep`.

Với `12 rep`:

- Tốt để xác nhận các kết quả sát nhau hoặc nhiễu cao.
- Không khuyến nghị chạy full ngay: `5 x 3 x 5 x 12 = 900 run`.
- Nên dùng như pass bổ sung cho subset quan trọng.

## 3. Ma trận đề xuất cho report

### Phương án A - Khuyến nghị chính

Chạy full static matrix:

```text
maps = Alpha32, Mansion, Chantry, Gallows, Maze128
algorithms = AStar, PIBT, PIBT_TCP
agent_counts = 6, 12, 24, 36, 72
repetitions = 6
dynamic_obstacles = off
timeout = 120s/run

total = 5 x 3 x 5 x 6 = 450 run
worst_case_time = 450 x 120s = 54,000s ≈ 15 giờ
```

Đây là phương án tốt nhất để đưa vào report nếu có thể chạy qua đêm.

### Phương án B - Rút gọn nhưng vẫn đủ dùng

Chạy full đến 36 agent, còn 72 agent là stress test:

```text
Core:
5 map x 3 thuật toán x 4 agent_counts (6,12,24,36) x 6 rep = 360 run

Stress:
5 map x 3 thuật toán x 1 agent_count (72) x 3 rep = 45 run

Total = 405 run
worst_case_time ≈ 13.5 giờ
```

Phương án này hợp lý nếu lo 72 agent gây timeout nhiều hoặc tốn thời gian.

### Phương án C - Minimum acceptable

Chạy tất cả mức agent nhưng chỉ 3 rep:

```text
5 map x 3 thuật toán x 5 agent_counts x 3 rep = 225 run
worst_case_time ≈ 7.5 giờ
```

Chỉ dùng khi deadline gấp. Trong report phải ghi rõ đây là thực nghiệm định hướng, chưa phải đánh giá thống kê mạnh.

### Phương án D - 12 rep có chọn lọc

Không chạy full 12 rep ngay. Sau khi có kết quả 6 rep, chọn subset để tăng lên 12 rep:

```text
maps = 2 map đại diện
  - 1 map thoáng: Alpha32 hoặc Maze128
  - 1 map hẹp: Mansion hoặc Gallows

agent_counts = 6, 36, 72
algorithms = AStar, PIBT, PIBT_TCP
extra_reps = 6 để nâng từ 6 lên 12

extra = 2 x 3 x 3 x 6 = 108 run
```

Dùng phương án này khi:

- AStar/PIBT/PIBT_TCP có kết quả quá sát nhau;
- timeout rate ở một mức agent dao động mạnh;
- cần thêm bằng chứng cho biểu đồ cuối cùng trong report.

## 4. Thứ tự chạy để công bằng

Không nên chạy hết AStar rồi mới chạy PIBT. Nên giữ cùng seed/spawn set cho cả 3 thuật toán.

Thứ tự đề xuất:

```text
for map in maps:
  for agent_count in [6, 12, 24, 36, 72]:
    for rep in [1..6]:
      seed = stable_hash(map, agent_count, rep)
      run AStar with seed
      run PIBT with same seed
      run PIBT_TCP with same seed
```

Cách này giúp so sánh thuật toán trên cùng điều kiện spawn.

## 5. Cần bổ sung code trước khi chạy đúng matrix

Hiện `BacktestRunner` chỉ có map, algorithm, repetition. `MapTankTestBootstrap.ComputeEnemySpawnCells()` đang dùng 6 spawn cell mặc định khi không ở LAN.

Cần sửa nhỏ:

1. Thêm `agentCount` vào `BacktestRunner.Job`.
2. Thêm list agent-count: `{ 6, 12, 24, 36, 72 }`.
3. Thêm `seed` vào job.
4. Mở rộng `BacktestMode`:

```text
BacktestMode.AgentCount
BacktestMode.Seed
```

5. Trong `ComputeEnemySpawnCells()`:

```text
if BacktestMode.IsActive and BacktestMode.AgentCount > 0:
    needed = BacktestMode.AgentCount
else if LanSessionManager.IsActive:
    needed = LanSessionManager.EnemyCount
else:
    needed = baseSet.Count
```

6. Spawn bổ sung phải deterministic theo seed, không dùng seed cố định `42` cho mọi run.
7. Export thêm vào CSV:

```text
AgentCountRequested
AgentCountActual
Seed
DynamicObstacle
MapFile
```

8. Nếu map không sinh đủ agent, ghi `InvalidSpawnSet` hoặc `AgentCountActual < AgentCountRequested`, không được im lặng coi như hợp lệ.

## 6. Metric đưa vào report

Nên báo cáo theo `map x agent_count x algorithm`:

- `success_rate`: tỷ lệ kết thúc không timeout.
- `timeout_rate`: tỷ lệ timeout.
- `duration_mean ± duration_std`.
- `eagle_hp_mean`.
- `enemies_alive_mean`.
- `total_replans_mean`.
- `total_recoveries_mean`.
- `total_cells_mean`.
- `total_shots_mean`.

Với `6 rep`, bảng report có thể dùng:

```text
mean ± std, n=6
```

Với stress 72 agent nếu chỉ chạy 3 rep, ghi:

```text
stress test, n=3
```

## 7. Bảng nên có trong Chương 5

### Bảng 1 - Cấu hình thực nghiệm

| Thành phần | Giá trị |
| --- | --- |
| Map | Alpha32, Mansion, Chantry, Gallows, Maze128 |
| Thuật toán | AStar, PIBT C#, PIBT TCP/C++ |
| Agent count | 6, 12, 24, 36, 72 |
| Repetition chính | 6 |
| Timeout | 120 giây/run |
| Dynamic obstacle | Tắt trong bảng chính |

### Bảng 2 - Kết quả tổng hợp theo agent count

Mỗi dòng là `agent_count x algorithm`, gộp qua 5 map:

```text
AgentCount | Algorithm | SuccessRate | DurationMean | TimeoutRate | ReplansMean | RecoveriesMean
```

### Bảng 3 - Kết quả theo map

Mỗi dòng là `map x algorithm`, hoặc tách thành các section theo agent count:

```text
Map | AgentCount | Algorithm | SuccessRate | DurationMean±Std | EagleHPMean | EnemiesAliveMean
```

## 8. Biểu đồ nên xuất

Ít nhất 3 hình:

1. `Duration mean` theo `agent_count`, mỗi thuật toán một line.
2. `Timeout rate` theo `agent_count`, mỗi thuật toán một line.
3. `Total replans/recoveries` theo `agent_count`.

Nếu có chỗ, thêm:

4. Heatmap `map x agent_count` cho từng thuật toán.
5. Bar chart so sánh AStar/PIBT/PIBT_TCP ở agent 36 và 72.

## 9. Cách diễn giải trong report

Viết an toàn:

- `6 agent` là baseline của gameplay hiện tại.
- `12-36 agent` là vùng đánh giá chính.
- `72 agent` là stress test để xem hệ thống suy giảm ra sao.
- `6 rep` cho phép quan sát xu hướng và độ ổn định tương đối.
- `12 rep` chỉ dùng để xác nhận subset có nhiễu cao hoặc kết quả sát nhau.

Không nên viết:

- "Thuật toán A tốt hơn tuyệt đối thuật toán B" nếu chỉ dựa trên duration.
- "PIBT TCP/C++ nhanh hơn" nếu chưa tách chi phí TCP/server.
- "72 agent là gameplay thực tế" nếu đây chỉ là stress test.

## 10. Quyết định cuối cùng

Nếu chỉ chọn một cấu hình để làm report:

```text
agent_counts = [6, 12, 24, 36, 72]
repetitions = 6
total = 450 run
```

Nếu cần giảm thời gian:

```text
agent_counts chính = [6, 12, 24, 36], repetitions = 6
agent_counts stress = [72], repetitions = 3
total = 405 run
```

Nếu chỉ cần chạy ngay để có dữ liệu ban đầu:

```text
agent_counts = [6, 12, 24, 36, 72]
repetitions = 3
total = 225 run
```

## 11. Nguồn tham khảo

- MovingAI MAPF benchmarks: https://movingai.com/benchmarks/mapf.html
- Stern et al., "Multi-Agent Pathfinding: Definitions, Variants, and Benchmarks": https://arxiv.org/abs/1906.08291
