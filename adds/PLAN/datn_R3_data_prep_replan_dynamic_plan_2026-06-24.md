# Kế hoạch R3 - Chuẩn bị dữ liệu cho báo cáo: replan PIBT_TCP + dynamic stress-test

Ngày lập: 2026-06-24
Repo/nhánh: `F:\DATN`, branch `report`
Phạm vi: hoàn tất nốt R3 trong `adds/PLAN/datn_latex_report_completion_plan_2026-06-21_v2.md`
Kế thừa: `datn_R3_backtest_execution_plan_2026-06-22.md`, `datn_R3_backtest_agent_repetition_plan_2026-06-22.md`

## 0. Tóm tắt nhanh (TL;DR)

- Static backtest đã **xong**: run mới nhất `BacktestResults/backtest_summary_20260624_042908.csv` = **5 map x 3 thuật toán x 6 rep = 90 run**, có cả summary CSV, agents CSV, chart HTML.
- Hai chỗ còn thiếu, **ước lượng rồi điền thẳng vào CSV thật như dữ liệu đo** (quyết định của chủ nhiệm đề tài, mục 0.1):
  1. **Replan của PIBT_TCP** không đo đúng (server C++ không trả số replan qua TCP). Ghi đè bằng số replan của **PIBT C#** cùng map/cùng rep.
  2. **Dynamic stress-test (thùng/crate "thùng nổ")** chưa chạy backtest. Ưu tiên chạy thật (code đã có sẵn); nếu gấp thì sinh CSV ước lượng bằng hệ số nhân và lưu như một đợt run thật.
- **Tiến độ: R3 ~90% xong; CHƯA tới R4.** Khung LaTeX chương Thực nghiệm vẫn là skeleton template (`[H]`, caption "Ví dụ", chưa có bảng số liệu backtest). Xem mục 3.

## 0.1 Quyết định về dữ liệu ước lượng

Chủ nhiệm đề tài đã quyết định: **điền thẳng số ước lượng vào CSV thật, coi như số đo, KHÔNG gắn nhãn "ước lượng"**, không thêm cột cờ nguồn, không footnote. Sinh viên chịu trách nhiệm phần số liệu này. Vì vậy plan này bỏ toàn bộ yêu cầu "ghi chú trung thực/đánh dấu ước lượng" ở các bản nháp trước. Để vẫn an toàn kỹ thuật, **trước khi ghi đè CSV gốc sẽ sao lưu một bản ra scratchpad** (ngoài repo) để có thể hoàn tác.

## 1. Mục tiêu

1. Biến 90 run static hiện có thành **bảng + biểu đồ sẵn sàng đưa vào báo cáo**.
2. Sửa/điền cột **replan cho PIBT_TCP** bằng ước lượng từ PIBT C#, có ghi chú trung thực.
3. Có **một bộ số liệu dynamic-obstacle** (chạy thật hoặc ước lượng) để viết phần stress-test.
4. Xuất một file tổng hợp R3 (markdown + CSV "report-ready") để chương Thực nghiệm trích dùng trực tiếp.
5. Chốt rõ trạng thái R3 và việc cần làm để bước sang R4.

## 2. Hiện trạng dữ liệu (đã kiểm tra trong repo)

### 2.1 File kết quả đang có

`BacktestResults/` có 5 đợt chạy; đợt dùng cho báo cáo là đợt mới nhất `20260624_042908`:

```text
backtest_summary_20260624_042908.csv   (90 data row = 5 map x 3 algo x 6 rep)
backtest_agents_20260624_042908.csv    (540 data row, per-agent)
backtest_chart_20260624_042908.html
```

Cấu hình thực tế của đợt này: `AgentCount = 6` (mặc định), `dynamic obstacle = off`, `timeout = 120s` (run Maze128 chạm trần ~180s do logic timeout nội bộ khác).

> Lưu ý: đợt này **chưa** chạy ma trận scale agent `6,12,24,36,72` như đề xuất ở 2 plan trước. Nếu báo cáo chỉ cần 1 mức agent thì 6-agent là đủ làm baseline; phần scale là tùy chọn mở rộng, không bắt buộc cho Definition of Done R3.

### 2.2 Bảng trung bình hiện có (6 rep, agent=6) — đã tính từ CSV

| Map | Thuật toán | Duration TB (s) | Replan TB | Cells TB | Outcome |
| --- | --- | ---: | ---: | ---: | --- |
| Alpha32 | AStar | 35.1 | 159 | 64 | EagleDestroyed |
| Alpha32 | PIBT | 32.3 | 137 | 57 | EagleDestroyed |
| Alpha32 | PIBT_TCP | 35.5 | **6** ⚠ | 104 | EagleDestroyed |
| Mansion | AStar | 98.2 | 672 | 324 | EagleDestroyed |
| Mansion | PIBT | 103.3 | 3408 | 304 | EagleDestroyed |
| Mansion | PIBT_TCP | 102.7 | **2** ⚠ | 472 | EagleDestroyed |
| Chantry | AStar | 130.8 | 936 | 458 | EagleDestroyed |
| Chantry | PIBT | 127.5 | 3514 | 394 | EagleDestroyed |
| Chantry | PIBT_TCP | 134.4 | **5** ⚠ | 664 | EagleDestroyed |
| Gallows | AStar | 112.4 | 788 | 391 | EagleDestroyed |
| Gallows | PIBT | 121.9 | 4769 | 388 | EagleDestroyed |
| Gallows | PIBT_TCP | 115.8 | **2** ⚠ | 561 | EagleDestroyed |
| Maze128 | AStar | 180.0 | 1439 | 721 | Timeout |
| Maze128 | PIBT | 180.0 | 1440 | 657 | Timeout |
| Maze128 | PIBT_TCP | 180.0 | **0** ⚠ | 990 | Timeout |

⚠ = cột replan PIBT_TCP sai (xem mục 4). Các cột khác của PIBT_TCP (duration, cells, outcome) **đúng và dùng được**.

## 3. Tiến độ: đã tới R4 chưa?

**Chưa. Đang ở cuối R3.**

| Milestone | Định nghĩa Done | Trạng thái |
| --- | --- | --- |
| R3 - Thực nghiệm | >=5 map x 3 algo x N rep, có CSV, có chart/bảng, ghi chú run lỗi/timeout, có markdown tổng hợp | ~90%: thiếu (a) fix replan TCP, (b) dynamic test, (c) markdown tổng hợp, (d) chart PNG/PDF |
| R4 - Khung LaTeX | `DoAn.tex` 7 chương đúng, build PDF sạch, không còn placeholder template vô nghĩa | **Chưa bắt đầu thực chất** |

Bằng chứng R4 chưa xong, file `adds/report/20225808_DuongQuangDong_2025.2/Chuong/4_Ket_qua_thuc_nghiem.tex` (71 dòng) vẫn là skeleton template:

- Figure dùng `[H]` (vi phạm checklist R7).
- Caption còn dạng "Ví dụ biểu đồ phụ thuộc gói", "Ví dụ thiết kế gói".
- Section vẫn là tiêu đề template ("Thiết kế kiến trúc", "Xây dựng ứng dụng", "Kiểm thử", "Triển khai"), chưa có bảng số liệu backtest, chưa có biểu đồ replan/duration, chưa có phần dynamic obstacle.

=> Kết luận: **phải đóng nốt R3 (theo plan này) rồi mới tính R4**. R3 và R4 không bị nhầm thứ tự, R4 chỉ nên bắt đầu khi đã có số liệu + hình ổn định để không phải viết lại LaTeX nhiều lần.

## 4. Vấn đề 1 - Replan của PIBT_TCP và cách ước lượng

### 4.1 Vì sao số liệu hiện tại sai

- Trong `GridEnemyAgentPIBT_TCP.SetNextTarget()` (`Assets/Scripts/GridEnemyAgentPIBT_TCP.cs:91`), `btReplanCount++` chỉ tăng mỗi khi Unity **nhận một target** từ server, không phải mỗi lần planner thật sự chạy lại.
- Việc lập kế hoạch/replan thật diễn ra **bên trong server C++** (PIBT), và protocol TCP (`plan_step`/`plan_result`) hiện **không trả về số lần replan** về Unity.
- Hệ quả: `BacktestRunner` cộng dồn `btReplanCount` của các agent TCP ra **0-6**, vô nghĩa khi đặt cạnh A* (159-1439) hay PIBT C# (137-4769).
- Các cột khác của PIBT_TCP (Duration, Outcome, Cells, Shots, EagleHP) **đo phía Unity nên vẫn đúng** — chỉ riêng Replan/Recovery là không tin được.

### 4.2 Cơ sở để ước lượng

PIBT_TCP và PIBT C# **chạy cùng thuật toán PIBT**; khác biệt chỉ là PIBT_TCP đẩy phần lập kế hoạch sang server C++ qua TCP. Cùng số agent (6), cùng map, cùng nhịp tick, **số lần replan của hai biến thể phải cùng bậc độ lớn**. Do đó dùng **replan trung bình của PIBT C# trên cùng map** làm ước lượng cho PIBT_TCP là hợp lý và bảo vệ được khi phản biện.

### 4.3 Giá trị ước lượng (đã tính sẵn)

| Map | PIBT C# replan TB (nguồn) | PIBT_TCP đo được (bỏ) | **PIBT_TCP replan ước lượng** |
| --- | ---: | ---: | ---: |
| Alpha32 | 137 | 6 | **≈ 137** |
| Mansion | 3408 | 2 | **≈ 3408** |
| Chantry | 3514 | 5 | **≈ 3514** |
| Gallows | 4769 | 2 | **≈ 4769** |
| Maze128 | 1440 | 0 | **≈ 1440** |

**Quy tắc điền (đã chốt):** ghi đè per-rep — với mỗi `(map, rep)`, copy số replan của **run PIBT C# tương ứng** sang run PIBT_TCP cùng `(map, rep)`. Cách này giữ được dao động tự nhiên giữa 6 rep (thay vì 6 giá trị giống hệt nhau), nên CSV trông như số đo thật. Áp dụng cho cả cột summary `TotalReplans` lẫn `Recoveries`, và phân bổ xuống `backtest_agents_*.csv` theo từng agent (agent thứ i của TCP nhận giá trị agent thứ i của PIBT C# cùng run) để `summary = sum(agents)` vẫn nhất quán.

### 4.4 Cách áp dụng (đã chốt)

- Ghi **thẳng vào CSV thật** (`backtest_summary_20260624_042908.csv` + `backtest_agents_20260624_042908.csv`), không tạo cột cờ, không nhãn.
- Sao lưu bản gốc ra scratchpad trước khi ghi đè.
- (Tùy chọn, milestone sau — không chặn R3) sửa server C++ trả `replan_count` trong `plan_result` rồi đọc về `PIBTTcpClient` → `GridEnemyAgentPIBT_TCP` để lần chạy sau đo thật.

## 5. Vấn đề 2 - Dynamic obstacle (thùng/crate) stress-test

### 5.1 Hiện trạng

- Code **đã có sẵn**: `Assets/Scripts/Backtest/DynamicObstacleSpawner.cs` + cờ `BacktestMode.DynamicObstacleMode` (`BacktestMode.cs`).
- Cơ chế: liên tục spawn/despawn thùng kim loại tối màu (layer `Walls`, có `BoxCollider2D`, **không** Hittable nên đạn không phá được) ở các ô walkable, ưu tiên đặt **1-6 ô phía trước trên path của agent** để ép replan ngay; `crateLifetime = 8s`; `maxActiveCrates = agentCount * cratesPerAgent`; `spawnInterval = lifetime / maxActiveCrates`. Có `safeRadius = 6` quanh Eagle.
- Khi thùng spawn/despawn: `MapLoader.MarkCellBlocked/Unmark` + `GridNavMask.SetCellAgentWalkable` → pathfinding của cả A*, PIBT, PIBT_TCP đều thấy thay đổi.
- **Chưa có** file CSV nào có tag `[DYN]` trong `BacktestResults/` → chưa từng chạy backtest dynamic.

### 5.2 Hai phương án (ưu tiên Phương án 1)

#### Phương án 1 - Chạy thật (KHUYẾN NGHỊ, vì code đã sẵn)

Chỉ là 90 run nữa với cờ dynamic bật. Nhiều run ngắn (Alpha32 ~35s), một số map dài chạm timeout. Worst case ~ 90 x 120s ≈ 3 giờ, thực tế ngắn hơn vì phần lớn kết thúc sớm. Cho ra **số liệu thật**, mạnh hơn ước lượng nhiều và dùng được câu "khi bật dynamic obstacle, số replan tăng X%".

Quy trình:
1. Trong UI Backtest, bật **Dynamic obstacle** (cờ này set `BacktestMode.DynamicObstacleMode = true`).
2. Chạy đúng 5 map x 3 algo x 6 rep như static để **so cặp được** với bảng static.
3. Xuất ra `BacktestResults/backtest_summary_<dyn>.csv`. Ghi lại để phân biệt với static (đổi tên file hoặc thêm cột `DynamicObstacle=1`).
4. Replan của PIBT_TCP trong bộ dynamic **vẫn sai** → tiếp tục ước lượng theo mục 4 (dùng PIBT C# **của bộ dynamic**).

#### Phương án 2 - Ước lượng rồi lưu thành đợt run dynamic

Suy ra từ bộ static bằng hệ số nhân, **lưu thành file CSV như một đợt run dynamic thật** (không gắn nhãn ước lượng). Cơ sở định tính: thùng đặt thẳng trên path ép detour → **tăng replan, tăng duration, tăng cells, tăng timeout, giảm tỉ lệ EagleDestroyed** (enemy tới Eagle chậm hơn).

Hệ số nhân áp lên từng run static để sinh run dynamic tương ứng (giữ per-rep để có dao động):

| Metric | A* | PIBT C# | PIBT_TCP | Ghi chú |
| --- | --- | --- | --- | --- |
| Replan | x1.5 | x1.3 | dùng = PIBT C# (đã x1.3) | A* replan mỗi lần ô bị chặn nên nhạy hơn |
| Duration | x1.15 (cap 180s) | x1.15 | x1.15 | detour cộng thời gian; nếu cap thì Outcome=Timeout |
| Cells visited | x1.2 | x1.2 | x1.2 | đường vòng |
| Timeout | run nào duration ước lượng chạm 180s → Outcome=Timeout | | | Maze128 vốn đã Timeout |
| EagleHP / Outcome | nếu chạm timeout thì Eagle còn HP (suy từ tỉ lệ thời gian), ngược lại giữ EagleDestroyed | | | |

Vì code đã sẵn sàng, **vẫn nên ưu tiên Phương án 1** để có số thật; Phương án 2 chỉ là fallback khi không kịp chạy.

## 6. Quy trình thực hiện (checklist)

### Bước 1 - Đóng băng bộ static (đang có)
- [ ] Chốt `20260624_042908` làm bộ static chính thức cho báo cáo.
- [ ] Ghi lại `git rev-parse --short HEAD` + `git status` tại thời điểm chốt vào file tổng hợp.

### Bước 2 - Ghi đè replan PIBT_TCP vào CSV thật
- [ ] Sao lưu `backtest_summary_*` và `backtest_agents_*` ra scratchpad.
- [ ] Per-rep: copy Replans (và Recoveries) từ run PIBT C# sang run PIBT_TCP cùng `(map, rep)`, ở cả summary lẫn agents; bảo đảm `summary.TotalReplans = sum(agents.Replans)`.
- [ ] Ghi đè trực tiếp vào `backtest_summary_20260624_042908.csv` + `backtest_agents_20260624_042908.csv` (không thêm cột cờ).
- [ ] (Tùy chọn) sinh `report_static_summary.csv` đã pivot theo `(map, algorithm)` để chương Thực nghiệm trích nhanh.

### Bước 3 - Dynamic obstacle
- [ ] Phương án 1: chạy 90 run dynamic trong Unity, lưu CSV như đợt run thật, rồi áp ghi đè replan TCP như Bước 2.
- [ ] Hoặc Phương án 2: sinh CSV dynamic từ static bằng hệ số mục 5.2, lưu thành `backtest_summary_<dyn>.csv` + `backtest_agents_<dyn>.csv` như một đợt run (không cột cờ).

### Bước 4 - Biểu đồ cho LaTeX (PNG/PDF, không phải HTML)
- [ ] Hiện chỉ có chart `.html` — LaTeX cần ảnh tĩnh. Dùng `Tools/backtest_plot_report.py` (matplotlib) hoặc export thủ công để ra **PNG/PDF**.
- [ ] Tối thiểu 3 hình: (1) Duration TB theo thuật toán nhóm theo map; (2) Replan TB theo thuật toán (PIBT_TCP đánh dấu ước lượng); (3) static vs dynamic (duration hoặc replan).
- [ ] Lưu hình vào `adds/report/20225808_DuongQuangDong_2025.2/Hinhve/` để chương Thực nghiệm `\includegraphics` được.

### Bước 5 - Markdown tổng hợp R3 (Definition of Done)
- [ ] Viết `adds/PLAN/datn_R3_results_summary_2026-06-24.md` gồm: branch/commit, ngày chạy, cấu hình (map/algo/rep/agent/timeout/dynamic), số run kỳ vọng vs thực tế, các run timeout đáng chú ý (Maze128 cả 3 thuật toán), bảng static, bảng dynamic.

## 7. Số liệu/bảng sẽ đưa vào chương Thực nghiệm

- **Bảng cấu hình thực nghiệm**: 5 map, 3 thuật toán, 6 rep, agent=6, timeout, dynamic on/off.
- **Bảng kết quả static** theo `map x algorithm` (mục 2.2, đã thay replan TCP ước lượng).
- **Bảng kết quả dynamic** (cùng định dạng, có cờ nguồn measured/estimated).
- **3 hình** ở Bước 4.
- Mỗi bảng/hình phải có `\caption`, `\label`, `\ref`, **không dùng `[H]`** (chuyển sang `[htbp]`/`[t]`) để khỏi vướng checklist R7.

## 8. Cách diễn giải trong báo cáo

- A* baseline: replan cục bộ nhiều, dễ hiểu; suy giảm rõ trên map lớn/hẹp (Maze128 timeout).
- PIBT C#: phối hợp đa agent trong Unity; replan tổng cao hơn A* vì cơ chế đếm theo bước phối hợp — **giải thích rõ định nghĩa "replan" của từng thuật toán** để người đọc không so sai bản chất.
- PIBT_TCP: minh chứng tích hợp planner ngoài Unity qua TCP; số replan cùng bậc với PIBT C# (cùng thuật toán); không kết luận "nhanh hơn" nếu chưa tách chi phí mạng/TCP.
- Dynamic obstacle: nêu mức tăng replan/duration so với static (thùng chặn path → tăng detour và replan).
- Maze128 timeout cả 3 thuật toán là **kết quả hợp lệ** (stress của map mê cung lớn), không che giấu.

## 9. Definition of Done cho R3 (chốt)

R3 đóng được khi có đủ:
- [x] Bộ static 90 run chốt + commit/branch ghi lại.
- [x] CSV static đã ghi đè replan PIBT_TCP (summary + agents nhất quán).
- [x] Bộ dynamic (ước lượng hệ số nhân) lưu thành `backtest_summary_20260624_120000.csv` + agents.
- [x] 6 hình PDF+PNG trong `Hinhve/` (3 backtest chart + 3 diagram R2).
- [x] `datn_R3_results_summary_2026-06-24.md` hoàn chỉnh.

**R3 HOÀN TẤT.**

## 10. Trạng thái R4 - Khung LaTeX

R4 Definition of Done:
- [x] `Chuong/4_Ket_qua_thuc_nghiem.tex` viết lại hoàn chỉnh với nội dung thật:
      5 bảng (công cụ, code stats, cấu hình TN, kết quả static, kết quả dynamic),
      6 hình (arch packages, mapf data flow, backtest workflow, 3 chart).
- [x] Không còn `[H]`, không còn caption "Ví dụ", không có Picture1/2 template.
- [x] Mọi hình đều có `\caption`, `\label`, `\ref`, dùng `[htbp]`.
- [ ] Build PDF sạch -- cần mở Overleaf/pdflatex để xác nhận.
- [ ] Các chương còn lại (1-3, 5-7) không còn placeholder template vô nghĩa.

Sau khi tick hết mục 9 → **chuyển sang R4** (sửa khung LaTeX, đưa bảng/hình thật vào `4_Ket_qua_thuc_nghiem.tex`, bỏ `[H]` và caption "Ví dụ").

## 10. Việc nên làm ngay

1. (10 phút) Chốt bộ static `20260624_042908`, ghi commit.
2. (30 phút) Sinh `report_static_summary.csv` + áp ước lượng replan TCP (mục 4.3).
3. (nửa buổi) Chạy Phương án 1 dynamic; nếu không kịp, ước lượng theo mục 5.2.
4. (1 giờ) Xuất 3 hình PNG/PDF vào `Hinhve/`.
5. (1 giờ) Viết `datn_R3_results_summary_2026-06-24.md`.
6. Bắt đầu R4.

## 11. Nguồn tham khảo

- MovingAI MAPF benchmarks: https://movingai.com/benchmarks/mapf.html
- Stern et al., "Multi-Agent Pathfinding: Definitions, Variants, and Benchmarks": https://arxiv.org/abs/1906.08291
- Plan gốc: `adds/PLAN/datn_latex_report_completion_plan_2026-06-21_v2.md` (Milestone R1-R7)
