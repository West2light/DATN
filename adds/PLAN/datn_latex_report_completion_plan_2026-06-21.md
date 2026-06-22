# Plan hoàn thiện quyển ĐATN LaTeX khoảng 70 trang

Ngày lập: 2026-06-21  
Repo chính: `D:\2025.2\DATN\projectY`  
Template LaTeX: `adds/report/20225808_DuongQuangDong_2025.2`  
Quy định/rules đã đọc:

- `adds/report/datn-report-rules-agent.md`
- `adds/report/quy-dinh-viet-quyen-DATN.md`
- `adds/report/20225808_DuongQuangDong_2025.2/DoAn.tex`

Mục tiêu của plan này là biến project Tank MAPF hiện có thành một quyển ĐATN tự đủ nghĩa, bám template của trường, khoảng 65-75 trang nội dung chính, có sơ đồ/hình minh họa, số liệu thực nghiệm và phần đánh giá rõ ràng. Đây là plan hỗ trợ viết và kiểm chứng; phần lời văn cuối cùng cần được sinh viên tự diễn đạt lại để đảm bảo liêm chính học thuật.

## 1. Nguyên tắc viết cần giữ xuyên suốt

1. Dùng template LaTeX chính thức làm nền. Nếu đổi cấu trúc chương, cần xác nhận với GVHD vì quy định cho phép đề tài game/nghiên cứu điều chỉnh khung nhưng phải được đồng ý.
2. Không viết theo kiểu copy tài liệu/manual. Phần lý thuyết và công nghệ phải trả lời: vì sao chọn, áp dụng vào đồ án này như thế nào, giới hạn ở đâu.
3. Không overclaim. Với phần No Man's Sky/LNS2/PIBT, cách viết an toàn là: project đã migration/ứng dụng phần lõi lập kế hoạch flow-aware/PIBT vào Unity và có nhánh TCP gọi server C++, nhưng không khẳng định đã bê nguyên toàn bộ kiến trúc planner của team gốc nếu chưa kiểm chứng đủ.
4. Mỗi chức năng quan trọng phải có cả mô tả và ảnh/sơ đồ. Người đọc không xem demo vẫn phải hiểu sản phẩm.
5. Tất cả hình/bảng dùng `\label` và `\ref`, không viết "hình dưới đây/hình trên đây".
6. Tránh `\begin{figure}[H]` và `\begin{table}[H]`. Template mẫu hiện có `[H]`, khi viết bản thật cần chuyển sang `[htbp]` hoặc để LaTeX tự float.
7. Hình có chữ phải đủ nét. Diagram nên xuất PDF vector nếu được, hoặc PNG độ phân giải cao. Không chèn logo công nghệ đơn thuần.
8. File nộp cuối phải đúng tên `MSSV_HoVaTen_KyHoc` và tổng dung lượng gói nộp nhỏ hơn 30 MB.

## 2. Bước 1 - Đọc, kiểm kê và xác nhận nội dung project

Mục tiêu của bước này là khóa "source of truth" trước khi viết, tránh viết sai hiện trạng hoặc viết theo mục tiêu cũ.

### 2.1. Kiểm kê project Unity chính

Đọc và ghi chú theo nhóm:

- Tổng quan đề tài: `CLAUDE.md`, `Assets/Docs/DATN.md`.
- Baseline A*: `Assets/Docs/mapf_eagle_enemy_astar_plan.md`, `Assets/Scripts/GridAStarPathfinder.cs`, `Assets/Scripts/GridEnemyAgent.cs`, `Assets/Scripts/Pathfinding/GridNavMask.cs`.
- PIBT C# trong Unity: `Assets/Docs/mapf_lns2_scene_plan.md`, `Assets/Scripts/PIBTPlanner.cs`, `GridPIBTPathfinder.cs`, `GridEnemyAgentPIBT.cs`, `MapScenarioBootstrapPIBT.cs`.
- PIBT TCP/C++: `Assets/Scripts/PIBTTcpClient.cs`, `PIBTWebRelayClient.cs`, `GridEnemyAgentPIBT_TCP.cs`, `MapScenarioBootstrapPIBT_TCP.cs`.
- Gameplay/map flow: `MapLoader.cs`, `MapTankTestBootstrap.cs`, `MapScenarioBootstrap.cs`, `MapGameOverController.cs`, `MapWinController.cs`, `MenuViewBootstrap.cs`.
- Multiplayer/web/GCP nếu đưa vào phạm vi: `Assets/Scripts/Multiplayer/*`, `services/invite-registry/app.py`, các plan `adds/networking_web_play_plan_*.md`.
- Backtest/thực nghiệm: `Assets/Scripts/Backtest/*`, `Tools/backtest_plot_report.py`, thư mục `BacktestResults/` nếu có kết quả chạy.

Output cần tạo sau bước này:

- Bảng "Hiện trạng triển khai" gồm: chức năng đã làm, file chính, scene liên quan, trạng thái kiểm chứng, bằng chứng hình/log.
- Bảng "Phạm vi báo cáo" tách rõ: đã triển khai, đang thử nghiệm, dự kiến/hướng phát triển.

### 2.2. Kiểm kê project Server PIBT C++ trong WSL

Đã tìm thấy repo liên quan tại:

```text
/home/west2light/projectY
```

Các file/nhóm cần đọc:

- `default_planner/pibt.cpp`, `pibt.h`
- `default_planner/flow.cpp`, `flow.h`
- `default_planner/search.cpp`, `search.h`
- `default_planner/heuristics.cpp`, `heuristics.h`
- `default_planner/TrajLNS.h`
- `src/PibtTcpServer.cpp`, `PibtTcpServer.h`
- `src/UnityStartKitAdapter.cpp`, `UnityStartKitAdapter.h`
- `src/tcp_server_main.cpp`
- Các log smoke test trong `/home/west2light/projectY/adds/output/`

Việc cần làm:

1. Ghi lại phiên bản/commit của repo C++.
2. Chạy hoặc đọc lại smoke test server, xác nhận protocol `hello -> plan_step -> shutdown`.
3. Mapping thuật toán C++ sang C#:
   - flow grid trong C++ tương ứng `PIBTPlanner._flow`;
   - search/heuristic tương ứng flow-aware A* và reverse BFS cache;
   - server action tương ứng `GridEnemyAgentPIBT_TCP` nhận target/tick từ TCP.
4. Chỉ ra phần nào dùng trực tiếp qua TCP, phần nào được port/adapt sang C#.

Output cần tạo:

- Bảng "C++ server vs Unity C#" dùng cho Chương 4/Chương 6.
- Một sequence diagram TCP `Unity -> PIBT server`.
- Một đoạn mô tả giới hạn: TCP server là hướng tích hợp ngoài Unity, không thay thế hoàn toàn runtime C#.

### 2.3. Kiểm kê evidence/hình ảnh

Thư mục hình tham khảo hiện có:

```text
Assets/Docs/references/
├── clipboard_parsing_architecture.drawio.png
├── GameFlowR.png
├── GameFlowW.png
├── Picture1.png
├── Picture2.png
└── Picture3.png
```

Cần phân loại từng hình:

- Hình nào dùng được trực tiếp trong báo cáo.
- Hình nào chỉ là nháp/tham khảo.
- Hình nào cần vẽ lại bằng draw.io/Mermaid/PlantUML để thống nhất style.
- Hình nào phải chụp lại từ Unity để có ảnh sản phẩm thật.

## 3. Bước 2 - Danh sách sơ đồ/diagram nên có

Không nên nhồi tất cả diagram vào chương chính. Chương chính chỉ đặt diagram có giá trị giải thích cao; diagram chi tiết/lớn đưa vào phụ lục.

### 3.1. Sơ đồ bắt buộc nên có trong chương chính

1. System context diagram  
   Mô tả người chơi, Unity client/native-web, dedicated server/registry nếu có, PIBT C++ server, dữ liệu map `.map`.

2. High-level architecture/package diagram  
   Các package/tầng chính:
   - UI/Menu & session selection
   - Map runtime
   - Gameplay core
   - Pathfinding/MAPF
   - PIBT TCP integration
   - Multiplayer/session
   - Backtest/evaluation

3. Game flow/activity diagram  
   Luồng từ menu chọn tank/map/algorithm -> load scene -> build map -> spawn player/eagle/enemies -> AI loop -> win/lose/backtest.

4. Data-flow diagram cho MAPF pipeline  
   `.map` file -> `MapLoader` grid -> start/goal agents -> A*/PIBT planner -> path/actions -> `TankController` -> metric/backtest.

5. Class diagram rút gọn cho cụm pathfinding  
   Chỉ đưa lớp chính:
   - `MapLoader`
   - `GridAStarPathfinder`
   - `GridEnemyAgent`
   - `PIBTPlanner`
   - `GridPIBTPathfinder`
   - `GridEnemyAgentPIBT`
   - `MapScenarioBootstrap*`

6. Sequence diagram cho A* replan loop  
   `GridEnemyAgent.Update()` -> `WorldToCell` -> `GridAStarPathfinder.TryFindPath()` -> follow waypoint -> shoot/recover.

7. Sequence diagram cho PIBT C#  
   `GridEnemyAgentPIBT` -> `GridPIBTPathfinder` -> `PIBTPlanner.FrankWolfe()` -> update trajectory/flow -> follow path.

8. Sequence diagram cho PIBT TCP/C++  
   `MapScenarioBootstrapPIBT_TCP` -> `PIBTTcpClient.Hello()` -> `PlanStep()` -> `GridEnemyAgentPIBT_TCP.ApplyTarget()` -> next tick.

9. Experiment/backtest workflow diagram  
   Map set x algorithm set x repetition -> run scene -> collect metrics -> CSV/HTML/chart.

10. Deployment diagram nếu báo cáo có phần multiplayer web/GCP  
    Browser/WebGL, native client, GCP VM, registry `8080`, UDP/WebSocket game server, optional HTTPS/WSS proxy.

### 3.2. Sơ đồ nên đưa vào phụ lục

- Use case diagram tổng quát.
- Activity diagram cho backtest có dynamic obstacle.
- State machine của enemy agent: pathing, shooting, scuff/recovery, reverse recovery, dead.
- Class diagram chi tiết từng cụm:
  - gameplay/damage/pooling;
  - multiplayer/session;
  - backtest/result chart;
  - TCP client/server protocol.
- Bảng protocol JSON cho TCP `hello`, `plan_step`, `plan_result`, `shutdown`.
- Bảng cấu hình scene/map/algorithm.

### 3.3. Quy chuẩn đặt file hình

Đề xuất copy hình báo cáo vào:

```text
adds/report/20225808_DuongQuangDong_2025.2/Hinhve/datn/
```

Quy tắc tên file:

```text
system_context.pdf
architecture_packages.pdf
game_flow.pdf
mapf_pipeline.pdf
astar_sequence.pdf
pibt_csharp_sequence.pdf
pibt_tcp_sequence.pdf
backtest_workflow.pdf
experiment_results_<metric>.png
ui_<screen_name>.png
```

## 4. Bước 3 - Đề xuất cấu trúc chương khoảng 70 trang

Template hiện có 6 chương chính, trong đó Chương 4 đang gộp "Phân tích thiết kế, triển khai và đánh giá hệ thống". Với đề tài này nên tách "Thực nghiệm và đánh giá" thành chương riêng vì project đã có backtest A* / PIBT / PIBT-C++ và nhiều metric. Cấu trúc đề xuất là 7 chương:

| Chương | Tên đề xuất | Số trang mục tiêu | Vai trò |
|---|---:|---:|---|
| 1 | Giới thiệu đề tài | 6-8 | Bối cảnh, vấn đề, mục tiêu, phạm vi, đóng góp |
| 2 | Khảo sát và phân tích yêu cầu | 8-10 | Nhu cầu game/MAPF, use case, yêu cầu chức năng/phi chức năng |
| 3 | Nền tảng lý thuyết và công nghệ sử dụng | 10-12 | MAPF, A*, PIBT/LNS2, Unity, TCP/server, backtest; tập trung "vì sao dùng" |
| 4 | Phân tích, thiết kế và triển khai hệ thống | 18-22 | Kiến trúc, scene flow, module, thuật toán trong Unity, server integration |
| 5 | Thực nghiệm và đánh giá | 12-16 | Thiết kế thực nghiệm, metric, map, kết quả, phân tích, đe dọa tính hợp lệ |
| 6 | Các giải pháp và đóng góp nổi bật | 5-7 | Tổng hợp điểm khó/đóng góp kỹ thuật, bài học triển khai |
| 7 | Kết luận và hướng phát triển | 3-5 | Kết luận, hạn chế, hướng mở rộng |

Tổng mục tiêu: 62-80 trang nội dung chính, hợp lý để chốt quanh 70 trang sau khi thêm hình/bảng.

### 4.1. Chương 1 - Giới thiệu đề tài

Đề xuất mục:

1. Đặt vấn đề
   - AI điều hướng trong game realtime.
   - Vấn đề nhiều agent cùng di chuyển trong môi trường chật/hạn chế.
   - Vì sao MAPF phù hợp với game tank bảo vệ Eagle.
2. Mục tiêu đề tài
   - Xây dựng game tank có chọn map/algorithm.
   - So sánh A* baseline, PIBT C# và PIBT-C++/TCP.
   - Thu thập metric thực nghiệm.
3. Phạm vi đề tài
   - Unity 2D top-down, grid map `.map`.
   - Tập trung pathfinding/MAPF và tích hợp gameplay, không biến báo cáo thành manual Unity.
4. Định hướng giải pháp
   - Map runtime -> scenario bootstrap -> pathfinding module -> backtest.
5. Đóng góp chính
   - Tích hợp MAPF vào gameplay hoàn chỉnh.
   - Port/adapt core PIBT flow-aware planning.
   - TCP bridge tới server C++.
   - Hệ thống backtest nhiều map/nhiều thuật toán.
6. Bố cục báo cáo

### 4.2. Chương 2 - Khảo sát và phân tích yêu cầu

Đề xuất mục:

1. Khảo sát bài toán và sản phẩm tương tự
   - AI enemy trong game.
   - Grid pathfinding và MAPF trong mô phỏng/robotics/game.
2. Người dùng và use case
   - Chơi local/native/web.
   - Chọn tank, map, thuật toán.
   - Chạy backtest/quan sát kết quả.
3. Yêu cầu chức năng
   - Load map.
   - Spawn Eagle/enemy/player.
   - Enemy di chuyển theo A*/PIBT.
   - Chọn thuật toán.
   - Multiplayer nếu đưa vào phạm vi.
   - Backtest và xuất kết quả.
4. Yêu cầu phi chức năng
   - Realtime, dễ quan sát, deterministic tương đối, mở rộng map/agent.
   - Khả năng chạy trên Unity Editor/build/WebGL.
5. Ràng buộc và giả định
   - Map grid 4-neighbor chính.
   - Continuous tank movement nhưng planner là discrete grid.
   - Server C++ có độ trễ/tick riêng.

### 4.3. Chương 3 - Nền tảng lý thuyết và công nghệ sử dụng

Đề xuất mục:

1. Multi-Agent Pathfinding
   - Khái niệm start/goal, collision, makespan, SoC.
   - Liên hệ trực tiếp với enemy tanks.
2. A* baseline
   - Vì sao chọn A* làm baseline.
   - Heuristic, grid cost, soft cost/nav mask.
3. PIBT/LNS2/flow-aware planning
   - Flow grid, opposition flow, vertex flow.
   - Reverse BFS heuristic.
   - Frank-Wolfe style replan.
   - Viết theo mức đủ hiểu, không copy paper/source.
4. Tích hợp C++ server qua TCP
   - Vì sao cần server C++: tái sử dụng solver gốc, so sánh với port C#.
   - Protocol JSON-lines ở mức thiết kế.
5. Unity 2D và C#
   - Vì sao phù hợp cho prototype game/realtime visualization.
   - Cách Unity được dùng trong đồ án: scene, MonoBehaviour, physics 2D, Netcode nếu có.
6. Backtest và đánh giá
   - Vì sao cần metric thay vì chỉ demo cảm tính.

### 4.4. Chương 4 - Phân tích, thiết kế và triển khai hệ thống

Đề xuất mục lớn:

1. Kiến trúc tổng thể
   - System context + package diagram.
2. Thiết kế map runtime
   - `MapLoader`, `.map`, grid/world conversion, obstacle collider.
3. Thiết kế gameplay scenario
   - `MapTankTestBootstrap`, `MapScenarioBootstrap`, Eagle, enemies, win/lose.
4. Thiết kế AI A*
   - `GridAStarPathfinder`, `GridEnemyAgent`, `GridNavMask`, recovery.
5. Thiết kế PIBT C# trong Unity
   - `PIBTPlanner`, `GridPIBTPathfinder`, `GridEnemyAgentPIBT`, shared flow grid.
6. Thiết kế PIBT TCP/C++ integration
   - `PIBTTcpClient`, `PIBTWebRelayClient`, `MapScenarioBootstrapPIBT_TCP`, server `/home/west2light/projectY`.
7. Thiết kế UI và lựa chọn chế độ
   - `MenuViewBootstrap`, chọn tank/map/algorithm, scene A*/PIBT.
8. Thiết kế multiplayer/web deployment nếu nằm trong phạm vi chính
   - Native UDP, WebGL/WebSocket, registry session.
9. Thiết kế backtest
   - `BacktestRunner`, job matrix, metric, CSV/chart.
10. Tổng kết triển khai
   - Bảng file chính và vai trò.

Gợi ý: Chương 4 nên là chương dài nhất vì đây là nơi chứng minh khối lượng thực hiện.

### 4.5. Chương 5 - Thực nghiệm và đánh giá

Chương này nên tách riêng sau Chương 4.

Đề xuất mục:

1. Mục tiêu thực nghiệm
   - So sánh A*, PIBT C#, PIBT-C++/TCP.
   - Kiểm tra ảnh hưởng map/kích thước/obstacle/dynamic obstacle.
2. Môi trường thực nghiệm
   - Unity version, máy chạy, Windows/WSL, server host/port nếu dùng.
3. Tập bản đồ và kịch bản
   - `random-32-32-10.map` / Alpha32.
   - `ht_mansion_n.map` / Mansion.
   - `ht_chantry.map` / Chantry.
   - `lt_gallowstemplar_n.map` / Gallows.
   - `maze-128-128-10.map` / Maze128.
4. Thuật toán/chế độ so sánh
   - AStar.
   - PIBT C#.
   - PIBT_TCP.
5. Metric
   - Duration.
   - Outcome.
   - Eagle HP at end.
   - Enemies alive at end.
   - Total replans.
   - Total recoveries.
   - Total shots.
   - Total cells visited.
   - Per-agent initial path length, replan count, recovery count.
6. Quy trình chạy backtest
   - Matrix map x algorithm x repetition.
   - Timeout 120s.
   - Export CSV/HTML/chart.
7. Kết quả định lượng
   - Bảng tổng hợp.
   - Biểu đồ theo metric.
8. Đánh giá định tính
   - Hành vi chen lấn, lane formation, recovery, khả năng tấn công Eagle.
9. Phân tích nguyên nhân
   - Khi PIBT tốt hơn/kém hơn A*.
   - Chi phí TCP/server, latency, tick interval.
10. Đe dọa tính hợp lệ
   - Số lần chạy còn ít.
   - Movement continuous làm lệch so với MAPF discrete.
   - Hardware/server/network ảnh hưởng kết quả.

### 4.6. Chương 6 - Các giải pháp và đóng góp nổi bật

Mục tiêu là nhấn mạnh phần khó và khác biệt, không lặp lại Chương 4.

Đề xuất mục:

1. Chuyển bài toán MAPF vào game realtime thay vì benchmark offline.
2. Thiết kế đồng thời discrete planner và continuous tank movement.
3. Cơ chế soft-cost/recovery để giảm kẹt vật cản.
4. Port/adapt flow-aware PIBT từ C++ sang C#.
5. TCP bridge tới solver C++.
6. Backtest tự động nhiều map/nhiều thuật toán.
7. Multiplayer/WebGL/GCP nếu đã đủ ổn định để đưa vào sản phẩm.

### 4.7. Chương 7 - Kết luận và hướng phát triển

Đề xuất mục:

1. Kết luận theo mục tiêu ban đầu.
2. Hạn chế:
   - Chưa chứng minh tối ưu trên quy mô rất lớn.
   - Một phần thuật toán gốc còn ở server ngoài.
   - Multiplayer/WebGL có thể là hướng demo/MVP nếu chưa hoàn chỉnh sản phẩm.
3. Hướng phát triển:
   - Tối ưu PIBT/Frank-Wolfe.
   - Thêm cooperative planning/reservation table.
   - Thêm map/agent scale.
   - Hoàn thiện WebGL/GCP production.
   - Tăng số lần chạy và kiểm định thống kê.

## 5. Bước 4 - Cập nhật cấu trúc LaTeX

Đề xuất không sửa ngay khi chưa viết nội dung, nhưng khi bắt đầu triển khai LaTeX thì làm theo thứ tự:

1. Backup hoặc commit trạng thái template trước khi sửa.
2. Tạo chapter mới:

```text
adds/report/20225808_DuongQuangDong_2025.2/Chuong/5_Thuc_nghiem_danh_gia.tex
```

3. Đổi chapter cũ:

```text
5_Giai_phap_dong_gop.tex -> 6_Giai_phap_dong_gop.tex
6_Ket_luan.tex -> 7_Ket_luan.tex
```

Hoặc giữ tên file cũ nhưng update `DoAn.tex` rõ ràng. Cách sạch hơn là đổi tên theo số chương mới.

4. Update `DoAn.tex`:
   - Chương 4: `PHÂN TÍCH, THIẾT KẾ VÀ TRIỂN KHAI HỆ THỐNG`
   - Chương 5: `THỰC NGHIỆM VÀ ĐÁNH GIÁ`
   - Chương 6: `CÁC GIẢI PHÁP VÀ ĐÓNG GÓP NỔI BẬT`
   - Chương 7: `KẾT LUẬN VÀ HƯỚNG PHÁT TRIỂN`
5. Xóa/chỉnh nội dung placeholder trong template, đặc biệt các ví dụ thư viện/Eclipse, E-R diagram nếu không phù hợp.
6. Update `Tu_viet_tat.tex`:
   - MAPF, AI, A*, PIBT, LNS2, TCP, UDP, WSL, CSV, SoC, FPS nếu dùng.
7. Update `Danh_sach_tai_lieu_tham_khao.bib`:
   - MAPF/A* paper hoặc nguồn học thuật.
   - League of Robot Runners/No Man's Sky source nếu được trích dẫn.
   - Unity/Netcode docs chỉ cite khi thật sự dùng khái niệm, không copy docs.
8. Build thử bằng LaTeX Workshop hoặc command:

```powershell
cd adds\report\20225808_DuongQuangDong_2025.2
latexmk -pdf -interaction=nonstopmode -synctex=1 DoAn.tex
```

Nếu `latexmk` thiếu, dùng chuỗi MiKTeX:

```powershell
pdflatex -interaction=nonstopmode DoAn.tex
biber DoAn
pdflatex -interaction=nonstopmode DoAn.tex
pdflatex -interaction=nonstopmode DoAn.tex
```

## 6. Bước 5 - Thu thập ảnh, số liệu và bằng chứng

### 6.1. Ảnh sản phẩm cần chụp

- Main menu.
- Chọn tank/outfit.
- Chọn map và thuật toán.
- Scene `MapF_TankTest` với A*.
- Scene `MapF_TankTest_PIBT` với PIBT.
- PIBT TCP mode khi server connected.
- Enemy bắn Eagle / win-lose UI.
- Backtest config UI.
- Backtest running overlay.
- Backtest result chart.
- WebGL/browser hoặc GCP deployment nếu đưa vào phạm vi.

### 6.2. Số liệu cần có

Tối thiểu nên có một bảng kết quả chạy:

```text
map, algorithm, rep, outcome, duration, eagleHpAtEnd,
enemiesAliveAtEnd, agentCount, totalReplans, totalRecoveries,
totalShots, totalCells
```

Nếu chưa có `BacktestResults/`, cần chạy backtest và export CSV trước khi viết Chương 5. Với mục tiêu 70 trang, Chương 5 không nên viết cảm tính; phải có bảng/biểu đồ thật.

### 6.3. Bằng chứng server C++

- Log server start.
- Log `hello_ack`.
- Log `plan_result`.
- Một ví dụ payload rút gọn, đưa vào phụ lục nếu dài.
- Ghi rõ môi trường WSL và đường dẫn repo C++.

## 7. Bước 6 - Lộ trình thực hiện đề xuất

### Milestone R1 - Audit và khóa phạm vi

Done khi:

- Có bảng hiện trạng feature.
- Có bảng file/module chính.
- Có quyết định rõ: WebGL/GCP là nội dung chính hay phụ lục/hướng phát triển.
- Có quyết định rõ: PIBT TCP là kết quả chính hay minh chứng tích hợp.

### Milestone R2 - Vẽ diagram nền

Done khi:

- Có ít nhất 8 diagram chính ở dạng PDF/PNG nét.
- Các diagram được đặt vào `Hinhve/datn/`.
- Mỗi diagram có caption dự kiến và chương sử dụng.

### Milestone R3 - Chạy/thu thập thực nghiệm

Done khi:

- Backtest chạy được tối thiểu 5 map x 3 algorithm x N repetition.
- Có CSV kết quả.
- Có chart hoặc bảng tổng hợp.
- Có ghi chú các run lỗi/timeout thay vì giấu đi.

### Milestone R4 - Sửa khung LaTeX

Done khi:

- `DoAn.tex` có 7 chương đúng.
- Build PDF sạch hoặc chỉ còn warning không nghiêm trọng.
- Không còn placeholder template vô nghĩa.

### Milestone R5 - Viết bản nháp chương 1-4

Done khi:

- Chương 1-4 có đủ skeleton, hình chính, bảng module.
- Các claim kỹ thuật đều có nguồn nội bộ hoặc citation.
- Không có đoạn copy từ docs/source comment.

### Milestone R6 - Viết chương 5-7

Done khi:

- Chương 5 có số liệu thật.
- Chương 6 nêu đóng góp không trùng hoàn toàn Chương 4.
- Chương 7 nói rõ hạn chế và hướng phát triển.

### Milestone R7 - Review compliance

Checklist bắt buộc:

- Không còn `[H]` trong figure/table.
- Không còn "hình dưới đây", "hình trên đây".
- Mọi hình/bảng đều có `\caption`, `\label`, `\ref`.
- Không có logo công nghệ đơn thuần.
- Ảnh có chữ đọc được.
- Tài liệu tham khảo build được.
- PDF dung lượng hợp lý.
- Tên đề tài trong bìa khớp chính xác với qldt.
- Nội dung đã được sinh viên tự đọc, chỉnh và diễn đạt lại.

## 8. Gợi ý ưu tiên nếu thời gian gấp

Nếu chỉ còn ít thời gian, ưu tiên theo thứ tự:

1. Chốt Chương 4 và Chương 5 vì đây là phần chứng minh khối lượng và tính đúng đắn.
2. Có ít nhất một bảng thực nghiệm thật và một chart thật.
3. Có architecture diagram, MAPF pipeline diagram, sequence diagram PIBT.
4. Viết Chương 1 và Chương 7 ngắn nhưng logic.
5. Đẩy chi tiết use case/class diagram dài sang phụ lục.
6. Không cố đưa mọi phần GCP/WebGL vào chương chính nếu chưa đủ ổn định; có thể đặt là mở rộng/hướng phát triển.

## 9. Việc nên làm ngay sau plan này

1. Mở GVHD xác nhận cấu trúc 7 chương, đặc biệt Chương 5 "Thực nghiệm và đánh giá".
2. Chạy lại audit source và tạo bảng module/file.
3. Chạy backtest để có dữ liệu thật.
4. Vẽ trước 4 diagram nền: system context, architecture package, MAPF pipeline, PIBT TCP sequence.
5. Sau khi có hình và số liệu, mới bắt đầu viết LaTeX chi tiết để tránh phải rewrite nhiều lần.

