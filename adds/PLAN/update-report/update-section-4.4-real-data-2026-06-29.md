# Plan: Cập nhật số liệu Mục 4.4 bằng dữ liệu backtest THẬT (chốt 2026-06-29)

> Mục tiêu: thay toàn bộ số liệu **ngoại suy** ở Mục 4.4 (Kiểm thử và đánh giá) bằng số **đo
> thật**, scaling 6 → 12 → 24 → 36 → 72 agent (6 reps × 5 bản đồ × 2 môi trường), rồi cập nhật
> bảng/biểu đồ/diễn giải cho khớp.

---

## 0. Bối cảnh — vì sao phải làm

- Hiện **chỉ mức 6 agent là đo thật** (neo từ run 24/06). Các mức **12/24/36/72 trong quyển là
  NGOẠI SUY** do `Tools/backtest_scale_synth.py` sinh ra bằng mô hình lũy thừa (`r^p`), ghi thẳng
  vào CSV pivot → nuôi Bảng 4.9/4.10/4.11 và Hình 4.24–4.27.
- Dữ liệu **đo thật ngày 29/06** (code đã fix) **mâu thuẫn mạnh** với số ngoại suy → phải thay.
- Thêm nữa: neo 6-agent (24/06) là **code CŨ**, còn data 29/06 là **code MỚI** → trộn hai nguồn là
  không hợp lệ; cần chạy lại **toàn bộ** trên cùng một version code.

---

## 1. Dữ liệu thật đang có (29/06, mới 1 rep)

| File | Mức | Môi trường | Phủ | Ghi chú |
|---|---|---|---|---|
| `backtest_summary_20260629_140945.csv` | 72 | tĩnh | **chỉ Alpha32** | thiếu 4 map |
| `backtest_summary_20260629_151541.csv` | 72 | động | **chỉ Mansion** | thiếu 4 map; TCP timeout |
| `backtest_summary_20260629_160612.csv` | 24 | tĩnh | đủ 5 map | TCP timeout ở Maze128 |
| `backtest_summary_20260629_162717.csv` | 24 | động | đủ 5 map | TCP timeout Mansion/Gallows |
| `backtest_summary_20260629_164727.csv` | 36 | tĩnh | đủ 5 map | TCP timeout Mansion |
| `backtest_summary_20260629_171427.csv` | 36 | động | đủ 5 map | TCP timeout 4/5 map |

→ Đủ 5-map cho **24 và 36** (1 rep). Mức **72 mới chạy 2 ô lẻ**. Mức **6/12 chưa chạy với code mới**.

---

## 2. Đối chiếu THẬT vs QUYỂN — các mâu thuẫn chính

### 2.1. Replan của PIBT C# bị thổi phồng (nghiêm trọng nhất)

Bảng 4.11 (quyển, ngoại suy) so với đo thật @24 tĩnh — cột **PIBT C#** (replan/run):

| Bản đồ | Quyển (synth) | Thật 29/06 | Lệch |
|---|---|---|---|
| Mansion | 28.656 | **2.619** | ~11× |
| Chantry | 38.182 | **2.383** | ~16× |
| Gallows | 46.499 | **2.663** | ~17× |

→ Mô hình `r^1.5–1.7` cho PIBT C# **sai về bản chất**. Thật ra replan PIBT C# **xấp xỉ A\***
(trong ~2×), không "bùng nổ hàng trăm nghìn". Toàn bộ narrative "PIBT C# vọt lên ~140.000 replan ở
72 agent" (Hình 4.25, mục 4.4.5/4.4.6) **không còn đúng**.

### 2.2. Tỉ lệ phá Eagle thật CAO hơn nhiều

| Mức | Quyển (Bảng 4.9, %Phá Eagle) | Thật 29/06 (1 rep) |
|---|---|---|
| 24 | A\* 80 / PIBT 80 / TCP 40 | A\* 5/5, PIBT 5/5, TCP 4/5 (≈100/100/80%) |
| 36 | A\* 40 / PIBT 40 / TCP 20 | A\* 5/5, PIBT 5/5, TCP 4/5 (tĩnh) |

→ Quyển (synth) **quá bi quan**: nhiều map bị cho "timeout" ở 24/36 nhưng thật ra **giải được**.
Quy luật "vách timeout từ 36 agent" và "sụp đổ sau ~50 agent" cần xem lại — thực tế A\*/PIBT vẫn
phá Eagle ở 36, kể cả Mansion động ở 72 (file 151541).

### 2.3. Recovery (động) cũng bị thổi cao

Quyển @24 động: PIBT recov ≈ 3.031 (aggregate). Thật: PIBT recov/map ≈ 72–142 (Σ5map ≈ 550) → ~5×.

### 2.4. Duration hơi lệch (nhẹ)

Quyển @24 ≈ 136–144s; thật @24 ≈ 22–177s tùy map (trung bình ~90s) → quyển cao hơn ~1,5×.

### 2.5. PIBT_TCP còn LỖI — chưa tin được

TCP timeout bất thường: Maze128@24 tĩnh (0/0/0), Mansion/Gallows@24 động, **Alpha32@36 động**
(lẽ ra map dễ nhất). Cells ≈ 80–160 cho 72 agent = gần như đứng im. **Đây là bug đang fix trên
nhánh `feature/fix_backtest_PIBT_TCP`** — số TCP hiện tại chưa dùng làm dữ liệu chính thức được.

---

## 3. Việc phải xử lý TRƯỚC khi cập nhật quyển

1. **Đồng nhất version code**: bỏ neo 24/06; chạy lại **cả mức 6** bằng code hiện tại để mọi mức
   cùng một code.
2. **Hoàn tất fix PIBT_TCP** (timeout/0-progress) — nếu không, cột TCP@36/72 sẽ toàn timeout giả.
   Nếu fix không kịp: ghi rõ giới hạn của biến thể TCP ở map khó + mật độ cao, hoặc giới hạn TCP ở
   các mức/map mà nó chạy ổn.
3. **Tăng số rep lên 6** (hiện 1 rep → phương sai lớn, chưa kết luận được).

---

## 4. Ma trận dữ liệu cần thu thập

`5 map × 3 thuật toán × 5 mức (6,12,24,36,72) × 2 môi trường × 6 rep = 900 run`

| Mức | Tĩnh (5 map × 6 rep) | Động (5 map × 6 rep) |
|---|---|---|
| 6  | ⬜ chạy lại (code mới) | ⬜ |
| 12 | ⬜ | ⬜ |
| 24 | 🟡 có 1 rep → cần thêm 5 | 🟡 có 1 rep → cần thêm 5 |
| 36 | 🟡 có 1 rep → cần thêm 5 | 🟡 có 1 rep → cần thêm 5 |
| 72 | ⬜ (mới 1 map) | ⬜ (mới 1 map) |

**Phương án trần agent**: ưu tiên đủ **6→72**. Nếu 72 quá nặng hoặc TCP không ổn định ở 72, **hạ
trần xuống 36** và thang còn **6/12/24/36** (vẫn đủ thể hiện xu hướng scaling) — cập nhật lại
LEVELS trong script + câu chữ tương ứng.

---

## 5. Quy trình chạy (tận dụng code mới trên nhánh feature)

- **Tua nhanh**: bật `Time.timeScale` (phím 1–8) — A\*/PIBT chạy x3, TCP tự về 1x. Metrics không
  đổi (đo theo `Time.time`). 900 run ở x3 ≈ rút ~2/3 thời gian chờ.
- **Cap agent** đã nâng 72.
- Mỗi tổ hợp (mức × môi trường) chạy ra 1 cặp `backtest_summary_*.csv` + `backtest_agents_*.csv`.
- **Đặt tên/sắp xếp**: gom theo quy ước, ví dụ thư mục `BacktestResults/real_2026xxxx/` để
  aggregator quét. Ghi lại mapping (mức, môi trường, reps) cho từng file.

---

## 6. Pipeline tổng hợp MỚI — thay ngoại suy bằng dữ liệu thật

1. **Viết `Tools/backtest_scale_aggregate.py`** (mới): đọc **nhiều** CSV thật, gộp theo
   `(env, map, agentcount, algo)` lấy trung bình trên các rep, xuất:
   - `report_scaling_by_agentcount.csv`
   - `report_scaling_by_map.csv`
   (đúng schema mà `backtest_scale_tables.py` + `backtest_scale_plots.py` đang dùng).
2. **Khai tử `backtest_scale_synth.py`** (đổi tên `*.deprecated` hoặc xóa) — không sinh số ngoại
   suy nữa.
3. Chạy `backtest_scale_tables.py` → in lại thân Bảng 4.7–4.11 (LaTeX) từ pivot thật.
4. Chạy `backtest_scale_plots.py` → render lại Hình 4.19–4.27 (PNG) vào `Hinhve/`.

> Lợi: bảng và biểu đồ vẫn chung một nguồn pivot → luôn khớp. Chỉ thay nguồn pivot từ "synth" sang
> "aggregate thật".

---

## 7. Cập nhật cụ thể trong quyển (Mục 4.4 + liên quan)

### 7.1. Bảng/biểu đồ (regenerate, dán số mới)
- **Bảng 4.7** (tĩnh, 6 agent) và **Bảng 4.8** (động, 6 agent): thay bằng số thật 6-agent code mới.
- **Bảng 4.9** (scaling tĩnh), **Bảng 4.10** (scaling động), **Bảng 4.11** (replan chi tiết/map):
  regenerate từ pivot thật.
- **Hình 4.19, 4.20, 4.23** (6 agent theo map) và **Hình 4.24–4.27** (scaling theo mức): render lại.
- **Bảng 4.4** (cấu hình): cập nhật mốc agent nếu đổi trần (6–72 hay 6–36), và số "tổng run" nếu
  reps/levels đổi.

### 7.2. Diễn giải (phải viết lại các nhận định sai)
- **4.4.5 / 4.4.6**: bỏ/sửa câu "PIBT C# vọt lên ~140.000 replan", "ba đường cong tách biệt rõ" —
  thay bằng quan sát thật (replan ba thuật toán gần nhau hơn; PIBT C# không bùng nổ).
- **Tỉ lệ phá Eagle theo mức** (đoạn quanh Bảng 4.9 đã thêm): cập nhật con số 80→40→20 theo dữ liệu
  thật (nhiều khả năng cao hơn); chỉnh lại lập luận "vách timeout" và "sụp đổ sau ~50 agent".
- **Recovery (4.4.4 / Hình 4.27)**: cập nhật mức recovery thật.
- **PIBT_TCP**: nêu trung thực hành vi thật (ổn ở map thoáng; dễ timeout ở map hẹp + mật độ/động cao).

### 7.3. Chương 6 (Hạn chế)
- **Bỏ câu** "ở các mức agent lớn, một phần số liệu mang tính ngoại suy theo mô hình thay vì đo
  trực tiếp..." — vì giờ đã đo thật. (Nếu vẫn còn mức nào ngoại suy thì giữ, ghi rõ mức nào.)

### 7.4. Nhất quán chéo
- Phụ lục A (Bảng A.1/A.2 chi tiết 6 agent ± độ lệch chuẩn): cập nhật theo số thật 6-agent mới.
- Tóm tắt (0_3) + Chương 1: con số "mức tăng replan 30–50%", "900 run" — rà lại cho khớp.

---

## 8. Logistics nhánh

- **Code chạy backtest** (cap72, fast-forward, chart export, 4 script Tools) → ở nhánh
  `feature/fix_backtest_PIBT_TCP` (đang chạy test).
- **Quyển + dữ liệu/bảng/biểu đồ kết quả** → ở nhánh `report`.
- **Bước handoff**: copy CSV thật 29/06 (và các đợt chạy đủ 6-rep sau này) sang `report`, regenerate
  pivot/bảng/hình tại `report`, cập nhật `.tex`, build PDF.
- Lưu ý: `adds/report/` chỉ có trên nhánh `report`; thao tác sửa quyển phải ở `report`.

---

## 9. Thứ tự thực hiện (checklist)

1. [ ] Fix xong PIBT_TCP (hoặc chốt cách xử lý nếu chưa kịp).
2. [ ] Chạy lại đủ ma trận §4 (ưu tiên 6 & 12 trước vì còn thiếu hẳn; bù thêm rep cho 24/36; hoàn
       thiện 72 hoặc hạ trần 36), 6 rep, code hiện tại, tua x3.
3. [ ] Viết `backtest_scale_aggregate.py`; khai tử `backtest_scale_synth.py`.
4. [ ] Sinh lại pivot thật → chạy `backtest_scale_tables.py` + `backtest_scale_plots.py`.
5. [ ] Trên `report`: dán số bảng 4.7–4.11, thay hình 4.19–4.27, viết lại diễn giải §7.2, bỏ caveat
       §7.3, rà nhất quán §7.4.
6. [ ] Build PDF, kiểm tra lề/free-space, commit `report`.

---

## 10. Rủi ro & điểm cần người dùng quyết

- **72 agent**: TCP có thể không ổn → quyết giữ trần 72 hay hạ 36.
- **PIBT_TCP chưa fix xong**: nếu vẫn timeout nhiều, cân nhắc (a) giới hạn phạm vi báo cáo của TCP,
  hoặc (b) nêu rõ là giới hạn của kiến trúc cầu nối TCP thời gian thực dưới tải cao.
- **Thời lượng 900 run**: ngay cả x3 vẫn tốn; có thể chạy theo lô (từng mức/môi trường) nhiều buổi.
- **Câu chuyện đổi chiều**: nhiều nhận định "PIBT C# bùng nổ replan / sụp đổ sớm" sẽ phải viết lại —
  cần GVHD đồng ý với hướng kết luận mới (số thật "đẹp" hơn cho PIBT nhưng khác narrative cũ).

---

## Phụ lục: số thật 29/06 đã trích (để đối chiếu nhanh khi update)

**@24 tĩnh (replan/run, A\* / PIBT C# / TCP):** Alpha32 618/841/758 · Mansion 2272/2619/2563 ·
Chantry 2272/2383/2581 · Gallows 2674/2663/2746 · Maze128 3768/4262/0†(TCP timeout).

**@36 tĩnh:** Alpha32 939/1118/1061 · Mansion 3530/3906/152†(TCP) · Chantry 3449/3925/3251 ·
Gallows 4114/5171/4089 · Maze128 5501/6004/5549.

**@72 (mới có):** Alpha32 tĩnh 1878/1795/2777 (đều phá Eagle); Mansion động 9119/8129/295†(TCP timeout).
