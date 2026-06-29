# Kế hoạch sửa Chương 2: khoảng trắng, ảnh bị cắt, diagram cho từng use case — 2026-06-25

Phạm vi: **chỉ Chương 2** (`Chuong/2_Khao_sat.tex`) + Phụ lục B (`Chuong/Phu_luc_B.tex`).
Đối chiếu: `datn-report-rules-agent.md`, `quy-dinh-viet-quyen-DATN.md`.
Công cụ render đã có: `plantuml.jar` (Java 11, Smetana), `pdftoppm` (MiKTeX), Python+PIL.

## 1. Kết quả rà soát rule (Chương 2 hiện tại)

| Rule | Nội dung | Trạng thái | Ghi chú |
|---|---|---|---|
| STR-03 | Soạn bằng LaTeX | ✅ PASS | |
| FMT-02 | Không dùng `[H]` ép float | ✅ PASS | Đã dùng `[htbp]` toàn bộ |
| FMT-03 | Dùng `\ref`, không "hình trên/dưới" | ✅ PASS | |
| TECH-04 | Không chèn logo | ✅ PASS | |
| INT-03 | Trích dẫn đầy đủ | ✅ PASS | Phần khảo sát có cite |
| **§6 quy-dinh (khoảng trắng)** | Tránh trang trống do float | ❌ **FAIL** | Gap lớn giữa Hình 2.2 và Bảng 2.3 |
| **FMT-04** | Ảnh có chữ phải đọc rõ, **đầy đủ** | ❌ **FAIL** | `uc_groupB` **bị cắt** mất viền + nhãn "Chơi nhiều người" |
| FMT-01 | PNG độ phân giải lớn / PDF vector | ⚠️ WARN | Đang để dpi 170; nên ≥200–300 |

Ba việc cần xử lý: (I) khoảng trắng, (II) ảnh group B bị cắt, (III) thêm diagram cho từng sub use case.

---

## 2. Vấn đề I — Khoảng trắng giữa hình và bảng

**Nguyên nhân:** Chương 2 có ~5 hình + 14 bảng, tất cả đều là *float* (`figure`/`table [htbp]`).
Khi một hình float lên đầu trang còn bảng kế tiếp quá lớn không vừa, LaTeX đẩy bảng sang
trang sau, để lại khoảng trắng (đúng hiện tượng ở Hình 2.2 → Bảng 2.3). Đây là "float
starvation", không phải lỗi nội dung ảnh.

**Hướng xử lý (khuyến nghị):**
1. **Chuyển 16 bảng đặc tả use case sang dạng in-flow, không float** bằng `longtable`
   (thêm `\usepackage{longtable}` vào `DoAn.tex`). `longtable` nằm đúng vị trí soạn, tự
   ngắt trang khi cần, **không trôi** → triệt tiêu gap. Vẫn có `\caption` + vào Danh mục bảng.
2. **Ghép mỗi diagram-từng-UC ngay trên bảng đặc tả của UC đó** (xem mục 4) thành một khối
   liền mạch diagram + bảng, đọc theo trình tự, không tạo orphan-float.
3. Các hình mức mục (overview, và group nếu giữ) để `figure[htbp]`, cỡ vừa phải, kèm 1–2 câu
   dẫn để lấp chỗ (đúng tinh thần §6 quy-dinh: "chủ động viết thêm text để lấp khoảng trống").
4. Tuyệt đối **không** quay lại `[H]` (vi phạm FMT-02).

> Lưu ý: hai bảng ở §2.1 (so sánh giải pháp, danh sách bản đồ) nhỏ, giữ nguyên float cũng được.

---

## 3. Vấn đề II — Ảnh `uc_groupB_multiplayer.png` bị cắt

**Nguyên nhân:** lỗi tính khung (bounding box) của Smetana với `rectangle` có nhãn đặt sát
mép trên trong layout rộng (`left to right direction`). PNG hiện 1372×729 đã cắt mất viền
trên + nhãn "Chơi nhiều người". Group A/D không bị vì nhỏ hơn.

**Hướng xử lý — chọn 1 trong các cách (sẽ thử và kiểm tra lại ảnh):**
- (a) **Thêm `title Chơi nhiều người`** cho diagram (PlantUML chừa lề trên cho title) thay vì
  chỉ dựa vào nhãn `rectangle` → hết cắt. *(ưu tiên, đơn giản)*
- (b) **Tách group B thành 2 diagram nhỏ** (LAN: B1,B2 và Internet: B3,B4; chung B5,B6,B7) cho
  bớt rộng — đồng thời hợp với hướng "mỗi UC một ảnh" ở mục 4.
- (c) Đổi sang `top to bottom direction` cho bớt rộng.
- Render lại ở **dpi 200–300**; sau khi render, **PIL kiểm tra**: nếu viền/nhãn chạm mép thì
  PIL tự **thêm đệm trắng** (padding) quanh ảnh để chắc chắn không sát mép.

**Định dạng & độ phân giải (FMT-01/FMT-04):** giữ **PNG** theo ý anh, nhưng nâng `skinparam
dpi 200` (hoặc 300 cho ảnh nhiều chữ). *(Ghi chú: rule FMT-01 ưu tiên PDF vector; nhưng
`plantuml.jar` bản gốc không xuất PDF nếu thiếu Batik/FOP, và khoảng-trắng-PDF mà anh gặp
trước đây là do drawio xuất full-trang — không phải PlantUML. Vì vậy PNG dpi cao là lựa chọn
thực tế, đủ nét, không cắt.)*

---

## 4. Vấn đề III — Diagram riêng cho từng sub use case

Mỗi use case có một diagram nhỏ (cùng style cream như `uc_group`) đặt **ngay trên** bảng đặc
tả của nó. Diagram tối thiểu gồm: tác nhân chính + (tác nhân phụ nếu có) + chính use case đó +
quan hệ `<<include>>`/`<<extend>>` liên quan. Tổng **16 diagram**:

| Nhóm | Diagram cần tạo |
|---|---|
| A (Chơi đơn) | `uc_A1`*(kèm include Chọn bản đồ/thuật toán/skin)*, `uc_A2`, `uc_A3`, `uc_A4` |
| B (Nhiều người) | `uc_B1`, `uc_B2`, `uc_B3`, `uc_B4`, `uc_B5`*(include B6)*, `uc_B6`, `uc_B7` |
| D (Backtest) | `uc_D1`, `uc_D2`, `uc_D3` |
| E (Phụ lục, DevOps) | `uc_E1`, `uc_E2` |

**Mẫu PlantUML cho 1 sub-UC (vd A2):**
```plantuml
@startuml uc_A2
!pragma layout smetana
skinparam dpi 220
skinparam shadowing false
left to right direction
skinparam usecase { BackgroundColor #FDF6E3 BorderColor #B58900 }
actor "Người chơi" as P
actor "Agent AI" as AI
rectangle "A2. Điều khiển xe tăng" {
  usecase "Điều khiển xe tăng" as A2
}
P --> A2
A2 ..> AI
@enduml
```

**Cách render hàng loạt:**
```
java -jar plantuml.jar -charset UTF-8 -tpng -o <Hinhve> plantuml/uc_*.puml
```
Source `.puml` đặt ở `adds/PLAN/R2_DIAGRAMS_V2/plantuml/`; PNG ra `Hinhve/`.

**Bố cục trong .tex (mỗi UC):**
```latex
\subsubsection*{UC-A2 -- Điều khiển xe tăng}
\begin{figure}[htbp]\centering
  \includegraphics[width=0.55\textwidth]{Hinhve/uc_A2.png}
  \caption{Use case UC-A2 -- Điều khiển xe tăng}\label{fig:ucA2}
\end{figure}
% ngay dưới là longtable đặc tả UC-A2 (in-flow, không float)
```

> **Câu hỏi cần chốt (mục 7):** giữ cả 4 diagram group *và* thêm 16 diagram per-UC sẽ rất
> nhiều hình (1 overview + 4 group + 16 = 21). Phương án gọn: **overview + 16 per-UC**, bỏ 4
> group (vì per-UC đã thay thế). Cần anh chọn.

---

## 5. Ảnh hưởng dung lượng (SUB-01: < 30MB)

21 PNG dpi cao có thể vài MB; PDF hiện ~1MB. Vẫn xa ngưỡng 30MB. Sẽ kiểm tra tổng `Hinhve/`
sau khi render; nếu cần thì hạ dpi ảnh nhỏ hoặc nén PNG bằng PIL (`optimize=True`).

---

## 6. Thứ tự thực hiện
1. Thêm `\usepackage{longtable}` vào `DoAn.tex`.
2. Sửa group B (mục 3) + nâng dpi tất cả diagram hiện có; render lại; PIL kiểm tra biên.
3. Tạo 16 `.puml` per-UC (mục 4), render PNG, PIL kiểm tra biên + padding nếu cần.
4. Viết lại §2.3 và Phụ lục B: mỗi UC = diagram (figure) + bảng đặc tả (longtable) liền nhau.
5. Build XeLaTeX, mở PDF kiểm tra: hết gap lớn, ảnh đủ khung, chữ đọc rõ.
6. Kiểm tra tổng dung lượng ảnh.

## 7. Quyết định cần chốt
1. **Số lượng diagram:** (a) overview + 16 per-UC (bỏ 4 group — gọn, khuyến nghị), hay
   (b) giữ cả group + thêm 16 per-UC (đầy đủ nhưng 21 hình)?
2. **Bảng đặc tả:** chuyển sang `longtable` in-flow (khuyến nghị, hết khoảng trắng) — đồng ý?
3. **Định dạng ảnh:** PNG dpi 220–300 (theo ý anh) — hay muốn thử xuất PDF vector (nét nhất,
   nhưng cần thêm thư viện cho PlantUML)?
4. **Group B:** ưu tiên cách (a) thêm title, (b) tách LAN/Internet, hay (c) đổi chiều layout?
