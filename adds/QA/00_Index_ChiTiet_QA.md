# Mục Lục Tài Liệu Ôn Tập — Bộ QA Chi Tiết

Đây là **bản đồ đọc** cho toàn bộ tài liệu giải thích code trong `adds/QA/`. Đọc theo thứ tự dưới đây để đi từ tổng quan → nền móng → ba tầng thuật toán → hệ thống mạng/triển khai → luyện phản biện. Mỗi dòng ghi rõ file giải thích cái gì và những điểm phản biện cốt lõi cần thuộc.

> Quy ước: 📄 = tài liệu tổng quan/ôn tập · 🔬 = giải thích code chi tiết (loạt `ChiTiet_*`) · 📝 = ghi chú bổ trợ.

---

## Chặng 0 — Tổng quan (đọc trước tiên)

- 📄 **[01_Tom_tat_DATN.md](01_Tom_tat_DATN.md)** — Tóm tắt toàn đồ án: bài toán MAPF, ba tầng thuật toán, đóng góp. Đọc để nắm bức tranh lớn trước khi vào code.
- 📄 **[02_Plan_on_tap_phan_bien.md](02_Plan_on_tap_phan_bien.md)** — Lộ trình ôn theo chặng + checklist demo + danh sách câu hỏi phản biện thường gặp. **Đây là tài liệu "trung tâm"** — các file 🔬 bên dưới là phần đào sâu cho từng chặng của nó.

---

## Chặng 1 — Nền móng: Bản đồ & Tọa độ

- 🔬 **[ChiTiet_MapLoader_QA.md](ChiTiet_MapLoader_QA.md)** — `MapLoader.cs`: đọc file `.map` → lưới ô → tọa độ world; build gạch/tường/collider; các hàm spawn tối ưu.
  - **Phản biện cốt lõi:** hệ tọa độ ô (góc trên-trái, y tăng xuống) ↔ world; vì sao tường có 2 collider; vì sao dùng lưới thay vì tọa độ world thuần.
- 📄 **[04_Giai_thich_MapLoader.md](04_Giai_thich_MapLoader.md)** — Bản giải thích `MapLoader` theo lối liệt kê từng hàm (bổ trợ, tra cứu nhanh theo số dòng).

---

## Chặng 2 — Tầng 1: A* Baseline

- 🔬 **[ChiTiet_GridAStar_QA.md](ChiTiet_GridAStar_QA.md)** — `GridAStarPathfinder.cs` + `GridNavMask.cs`: A* `f = g + h`, binary min-heap, lazy deletion, và toàn bộ tầng **soft-cost / phình vật cản** cho bề rộng xe tăng.
  - **Phản biện cốt lõi:** A* khác Dijkstra ra sao; vì sao heuristic Manhattan; soft-cost khác cấm cứng; vì sao xe không đâm tường dù chạy trên ô-điểm.
- 🔬 **[ChiTiet_GridEnemyAgent_QA.md](ChiTiet_GridEnemyAgent_QA.md)** — `GridEnemyAgent.cs`: bộ điều khiển xe địch mode A*. Cây quyết định (bắn > thoát kẹt > phá vật cản > đi đường), logic xoay-rồi-tiến, phát hiện kẹt, thang cứu hộ.
  - **Phản biện cốt lõi:** xe có hướng thân sao đi mượt; hai xe cùng nhắm một ô; ba kiểu cảm biến kẹt.
- 📝 **[note_variables_GridEnemyAgent.md](note_variables_GridEnemyAgent.md)** — Ghi chú các biến/tham số của `GridEnemyAgent` (tra cứu khi chỉnh Inspector).
- 🔬 **[ChiTiet_Replan_Recovery_QA.md](ChiTiet_Replan_Recovery_QA.md)** — Đào sâu riêng cơ chế **tính lại đường (replan) + cứu hộ kẹt** — phần công phu nhất của tầng điều khiển.

---

## Chặng 3 — Tầng 2: PIBT C# (Dẫn hướng luồng)

- 🔬 **[ChiTiet_PIBTPlanner_QA.md](ChiTiet_PIBTPlanner_QA.md)** — `PIBTPlanner.cs`: bản đồ luồng chung `_flow`, flow-aware A* (op_flow + vertex_flow), Frank-Wolfe, heuristic BFS ngược.
  - ⚠️ **Điểm phản biện QUAN TRỌNG NHẤT (đọc Mục 0 của file):** file này **chỉ port tầng flow-guidance**, KHÔNG phải `causalPIBT`. Phải nói "port lõi dẫn hướng + thích nghi tầng va chạm cho real-time", **tuyệt đối không nói "port 100%"**.
- 🔬 **[ChiTiet_GridEnemyAgentPIBT_QA.md](ChiTiet_GridEnemyAgentPIBT_QA.md)** — `GridEnemyAgentPIBT.cs`: bản song sinh của agent A*, khác đúng 5 điểm (dùng PIBT, đăng ký agentId, bỏ bậc cứu hộ ExpandedMask...).
  - **Phản biện cốt lõi:** vì sao gần như copy (cô lập biến số để so sánh công bằng); gizmo cyan=PIBT vs đỏ=A*.

---

## Chặng 4 — Tầng 3: PIBT C++/TCP (chạy code gốc đoạt giải)

- 🔬 **[ChiTiet_PIBT_TCP_QA.md](ChiTiet_PIBT_TCP_QA.md)** — Cặp `PIBTTcpClient.cs` (đường dây TCP/JSON) + `GridEnemyAgentPIBT_TCP.cs` (tài xế câm nhận lệnh từ server C++).
  - **Phản biện cốt lõi:** vì sao cần bản TCP (tái dùng đúng code C++ đoạt giải, mở rộng cloud); validate khớp kích thước map chống bug tọa độ; ⚠️ cú hích tham lam là **NON-PIBT** đã log tách biệt.

---

## Chặng 5 — Hệ thống Mạng & Triển khai

- 🔬 **[ChiTiet_Multiplayer_GCP_QA.md](ChiTiet_Multiplayer_GCP_QA.md)** — Tầng multiplayer (NGO, server-authoritative, 3 chế độ kết nối LAN/Relay/Internet) + triển khai dedicated server lên GCP (Terraform, registry, CI/CD).
  - **Phản biện cốt lõi:** multiplayer chạy thật (khung trung thực về Internet); ai chạy AI khi nhiều người; client prediction che độ trễ; bug WebSocket queue đã sửa; vì sao Terraform/CI-CD.

---

## Tài liệu tham chiếu khác

- 📄 **[03_Giai_thich_code_va_migration_PIBT.md](03_Giai_thich_code_va_migration_PIBT.md)** — Đối chiếu chi tiết C++ ↔ C# của quá trình port PIBT (bảng tương ứng dòng-theo-dòng). Đọc kèm `ChiTiet_PIBTPlanner_QA.md`.
- 📄 **[KienTrucXeTang.md](KienTrucXeTang.md)** — Kiến trúc điều khiển xe tăng (`TankController`/`TankMover`/`Turret`/`AimTurret`) mà cả AI lẫn người chơi đều dùng chung.

---

## Ba điểm trung thực xuyên suốt (thuộc lòng — dễ mất điểm nhất)

1. **PIBT C# = chỉ port tầng flow-guidance**, không phải causalPIBT; va chạm thay bằng chờ-và-replan mức agent. → `ChiTiet_PIBTPlanner_QA.md` Mục 0.
2. **Cú hích tham lam trong PIBT-TCP là NON-PIBT**, chỉ bật khi kẹt >4s, đã log tách biệt để không lẫn vào số liệu PIBT. → `ChiTiet_PIBT_TCP_QA.md` Mục B4.
3. **Internet multiplayer đã dựng nhưng ổn định tùy mạng** — LAN thì chắc chắn ổn. Không khẳng định quá cái chưa test kỹ. → `ChiTiet_Multiplayer_GCP_QA.md` Mục 2.

---

## Thứ tự đọc gợi ý (đường ngắn nhất để hiểu toàn hệ thống)

```
01 → 02  (tổng quan + lộ trình)
   → ChiTiet_MapLoader           (nền móng)
   → ChiTiet_GridAStar           (tầng 1: thuật toán)
   → ChiTiet_GridEnemyAgent      (tầng 1: điều khiển)  [+ Replan_Recovery nếu có thời gian]
   → ChiTiet_PIBTPlanner         (tầng 2: bộ não)      ⚠️ nhớ điểm trung thực
   → ChiTiet_GridEnemyAgentPIBT  (tầng 2: điều khiển)
   → ChiTiet_PIBT_TCP            (tầng 3: server C++)  ⚠️ nhớ NON-PIBT fallback
   → ChiTiet_Multiplayer_GCP     (mạng + triển khai)
```
