# Plan V2 hoàn thiện quyển ĐATN LaTeX khoảng 70 trang

Ngày lập: 2026-06-21  
Ngày cập nhật V2: 2026-06-21  
Repo chính: `D:\2025.2\DATN\projectY`  
Template LaTeX: `adds/report/20225808_DuongQuangDong_2025.2`  
Quy định/rules đã đọc:

- `adds/report/datn-report-rules-agent.md`
- `adds/report/quy-dinh-viet-quyen-DATN.md`
- `adds/report/20225808_DuongQuangDong_2025.2/DoAn.tex`

Mục tiêu của plan này là biến project Tank MAPF hiện có thành một quyển ĐATN tự đủ nghĩa, bám template của trường, khoảng 65-75 trang nội dung chính, có sơ đồ/hình minh họa, số liệu thực nghiệm và phần đánh giá rõ ràng. Đây là plan hỗ trợ viết và kiểm chứng; phần lời văn cuối cùng cần được sinh viên tự diễn đạt lại để đảm bảo liêm chính học thuật.

Bản V2 bổ sung bốn điểm so với V1:

- Làm rõ tuyến cải tiến thuật toán từ Causal PIBT sang EPIBT dựa trên tài liệu `adds/*` ở nhánh `feature/pibt-tcp-client-server`.
- Phân tách rõ hai luồng sản phẩm: Single Play và Multiplay Internet.
- Bổ sung phần deploy production trên GCP, Nginx, registry, WebGL, WebSocket, CI/CD.
- Thêm danh sách screenshot/placeholder cần chụp từ production để đưa vào quyển sau.

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

## 10. Bổ sung V2 - Cải tiến từ Causal PIBT sang EPIBT

Nguồn cần đọc/bám vào khi viết phần này:

- Nhánh `feature/pibt-tcp-client-server`:
  - `adds/epibt_full.md`
  - `adds/PLAN_EPIBT_ENEMY_MOVEMENT_IMPROVEMENT_2026-06-09.md`
  - `adds/PLAN_EPIBT_ENEMY_MOVEMENT_IMPROVEMENT_V2_2026-06-09.md`
  - `adds/pibt_tcp_enemy_movement_root_cause_plan.md`
  - `adds/pibt_tcp_enemy_smooth_movement_root_cause_plan_2026-06-09.md`
  - `adds/pibt_tcp_enemy_jitter_outside_map_plan.md`
- Repo C++ đang thấy trong WSL hiện tại: `/home/west2light/projectY`.
- Các file server C++ cần đối chiếu lại: `default_planner/pibt.cpp`, `default_planner/epibt.cpp`, `src/PibtTcpServer.cpp`, `src/PlannerSession.cpp`, `src/UnityStartKitAdapter.cpp`.

Lưu ý khi dùng tài liệu ở nhánh feature: một số file path cũ trong plan dùng dạng `Assets/Scripts/PibtTcp/...` hoặc repo `F:\DATN`, trong khi nhánh hiện tại `networking-gcp` đang có các file như `Assets/Scripts/PIBTTcpClient.cs`, `GridEnemyAgentPIBT_TCP.cs`, `MapScenarioBootstrapPIBT_TCP.cs`. Vì vậy khi viết báo cáo phải dùng khái niệm/thiết kế từ tài liệu feature, nhưng tên file cuối cùng phải được audit lại trên nhánh đang dùng để làm quyển.

### 10.1. Cách trình bày trong quyển

Không nên chỉ viết "dùng PIBT". Phần thuật toán nên trình bày thành tiến trình tiến hóa:

```text
PIBT gốc / Causal PIBT một bước
  -> vấn đề với rotation action model trong game tank
  -> EPIBT: multi-action operations + revisiting + operation inheritance
  -> EPIBT + LNS/GG là hướng nâng cao sau core
  -> Unity/Server integration: chỉ thực thi action đầu mỗi operation
```

Trong Chương 3, phần lý thuyết cần giải thích:

- Causal PIBT/PIBT một bước chỉ nhìn một action kế tiếp, phù hợp khi agent có thể rời ô ngay nhưng yếu trong rotation model.
- Game tank có hướng thân xe; để đi tới ô bên cạnh, agent có thể cần `CR/CCR` trước rồi mới `FW`.
- EPIBT dùng operation nhiều bước, ví dụ `CR,FW,W`, để nhìn trước vài timestep mà không cần A* window dài như winPIBT.
- Ba cải tiến lõi:
  - Multi-action Operations: operation độ dài `op_len=3`, action alphabet `FW/CR/CCR/W`.
  - Revisiting Agents: agent có thể được xét lại trong cùng timestep, giới hạn `L=10`.
  - Inheritance of Operations: kế thừa operation từ timestep trước bằng cách bỏ action đầu và thêm `W`.
- EPIBT vẫn chỉ trả action đầu cho runtime Unity, phần còn lại lưu ở server/session state.

Trong Chương 4, phần thiết kế/triển khai cần trình bày:

- C++ server giữ state operation và planner mode.
- Unity gửi `hello` một lần với map tĩnh, sau đó gửi `plan_step` gồm loc/orientation/goal của agent.
- Server chạy planner, trả action một bước kèm metadata optional như `planner`, `operation`, `opIndex`, `debugReason`.
- Unity execute strict action model để state vật lý không phá logic EPIBT.

Trong Chương 5, phần thực nghiệm cần so sánh ít nhất:

- AStar.
- PIBT C# trong Unity.
- PIBT TCP/C++ default hoặc Causal PIBT.
- EPIBT TCP/C++ nếu đã có build và log pass.

Nếu EPIBT chưa được production hóa hoàn chỉnh, viết rõ là "hướng cải tiến/nhánh nghiên cứu đã có plan và prototype server", không viết như tính năng shipped.

### 10.2. Cấu trúc thuật toán EPIBT cần đưa vào báo cáo

Đề xuất có một subsection trong Chương 3:

```text
3.x. Cải tiến từ Causal PIBT sang Enhanced PIBT
  3.x.1. Hạn chế của PIBT một bước trong mô hình có hướng quay
  3.x.2. Multi-action operations
  3.x.3. Revisiting agents và giới hạn L
  3.x.4. Operation inheritance
  3.x.5. LNS và Graph Guidance như hướng mở rộng
```

Đề xuất có một subsection trong Chương 4:

```text
4.x. Thiết kế tích hợp EPIBT qua server C++
  4.x.1. Planner mode và feature flag
  4.x.2. Reservation theo horizon 3
  4.x.3. Protocol action một bước và operation metadata
  4.x.4. Strict execution trong Unity
  4.x.5. Diagnostics, logging và rollback về DefaultPlanner
```

### 10.3. Pseudocode rút gọn nên đưa vào phụ lục hoặc chương lý thuyết

Pseudocode chính cần có:

```text
BUILD_OPERATIONS(op_len)
INHERIT_OPERATIONS(prev_operations)
EPIBT_MAIN_LOOP(current_states, goals, inherited_ops)
EPIBT_SELECT_OPERATION(agent, inherited_priority)
```

Không cần đưa toàn bộ pseudocode dài vào chương chính nếu làm loãng nội dung. Chương chính nên có hình/flow ngắn; phụ lục chứa pseudocode đầy đủ.

### 10.4. Diagram cần bổ sung cho EPIBT

1. Diagram "PIBT -> EPIBT improvement path":

```text
Causal PIBT
  one-step action
  weak with rotation delay
        |
        v
EPIBT core
  MAO(op_len=3)
  Revisiting(L=10)
  Operation inheritance
        |
        v
EPIBT + LNS/GG
  remaining-budget local search
  congestion guidance
```

2. Sequence diagram "EPIBT TCP tick":

```text
Unity MapScenarioBootstrapPIBT_TCP
  -> PIBTTcpClient.PlanStep(loc, orientation, goal)
  -> C++ PlannerSession
  -> EpibtPlanner.plan()
  -> response actions + operation metadata
  -> GridEnemyAgentPIBT_TCP executes first action
```

3. State/execution diagram "strict action model":

```text
CR/CCR: rotate in place
FW: move one grid cell along committed facing
W: wait
shoot_eagle: clear pending movement and aim/fire
recover: only when runtime state invalid/stuck
```

### 10.5. Evidence cần thu thập cho EPIBT

Tối thiểu cần có:

- Log server default/Causal PIBT 60 giây.
- Log server EPIBT 60 giây.
- Unity console trace tương ứng.
- Bảng so sánh:
  - timeout count;
  - invalid/sanitize action count;
  - computeMs avg/max;
  - snap distance hoặc movement reject;
  - throughput/progress tới Eagle;
  - số lần pending `FW` timeout;
  - số enemy bắn Eagle hợp lệ.
- Screenshot/video ngắn:
  - enemy movement với Causal PIBT;
  - enemy movement với EPIBT;
  - trace overlay hoặc console summary.

### 10.6. Cách viết tránh overclaim

Câu nên dùng:

```text
Đồ án nghiên cứu và thiết kế hướng cải tiến từ Causal PIBT sang EPIBT để phù hợp hơn với mô hình hành động có hướng quay của tank. Phần tích hợp server C++ cho phép thử nghiệm planner ngoài Unity và so sánh với runtime PIBT C#.
```

Tránh viết:

```text
Đồ án đã triển khai đầy đủ toàn bộ EPIBT/LNS/GG state-of-the-art giống bài báo.
```

Chỉ được viết "đã triển khai đầy đủ" nếu audit code và thực nghiệm xác nhận đủ các phần: operation inheritance, revisiting, reservation horizon, LNS/GG nếu nhắc tới.

## 11. Bổ sung V2 - Tách rõ Single Play và Multiplay Internet

Quyển nên tách hai luồng sản phẩm, vì chúng phục vụ hai mục tiêu khác nhau và có kiến trúc runtime khác nhau.

### 11.1. Luồng Single Play

Mục tiêu:

- Người chơi chạy game một mình, chọn tank/map/algorithm, vào scene gameplay.
- AI enemy dùng AStar hoặc PIBT để tấn công Eagle.
- Đây là luồng chính để giải thích MAPF trong game và để chạy backtest.

Workflow đề xuất đưa vào báo cáo:

```text
Main Menu
  -> Single Play
  -> Chọn tank/outfit
  -> Chọn map
  -> Chọn algorithm: AStar / PIBT
  -> Load scene:
       AStar -> MapF_TankTest
       PIBT  -> MapF_TankTest_PIBT
  -> MapLoader build grid
  -> Spawn player + Eagle + enemies
  -> Enemy AI loop
  -> Win/Lose + metrics/backtest
```

Các file chính cần audit và đưa vào bảng Chương 4:

- `MenuViewBootstrap.cs`
- `MapLoader.cs`
- `MapTankTestBootstrap.cs`
- `MapScenarioBootstrap.cs`
- `MapScenarioBootstrapPIBT.cs`
- `GridEnemyAgent.cs`
- `GridEnemyAgentPIBT.cs`
- `PIBTPlanner.cs`
- `BacktestRunner.cs`

Diagram cần có:

- Activity diagram Single Play.
- Sequence diagram load scene + spawn scenario.
- MAPF pipeline diagram cho AStar/PIBT.

Screenshot cần chụp:

- Main menu.
- Chọn tank.
- Chọn map/mode Single Play.
- Gameplay AStar.
- Gameplay PIBT.
- Win/Lose hoặc Eagle bị tấn công.
- Backtest overlay/result nếu chạy từ Single/Backtest mode.

### 11.2. Luồng Multiplay Internet

Nguồn workflow markdown ở nhánh hiện tại `networking-gcp`:

- `adds/multiplayer_internet_workflow_plan.md`
- `adds/multiplayer_internet_lobby_workflow_plan.md`
- `adds/multiplayer_internet_lobby_workflow_plan_v2.md`
- `adds/multiplayer_internet_production_workflow_fix_plan.md`
- `adds/multiplayer_internet_production_workflow_fix_plan_v2.md`
- `adds/networking_web_play_plan_v1.md`
- `adds/networking_web_play_plan_v2_agent_goal.md`

Mục tiêu:

- Người chơi qua trình duyệt hoặc native client có thể tạo/join room Internet.
- Host chọn map/mode, hệ thống sinh room code và invite link.
- Người chơi vào waiting lobby, chọn tank, ready.
- Owner bấm Start, server load gameplay scene cho tất cả.
- Mỗi client điều khiển đúng tank của mình; enemy scale theo số người nếu đã triển khai.

Workflow đề xuất đưa vào báo cáo:

```text
Main Menu
  -> Multiplayer Internet
  -> Host: chọn map + algorithm
  -> POST /api/rooms
  -> Registry/helper restart WebSocket game server đúng map/mode
  -> Server ready gate
  -> Trả room code + invite link
  -> Waiting Lobby
      - copy code/link
      - chọn tank
      - ready
      - owner start
  -> Dedicated server load gameplay scene
  -> Clients spawn/link player tanks
  -> Gameplay online
```

Luồng Join:

```text
Open invite link / nhập code
  -> WebGL auto parse URL hoặc native input
  -> GET /api/sessions/{code}
  -> lấy webHost/webGamePort/webTransport/map/algorithm
  -> connect WebSocket
  -> Waiting Lobby
```

Các file chính cần audit:

- `MenuViewBootstrap.cs`
- `InternetSessionClient.cs`
- `InternetJoinParser.cs`
- `LanLobbyController.cs`
- `LanSessionManager.cs`
- `LanNetworkBridge.cs`
- `LanGameCoordinator.cs`
- `DedicatedServerBootstrap.cs`
- `NetworkManagerFactory.cs`
- `NetworkLaunchArgs.cs`
- `NetworkEndpointConfig.cs`
- `services/invite-registry/app.py`

Diagram cần có:

- Activity diagram Host room.
- Activity diagram Join room.
- Sequence diagram Create Room:

```text
WebGL UI -> registry /api/rooms
registry -> helper/systemd restart web server
registry -> session store
UI -> connect WebSocket endpoint
```

- Sequence diagram Ready/Start lobby.
- Deployment diagram Internet Multiplayer.

Screenshot production cần chụp:

- WebGL production homepage.
- Multiplayer Internet menu.
- Chọn map/mode Internet.
- Room creating progress.
- Waiting Lobby có room code/link.
- 2 client ở cùng lobby, mỗi người tank khác.
- Ready trạng thái đủ 2 người.
- Owner Start.
- Hai người vào gameplay, mỗi người điều khiển tank riêng.
- Invite link mở trên máy/tab khác.
- Error state có thông báo rõ nếu room/server chưa sẵn sàng.

### 11.3. Cách đặt trong outline chương

Chương 2:

- Use case nhóm Single Play.
- Use case nhóm Multiplay Internet.
- Bảng so sánh yêu cầu riêng của hai luồng.

Chương 4:

- Section 4.x Single Play runtime.
- Section 4.y Multiplay Internet runtime.
- Section 4.z Điểm giao nhau: map/mode/algorithm và scene gameplay.

Chương 5:

- Backtest thuật toán chủ yếu thuộc Single Play/Headless scene.
- Production Internet smoke test thuộc nhóm triển khai hệ thống, không thay thế benchmark thuật toán.

## 12. Bổ sung V2 - Deploy production trên GCP, Nginx và CI/CD

Phần deploy không nên biến thành hướng dẫn thao tác dài, nhưng cần nhắc đủ để hội đồng hiểu sản phẩm có deployment thực tế, không chỉ chạy Editor.

### 12.1. Kiến trúc production cần mô tả

Nguồn chính:

- `adds/networking_gcp_deployment_plan.md`
- `adds/networking_gcp_deployment_plan_v2.md`
- `adds/networking_gcp_deployment_plan_v3.md`
- `adds/networking_web_play_plan_v1.md`
- `adds/networking_web_play_plan_v2_agent_goal.md`
- `adds/github_actions_cicd_projectY_plan.md`

Sơ đồ deployment đề xuất:

```text
Internet users
  |
  | HTTP/HTTPS
  v
GCP VM: tank-mapf-server
  |
  |-- Nginx
  |     /                  -> WebGL static files
  |     /play?session=...  -> WebGL fallback route
  |     /registry or /api  -> invite registry proxy
  |     /ws optional       -> WebSocket proxy when HTTPS/WSS
  |
  |-- invite registry :8080
  |     healthz, sessions, rooms
  |
  |-- native dedicated server
  |     UDP :7777
  |
  |-- web dedicated server
        WebSocket TCP :7778
```

Nội dung cần nhắc trong Chương 4 hoặc Chương 6:

- GCP project: `tankmapf`.
- VM hiện dùng cho MVP: `tank-mapf-server`.
- Static IP đã thấy trong docs: `35.240.203.91`.
- Native UDP lane: port `7777`.
- Registry lane: port `8080`.
- WebSocket lane cho WebGL: port `7778`.
- WebGL static hosting qua Nginx tại `/var/www/tank-mapf-web/current`.
- Artifact server và WebGL deploy từ build output.
- Terraform quản lý VM/firewall/bucket.
- GitHub Actions/CI-CD dự kiến build server + WebGL, upload artifact, deploy manual có kiểm soát.

### 12.2. Nội dung Nginx nên đưa vào báo cáo

Chỉ cần một đoạn mô tả và một sơ đồ ngắn:

- Nginx phục vụ WebGL build (`index.html`, `.wasm`, `.data`, `.js`).
- Route `/play?session=<code>` vẫn trả WebGL để Unity đọc URL và auto join.
- Route registry/API proxy về service Python ở `127.0.0.1:8080`.
- Với production HTTPS, Nginx hoặc Caddy/Certbot cấp TLS và proxy WebSocket thành WSS để tránh mixed content.

Không cần đưa toàn bộ config Nginx vào chương chính. Nếu cần, đưa snippet vào phụ lục:

```nginx
location / {
    try_files $uri $uri/ /index.html;
}

location /registry/ {
    proxy_pass http://127.0.0.1:8080/;
}
```

Nếu đã có WSS/domain, bổ sung:

```nginx
location /ws/ {
    proxy_pass http://127.0.0.1:7778/;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
}
```

### 12.3. CI/CD nên trình bày ở mức nào

Trong quyển, CI/CD là minh chứng kỹ thuật/phụ trợ, không nên lấn át MAPF. Nên đặt trong:

- Chương 4: "Triển khai và đóng gói hệ thống".
- Chương 6: "Đóng góp về triển khai production".
- Phụ lục: workflow chi tiết nếu cần.

Nội dung chính:

- Build Linux dedicated server bằng Unity batchmode.
- Build WebGL client bằng Unity batchmode.
- Đóng gói artifact theo commit SHA.
- Upload lên GCS bucket.
- Deploy lên VM bằng script.
- Smoke test registry/web URL.
- Bảo vệ secrets: không commit tfvars, token, service account key.

### 12.4. Screenshot/log production cần placeholder

Tạo placeholder trong LaTeX bằng caption tạm, chưa cần ảnh ngay:

```latex
\begin{figure}[htbp]
    \centering
    \includegraphics[width=0.95\linewidth]{Hinhve/datn/production_web_home_placeholder.png}
    \caption{Giao diện WebGL production của Tank MAPF trên GCP}
    \label{fig:production-web-home}
\end{figure}
```

Danh sách ảnh production cần chụp:

- `production_web_home.png`: trang WebGL mở từ production.
- `production_create_room.png`: màn chọn map/mode Internet.
- `production_room_created.png`: room code/invite link.
- `production_waiting_lobby_two_clients.png`: 2 client cùng lobby.
- `production_ready_start.png`: ready và owner start.
- `production_gameplay_two_clients.png`: 2 player trong cùng trận.
- `production_registry_health.png`: `/healthz` hoặc API session response.
- `production_gcp_vm_services.png`: status `tank-mapf-server`, `tank-mapf-server-web`, `tank-mapf-registry`, `nginx`.
- `production_nginx_serving_webgl.png`: DevTools/network hoặc curl chứng minh WebGL static files load được.
- `production_github_actions_success.png`: workflow build/deploy pass nếu CI/CD đã chạy.

Placeholder nên đặt trong repo:

```text
adds/report/20225808_DuongQuangDong_2025.2/Hinhve/datn/placeholders/
```

Ảnh thật sau này đặt:

```text
adds/report/20225808_DuongQuangDong_2025.2/Hinhve/datn/production/
```

## 13. Bổ sung V2 - Điều chỉnh lại phân bổ trang

Vì V2 thêm EPIBT và Multiplay Internet/GCP, cần điều chỉnh số trang để quyển vẫn quanh 70 trang.

| Chương | Tên | Số trang V1 | Số trang V2 đề xuất | Ghi chú |
|---|---:|---:|---:|---|
| 1 | Giới thiệu đề tài | 6-8 | 6-7 | Giữ gọn |
| 2 | Khảo sát và phân tích yêu cầu | 8-10 | 9-11 | Tách use case Single/Internet |
| 3 | Nền tảng lý thuyết và công nghệ | 10-12 | 12-14 | Thêm EPIBT |
| 4 | Phân tích, thiết kế và triển khai | 18-22 | 22-25 | Thêm Internet/GCP nhưng viết cô đọng |
| 5 | Thực nghiệm và đánh giá | 12-16 | 13-16 | Thêm so sánh Causal PIBT/EPIBT nếu có dữ liệu |
| 6 | Giải pháp và đóng góp nổi bật | 5-7 | 5-7 | Nhấn vào EPIBT + production |
| 7 | Kết luận và hướng phát triển | 3-5 | 3-4 | Giữ gọn |

Tổng mục tiêu V2: 70-78 trang nếu đầy đủ hình. Nếu vượt 80 trang:

- Đẩy protocol JSON, pseudocode EPIBT đầy đủ, Nginx config, CI/CD YAML sang phụ lục.
- Chương chính chỉ giữ sơ đồ và bảng tóm tắt.

## 14. Bổ sung V2 - Checklist cập nhật sau khi viết bản nháp

Sau khi triển khai bản thảo LaTeX, chạy checklist riêng cho V2:

- Đã có section "Cải tiến từ Causal PIBT sang EPIBT".
- Đã có ít nhất 1 diagram PIBT -> EPIBT.
- Đã có ít nhất 1 sequence diagram PIBT TCP/EPIBT.
- Đã phân biệt rõ Single Play và Multiplay Internet trong Chương 2 và Chương 4.
- Đã có workflow Host/Join Internet room.
- Đã có deployment diagram GCP/Nginx/registry/server.
- Đã có placeholder ảnh production để chụp sau.
- Đã ghi rõ WebGL dùng WebSocket lane, native dùng UDP lane, không nhập nhằng hai luồng.
- Đã ghi rõ phần nào đã shipped, phần nào là prototype/plan/hướng phát triển.
- Không overclaim EPIBT nếu chưa có đủ code/evidence.
- Không đưa screenshot production giả; placeholder phải được ghi rõ là cần thay bằng ảnh thật trước khi nộp.
