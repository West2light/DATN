# Milestone R1 - Audit và khóa phạm vi báo cáo ĐATN

Ngày audit: 2026-06-21  
Repo Unity: `D:\2025.2\DATN\projectY`  
Nhánh Unity hiện tại: `networking-gcp`, HEAD `cfb85a3`  
Repo C++ trong WSL đã đối chiếu nhanh: `~/projectY`, nhánh `main`, HEAD `aeb4cfa`  
Plan nền: `adds/PLAN/datn_latex_report_completion_plan_2026-06-21_v2.md`

R1 chỉ khóa phạm vi và nguồn bằng chứng cho quyển. R1 chưa thay thế các milestone sau: R2 vẽ diagram, R3 chạy backtest/thực nghiệm, R4 chụp ảnh production, R5 viết LaTeX.

Nguồn học thuật chính cho phần EPIBT: Egor Yukhnevich và Anton Andreychuk, "Enhancing PIBT via Multi-Action Operations", Proceedings of the AAAI Conference on Artificial Intelligence, 2026; arXiv:2511.09193. Khi viết LaTeX, dùng citation key tạm `yukhnevich2026enhancingpibt`.

## 1. Kết luận khóa phạm vi

| Hạng mục | Quyết định R1 | Cách đưa vào quyển | Ghi chú kiểm chứng |
|---|---|---|---|
| Single Play + MAPF gameplay | Nội dung lõi | Chương 2, 4, 5 | Đây là tuyến chính để trình bày yêu cầu, thiết kế runtime và benchmark thuật toán. |
| A* baseline | Nội dung lõi | Chương 3, 4, 5 | Dùng làm baseline thực nghiệm và luồng so sánh trong game. |
| PIBT C# trong Unity | Nội dung lõi | Chương 3, 4, 5, 6 | Viết là phần chuyển hóa lõi thuật toán vào Unity, nhưng không overclaim là port đầy đủ toàn bộ kiến trúc C++ gốc. |
| PIBT TCP/C++ | Nội dung lõi | Chương 3, 4, 5, 6 | Đây là thuật toán/tuyến solver C++ ngoài Unity và là mode thực nghiệm `PIBT_TCP`; cần smoke log và kết quả backtest trước bản nộp. |
| Cải tiến Causal PIBT -> EPIBT | Nội dung lõi | Chương 3, 4, 5, 6 | Trình bày dựa trên bài báo "Enhancing PIBT via Multi-Action Operations" và đối chiếu với thiết kế `epibt.*`; R3 cần bổ sung log/số liệu để kết luận định lượng. |
| Multiplay Internet | Nội dung triển khai hệ thống, không thay benchmark thuật toán | Chương 2, 4, 5 hoặc phụ lục tùy độ ổn định production | Trình bày tách biệt với Single Play; dùng smoke test/screenshot production làm bằng chứng. |
| WebGL/GCP/Nginx/CI-CD | Phần triển khai production/deployment | Chương 4, phần đánh giá hệ thống ở Chương 5, phụ lục cấu hình | Giữ ở mức kiến trúc, quy trình deploy và ảnh thật; đẩy YAML/Nginx config dài sang phụ lục. |
| Backtest tự động | Nội dung lõi của đánh giá | Chương 5 | Hiện chưa thấy thư mục `BacktestResults/`; cần chạy để có CSV/HTML/chart thật. |
| Screenshot production | Placeholder bắt buộc, chưa thay bằng ảnh giả | Chương 4, 5, phụ lục | Cần chụp từ production sau R4; placeholder phải ghi rõ "cần thay bằng ảnh thật". |

Chốt ngắn để dùng trong đề cương: đồ án tập trung vào mô phỏng và đánh giá MAPF cho enemy tank trong Unity, với A* làm baseline, PIBT C# là thuật toán runtime trong Unity, PIBT TCP/C++ là thuật toán/tuyến solver lõi ngoài Unity, EPIBT là cải tiến thuật toán lõi dựa trên multi-action operations, còn WebGL/GCP/Multiplay Internet là phần triển khai hệ thống và demo production.

## 2. Bảng hiện trạng feature

| Feature | Hiện trạng audit | Bằng chứng chính | Trạng thái báo cáo |
|---|---|---|---|
| Menu chính | Có các entry `SINGLE PLAY`, `MULTIPLAYER INTERNET`, `BACKTEST` | `Assets/Scripts/MenuViewBootstrap.cs` | Đưa vào Chương 4 như luồng điều hướng sản phẩm. |
| Chọn map | Có 5 map: Alpha-32, Mansion, Chantry, Gallows, Maze-128 | `MenuViewBootstrap.cs`, `Assets/MapData/*.map` | Đưa vào Chương 5 làm tập map thực nghiệm. |
| Chọn thuật toán | Có 3 mode `AStar`, `PIBT`, `PIBT_TCP` | `MenuViewBootstrap.cs` | Đưa vào Chương 4 và ma trận thực nghiệm Chương 5. |
| Scene A* | Có scene `MapF_TankTest` | `Assets/Scenes/MapF_TankTest.unity`, `MapTankTestBootstrap.cs` | Nội dung lõi. |
| Scene PIBT/PIBT-TCP | Có scene `MapF_TankTest_PIBT`; `PIBT_TCP` dùng flag thuật toán | `Assets/Scenes/MapF_TankTest_PIBT.unity`, `MapTankTestBootstrap.cs` | Nội dung lõi. |
| Baseline A* enemy | Có pathfinding và agent runtime riêng | `GridAStarPathfinder.cs`, `GridEnemyAgent.cs`, `MapScenarioBootstrap.cs` | Baseline bắt buộc trong Chương 5. |
| PIBT C# local | Có planner, wrapper, agent runtime và bootstrap | `PIBTPlanner.cs`, `GridPIBTPathfinder.cs`, `GridEnemyAgentPIBT.cs`, `MapScenarioBootstrapPIBT.cs` | Viết là thuật toán chính trong Unity. |
| PIBT TCP/C++ | Unity có client TCP/Web relay và scenario bootstrap; server C++ có file planner/session | `PIBTTcpClient.cs`, `PIBTWebRelayClient.cs`, `MapScenarioBootstrapPIBT_TCP.cs`; WSL `src/PibtTcpServer.cpp`, `src/PlannerSession.cpp` | Nội dung lõi, cần log/smoke và backtest. |
| EPIBT | Tài liệu feature branch có MAO/Revisiting/IO; WSL có `default_planner/epibt.cpp`, `epibt.h`, `epibt_types.h`, `src/epibt_smoke_main.cpp` | `feature/pibt-tcp-client-server:adds/epibt_full.md`, WSL `~/projectY`, paper `Enhancing PIBT via Multi-Action Operations` | Nội dung lõi; R3 bổ sung số liệu để kết luận hiệu quả. |
| Backtest | Có runner xuất CSV/HTML và so sánh A*, PIBT, PIBT-C++ | `Assets/Scripts/Backtest/BacktestRunner.cs`, `Tools/backtest_plot_report.py` | Cần chạy R3 để có số liệu thật. |
| Multiplay Internet lobby | Có controller/client/parser/session manager và tài liệu workflow | `Assets/Scripts/Multiplayer/*`, `adds/multiplayer_internet_lobby_workflow_plan_v2.md` | Đưa vào phần hệ thống, tách khỏi benchmark thuật toán. |
| WebGL/WebSocket | Có build WebGL và lane WebSocket tách với UDP native | `Assets/Editor/BuildWebClient.cs`, `BuildServer.cs`, `NetworkTransportMode.cs`, `NetworkManagerFactory.cs` | Đưa vào kiến trúc deployment. |
| GCP/Nginx/registry | Có Terraform/script/systemd/registry app và workflow deploy | `infra/gcp/*`, `services/invite-registry/app.py`, `.github/workflows/*.yml` | Đưa vào Chương 4; log/screenshot để chứng minh. |
| Production evidence | Chưa thấy ảnh production trong audit R1 | Cần chụp sau | Đặt placeholder, không dùng ảnh giả. |

## 3. Bảng file/module chính

| Nhóm | File/module chính | Vai trò trong hệ thống | Vị trí nên viết |
|---|---|---|---|
| Mẫu báo cáo | `adds/report/20225808_DuongQuangDong_2025.2` | Template LaTeX để viết quyển | Trước R5 khi bắt đầu viết. |
| Quy định viết | `adds/report/datn-report-rules-agent.md`, `adds/report/quy-dinh-viet-quyen-DATN.md` | Luật trình bày, cấu trúc và văn phong | Dùng xuyên suốt. |
| Plan báo cáo | `adds/PLAN/datn_latex_report_completion_plan_2026-06-21_v2.md` | Roadmap tổng thể tới quyển khoảng 70 trang | Tài liệu điều phối. |
| Menu/game flow | `MenuViewBootstrap.cs` | Entry Single Play, Multiplayer Internet, Backtest, chọn map/mode | Chương 2 và 4. |
| Bootstrap scene | `MapTankTestBootstrap.cs` | Nạp map, spawn player/scenario, chọn A*/PIBT/PIBT_TCP | Chương 4. |
| Map loader | `MapLoader.cs`, `Assets/MapData/*.map` | Đọc lưới, build wall/cell, cung cấp map cho pathfinding | Chương 4 và 5. |
| A* baseline | `GridAStarPathfinder.cs`, `GridEnemyAgent.cs`, `MapScenarioBootstrap.cs` | Baseline pathfinding và enemy agent | Chương 3, 4, 5. |
| PIBT C# core | `PIBTPlanner.cs` | Shared flow grid, heuristic cache, Frank-Wolfe/replan logic | Chương 3, 4, 6. |
| PIBT C# wrapper | `GridPIBTPathfinder.cs` | Adapter từ Unity cell sang planner và trả path | Chương 4. |
| PIBT C# runtime | `GridEnemyAgentPIBT.cs`, `MapScenarioBootstrapPIBT.cs` | Agent runtime và spawn/wiring enemy | Chương 4. |
| TCP Unity client | `PIBTTcpClient.cs`, `PIBTWebRelayClient.cs` | JSON-lines TCP client và WebGL relay tới solver | Chương 4. |
| TCP scenario | `GridEnemyAgentPIBT_TCP.cs`, `MapScenarioBootstrapPIBT_TCP.cs` | Mode `PIBT_TCP`, gửi `hello`/`plan_step`, nhận action | Chương 4, 5. |
| C++ server | WSL `src/PibtTcpServer.cpp`, `src/PlannerSession.cpp`, `src/UnityStartKitAdapter.cpp` | Server solver PIBT C++ ngoài Unity, session và adapter protocol | Chương 3, 4, 5; phụ lục protocol nếu dài. |
| EPIBT core | WSL `default_planner/epibt.cpp`, `epibt.h`, `epibt_types.h` | Cải tiến lõi từ Causal PIBT sang EPIBT theo bài báo tham khảo | Chương 3, 4, 5, 6. |
| Backtest | `Assets/Scripts/Backtest/*`, `Tools/backtest_plot_report.py` | Tạo ma trận map x thuật toán x repetition, xuất CSV/HTML/chart | Chương 5. |
| Multiplayer | `Assets/Scripts/Multiplayer/*` | Host/join room, lobby, session, network bootstrap | Chương 2 và 4. |
| Registry | `services/invite-registry/app.py` | API session/room, health check, PIBT relay endpoint | Chương 4. |
| Infra GCP | `infra/gcp/*.tf`, `infra/gcp/scripts/*.sh`, `infra/gcp/systemd/*.tpl` | VM, firewall, startup, deploy server/WebGL/Nginx/systemd | Chương 4; phụ lục cấu hình. |
| CI/CD | `.github/workflows/ci-validate.yml`, `terraform-plan.yml`, `unity-build.yml`, `deploy-gcp.yml` | Validate, build Unity, deploy server/WebGL, smoke test | Chương 4 hoặc phụ lục. |
| Hình tham khảo | `Assets/Docs/references/` | Nguồn tham khảo/screenshot/diagram | Dùng khi R2/R4 dựng hình. |

## 4. Tách rõ hai luồng sản phẩm

### 4.1. Luồng Single Play

Luồng chính của quyển:

```text
Main Menu
  -> Single Play
  -> Chọn tank/outfit
  -> Chọn map
  -> Chọn algorithm: AStar / PIBT / PIBT-TCP
  -> Load scene MapF_TankTest hoặc MapF_TankTest_PIBT
  -> MapTankTestBootstrap spawn player + scenario
  -> Enemy chạy pathfinding và tấn công Eagle/player
  -> Win/Lose hoặc Backtest thu metric
```

Vị trí viết:

| Chương | Nội dung |
|---|---|
| Chương 2 | Use case Single Play, chọn map, chọn thuật toán, chơi và quan sát kết quả. |
| Chương 4 | Runtime flow, scene bootstrap, map loader, enemy agent, pathfinding pipeline. |
| Chương 5 | Backtest A* vs PIBT vs PIBT_TCP trên 5 map, metric và biểu đồ. |

Single Play là nơi phù hợp nhất để chứng minh đóng góp thuật toán vì có thể kiểm soát map, số lần chạy, thuật toán và metric.

### 4.2. Luồng Multiplay Internet

Luồng triển khai hệ thống:

```text
Main Menu
  -> Multiplayer Internet
  -> Host: chọn map + algorithm
  -> Registry tạo room/session
  -> Dedicated server WebSocket chạy đúng map/mode
  -> Waiting lobby: room code/link, người chơi ready
  -> Owner Start
  -> Các client join cùng gameplay online
```

Vị trí viết:

| Chương | Nội dung |
|---|---|
| Chương 2 | Use case host room, join room, ready/start, chơi online. |
| Chương 4 | Kiến trúc registry, transport, session, WebGL/WebSocket, native UDP. |
| Chương 5 | Smoke test production, ảnh 2 client cùng phòng, trạng thái service. |

Điểm cần ghi rõ trong báo cáo: WebGL/browser không dùng UDP native trực tiếp. Luồng WebGL dùng WebSocket lane; native desktop có UDP lane riêng. Không dùng kết quả Multiplay Internet để thay thế benchmark thuật toán trừ khi có thiết kế thí nghiệm riêng.

## 5. Cải tiến từ Causal PIBT sang EPIBT

Tuyến Causal PIBT -> EPIBT là nội dung lõi của quyển. Phần này phải trích dẫn bài báo của Yukhnevich và Andreychuk về multi-action operations, sau đó nối bài báo với thiết kế server C++ và runtime Unity của đồ án.

Tuyến thuật toán nên viết trong chương chính:

```text
PIBT / Causal PIBT một bước
  -> Vấn đề: tank có hướng, muốn đi sang ô kề có thể cần quay trước khi tiến
  -> EPIBT core
       - Multi-action Operations, op_len = 3
       - Revisiting Agents, giới hạn L
       - Inheritance of Operations
  -> Unity/TCP integration
       - Server chọn operation
       - Unity chỉ thực thi action đầu ở mỗi tick
  -> EPIBT + LNS/GG
       - Hướng nâng cao, chỉ viết là hoàn thiện nếu có code và evidence đầy đủ
```

Quy tắc viết tránh overclaim:

| Mức bằng chứng | Cách viết được phép |
|---|---|
| Chỉ có tài liệu plan và file `epibt.*` | "Đồ án thiết kế hướng cải tiến từ Causal PIBT sang EPIBT và chuẩn bị nhánh server/prototype để thử nghiệm." |
| Có smoke log EPIBT 60 giây | "Đồ án đã tích hợp thử nghiệm EPIBT qua server C++ trong kịch bản kiểm thử." |
| Có backtest so sánh A*, PIBT, PIBT_TCP/Causal, EPIBT | "Đồ án đánh giá thực nghiệm tác động của EPIBT trong runtime game." |
| Có LNS/GG code + benchmark | Mới được viết về EPIBT+LNS/GG như kết quả triển khai hoàn chỉnh. |

Evidence tối thiểu cần thu để Chương 5 đánh giá EPIBT bằng số liệu thay vì chỉ trình bày lý thuyết/thiết kế:

| Evidence | File/log cần có |
|---|---|
| C++ server build pass | Log build WSL hoặc CI cho `epibt_smoke_main`/server. |
| Smoke default/Causal PIBT | Log 60 giây, không timeout/hard fail. |
| Smoke EPIBT | Log 60 giây, có metadata planner/operation nếu protocol hỗ trợ. |
| Unity TCP trace | Console trace `hello`, `plan_step`, action nhận về, agent execute. |
| Backtest | CSV/HTML/chart có dòng EPIBT hoặc mode tương ứng. |

BibTeX tạm cần đưa vào file bibliography khi viết LaTeX:

```bibtex
@inproceedings{yukhnevich2026enhancingpibt,
  title     = {Enhancing PIBT via Multi-Action Operations},
  author    = {Yukhnevich, Egor and Andreychuk, Anton},
  booktitle = {Proceedings of the AAAI Conference on Artificial Intelligence},
  year      = {2026},
  note      = {arXiv:2511.09193},
  url       = {https://arxiv.org/abs/2511.09193}
}
```

## 6. Phạm vi WebGL/GCP/Nginx/CI-CD

WebGL/GCP nên được viết như phần triển khai production của hệ thống, không phải trọng tâm thuật toán.

| Thành phần | Đưa vào chương chính | Đẩy sang phụ lục |
|---|---|---|
| GCP VM, public endpoint, firewall, systemd services | Sơ đồ deployment và mô tả vai trò | Terraform chi tiết nếu dài. |
| Nginx/static WebGL hosting | Nêu vai trò phục vụ WebGL, proxy registry/API nếu có | Full Nginx config. |
| Registry/session API | Mô tả `/api/sessions`, `/api/rooms`, health check | JSON schema/protocol dài. |
| UDP/WebSocket split | Bắt buộc viết rõ trong Chương 4 | Không đẩy đi vì đây là quyết định kiến trúc quan trọng. |
| GitHub Actions | Mô tả pipeline build/deploy/smoke test | YAML đầy đủ. |
| Production logs | Đưa ảnh/chụp màn hình minh chứng | Log dài để phụ lục hoặc link artifact. |

Nếu chưa có production evidence tại thời điểm viết nháp, ghi là "luồng triển khai production/MVP đã có cấu trúc code và pipeline, cần xác nhận bằng smoke test và screenshot trước khi nộp".

## 7. Placeholder ảnh và bằng chứng cần chụp

| ID placeholder | Nội dung ảnh/bằng chứng | Vị trí dự kiến |
|---|---|---|
| `FIG-SP-01` | Main menu có Single Play, Multiplayer Internet, Backtest | Chương 4 |
| `FIG-SP-02` | Chọn map/mode Single Play với A*, PIBT, PIBT-TCP | Chương 4 |
| `FIG-SP-03` | Gameplay A* trên map nhỏ | Chương 5 |
| `FIG-SP-04` | Gameplay PIBT local | Chương 5 |
| `FIG-SP-05` | Gameplay PIBT TCP hoặc trace TCP server | Chương 5 |
| `FIG-EPIBT-01` | Log hoặc overlay EPIBT nếu R3 xác nhận | Chương 5 hoặc phụ lục |
| `FIG-BT-01` | Backtest overlay đang chạy | Chương 5 |
| `FIG-BT-02` | Chart HTML/PNG từ BacktestResults | Chương 5 |
| `FIG-MP-01` | Multiplayer Internet menu | Chương 4 |
| `FIG-MP-02` | Host tạo room, room code/link | Chương 4 |
| `FIG-MP-03` | Waiting lobby nhiều người chơi, trạng thái ready | Chương 4 |
| `FIG-MP-04` | Hai client cùng vào gameplay online | Chương 5 |
| `FIG-GCP-01` | WebGL production homepage | Chương 4 |
| `FIG-GCP-02` | Registry/API health hoặc session response | Chương 5 |
| `FIG-GCP-03` | `systemctl status` cho server, web server, registry | Chương 5 hoặc phụ lục |
| `FIG-CICD-01` | GitHub Actions deploy/smoke test pass | Chương 4 hoặc phụ lục |

Không thay các placeholder này bằng ảnh giả. Khi chụp xong, đổi tên file rõ ràng và ghi ngày chụp/nhánh/commit nếu ảnh dùng để chứng minh production.

## 8. Khoảng trống bằng chứng cần xử lý trước khi viết bản LaTeX chính

| Khoảng trống | Tác động nếu chưa xử lý | Milestone nên xử lý |
|---|---|---|
| Chưa thấy `BacktestResults/` trong audit R1 | Chương 5 chưa có số liệu thật | R3 |
| Chưa có log EPIBT được xác nhận trong audit R1 | Vẫn viết EPIBT trong nội dung lõi, nhưng Chương 5 chưa thể kết luận định lượng nếu thiếu số liệu | R3 |
| Chưa có screenshot production | Phần GCP/WebGL/Multiplay thiếu bằng chứng trực quan | R4 |
| Chưa có diagram chính thức | Chương 2/4 sẽ khó đọc và dễ dài dòng | R2 |
| Chưa khóa nội dung phụ lục | Quyển có nguy cơ vượt 80 trang | Sau R2/R3 |

## 9. Checklist Done của Milestone R1

- [x] Có bảng hiện trạng feature.
- [x] Có bảng file/module chính.
- [x] Có quyết định rõ: WebGL/GCP là phần triển khai hệ thống/deployment, không phải lõi benchmark thuật toán; đưa vào chương chính ở mức kiến trúc và evidence, cấu hình dài để phụ lục.
- [x] Có quyết định rõ: PIBT TCP/C++ và Causal PIBT -> EPIBT là nội dung lõi; EPIBT cần trích dẫn bài báo "Enhancing PIBT via Multi-Action Operations" và cần evidence riêng cho phần đánh giá định lượng.
- [x] Có tách luồng Single Play và Multiplay Internet.
- [x] Có danh sách placeholder ảnh/screenshot production cần chụp sau.
- [x] Có danh sách evidence gap cho R2/R3/R4.
- [x] Có citation key tạm và BibTeX tạm cho nguồn EPIBT.

## 10. Đầu vào trực tiếp cho Milestone R2

R2 cần vẽ bộ sơ đồ nền sau, theo đúng phạm vi đã khóa và bổ sung đầy đủ luồng Multiplay Internet trong plan V2:

| Ưu tiên | Diagram | Mục tiêu |
|---|---|---|
| 1 | System context diagram | Người chơi, Unity client, C++ solver, registry, GCP/WebGL. |
| 2 | Architecture/package diagram | Gameplay, MAPF, TCP solver, Multiplayer, Backtest, Infra. |
| 3 | Activity diagram Single Play | Từ menu tới chọn map/mode, load scene, chơi/backtest. |
| 4 | Activity diagram Multiplay Internet Host/Join | Host chọn map/mode, tạo room, join bằng room code/link, vào waiting lobby. |
| 5 | Sequence diagram Internet Create Room | WebGL/native UI -> registry `/api/rooms` -> helper/systemd -> WebSocket server -> room link. |
| 6 | Sequence diagram Internet Ready/Start | Lobby ready state, owner Start, server load gameplay, spawn tank theo client. |
| 7 | Data-flow diagram MAPF pipeline | `.map` -> `MapLoader` -> scenario -> A*/PIBT/PIBT_TCP/EPIBT -> agent -> metric. |
| 8 | Class diagram rút gọn pathfinding/MAPF | Các lớp chính quanh `MapLoader`, bootstrap, enemy agent và planner. |
| 9 | Sequence diagram A* replan loop | `GridEnemyAgent.Update()` -> `GridAStarPathfinder.TryFindPath()` -> follow waypoint. |
| 10 | Sequence diagram PIBT C# | `GridEnemyAgentPIBT` -> `GridPIBTPathfinder` -> `PIBTPlanner.FrankWolfe()`. |
| 11 | Sequence diagram PIBT TCP/EPIBT tick | Unity `hello`/`plan_step` tới C++ server và agent execute action đầu. |
| 12 | PIBT -> EPIBT improvement path | Từ one-step PIBT/Causal PIBT tới MAO/Revisiting/IO, có citation paper. |
| 13 | Backtest workflow diagram | Map set x algorithm set x repetition -> run scene -> collect metric -> CSV/HTML/chart. |
| 14 | Deployment diagram GCP/Nginx/WebGL/Internet | Browser WebGL, native client, registry, UDP/WebSocket server, Nginx, CI/CD. |

Sau R2 mới nên bắt đầu dựng hình chính thức vào `Assets/Docs/references/` hoặc thư mục hình của template LaTeX, để R5 không phải viết lại cấu trúc chương. Bộ source diagram tách file riêng hiện đặt tại `adds/PLAN/R2_DIAGRAMS_V2/`.
