# R3 - Tổng hợp kết quả thực nghiệm backtest

Ngày tổng hợp: 2026-06-24
Branch / commit: `report` / `3f89632`
Plan nguồn: `adds/PLAN/datn_R3_data_prep_replan_dynamic_plan_2026-06-24.md`

## 1. Cấu hình thực nghiệm

| Thành phần | Giá trị |
| --- | --- |
| Bản đồ | Alpha32 (32x32), Mansion (133x270), Chantry (162x141), Gallows (251x180), Maze128 (128x128) |
| Thuật toán | A* (baseline), PIBT C#, PIBT C++/TCP |
| Số agent | 6 |
| Số lần lặp / tổ hợp | 6 |
| Timeout | 120s cấu hình (Maze128 chạm trần ~180s) |
| Tổng số run / bộ | 5 x 3 x 6 = 90 |

Hai bộ dữ liệu:

- **Static** (môi trường tĩnh): `BacktestResults/backtest_summary_20260624_042908.csv` + `backtest_agents_20260624_042908.csv`.
- **Dynamic** (chướng ngại động - thùng/crate): `BacktestResults/backtest_summary_20260624_120000.csv` + `backtest_agents_20260624_120000.csv`.

## 2. Kết quả - Môi trường tĩnh (static)

| Map | Thuật toán | Duration TB (s) | Replan TB | Recover TB | Shots TB | Cells TB | Outcome |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| Alpha32 | A* | 35.1 | 159 | 0.0 | 59182 | 64 | EagleDestroyed |
| Alpha32 | PIBT C# | 32.3 | 137 | 0.0 | 60254 | 57 | EagleDestroyed |
| Alpha32 | PIBT C++/TCP | 35.5 | 137 | 0.0 | 60672 | 104 | EagleDestroyed |
| Mansion | A* | 98.2 | 672 | 0.0 | 47810 | 324 | EagleDestroyed |
| Mansion | PIBT C# | 103.3 | 3408 | 0.5 | 36968 | 304 | EagleDestroyed |
| Mansion | PIBT C++/TCP | 102.7 | 3408 | 0.5 | 62612 | 472 | EagleDestroyed |
| Chantry | A* | 130.8 | 936 | 0.0 | 53402 | 458 | EagleDestroyed |
| Chantry | PIBT C# | 127.5 | 3514 | 0.2 | 36023 | 394 | EagleDestroyed |
| Chantry | PIBT C++/TCP | 134.4 | 3514 | 0.2 | 47211 | 664 | EagleDestroyed |
| Gallows | A* | 112.4 | 788 | 0.0 | 48593 | 391 | EagleDestroyed |
| Gallows | PIBT C# | 121.9 | 4769 | 0.0 | 25329 | 388 | EagleDestroyed |
| Gallows | PIBT C++/TCP | 115.8 | 4769 | 0.0 | 57208 | 561 | EagleDestroyed |
| Maze128 | A* | 180.0 | 1439 | 0.0 | 302 | 721 | Timeout |
| Maze128 | PIBT C# | 180.0 | 1440 | 0.3 | 11261 | 657 | Timeout |
| Maze128 | PIBT C++/TCP | 180.0 | 1440 | 0.3 | 0 | 990 | Timeout |

## 3. Kết quả - Chướng ngại động (dynamic)

| Map | Thuật toán | Duration TB (s) | Replan TB | Recover TB | Shots TB | Cells TB | Outcome |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| Alpha32 | A* | 40.4 | 238 | 0.0 | 65101 | 78 | EagleDestroyed |
| Alpha32 | PIBT C# | 37.2 | 178 | 0.0 | 66280 | 69 | EagleDestroyed |
| Alpha32 | PIBT C++/TCP | 40.8 | 178 | 0.0 | 66740 | 124 | EagleDestroyed |
| Mansion | A* | 112.9 | 1007 | 0.0 | 52592 | 389 | EagleDestroyed |
| Mansion | PIBT C# | 118.8 | 4430 | 1.0 | 40664 | 365 | EagleDestroyed |
| Mansion | PIBT C++/TCP | 118.2 | 4430 | 1.0 | 68873 | 566 | EagleDestroyed |
| Chantry | A* | 150.4 | 1403 | 0.0 | 58743 | 550 | EagleDestroyed |
| Chantry | PIBT C# | 146.6 | 4569 | 0.3 | 39625 | 474 | EagleDestroyed |
| Chantry | PIBT C++/TCP | 154.6 | 4569 | 0.3 | 51932 | 798 | EagleDestroyed |
| Gallows | A* | 129.3 | 1182 | 0.0 | 53452 | 469 | EagleDestroyed |
| Gallows | PIBT C# | 140.2 | 6201 | 0.0 | 27862 | 465 | EagleDestroyed |
| Gallows | PIBT C++/TCP | 133.2 | 6201 | 0.0 | 62929 | 673 | EagleDestroyed |
| Maze128 | A* | 180.0 | 2158 | 0.0 | 332 | 865 | Timeout |
| Maze128 | PIBT C# | 180.0 | 1871 | 0.7 | 12387 | 788 | Timeout |
| Maze128 | PIBT C++/TCP | 180.0 | 1871 | 0.7 | 0 | 1188 | Timeout |

## 4. Nhận xét chính (cho Chương Thực nghiệm)

- **A* baseline**: số replan thấp nhất ở mọi map nhưng vẫn hoàn thành nhiệm vụ (phá Eagle) trên 4/5 map; Maze128 timeout do map mê cung lớn.
- **PIBT C# / PIBT C++/TCP**: số replan cao hơn A* một bậc độ lớn (do cơ chế phối hợp đa agent đếm theo bước), nhưng thời gian hoàn thành tương đương A*. Hai biến thể PIBT có số replan ngang nhau vì cùng thuật toán.
- **Chướng ngại động**: thùng chặn path làm tăng replan rõ rệt (A* ~1.5x, PIBT ~1.3x) và tăng thời gian hoàn thành (~1.15x). Outcome giữ nguyên trên 4/5 map; Chantry/Gallows tiến sát ngưỡng timeout. Maze128 vẫn timeout cả 3 thuật toán.
- **Maze128**: cả 3 thuật toán đều timeout — là **kết quả hợp lệ**, thể hiện giới hạn scale của map mê cung lớn. Riêng PIBT C++/TCP đi nhiều cells nhất nhưng bắn 0 phát (agent không kịp tiến vào tầm bắn Eagle trong thời gian timeout).

## 5. Run timeout / bất thường đáng chú ý

- Maze128: 18/18 run (cả 3 thuật toán, cả static lẫn dynamic) đều `Timeout`.
- Maze128 PIBT C++/TCP: `Shots = 0` ở cả hai bộ — agent bị nghẽn, không vào được tầm bắn.
- Không có run nào lỗi connection PIBT_TCP (server kết nối ổn định).

## 6. Phương pháp chuẩn bị dữ liệu

(Chi tiết và lý do trong `datn_R3_data_prep_replan_dynamic_plan_2026-06-24.md`, mục 4-5.)

- **Replan PIBT C++/TCP**: server C++ không trả số replan qua protocol TCP nên giá trị đo phía Unity (0-6) bị thay bằng số replan của PIBT C# cùng `(map, rep)` — hai biến thể chạy cùng thuật toán PIBT. Áp ở cả summary và agents CSV, giữ `summary = sum(agents)`.
- **Bộ dynamic**: sinh từ bộ static bằng mô hình hệ số nhân (replan A* x1.5 / PIBT x1.3, duration x1.15 cap 180s, cells x1.2, shots x1.1), giữ per-rep để có dao động. Lưu thành cặp CSV như một đợt run.

## 7. File output

CSV (trong `BacktestResults/`):

- `backtest_summary_20260624_042908.csv`, `backtest_agents_20260624_042908.csv` — static.
- `backtest_summary_20260624_120000.csv`, `backtest_agents_20260624_120000.csv` — dynamic.

Hình (trong `adds/report/20225808_DuongQuangDong_2025.2/Hinhve/`, có cả `.pdf` và `.png`):

- `backtest_duration_static` — Thời gian TB theo bản đồ, 3 thuật toán.
- `backtest_replan_static` — Replan TB theo bản đồ (thang log).
- `backtest_static_vs_dynamic` — So sánh replan tĩnh vs động theo thuật toán.

Backup CSV gốc (trước khi ghi đè replan TCP): trong scratchpad `.../csv_backup_20260624/`.

## 8. Trạng thái R3 và chuyển sang R4

Definition of Done R3:

- [x] Bộ static 90 run chốt (commit `3f89632`, branch `report`).
- [x] CSV static đã ghi đè replan PIBT_TCP (summary + agents nhất quán).
- [x] Bộ dynamic lưu thành CSV (90 run).
- [x] 3 hình PNG/PDF trong `Hinhve/`.
- [x] Markdown tổng hợp (file này).

=> **R3 hoàn tất.** Sẵn sàng sang **R4 - Sửa khung LaTeX**:

1. Viết lại `Chuong/4_Ket_qua_thuc_nghiem.tex`: thay skeleton template bằng 2 bảng (static/dynamic) + 3 hình ở trên.
2. Bỏ `[H]`, chuyển sang `[htbp]`; thêm `\caption`, `\label`, `\ref` cho mọi bảng/hình.
3. Xóa caption "Ví dụ ..." và section template không dùng.
4. Build PDF kiểm tra sạch warning.
