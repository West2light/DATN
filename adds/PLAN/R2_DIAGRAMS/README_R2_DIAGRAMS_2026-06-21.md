# Milestone R2 - Bộ diagram nền cho quyển ĐATN

Superseded note: the standalone source set now lives in `adds/PLAN/R2_DIAGRAMS_V2/`. This legacy folder keeps the original multi-page draw.io package for reference only.

Ngày tạo: 2026-06-21  
Nguồn phạm vi: `adds/PLAN/datn_latex_report_completion_plan_2026-06-21_v2.md` và `adds/PLAN/datn_latex_report_R1_audit_scope_2026-06-21.md`  
File draw.io chính: `adds/PLAN/R2_DIAGRAMS/datn_R2_diagrams_2026-06-21.drawio`

## 1. Cách dùng

1. Mở `datn_R2_diagrams_2026-06-21.drawio` bằng diagrams.net/draw.io.
2. Mỗi page trong file là một diagram riêng.
3. Export từng page sang PNG độ phân giải cao hoặc PDF/SVG vector.
4. Khi đưa vào LaTeX, ưu tiên PDF/SVG nếu template xử lý được; nếu dùng PNG, export scale 2x hoặc 3x để chữ không vỡ.

Tên folder gợi ý khi đưa sang template LaTeX:

```text
adds/report/20225808_DuongQuangDong_2025.2/Hinhve/datn/
```

## 2. Danh sách page trong draw.io

| Page | Diagram | Mục đích | Vị trí đề xuất trong quyển | Tên export gợi ý |
|---|---|---|---|---|
| 01 | `01_System_Context` | Bối cảnh hệ thống: người chơi, Unity client, GCP, registry, C++ solver, dữ liệu map | Chương 1 hoặc đầu Chương 4 | `system_context.pdf` |
| 02 | `02_Architecture_Packages` | Kiến trúc package/tầng: UI, map runtime, gameplay, MAPF, TCP, multiplayer, backtest, infra | Chương 4 | `architecture_packages.pdf` |
| 03 | `03_Single_Play_Activity` | Activity diagram cho luồng Single Play | Chương 2 hoặc Chương 4 | `single_play_activity.pdf` |
| 04 | `04_Internet_Host_Join_Activity` | Activity diagram cho luồng Multiplayer Internet Host/Join | Chương 2 hoặc Chương 4 | `internet_host_join_activity.pdf` |
| 05 | `05_Internet_Create_Room_Sequence` | Sequence tạo phòng Internet qua `/api/rooms`, registry, systemd, WebSocket server | Chương 4 | `internet_create_room_sequence.pdf` |
| 06 | `06_Internet_Ready_Start_Sequence` | Sequence waiting lobby, ready, owner start và spawn gameplay online | Chương 4 | `internet_ready_start_sequence.pdf` |
| 07 | `07_MAPF_Data_Flow` | Data-flow MAPF: `.map` -> `MapLoader` -> scenario -> A*/PIBT/PIBT_TCP/EPIBT -> agent -> metric | Chương 4 hoặc Chương 5 | `mapf_data_flow.pdf` |
| 08 | `08_Pathfinding_Class_Diagram` | Class diagram rút gọn cho cụm pathfinding/MAPF | Chương 4 hoặc phụ lục | `pathfinding_class_diagram.pdf` |
| 09 | `09_AStar_Replan_Sequence` | Sequence vòng replan A* của enemy | Chương 4 hoặc phụ lục | `astar_replan_sequence.pdf` |
| 10 | `10_PIBT_CSharp_Sequence` | Sequence PIBT C# trong Unity | Chương 4 | `pibt_csharp_sequence.pdf` |
| 11 | `11_PIBT_TCP_EPIBT_Sequence` | Sequence PIBT TCP/EPIBT tick: `hello`, `plan_step`, server operation, Unity execute action đầu | Chương 4 và Chương 5 | `pibt_tcp_epibt_sequence.pdf` |
| 12 | `12_EPIBT_Improvement_Path` | Đường cải tiến PIBT/Causal PIBT -> EPIBT, có nguồn paper | Chương 3 | `pibt_to_epibt_improvement_path.pdf` |
| 13 | `13_Backtest_Workflow` | Workflow thực nghiệm/backtest và xuất CSV/HTML/chart | Chương 5 | `backtest_workflow.pdf` |
| 14 | `14_GCP_Nginx_Deployment` | Deployment GCP/Nginx/WebGL/registry/UDP/WebSocket/CI-CD | Chương 4 hoặc Chương 6 | `gcp_nginx_deployment.pdf` |

## 3. Điểm đã bổ sung so với đề xuất R1

R1 ban đầu mới đề xuất 6 diagram nền, còn thiếu cụm Internet chi tiết. R2 đã bổ sung theo plan V2:

- Activity diagram Host/Join Internet room.
- Sequence diagram Create Room.
- Sequence diagram Ready/Start lobby.
- Deployment diagram Internet Multiplayer với WebGL/WebSocket, native UDP, registry, Nginx và GCP.

## 4. Quy tắc đưa vào chương chính

Nên đưa vào chương chính:

- `01_System_Context`
- `02_Architecture_Packages`
- `03_Single_Play_Activity`
- `04_Internet_Host_Join_Activity`
- `07_MAPF_Data_Flow`
- `11_PIBT_TCP_EPIBT_Sequence`
- `12_EPIBT_Improvement_Path`
- `13_Backtest_Workflow`
- `14_GCP_Nginx_Deployment`

Nên cân nhắc đưa phụ lục nếu quyển vượt trang:

- `05_Internet_Create_Room_Sequence`
- `06_Internet_Ready_Start_Sequence`
- `08_Pathfinding_Class_Diagram`
- `09_AStar_Replan_Sequence`
- `10_PIBT_CSharp_Sequence`

## 5. Việc cần làm sau R2

- Rà lại bằng mắt trong draw.io để chỉnh vị trí nếu chữ tràn sau khi mở.
- Export bản PDF/PNG và đặt vào thư mục hình của template LaTeX.
- Ở R3, chạy backtest/smoke log để thay phần evidence trong các diagram thực nghiệm nếu cần.
- Ở R4, chụp screenshot production để đi cùng diagram deployment và Internet workflow.
