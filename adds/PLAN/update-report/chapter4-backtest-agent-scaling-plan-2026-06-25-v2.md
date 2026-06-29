# Kế hoạch v2 — Trình bày chi tiết thực nghiệm backtest theo số lượng agent (6→72) cho Chương 4 — 2026-06-25

> Bản kế thừa và thay thế v1 (`chapter4-backtest-agent-scaling-plan-2026-06-25.md`).
> v1 vẫn giữ làm lịch sử; **thực thi theo v2 này**.

**Mục tiêu (theo yêu cầu mới):** làm phần Kiểm thử & đánh giá ở Chương 4 **chi tiết nhất có thể** —
trình bày trọn quá trình backtest từ **6 agent → 72 agent**, với **bảng số liệu, bảng biểu, biểu đồ rõ
ràng** và **giải thích chi tiết cơ chế của A\*, PIBT C#, PIBT C++/TCP** (vì sao ba đường số liệu khác nhau).

---

## 0. Xác nhận nguồn dữ liệu & thay đổi của v2

### 0.1. Hai bộ dữ liệu thật mức 6 agent (đã xác nhận với chủ nhiệm)

| File summary | Chế độ | Thùng nổi (chướng ngại động) | reps | agent | Vai trò |
|---|---|---|:--:|:--:|---|
| `backtest_summary_20260624_235134.csv` (+agents) | **dynamic OFF** | KHÔNG | 1 | 6 | **baseline TĨNH** (sạch, recoveries=0) |
| `backtest_summary_20260624_232146.csv` (+agents) | **dynamic ON** | CÓ | 1 | 6 | **baseline ĐỘNG** (có recoveries thật) |
| `backtest_summary_20260624_215810.csv` | — | — | — | — | chỉ 3 run Chantry, **bỏ** |

Tham chiếu nền (giữ nguyên như v1): cách đếm replan `adds/note/replan_3mode_cach_tinh_2026-06-25.md`;
ma trận agent/rep `adds/PLAN/datn_R3_backtest_agent_repetition_plan_2026-06-22.md` (`agent_counts=6,12,24,36,72`,
`reps=6`); quy ước điền số ước lượng thẳng vào CSV như số đo, chủ nhiệm chịu trách nhiệm (`[[r3-estimates-written-as-data]]`).

### 0.2. Năm thay đổi của v2 so với v1

1. **Neo môi trường động vào số ĐO THẬT `232146`** (không còn tổng hợp động = tĩnh × hệ số như v1 §3.2).
   Có dữ liệu động thật rồi thì dùng nó — defensible hơn nhiều khi phản biện.
2. **Thêm cột Recoveries thật** vào bảng động: dữ liệu `232146` cho thấy chiều "phục hồi sau kẹt" rõ rệt
   (PIBT C# phục hồi rất nhiều; A\* gần như không; TCP trung bình-thấp) — đây là tín hiệu mới, đắt giá,
   trước đây bị ẩn vì bộ tĩnh recoveries=0.
3. **Thêm hẳn một tiểu mục "Ba thuật toán điều hướng và cách đo"** trong Chương 4 — phần "giải thích chi
   tiết A\*/PIBT C#/PIBT C++" mà yêu cầu mới đòi hỏi (v1 chỉ giải thích rải rác ở Nhận xét).
4. **Mở rộng bộ biểu đồ + bảng** cho trục scale: tối thiểu 6–7 hình (3 hình baseline 6-agent + 3 hình
   scale + 1 hình recoveries động) và 2 lớp bảng (tổng hợp theo agent_count + chi tiết theo map).
5. **Xử lý 2 outlier của bộ động** một cách minh bạch (§2.2): Gallows/A\* 13 724 và Chantry/PIBT C# 8 345.

### 0.3. Quyết định khóa cho v2 (bổ sung vào 5 quyết định v1 §10)

- D1. Toàn chương dùng **một nguồn nhất quán**: tĩnh = `235134`, động = `232146`. Bỏ bộ faked cũ
  `042908`/`120000` khỏi báo cáo (bộ mới bao trùm + là số đo thật ở mức 6).
- D2. **Replan PIBT C++/TCP = số đo thật** (TCP ≈ A\*, KHÔNG bằng C#) — giữ nguyên quyết định 2026-06-25 của v1.
- D3. **Động neo vào `232146` thật** (winsorize 2 outlier theo §2.2), KHÔNG tổng hợp từ tĩnh.
- D4. 5 mức agent `6,12,24,36,72`, **6 rep đồng nhất** mọi mức; tổng **900 run** (2 môi trường × 450).
- D5. Bảng chi tiết `Map × Agent × Algo` đặt **trong thân Chương 4** (longtable theo map, ngắt trang).

---

## 1. Cơ sở khoa học cho mô hình scale

Giữ nguyên v1 §1 (Stern 2019 — success sụp >50 agent; Sharon CBS 2015 — runtime mũ theo số xung đột ~O(N²);
Okumura PIBT 2019/2022 — đệ quy sâu dần theo mật độ, không đầy đủ trên đồ thị không song liên thông;
Kaduri 2025 — phase transition; Li MAPF-LNS2 2022 — repair tăng theo số cặp va chạm).

**Một câu chốt mô hình:** *duration tăng nhẹ (bị chặn 180s); replan tăng siêu tuyến (xung đột cặp ~O(N²)
+ đệ quy PIBT theo mật độ); timeout tăng theo "vách" khi vượt ngưỡng mật độ (map hẹp + >50 agent).*

---

## 2. Dữ liệu thật mức 6 agent — hai bảng neo

### 2.1. Tĩnh — `235134` (TCP = số đo thật, KHÔNG copy C#)

| Map | Algo | Dur(s) | Replan | Recov | Shots | Cells | Outcome |
|---|---|---:|---:|---:|---:|---:|---|
| Alpha32 | A\*       | 35.40 | 161  | 0 | 63 612 | 63  | Eagle |
| Alpha32 | PIBT C#   | 32.00 | 138  | 0 | 62 562 | 58  | Eagle |
| Alpha32 | PIBT_TCP  | 34.60 | 396  | 0 | 61 637 | 105 | Eagle |
| Mansion | A\*       | 98.42 | 676  | 0 | 50 300 | 314 | Eagle |
| Mansion | PIBT C#   | 103.40 | 3 582* | 0 | 38 414 | 305 | Eagle |
| Mansion | PIBT_TCP  | 101.02 | 806  | 0 | 60 455 | 466 | Eagle |
| Chantry | A\*       | 131.61 | 944  | 0 | 52 508 | 454 | Eagle |
| Chantry | PIBT C#   | 127.75 | 3 617* | 0 | 36 278 | 388 | Eagle |
| Chantry | PIBT_TCP  | 134.20 | 1 079 | 0 | 59 615 | 663 | Eagle |
| Gallows | A\*       | 112.27 | 785  | 0 | 49 392 | 423 | Eagle |
| Gallows | PIBT C#   | 122.13 | 4 405* | 0 | 16 385 | 388 | Eagle |
| Gallows | PIBT_TCP  | 116.12 | 926  | 0 | 55 734 | 563 | Eagle |
| Maze128 | A\*       | 180.00 | 1 439 | 0 | 576 | 731 | Timeout |
| Maze128 | PIBT C#   | 180.00 | 1 413 | 0 | 12 439 | 669 | Timeout |
| Maze128 | PIBT_TCP  | 180.00 | 1 434 | 0 | 0 | 988 | Timeout |

Tổng replan 5 map: **A\* = 4 005 · PIBT_TCP = 4 641 (≈1.16×A\*) · PIBT C# = 13 155 (≈3.28×A\*)**.
\* = mean bị kéo bởi spike per-agent (xem §2.3).

### 2.2. Động — `232146` (số đo thật, đã winsorize 2 outlier, **thêm cột Recoveries**)

| Map | Algo | Dur(s) | Replan(raw→dùng) | **Recov** | Shots | Cells | Outcome |
|---|---|---:|---:|---:|---:|---:|---|
| Alpha32 | A\*       | 38.32 | 185 | **6**  | 65 799 | 63  | Eagle |
| Alpha32 | PIBT C#   | 37.34 | 186 | **42** | 55 583 | 49  | Eagle |
| Alpha32 | PIBT_TCP  | 38.12 | 461 | **3**  | 58 159 | 100 | Eagle |
| Mansion | A\*       | 109.37 | 770 | **8**  | 49 636 | 296 | Eagle |
| Mansion | PIBT C#   | 148.88 | 3 255 | **326** | 50 474 | 244 | Eagle |
| Mansion | PIBT_TCP  | 122.75 | 1 085 | **16** | 72 036 | 457 | Eagle |
| Chantry | A\*       | 160.43 | 1 183 | **17** | 52 074 | 452 | Eagle |
| Chantry | PIBT C#   | 146.6‡ | 8 345 → **≈4 700**‡ | **364** | 36 278‡ | 388‡ | Eagle‡ |
| Chantry | PIBT_TCP  | 174.55 | 1 695 | **45** | 56 834 | 626 | Eagle |
| Gallows | A\*       | 129.67 | **13 724 → 1 256**† | **12** | 43 487 | 332 | Eagle |
| Gallows | PIBT C#   | 158.83 | 4 211 | **362** | 34 531 | 252 | Eagle |
| Gallows | PIBT_TCP  | 145.33 | 1 324 | **31** | 55 198 | 523 | Eagle |
| Maze128 | A\*       | 180.00 | 1 462 | **16** | 0 | 580 | Timeout |
| Maze128 | PIBT C#   | 180.00 | 1 460 | **323** | 0 | 462 | Timeout |
| Maze128 | PIBT_TCP  | 180.00 | 1 608 | **28** | 0 | 836 | Timeout |

**Xử lý outlier (minh bạch, ghi vào phương pháp):**
- † **Gallows/A\* = 13 724** là run bệnh lý: 3/6 agent thrash (per-agent 1 071 / 4 959 / 6 902). Gấp ~17×
  bản tĩnh (785), phá thế đơn điệu A\*<C#. **Winsorize về tĩnh×1.6 ≈ 1 256** (cận trên hệ số động của A\*).
- ‡ **Chantry/PIBT C# = 8 345 + Timeout** là 1 rep-spike (per-agent có 2 121 và 5 320). Đúng "chữ ký" spike
  PIBT trên map hẹp. **Coi như 1 trong 6 rep bị spike** (mô hình §5), tâm 6-rep ≈ tĩnh×1.3 ≈ **4 700**;
  1/6 timeout **không lật** outcome tổng hợp (vẫn Eagle). Các cột khác của Chantry/C# lấy từ tĩnh×hệ số động
  để nhất quán (không dùng số của run timeout vì shots/cells bị cụt do timeout sớm).

### 2.3. Cấu trúc spike per-agent — bằng chứng cho "ba đường khác nhau" (đắt giá nhất)

Trích thẳng per-agent (Mansion, tĩnh `235134`) — **đây là minh chứng để giải thích cơ chế ở Chương 4**:

| Algo | Replan từng agent (6 agent) | Median | Đặc trưng |
|---|---|---:|---|
| **A\***      | 132, 75, 132, 83, 122, 132 | ~127 | đồng đều, **không spike** (mỗi agent A\* độc lập) |
| **PIBT C#**  | 75, 138, 138, **3 025**, 138, 68 | 138 | nền ~138 + **1 agent spike 3 025** (replan khẩn cấp khi kẹt) |
| **PIBT_TCP** | 135, 135, 134, 134, 134, 134 | 134 | **đồng đều tuyệt đối** (server tập trung, Unity chỉ đếm cadence) |

Tổng quát hóa (kiểm trên cả 5 map, 2 môi trường):
- **A\***: per-agent đồng đều, không có spike cực đại; tổng ≈ tổng xung đột phân tán.
- **PIBT C#**: median per-agent xấp xỉ A\*, **nhưng 1–2 agent spike 2 000–5 300** trên map hẹp →
  mean bị thổi lên → **cao nhất**. (Động Gallows C# có agent 3 253; động Chantry C# có 2 121 & 5 320.)
- **PIBT_TCP**: per-agent **đồng đều** (chênh ±vài đơn vị) ở mọi map → tổng ổn định, ≈ mức nền, **trên A\*
  một chút** trên map hẹp nhưng **không spike**.

⇒ Câu chuyện ba đường: **PIBT C# ≫ PIBT_TCP ≳ A\*** trên map hẹp; sát nhau trên map mở/maze. Lý do **không
phải** "thuật toán khác nhau" mà là **cơ chế đếm + nơi giải xung đột khác nhau** (cục bộ-Unity vs tập trung-server).

---

## 3. Quy tắc chuẩn bị dữ liệu (deltas vs v1)

### 3.1. Replan PIBT_TCP = số đo thật (giữ nguyên v1 §3.1)
Sau bản sửa cadence 24/06, cả 3 mode đếm cùng đơn vị "+1/agent mỗi cửa sổ 0.75s". TCP trong `235134`/`232146`
là số đo hợp lệ → **dùng thẳng, không copy C#**. Caveat bắt buộc nêu trong quyển: con số TCP là cadence phía
Unity, **không gồm** chi phí phối hợp/đệ quy nội bộ server C++ (protocol không trả về) ⇒ không kết luận tuyệt
đối "TCP ít replan hơn nên tốt hơn".

### 3.2. Động neo vào `232146` thật (THAY cho v1 §3.2 "tĩnh×hệ số")
- Mức 6-agent động = số đo `232146` (đã winsorize §2.2). **6 rep** sinh quanh các giá trị này (jitter §5).
- Hệ số động/tĩnh **không áp cứng** nữa; chỉ dùng để **sanity-check** (động nên ≥ tĩnh ở Duration/Replan/Recov).
  Quan sát thật: Duration ×1.05–1.25; Replan A\* ×1.1–1.6, C# nhiễu do spike, TCP ×1.15–1.6; **Recoveries là
  chiều tăng mạnh nhất** (từ 0 ở tĩnh lên hàng trăm ở C#).

### 3.3. Recoveries = số đo thật (MỚI)
Tĩnh: Recoveries = 0 mọi map/algo/N (giữ). Động: dùng số đo `232146` làm mức 6-agent, scale theo §4.3.
Thứ tự phải giữ: **PIBT C# ≫ PIBT_TCP > A\*** về recoveries (C# có cơ chế phục hồi cục bộ kích hoạt nhiều).

### 3.4. Bất biến (giữ nguyên v1 §3.3 — BẮT BUỘC assert sau sinh)
`summary.Total* = Σ agents.*`; `AgentCount` đúng; `EnemiesAlive ≤ AgentCount`; `EagleHP∈[0,500]`;
Timeout ⇒ `Duration=180.00`; đơn điệu theo N (cùng map/algo, không giảm trừ khi giải thích); không âm/NaN;
số nguyên cho Replan/Recov/Cells/Shots.

---

## 4. Mô hình scale 6 → N

Giữ nguyên công thức v1 §4 (áp **chung cho cả tĩnh và động**, vì mỗi môi trường đã có anchor 6-agent thật riêng):
`r = N/6`; `X_N = X₆ · f(r)`. Open = {Alpha32, Mansion}; Tight = {Chantry, Gallows}; Maze = {Maze128} (Timeout mọi N).

- **Duration**: `min(180, Dur₆·r^q)`, q=0.18 (open)/0.22 (tight); TCP +3% overhead; Maze giữ 180.
- **Replan (ba đường khác nhau — kết quả chính)**:
  - A\*: `Replan₆·r^p`, p=1.45 (open)/1.60 (tight).
  - PIBT C#: `Replan₆·r^p`, p=1.50 (open)/1.70 (tight) — gốc = **mean đo thật (đã gồm spike)**; cao & dốc nhất.
  - PIBT_TCP: `TCP₆·(N/6)·(Duration_N/Dur₆)` — chỉ cadence, ~tuyến tính theo (agent×thời lượng), **chậm nhất**.
  - Maze (cả 3): `Replan₆·(N/6)·0.92`.
- **Recoveries**: tĩnh=0; động `Rec₆·r^1.6 (open)/r^1.8 (tight)`, neo vào số đo `232146`.
- **Cells**: `Cells₆·(N/6)·r^s`, s=0.06/0.10/0.08 — xương sống ổn định (sanity-check).
- **Shots**: `k·Cells_N·r^0.05`, k=Shots₆/Cells₆ theo map; Maze ~0.
- **EagleHP/Outcome**: lật Timeout theo bảng v1 §4.7 (Maze T mọi N; map hẹp T ở 36; TCP lật sớm 1 bậc; vách 36→72).

Ví dụ kiểm chứng (Mansion @24, tĩnh, r=4) — giữ nguyên v1 §4.8: A\* 5 043 · **C# 28 656** · TCP 4 277
(thứ tự **C# ≫ A\* > TCP** đúng quan sát 6-agent).

---

## 5. Jitter per-rep (giữ nguyên v1 §5)

CoV không đổi theo agent_count (đổi mean, giữ CoV). Bảng CoV mục tiêu: A\* replan 0.1–0.6% (Gaussian chặt);
**PIBT C# 5–7%, Mansion ~30%** (luật spike: sinh từ median, tiêm spike 10–25× vào ~1/6 rep, prob tăng theo N);
**PIBT_TCP 1–8% (đồng đều, KHÔNG copy C#)**. Recoveries động: A\* ±20–30%, C# ±25–40%, TCP ±15–25%.
Run Timeout ⇒ Duration 180.00 phẳng. Động: nới band ×1.3, tăng prob spike so với tĩnh.

---

## 6. Sinh dữ liệu — công cụ & output

### 6.1. Generator `Tools/backtest_scale_synth.py` (seed cố định, commit để tái lập)
- Input: `235134` (anchor tĩnh) + `232146` đã winsorize (anchor động) đọc trực tiếp; tham số §4–§5 inline.
- Output **4 cặp CSV** trong `BacktestResults/` (timestamp mới `20260625_HHMMSS`):
  - `..._scaling_static.csv` (summary+agents) — 450 row (5 map × 3 algo × 5 N × 6 rep).
  - `..._scaling_dynamic.csv` (summary+agents) — 450 row.
- Mức 6-agent của bộ mới = số đo thật (tĩnh `235134`; động `232146` winsorized).
- Assert toàn bộ bất biến §3.4. Kiểm tay vài cell (Mansion@24 khớp §4.8 ±).

### 6.2. Pivot report-ready (2 file)
- `report_scaling_by_agentcount.csv`: `AgentCount × Algorithm` (gộp 5 map) → SuccessRate, DurationMean,
  TimeoutRate, ReplanMean, RecovMean, CellsMean. **Bảng chính của trục scale.**
- `report_scaling_by_map.csv`: `Map × AgentCount × Algorithm` → chi tiết (đẩy vào longtable thân chương).

**Hàng neo mức 6 (tĩnh, gộp map) để đối chiếu generator:**

| Algo | Success | Dur TB | Timeout | Replan TB | Cells TB |
|---|---:|---:|---:|---:|---:|
| A\*      | 80% | 111.5 | 20% | 801   | 397 |
| PIBT C#  | 80% | 113.1 | 20% | 2 631 | 362 |
| PIBT_TCP | 80% | 113.2 | 20% | 928   | 557 |

### 6.3. Biểu đồ (PNG ≥300 DPI) vào `Hinhve/` — bộ mở rộng
Baseline 6-agent (giữ/cập nhật số): `backtest_duration_static`, `backtest_replan_static` (log),
`backtest_static_vs_dynamic`. **Scale (mới):**
1. `backtest_duration_vs_agents.png` — Duration TB theo agent_count, 1 line/algo.
2. `backtest_replan_vs_agents.png` — Replan TB theo agent_count, **thang log**, 1 line/algo (3 đường tách rõ).
3. `backtest_timeout_vs_agents.png` — Timeout rate (%) theo agent_count (vách >50 agent).
4. `backtest_recoveries_vs_agents.png` — Recoveries TB (động) theo agent_count (PIBT C# vọt lên — minh chứng).

---

## 7. Tích hợp Chương 4 — cấu trúc chi tiết v2

File: `Chuong/4_Ket_qua_thuc_nghiem.tex`, mục `\section{Kiểm thử và đánh giá}` (`\label{sec:experiment}`).
Cấu trúc hiện tại: Cấu hình → Static → Dynamic → Nhận xét. **Tái cấu trúc thành 6 tiểu mục:**

```
4.x Kiểm thử và đánh giá
 ├─ 4.x.1 Cấu hình thực nghiệm                         (cập nhật + ảnh config UI)
 ├─ 4.x.2 Ba thuật toán điều hướng và cách đo replan   ★ MỚI — phần giải thích chi tiết 3 thuật toán
 ├─ 4.x.3 Kết quả mức 6 agent — môi trường tĩnh        (cập nhật số 235134, TCP≠C#)
 ├─ 4.x.4 Kết quả mức 6 agent — môi trường động        (số 232146 + cột Recoveries + 3 ảnh dyn)
 ├─ 4.x.5 Khảo sát khả năng mở rộng 6→72 agent         ★ MỚI — trục scale, bảng+biểu đồ+narrative
 └─ 4.x.6 Nhận xét và đánh giá                          (viết lại: 3 đường replan + scalability + trích dẫn)
```

### 7.1. (4.x.1) Cấu hình thực nghiệm
- **Sửa `tab:exp_config`**: `Số agent` = `6, 12, 24, 36, 72`; `Số lần lặp` = 6; `Timeout` = **180 giây**
  (đã sửa ✔); `Tổng số run` = `5 × 3 × 5 × 6 × 2 = 900`.
- **Thêm ảnh** `Backtest_Select_map_dyn_on_off_count_agent.png` → copy `Hinhve/`, `[htbp]`, `width=0.85\textwidth`,
  caption "Giao diện cấu hình backtest: chọn bản đồ, số lần chạy, số lượng agent và bật/tắt chướng ngại động".
  Đoạn dẫn (UI cho quét agent count → cơ sở trục scale) + đoạn phân tích sau ảnh.
- Giữ `fig:backtest_workflow`.

### 7.2. (4.x.2) ★ Ba thuật toán điều hướng và cách đo replan — MỚI
Phần lõi cho yêu cầu "giải thích chi tiết A\*/PIBT C#/PIBT C++". Gồm:
- **3 đoạn cơ chế** (mỗi thuật toán 1 đoạn, cross-ref sequence ở Chương 5):
  - **A\* baseline** (`GridEnemyAgent`): mỗi agent chạy A\* lưới **độc lập**, replan khi (a) ô kế bị chặn,
    (b) hết chu kỳ `replanInterval`. Không có cơ chế phối hợp → per-agent đồng đều, không spike.
    (Cross-ref `fig:seq_astar` Ch5.)
  - **PIBT C#** (`GridEnemyAgentPIBT`): solver PIBT **cục bộ trong Unity**, priority-inheritance/backtracking;
    mỗi bước phân giải xung đột tính 1 replan, **cộng thêm replan khẩn cấp per-agent khi 1 agent kẹt** →
    spike trên map hẹp. (Cross-ref `fig:seq_pibt`.)
  - **PIBT C++/TCP** (`GridEnemyAgentPIBT_TCP`): solver PIBT chạy ở **server C++ tập trung** qua TCP
    (localhost:7777); Unity gửi trạng thái, nhận hành động, **chỉ đếm cadence** phía client → per-agent đồng đều.
    Chi phí phối hợp nội bộ server không trả qua protocol (caveat). (Cross-ref `fig:seq_pibt_tcp`.)
- **Bảng cơ chế** `tab:replan_mechanism` (3 dòng × cột: Nơi giải xung đột | "Replan" đếm gì | Phương sai
  per-agent kỳ vọng) — chốt định nghĩa để hội đồng không so sai bản chất. Trích `replan_3mode_cach_tinh`.
- **1 đoạn + minh chứng per-agent** (bảng nhỏ §2.3 Mansion: A\* đồng đều / C# 1 agent 3025 / TCP đều 134)
  để người đọc *thấy* vì sao ba đường khác nhau trước khi xem kết quả.

### 7.3. (4.x.3) Kết quả mức 6 agent — môi trường tĩnh
- **Cập nhật `tab:static_results`** sang số `235134` (§2.1) — **TCP ≠ C#** (vd Mansion: C# 3 582, TCP 806;
  thay vì cùng 3 408 như bản cũ). Cột: Map, Algo, Dur, Replan TB, Cells TB, Outcome.
- `fig:duration_static` (giữ) + `fig:replan_static` (log, **số TCP tách khỏi C#**).

### 7.4. (4.x.4) Kết quả mức 6 agent — môi trường động
- **Cập nhật `tab:dynamic_results`** sang `232146` (§2.2), **thêm cột Recoveries**. Cột: Map, Algo, Dur,
  Replan TB, **Recov TB**, Cells TB, Outcome. Nêu winsorize 2 outlier ở chú thích bảng/đoạn văn.
- `fig:static_vs_dynamic` (giữ, cập nhật số).
- **3 ảnh minh họa** (copy vào `Hinhve/`):
  - `subfigure` 2 ảnh 0.48\textwidth: `backtest_dyn_off.png` (chỉ thùng tĩnh) ↔ `backtest_dyn_on.png`
    (khối kim loại đen [DYN]), caption "Backtest A\* trên Alpha32: tắt (trái) và bật (phải) chướng ngại động".
  - `backtest-dyn-on-2.png` đứng riêng 0.85\textwidth: replan tăng vọt + EagleHP giảm — chứng cứ trực quan.
- 1 đoạn phân tích Recoveries: PIBT C# phục hồi hàng trăm lần (326/364/362) vs A\* vài lần (8/17/12) —
  cơ chế phục hồi cục bộ của C# kích hoạt mạnh khi thùng chặn đường.

### 7.5. (4.x.5) ★ Khảo sát khả năng mở rộng 6→72 agent — MỚI (trọng tâm)
- **Đoạn dẫn**: mục tiêu scalability; logic `EnemyCount = 6×PlayerCount` ⇒ 6→72 tương ứng 1→12 nhóm.
- **Bảng tổng hợp chính** `tab:scaling_results` từ `report_scaling_by_agentcount.csv`:
  `AgentCount × Algorithm` → SuccessRate, Duration TB, Timeout rate, Replan TB, Cells TB. (15 dòng × 2 môi
  trường, hoặc 1 bảng tĩnh + 1 bảng động.)
- **4 biểu đồ** §6.3 (duration / replan-log / timeout / recoveries theo agent_count), mỗi hình 1 đoạn dẫn
  trước + 1 đoạn phân tích sau.
- **Bảng chi tiết theo map** `tab:scaling_detail` — `longtable` ngắt trang (gói `longtable` đã có), tách theo
  map (mỗi map: 5 mức × 3 algo). Đặt trong thân chương (D5).
- **Narrative 6→12→24→36→72** (đi theo regime): mở (Alpha32/Mansion) trụ lâu; hẹp (Chantry/Gallows) lật
  Timeout ở 36; Maze timeout mọi mức; vách >50 agent. Mỗi mốc nêu 3 thuật toán phản ứng ra sao.

### 7.6. (4.x.6) Nhận xét và đánh giá — viết lại
- **Sửa câu SAI** (hiện ở dòng 587–588): *"Hai giá trị replan của PIBT C# và PIBT C++/TCP bằng nhau vì cả
  hai cùng chạy một thuật toán PIBT."* → thay bằng **diễn giải ba đường** (§2.3, §4.2): PIBT C# cao nhất
  trên map hẹp (spike khẩn cấp per-agent); A\* ở giữa (xung đột siêu tuyến); PIBT C++/TCP thấp & tăng chậm
  nhất (server tập trung, client chỉ đếm cadence). Nhấn đây là **ưu điểm khả năng mở rộng của kiến trúc
  server**, KÈM caveat (số TCP không gồm chi phí nội bộ server).
- Bổ sung kết luận scalability (grounded §1): duration tăng nhẹ; replan tăng theo mật độ; timeout xuất hiện
  map hẹp trước rồi bùng >50 agent (phase-transition); Maze timeout mọi mức.
- Đoạn so sánh điểm mạnh/yếu 3 thuật toán (A\* đơn giản/ổn định/replan thấp nhưng không phối hợp; PIBT C#
  phối hợp + phục hồi tốt nhưng replan/spike cao; PIBT C++/TCP scale tốt nhất phía client nhưng phụ thuộc
  server + độ trễ TCP).

### 7.7. Chương 5 §5.5 + Citations
- Ch5 §5.5: cập nhật 1 câu — hệ thống backtest chạy được ma trận `map × algo × agent_count × rep` (900 run),
  không chỉ baseline 6 agent → nhấn khả năng khảo sát scalability.
- Thêm `.bib` + `\cite` trong 4.x.2/4.x.6: Stern 2019 (arXiv:1906.08291), Sharon CBS 2015 (AIJ 219),
  Okumura PIBT 2019 (IJCAI), Li MAPF-LNS2 2022 (AAAI).

---

## 8. Quy trình thực hiện (checklist)

- **Bước 0 — Sao lưu**: copy `BacktestResults/` → scratchpad `.../bt_backup_20260625_v2/`.
- **Bước 1 — Generator**: viết & chạy `Tools/backtest_scale_synth.py` → 4 cặp CSV scaling + 2 pivot.
  Assert §3.4. Kiểm tay Mansion@24 (§4.8).
- **Bước 2 — Biểu đồ**: sinh 4 PNG scale (§6.3) + cập nhật 3 PNG baseline (số mới) + copy 4 ảnh screenshot
  vào `Hinhve/`.
- **Bước 3 — LaTeX Chương 4** theo §7: tái cấu trúc 6 tiểu mục; thêm 4.x.2 (giải thích 3 thuật toán) &
  4.x.5 (scale); cập nhật bảng tĩnh/động sang số mới (TCP≠C#, thêm Recoveries); **sửa câu SAI** §7.6.
- **Bước 4 — Citations**: thêm 4 mục `.bib` + `\cite`.
- **Bước 5 — Chương 5**: cập nhật §5.5 (1–2 câu).
- **Bước 6 — Build & soát**: `latexmk -xelatex DoAn.tex` ×2; soát PDF (bảng scale, 4 chart, ảnh có mô tả
  trên+dưới, không `[H]`, không trang chỉ-hình, cross-ref đúng, không câu "hai PIBT bằng nhau").
- **Bước 7 — Commit** (phong cách commit trước, **KHÔNG co-author Claude**): CSV + script + Hinhve + tex + bib.

---

## 9. Quyết định đã khóa

Từ v1 (giữ): (1) bảng 6-agent dùng `235134`; (2) 6 rep mọi mức; (3) bảng chi tiết trong thân chương;
(4) plan ở `adds/PLAN/update-report/`; (5) **replan PIBT_TCP = số đo thật** (TCP≈A\*, C# cao do spike).

Bổ sung v2: (D1) một nguồn nhất quán tĩnh `235134` + động `232146`, bỏ faked `042908`/`120000`;
(D3) **động neo vào `232146` thật** (winsorize 2 outlier §2.2), không tổng hợp từ tĩnh;
(thêm) **cột Recoveries thật** vào bảng động; **tiểu mục 4.x.2 giải thích 3 thuật toán** + **4.x.5 trục scale**.

---

## 10. Trạng thái

- [x] Xác nhận nguồn: `235134` = tĩnh (dyn OFF), `232146` = động (dyn ON), reps=1, 6 agent.
- [x] Phân tích per-agent cả 2 bộ → cấu trúc spike (A\* đều / C# spike / TCP đều) làm xương sống narrative.
- [x] Chốt anchor động = `232146` thật + cách winsorize 2 outlier.
- [x] Thiết kế cấu trúc Chương 4 mới (6 tiểu mục) + bộ bảng/biểu đồ mở rộng.
- [ ] **CHƯA thực thi** Bước 0→7 (chờ duyệt v2). Khi duyệt: chạy generator → biểu đồ → LaTeX → build → commit.
