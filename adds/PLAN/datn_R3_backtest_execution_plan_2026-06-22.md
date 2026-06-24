# Kế hoạch R3 - Backtest thực nghiệm A* / PIBT C# / PIBT C++

Ngày lập: 2026-06-22  
Phạm vi: R3 trong `adds/PLAN/datn_latex_report_completion_plan_2026-06-21_v2.md`

## 1. Kết luận tư vấn

Không nên hiểu dãy `1, 3, 5, 30, 50, 100` là thứ tự map. Project hiện có 5 map cố định; dãy này nên là số agent/enemy để khảo sát khả năng scale.

Khuyến nghị chính cho Chương 5:

- Nếu chạy theo code hiện tại, chỉ có thể lấy bộ cơ bản: `5 map x 3 thuật toán x 3 repetition = 45 run`. Đây là dữ liệu tối thiểu, nhưng thực chất đang đo ở số enemy mặc định hiện tại, không chứng minh được scale theo agent count.
- Nếu muốn có bộ kết quả đủ đẹp cho báo cáo, nên chạy ma trận scale: `5 map x 3 thuật toán x 6 mức agent x 3 repetition = 270 run`, với mức agent `1, 3, 5, 30, 50, 100`.
- Nếu cần kết quả mạnh hơn về độ ổn định, dùng `5 repetition`: `5 x 3 x 6 x 5 = 450 run`, nhưng thời gian chạy có thể dài.
- Không nên đưa dynamic obstacle vào ma trận chính. Chạy dynamic obstacle như stress-test phụ, ví dụ `5 map x 3 thuật toán x 3 mức agent (5, 30, 100) x 3 repetition = 135 run`.

Với timeout hiện tại 120 giây/run, worst case:

- 45 run: tối đa khoảng 1.5 giờ.
- 270 run: tối đa khoảng 9 giờ.
- 450 run: tối đa khoảng 15 giờ.

## 2. Cơ sở từ code hiện tại

File liên quan:

- `Assets/Scripts/Backtest/BacktestRunner.cs`
- `Assets/Scripts/Backtest/BacktestConfigUI.cs`
- `Assets/Scripts/Backtest/BacktestData.cs`
- `Assets/Scripts/MapTankTestBootstrap.cs`
- `Assets/Scripts/MapScenarioBootstrap.cs`
- `Assets/Scripts/MapScenarioBootstrapPIBT.cs`
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
- `Tools/backtest_plot_report.py`

Hiện trạng đã kiểm tra:

- `BacktestRunner` có 5 map:
  - `Alpha32`: `Assets/MapData/random-32-32-10.map`
  - `Mansion`: `Assets/MapData/ht_mansion_n.map`
  - `Chantry`: `Assets/MapData/ht_chantry.map`
  - `Gallows`: `Assets/MapData/lt_gallowstemplar_n.map`
  - `Maze128`: `Assets/MapData/maze-128-128-10.map`
- `BacktestRunner` có 3 thuật toán:
  - `AStar`
  - `PIBT`
  - `PIBT_TCP`
- `BacktestRunner.Reps = 3`.
- `BacktestRunner.RunTimeoutSec = 120`.
- Output được export vào `BacktestResults/`:
  - `backtest_summary_*.csv`
  - `backtest_agents_*.csv`
  - `backtest_chart_*.html`
  - `backtest_chart_*.png` nếu Python/matplotlib chạy được.
- `MapTankTestBootstrap.ComputeEnemySpawnCells()` hiện dùng 6 spawn cell cơ bản khi không ở LAN mode. Vì vậy UI backtest hiện tại chưa có tham số agent-count `1,3,5,30,50,100`.

Số ô đi được của 5 map:

| Map | Kích thước | Ô đi được | Tỷ lệ đi được | Nhận xét |
| --- | ---: | ---: | ---: | --- |
| Alpha32 | 32 x 32 | 922 | 90.0% | phù hợp smoke và small-scale |
| Mansion | 133 x 270 | 8959 | 24.9% | nhiều tường, dễ nghẽn hành lang |
| Chantry | 162 x 141 | 7461 | 32.7% | map trung bình-lớn, có cấu trúc |
| Gallows | 251 x 180 | 10021 | 22.2% | lớn nhưng nhiều vùng hẹp |
| Maze128 | 128 x 128 | 14818 | 90.4% | phù hợp stress số agent lớn |

## 3. Cơ sở benchmark tham khảo

Benchmark MAPF chuẩn thường tách rõ map và scenario. MovingAI MAPF benchmark cung cấp map cùng nhiều scenario/start-goal set; cách đánh giá phổ biến là tăng số agent dần trên cùng scenario cho đến khi timeout hoặc không còn hữu ích. Bài benchmark MAPF của Stern và cộng sự cũng dùng nhiều scenario trên mỗi map và chọn các cặp start-goal đầu tiên để tạo instance k-agent.

Áp dụng vào project này:

- Map là biến môi trường: 5 map.
- Thuật toán là biến so sánh: A*, PIBT C#, PIBT TCP/C++.
- Agent count là biến scale: `1, 3, 5, 30, 50, 100`.
- Repetition/seed là biến chống nhiễu do spawn, vật lý Unity, shooting, TCP timing.

Vì project hiện không có `.scen` benchmark files, cần tự sinh spawn/goal set deterministic và ghi seed vào CSV để chạy lại được.

## 4. Ma trận chạy đề xuất

### Bộ A - Smoke hiện trạng

Mục tiêu: xác nhận backtest chạy end-to-end, xuất CSV/chart, PIBT_TCP kết nối được server.

Ma trận:

```text
5 map x 3 thuật toán x 3 repetition = 45 run
agent count: số enemy mặc định hiện tại, dự kiến 6
dynamic obstacle: tắt
timeout: 120 giây
```

Chạy bộ này trước. Nếu PIBT_TCP lỗi kết nối server, không chạy tiếp ma trận lớn cho PIBT_TCP cho đến khi server ổn định.

### Bộ B - Main thesis scale matrix

Mục tiêu: dữ liệu chính cho Chương 5.

Ma trận khuyến nghị:

```text
maps = Alpha32, Mansion, Chantry, Gallows, Maze128
algorithms = AStar, PIBT, PIBT_TCP
agent_counts = 1, 3, 5, 30, 50, 100
repetitions = 3

total = 5 x 3 x 6 x 3 = 270 run
```

Thứ tự chạy nên là:

```text
for map in maps:
  for agent_count in [1, 3, 5, 30, 50, 100]:
    for rep in [1, 2, 3]:
      run AStar
      run PIBT
      run PIBT_TCP
```

Lý do: cùng một map, cùng agent_count, cùng rep/seed thì 3 thuật toán nên dùng cùng spawn set. Cách này công bằng hơn so với chạy hết A* trước rồi mới chạy PIBT.

### Bộ C - Dynamic obstacle stress-test

Mục tiêu: phần phụ hoặc ablation nhỏ, không trộn vào bảng chính.

Ma trận tiết kiệm:

```text
5 map x 3 thuật toán x 3 agent_count x 3 repetition = 135 run
agent_counts = 5, 30, 100
dynamic obstacle: bật
```

Nếu thiếu thời gian, chỉ chạy:

```text
5 map x 3 thuật toán x 1 agent_count x 3 repetition = 45 run
agent_count = 30
```

## 5. Điều kiện cần bổ sung trước khi chạy Bộ B

Hiện code chưa hỗ trợ chọn agent-count trong `BacktestRunner`. Cần sửa nhỏ trước khi chạy ma trận scale:

1. Thêm `agentCount` vào `BacktestRunner.Job`.
2. Thêm danh sách agent count vào runner, ví dụ `{ 1, 3, 5, 30, 50, 100 }`.
3. Thêm `BacktestMode.AgentCount` hoặc `PlayerPrefs` key như `BacktestAgentCount`.
4. Sửa `MapTankTestBootstrap.ComputeEnemySpawnCells()`:
   - Nếu `BacktestMode.IsActive` và `BacktestMode.AgentCount > 0`, dùng agent count đó làm `needed`.
   - Nếu không, giữ logic hiện tại.
5. Sinh spawn cell deterministic theo `(map, agent_count, rep)` để 3 thuật toán dùng cùng đầu vào.
6. Export thêm cột vào summary CSV:
   - `AgentCountRequested`
   - `AgentCountActual`
   - `Seed`
   - `DynamicObstacle`
7. Nếu không sinh đủ spawn cell cho một map/mức agent, ghi rõ `InvalidSpawnSet` thay vì im lặng chạy thiếu agent.

Không nên chỉnh bằng tay từng lần trong Inspector vì dễ sai và khó tái lập.

## 6. Quy trình chạy

### Bước 1 - Kiểm tra trạng thái trước khi chạy

Ghi lại:

```text
git status --short --branch
git rev-parse --short HEAD
```

Nếu chạy `PIBT_TCP`, ghi lại thêm:

- endpoint server đang dùng;
- version/commit hoặc log start của server C++;
- log `hello` thành công với đúng số agent.

### Bước 2 - Chạy smoke 45 run

Trong Unity:

1. Mở menu game.
2. Bấm `BACKTEST`.
3. Chọn cả 5 map.
4. Đặt `Số lần / map = 3`.
5. Tắt dynamic obstacle.
6. Start.

Kỳ vọng output:

```text
BacktestResults/backtest_summary_*.csv
BacktestResults/backtest_agents_*.csv
BacktestResults/backtest_chart_*.html
BacktestResults/backtest_chart_*.png
```

Sau khi chạy, kiểm tra:

- Summary có 45 row data.
- Mỗi tổ hợp map/algorithm có đủ 3 rep.
- `AgentCount` không bị 0.
- PIBT_TCP không bị toàn bộ timeout do connection failure.

### Bước 3 - Chạy main scale 270 run

Sau khi bổ sung agent-count support:

1. Chạy cùng một batch tự động, không chạy tay từng mức nếu có thể.
2. Ưu tiên thứ tự `map -> agent_count -> rep -> algorithm`.
3. Chạy static environment trước, dynamic obstacle để sau.
4. Với mỗi run timeout, vẫn giữ record; timeout ratio là metric quan trọng.

### Bước 4 - Tổng hợp kết quả

Từ CSV, tạo bảng cho báo cáo:

- Theo map + thuật toán:
  - duration trung bình;
  - timeout rate;
  - eagle HP cuối;
  - enemies alive cuối;
  - total replans;
  - total recoveries;
  - total shots;
  - total cells.
- Theo agent count:
  - curve duration vs agent count;
  - curve timeout rate vs agent count;
  - so sánh A* / PIBT / PIBT-C++.

Nên có ít nhất 3 biểu đồ:

1. Duration trung bình theo thuật toán, nhóm theo map.
2. Timeout rate theo agent count.
3. Replan/recovery tổng theo agent count.

## 7. Cách diễn giải trong báo cáo

Viết an toàn:

- A* là baseline dễ hiểu, tốt ở ít agent nhưng có thể suy giảm khi nhiều agent vì tránh va chạm/replan cục bộ.
- PIBT C# thể hiện hướng MAPF phối hợp trong Unity.
- PIBT TCP/C++ thể hiện tích hợp planner ngoài Unity; cần tách chi phí mạng/TCP khỏi chất lượng thuật toán nếu phân tích sâu.
- Mốc 100 agent là stress test, không bắt buộc tất cả thuật toán thắng/thành công. Timeout và nghẽn hành lang là kết quả hợp lệ.

Không nên overclaim:

- Không kết luận PIBT TCP/C++ nhanh hơn nếu chưa tách thời gian network/server.
- Không kết luận thuật toán tối ưu toàn cục nếu metric chỉ là gameplay duration.
- Không gộp dynamic obstacle vào main table mà không ghi rõ điều kiện khác.

## 8. Definition of Done cho R3

R3 có thể coi là đủ dữ liệu nếu có:

- Ít nhất một bộ smoke 45 run sạch.
- Bộ main scale 270 run, hoặc nếu chưa kịp sửa agent-count thì ghi rõ limitation và chỉ dùng 45 run làm kết quả cơ bản.
- CSV summary và agents còn giữ trong `BacktestResults/`.
- Chart HTML/PNG tạo được.
- Một markdown tổng hợp kết quả dưới `adds/PLAN` hoặc `adds/diffchecker`, ghi rõ:
  - branch/commit;
  - ngày chạy;
  - command/quy trình chạy;
  - số run kỳ vọng và số run thực tế;
  - lỗi/timeout đáng chú ý;
  - bảng trung bình dùng đưa vào Chương 5.

## 9. Khuyến nghị cuối cùng

Nên làm theo 2 pha:

1. Chạy ngay smoke 45 run bằng UI hiện tại để có bằng chứng R3 tối thiểu.
2. Sau đó bổ sung agent-count support và chạy main matrix 270 run với `1, 3, 5, 30, 50, 100`.

Nếu chỉ chọn một con số để lên kế hoạch chính thức: chọn `270 run` cho static backtest. Đây là mức cân bằng tốt giữa độ phủ 5 map, 3 thuật toán, scale theo agent, và thời gian chạy còn chấp nhận được.

## 10. Nguồn tham khảo

- MovingAI MAPF benchmarks: https://movingai.com/benchmarks/mapf.html
- Stern et al., "Multi-Agent Pathfinding: Definitions, Variants, and Benchmarks": https://arxiv.org/abs/1906.08291
