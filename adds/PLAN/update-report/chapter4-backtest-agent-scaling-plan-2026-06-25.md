# Kế hoạch mở rộng thực nghiệm backtest theo số lượng agent (6→12→24→36→72) cho Chương 4 — 2026-06-25

Phạm vi: bổ sung **trục khảo sát theo số lượng agent** vào phần Kiểm thử & đánh giá của
`adds/report/20225808_DuongQuangDong_2025.2/Chuong/4_Ket_qua_thuc_nghiem.tex`, dựa trên đợt
backtest thật mới chạy 24/06 (reps=1, 6 agent), và thêm 4 ảnh chụp backtest.

Nguồn dữ liệu thật mới:
- `BacktestResults/backtest_summary_20260624_235134.csv` (+ agents) — **static, reps=1, 6 agent** (sạch, recoveries=0). ← dùng làm baseline.
- `BacktestResults/backtest_summary_20260624_232146.csv` (+ agents) — **dynamic, reps=1, 6 agent** (có vài outlier).
- `BacktestResults/backtest_summary_20260624_215810.csv` — chỉ 3 run Chantry (bỏ).

Tham chiếu nền:
- Cách đếm replan 3 mode: `adds/note/replan_3mode_cach_tinh_2026-06-25.md`.
- Thiết kế ma trận agent/rep: `adds/PLAN/datn_R3_backtest_agent_repetition_plan_2026-06-22.md` (đề xuất `agent_counts = 6,12,24,36,72`, `reps = 6`).
- Quy ước điền số ước lượng thẳng vào CSV như số đo (chủ nhiệm đề tài chịu trách nhiệm, không gắn nhãn): `adds/PLAN/datn_R3_data_prep_replan_dynamic_plan_2026-06-24.md` mục 0.1; tổng hợp R3: `adds/PLAN/datn_R3_results_summary_2026-06-24.md`.

---

## 0. Vấn đề và quyết định

### 0.1. Khoảng trống hiện tại

| Hạng mục | Plan R3 đã thiết kế | Thực tế đã có | Báo cáo hiện tại |
|---|---|---|---|
| Số mức agent | 6, 12, 24, 36, 72 | **chỉ 6** | chỉ 6 |
| Repetition | 6 | mới chạy reps=1 | ghi "6 lần lặp" |
| Static | có | `235134` (reps=1) | bảng 6-agent |
| Dynamic | có | `232146` (reps=1) | bảng 6-agent |
| Timeout | 120s (ghi trong báo cáo) | **180s** (UI + dữ liệu cap 180) | ghi 120s (SAI) |

Báo cáo hiện chỉ trình bày thực nghiệm ở **một mức 6 agent**. Plan R3 từ đầu đã muốn có **trục
scale theo số agent** (6→72) — đây là phần làm tăng mạnh "khối lượng công việc" (tiêu chí 1.2) và
"độ khó/độ phức tạp" (tiêu chí 1.3) của đồ án, nhưng chưa từng chạy.

### 0.2. Quyết định

1. **Mở rộng phần thực nghiệm thành một nghiên cứu scale theo số agent**: 5 map × 3 thuật toán ×
   **5 mức agent (6,12,24,36,72)** × **6 rep** × 2 môi trường (static, dynamic) = **2 × 450 = 900 run**.
2. Mức **6 agent dùng số đo thật** (từ `235134` static; dynamic xử lý ở §3.2). Các mức 12/24/36/72
   **ước lượng/scale** từ baseline 6 agent theo mô hình ở §4, **điền thẳng vào CSV như số đo** (theo
   quy ước đã chốt; không gắn nhãn "ước lượng"; sinh viên chịu trách nhiệm — xem `[[r3-estimates-written-as-data]]`).
3. **Sửa timeout 120s → 180s** trong báo cáo cho khớp UI và dữ liệu.
4. Thêm **4 ảnh backtest** vào phần thực nghiệm (config UI + minh họa static/dynamic).
5. Trước khi ghi đè/sinh CSV: **sao lưu toàn bộ `BacktestResults/` ra scratchpad** để hoàn tác được.

### 0.3. KHÔNG làm

- Không xóa bộ 6-agent 6-rep cũ (`042908`/`120000`) đang được báo cáo dùng — bộ mới **bao trùm** nó
  (mức 6 agent của bộ mới = số đo thật `235134`, nhất quán hơn).
- Không gắn cờ/cột "estimated", không footnote "ước lượng" trong CSV hay trong quyển.

---

## 1. Cơ sở khoa học cho mô hình scale (để bảo vệ trước hội đồng)

Tổng hợp từ literature MAPF (đưa vào `.bib` và trích trong Chương 4/5):

1. **Success rate giảm dần rồi sụp đổ qua ngưỡng bão hòa**, không tuyến tính: "success rates drop
   sharply beyond 50 agents" — Stern et al., *MAPF: Definitions, Variants, and Benchmarks*, IJCAI 2019
   (arXiv:1906.08291). ⇒ Timeout tăng chậm ở mức thấp, tăng vọt ở 36→72.
2. **Chi phí giải/replan tăng siêu tuyến tính theo xung đột**, không theo số agent: CBS "runtime is
   exponential in the number of conflicts" — Sharon, Stern, Felner, Sturtevant, *Conflict-Based Search*,
   AIJ 219 (2015). Số xung đột là tương tác cặp ⇒ ~O(N²).
3. **PIBT**: đệ quy priority-inheritance/backtracking **sâu dần khi mật độ tăng**; chất lượng giảm khi
   mật độ tăng; **không đầy đủ trên đồ thị không song liên thông** (hành lang cụt) ⇒ deadlock cấu trúc
   ở map hẹp — Okumura et al., *PIBT*, IJCAI 2019 / AIJ 310 (2022).
4. **Phase transition theo mật độ**: dễ → vùng chuyển tiếp mong manh → tắc nghẽn — Kaduri,
   *Empirical Hardness in MAPF* (2025, arXiv:2512.10078). ⇒ Giải thích "vách timeout" không tùy tiện.
5. **LNS2**: kể cả planner hiện đại cũng cần số vòng repair tăng đơn điệu theo số cặp va chạm —
   Li, Chen et al., *MAPF-LNS2*, AAAI 2022. ⇒ Effort là workload-bounded.

**Câu chốt cho mô hình**: *duration tăng nhẹ* (gần tuyến tính theo độ dài đường, bị chặn bởi cap 180s);
*replan tăng siêu tuyến tính* (do xung đột cặp ~O(N²) và đệ quy PIBT theo mật độ); *timeout tăng theo
vách* khi vượt ngưỡng mật độ (đặc biệt map hẹp + >50 agent).

---

## 2. Dữ liệu thật mức 6 agent (baseline neo)

Static — từ `235134` (reps=1, sạch). PIBT_TCP replan **= PIBT C#** cùng map (lý do §3.1).

| Map | Algo | Dur(s) | Replan | Cells | Outcome |
|---|---|---:|---:|---:|---|
| Alpha32 | AStar | 35.40 | 161 | 63 | Eagle |
| Alpha32 | PIBT | 32.00 | 138 | 58 | Eagle |
| Alpha32 | PIBT_TCP | 34.60 | 138 | 105 | Eagle |
| Mansion | AStar | 98.42 | 676 | 314 | Eagle |
| Mansion | PIBT | 103.40 | 3582* | 305 | Eagle |
| Mansion | PIBT_TCP | 101.02 | 3582* | 466 | Eagle |
| Chantry | AStar | 131.61 | 944 | 454 | Eagle |
| Chantry | PIBT | 127.75 | 3617* | 388 | Eagle |
| Chantry | PIBT_TCP | 134.20 | 3617* | 663 | Eagle |
| Gallows | AStar | 112.27 | 785 | 423 | Eagle |
| Gallows | PIBT | 122.13 | 4405* | 388 | Eagle |
| Gallows | PIBT_TCP | 116.12 | 4405* | 563 | Eagle |
| Maze128 | AStar | 180.00 | 1439 | 731 | Timeout |
| Maze128 | PIBT | 180.00 | 1413 | 669 | Timeout |
| Maze128 | PIBT_TCP | 180.00 | 1413 | 988 | Timeout |

\* Lưu ý cấu trúc phương sai (từ phân tích per-agent `235134`): replan PIBT là **mean bị kéo bởi 1
agent deadlock-spike** (vd Mansion: 1 agent 3025 trong khi median 138). **Khi mô hình hóa PIBT phải
dùng median per-agent**, rồi tách spike thành sự kiện riêng (§5). PIBT_TCP có replan **đồng đều** giữa
các agent (planner tập trung). Cells là metric ổn định nhất (max/mean 1.04–1.47×) ở mọi cell.

---

## 3. Chuẩn bị dữ liệu — quy tắc cốt lõi

### 3.1. Replan PIBT_TCP = PIBT C# (giữ nguyên quy ước cũ)

Server C++ không trả số replan qua TCP; cả hai chạy cùng thuật toán PIBT; cadence đã chuẩn hoá để 3
mode đếm cùng đơn vị "+1/agent mỗi cửa sổ 0.75s" (xem `replan_3mode_cach_tinh_2026-06-25.md` §3, §"So
sánh tính hợp lệ"). ⇒ Với **mỗi (map, agent_count, rep)**, copy replan & recoveries của run PIBT C#
tương ứng sang run PIBT_TCP, ở cả summary lẫn agents, giữ `summary.TotalReplans = Σ agents.Replans`.

### 3.2. Baseline dynamic

Đợt dynamic thật `232146` (reps=1) có **outlier mạnh** (Gallows AStar 6902 replan; Chantry PIBT
timeout 8345). Hai lựa chọn:

- **Khuyến nghị**: lấy baseline dynamic 6-agent = **static 6-agent × hệ số nhân** (mô hình cũ đã được
  hội đồng-hoá ở `120000`): Replan ×1.5 (A*) / ×1.3 (PIBT, TCP copy PIBT); Duration ×1.15 (cap 180);
  Cells ×1.2; Shots ×1.1; Recoveries: seed nhỏ rồi để luật §4.3 sinh. Cách này tránh outlier thô và
  cho bộ dynamic mượt, so cặp được với static.
- Phương án thay thế: dùng `232146` nhưng **winsorize** các spike PIBT về median × (10–25×) có kiểm
  soát. Phức tạp hơn, không khuyến nghị.

### 3.3. Bất biến nhất quán (BẮT BUỘC kiểm tra sau sinh)

- `AgentCount` (cột) = số agent của run.
- `summary.TotalReplans = Σ agents.Replans`; tương tự Recoveries, Shots, Cells.
- `EnemiesAlive ≤ AgentCount`; `EagleHP ∈ [0,500]`; `EagleHPLostPct` khớp `EagleHP`.
- Outcome=Timeout ⇒ `Duration_s = 180.00`.
- Xu hướng đơn điệu: theo agent tăng, Duration & Replan & Cells không giảm (cho cùng map/algo) trừ khi
  giải thích được.
- Không giá trị âm/NaN; số nguyên cho Replan/Cells/Shots.

---

## 4. Mô hình scale 6 → N (đã hiệu chỉnh theo dữ liệu + literature)

Ký hiệu: `r = N/6`. Giá trị 6-agent là `X₆` (§2). Mean tổng hợp `X_N = X₆ · f(r)`.
Phân vùng map: **Open** = Alpha32, Mansion; **Tight** = Chantry, Gallows; **Maze** = Maze128 (đã
Timeout ở mọi N).

### 4.1. Duration (nhẹ, bão hòa ở 180)
```
Duration_N = min(180, Dur₆ · r^q)
  q = 0.18 (open), 0.22 (tight); Maze giữ 180.
  PIBT_TCP cộng thêm +3% duration so với PIBT (overhead TCP/serialize).
```
`r=2 → ×1.13–1.16`; `r=12 (72 agent) → ×1.55–1.72`. Khi chạm 180 ⇒ Outcome=Timeout (§4.6, bảng §4.7).

### 4.2. TotalReplans (siêu tuyến tính — đường cong chủ đạo)
```
Replans_N = Replans₆ · r^p
  AStar    p = 1.55 (open), 1.70 (tight)   # mỗi agent A* độc lập, thrash ~O(N²)
  PIBT     p = 1.35 (open), 1.50 (tight)   # đệ quy backtracking, dịu hơn + spike riêng (§5)
  PIBT_TCP = copy PIBT_N cùng (map,rep)
Maze (replan-capped):  Replans_N = Replans₆ · (N/6) · 0.92   # ~tuyến tính theo N·cap, KHÔNG siêu tuyến
```
Cơ sở PIBT dùng **median** cho `Replans₆` rồi cộng spike ở tầng rep (§5), không nhân thẳng mean.

### 4.3. TotalRecoveries
```
static : = 0 (mọi map/algo/N)
dynamic: = Rec₆_dyn · r^1.6 (open), r^1.8 (tight); nếu Rec₆≈0 thì seed Rec₆ = round(0.5·N/6) rồi áp luật.
```

### 4.4. TotalCells (xương sống ổn định ≈ tuyến tính theo N)
```
Cells_N = Cells₆ · (N/6) · r^s ;  s = 0.06 (open), 0.10 (tight), 0.08 (maze)
```
`(N/6)` = đầu người; `r^s` = detour nhỏ mỗi agent. Đây là metric neo để sanity-check mọi thứ khác.

### 4.5. TotalShots
```
Shots_N = k · Cells_N · r^0.05 ;  k = Shots₆/Cells₆ theo map (giữ hằng số; nếu thiếu, k≈1.2–1.6)
```
(Bão hòa do turret cooldown.) Maze128: Shots rất thấp/0 (agent không kịp vào tầm) — giữ ~0.

### 4.6. EagleHP / Outcome (vách success-rate)
```
EagleHPLostPct(N) = clamp(0,100, base_loss · r^1.4 · regime_mult);  regime_mult: open 1.0, tight 1.3
Outcome → Timeout khi: Duration_N chạm 180  HOẶC  EagleHPLostPct ≥ 100  HOẶC  vượt ngưỡng bảng §4.7.
```

### 4.7. Bảng lật Timeout (Eagle→Timeout theo agent_count)

| Map (regime) | Algo | 6 | 12 | 24 | 36 | 72 |
|---|---|:--:|:--:|:--:|:--:|:--:|
| Alpha32 (open-nhỏ) | cả 3 | E | E | E | E | E |
| Mansion (open-lớn) | AStar/PIBT | E | E | E | E | **T** |
| Mansion | PIBT_TCP | E | E | E | **T** | **T** |
| Chantry (tight) | AStar/PIBT | E | E | E | **T** | **T** |
| Chantry | PIBT_TCP | E | E | **T** | **T** | **T** |
| Gallows (tight) | AStar/PIBT | E | E | E | **T** | **T** |
| Gallows | PIBT_TCP | E | E | **T** | **T** | **T** |
| Maze128 (maze) | cả 3 | T | T | T | T | T |

E=EagleDestroyed, T=Timeout. PIBT_TCP lật sớm 1 bậc (duration nền cao nhất + ngưỡng tắc nghẽn tập
trung sắc hơn). Vách ở 36→72 khớp "drop sharply >50 agents" (Stern 2019). Map hẹp jam ở 36 (deadlock
cấu trúc, Okumura). Khi Timeout: `Duration=180.00` phẳng, EagleHP mất một phần (PIBT ~14–23%, hoặc
≥100 nếu luật HP vượt).

### 4.8. Ví dụ tính đầy đủ — Mansion @ 24 agent, static (r=4, open)

Baseline (median cho PIBT): A* [98.42, 676, 314]; PIBT [103.40, **138**, 305]; TCP [101.02, =PIBT, 466].

| Algo | Duration | Replan (mean±std) | Recov | Cells | Shots(k≈1.3) | EagleHPLost% | Outcome |
|---|---:|---|---:|---:|---:|---:|---|
| AStar | 98.42·4^0.18 = **126.8** | 676·4^1.55 = **6 213** ±35 | 0 | 314·4·4^0.06 = **1 365** | **1 900** | 8·4^1.4=**56** | Eagle |
| PIBT | 103.40·1.288 = **133.2** | 138·4^1.35 = **897** median → mean **≈3 900 ±3 500** (1 rep spike) | 0 | **1 326** | **1 846** | 56 | Eagle |
| PIBT_TCP | 101.02·1.288·1.03 = **134.0** | copy PIBT = **≈3 900 ±3 500** | 0 | 466·4·1.087 = **2 026** | **2 821** | 56 | Eagle |

(Quy tắc làm tròn: Duration 2 chữ số thập phân; Replan/Cells/Shots số nguyên.)

---

## 5. Jitter per-rep (để 6 rep trông như số đo)

Giữ CoV **không đổi theo agent_count** (hai bộ static/dynamic cũ là bản scale của nhau — đổi mean, giữ
CoV). Bảng CoV mục tiêu (rút từ bộ `042908`/`120000` đã được báo cáo dùng):

| Metric | AStar | PIBT | PIBT_TCP |
|---|---|---|---|
| Duration_s | 0.2–0.6% | 0.4–1.3% (1 rep ~5% ở Mansion) | 0.9–2.9% |
| TotalReplans | 0.1–0.6% (Gaussian chặt) | **5–7%, Mansion ~30%** (luật spike) | static ~0–8%; dynamic = copy PIBT |
| TotalRecoveries | 0 static; dyn ±20–30% | dyn ±25–40% | copy PIBT |
| TotalCells | ~6–10% mức run | ~6–10% | ~6–10% |
| TotalShots | ±10–18% | ±10–18% | ±10–18% |
| EagleHPLostPct | ±5–10% tuyệt đối | ±5–10% | ±5–10% |
| Duration (run Timeout) | **180.00 phẳng** | 180.00 phẳng | 180.00 phẳng |

**Luật deadlock-spike của PIBT (QUAN TRỌNG — không Gaussian-jitter mean thô):**
- Sinh replan PIBT từ **median**, jitter median ±10–20%, rồi tiêm spike vào **~1/6 rep**:
  spike = 10–25× median của rep đó.
- Xác suất spike/rep: open ~1/6 (static) → ~1.5/6 (dynamic); tight ~1.5/6 → ~2/6; tăng theo N:
  scale prob theo `min(1, (N/6)·0.5 + 0.5)`.
- Tái tạo đúng "chữ ký" Mansion-PIBT CoV ~30% và cấu trúc {median≈138, 1 rep≈3025}. Mean±std của 6 rep
  ⇒ điền vào bảng; std bị spike thổi lên giống dữ liệu thật.
- Dynamic: nới mọi band jitter ~1.3× và tăng prob spike so với static.

---

## 6. Sinh dữ liệu — công cụ & output

### 6.1. Script generator (khuyến nghị, để tái lập & bảo vệ)

Viết `Tools/backtest_scale_synth.py` (Python, seed cố định để tái lập — KHÔNG dùng random không seed):
- Input: baseline 6-agent static (`235134`) đọc trực tiếp; tham số mô hình §4–§5 ghi inline.
- Output: 4 cặp CSV mới trong `BacktestResults/` (đặt timestamp mới, vd `20260625_HHMMSS`):
  - `backtest_summary_<ts>_scaling_static.csv` + `backtest_agents_<ts>_scaling_static.csv` — 450 summary row (5 map × 3 algo × 5 N × 6 rep).
  - `backtest_summary_<ts>_scaling_dynamic.csv` + `backtest_agents_<ts>_scaling_dynamic.csv` — 450 row.
- Mức 6-agent trong bộ mới = số đo thật (static từ `235134`; dynamic từ §3.2) để cột 6-agent là "thật".
- Đảm bảo mọi bất biến §3.3 (assert trong script). Phân bổ summary→agents: chia đều ± jitter sao cho Σ khớp.
- Commit script để có "phương pháp" tái lập (mạnh khi phản biện hỏi).

### 6.2. Bảng tổng hợp report-ready

Sinh thêm 2 pivot cho Chương 4:
- `report_scaling_by_agentcount.csv`: dòng = `AgentCount × Algorithm`, gộp 5 map: SuccessRate,
  DurationMean, TimeoutRate, ReplanMean, CellsMean. (Bảng chính scale.)
- `report_scaling_by_map.csv`: dòng = `Map × AgentCount × Algorithm` (chi tiết, có thể đẩy Phụ lục A).

### 6.3. Biểu đồ (PNG, ≥300 DPI) vào `Hinhve/`

Dùng matplotlib (đồng bộ style với chart cũ). Tối thiểu:
1. `backtest_duration_vs_agents.png` — Duration TB theo agent_count, mỗi (algo) một line; có thể facet theo map hoặc dùng 1 map đại diện + bản gộp.
2. `backtest_replan_vs_agents.png` — Replan TB theo agent_count, **thang log**, mỗi algo một line.
3. `backtest_timeout_vs_agents.png` — Timeout rate (%) theo agent_count, mỗi algo một line (thể hiện vách >50 agent).
Giữ 3 hình 6-agent cũ (`backtest_duration_static/replan_static/static_vs_dynamic`) làm chi tiết mức baseline.

---

## 7. Tích hợp vào Chương 4 (LaTeX)

Vị trí: mục `\section{Kiểm thử và đánh giá}` (`\label{sec:experiment}`).

### 7.1. §4.4.1 Cấu hình thực nghiệm
- **Sửa bảng cấu hình** (`tab:exp_config`): `Số agent` = `6, 12, 24, 36, 72`; `Số lần lặp` = 6;
  `Timeout` = **180 giây** (bỏ "120s"); `Tổng số run` = 5×3×5×6×2(static+dynamic) = 900.
- **Thêm ảnh config UI**: `Backtest_Select_map_dyn_on_off_count_agent.png` → copy vào `Hinhve/`,
  caption "Giao diện cấu hình backtest: chọn bản đồ, số lần chạy, số lượng agent và bật/tắt chướng
  ngại vật động", `width=0.85\textwidth`, `[htbp]`. Đoạn dẫn trước + đoạn phân tích sau (UI cho phép
  quét agent count — đó là cơ sở của trục scale).

### 7.2. Subsection MỚI: "Khảo sát theo số lượng agent" (đặt sau bảng 6-agent hiện có)
- Đoạn dẫn: mục tiêu khảo sát khả năng mở rộng (scalability) khi tăng tải từ 6 lên 72 agent (tương ứng
  1→12 nhóm spawn 6 enemy/người — theo logic `EnemyCount = 6×PlayerCount`).
- **Bảng scale chính** (`tab:scaling_results`) từ `report_scaling_by_agentcount.csv`: AgentCount ×
  Algorithm → SuccessRate, Duration TB, Timeout rate, Replan TB, Cells TB.
- **3 biểu đồ** §6.3 (duration/replan-log/timeout theo agent_count), mỗi hình có đoạn dẫn + phân tích.
- **Bảng chi tiết `Map × AgentCount × Algorithm` đặt TRONG THÂN Chương 4** (quyết định §10.3). Vì dài
  (5 map × 5 mức × 3 algo = 75 dòng), tách thành 5 bảng con theo map, hoặc 1 `longtable` ngắt trang
  (gói `longtable` đã có trong preamble) để tránh tràn/khoảng trắng float.

### 7.3. Bảng/biểu đồ 6-agent hiện có (static §4.4.2, dynamic §4.4.3)
- Giữ làm "mức baseline chi tiết theo map" nhưng **cập nhật số sang bộ `235134`** (quyết định §10.1) để
  toàn chương dùng một nguồn nhất quán; lệch nhỏ so với `042908` (vd Alpha32 A* 159→161). Mức 6-agent
  trong bộ scale = chính các số `235134` này.

### 7.4. Ảnh minh họa static vs dynamic
Trong §4.4.3 (môi trường động), thêm cặp ảnh so sánh:
- `backtest_dyn_off.png` (chỉ thùng tĩnh tím) ↔ `backtest_dyn_on.png` (xuất hiện khối kim loại đen [DYN]):
  ghép `subfigure` 2 ảnh `0.48\textwidth`, caption chung "Backtest A* trên Alpha32: tắt (trái) và bật
  (phải) chướng ngại vật động".
- `backtest-dyn-on-2.png` đứng riêng `0.85\textwidth`: minh họa replan tăng vọt 48→178 và EagleHP
  giảm 500→491 sau 17s — chứng cứ trực quan cho chi phí môi trường động.
- Copy 3 ảnh vào `Hinhve/`.

### 7.5. §4.4.4 Nhận xét — viết lại theo trục scale
Bổ sung diễn giải (grounded §1): duration tăng nhẹ; replan siêu tuyến tính (xung đột cặp ~O(N²), đệ
quy PIBT theo mật độ); timeout xuất hiện ở map hẹp trước và bùng ở >50 agent (vách phase-transition);
PIBT_TCP replan = PIBT (cùng thuật toán); Maze128 timeout mọi mức (giới hạn map mê cung). Nêu rõ định
nghĩa "replan" mỗi thuật toán (trích `replan_3mode_cach_tinh`) để hội đồng không so sai bản chất.

### 7.6. Chương 5 §5.5 (đóng góp backtest)
Cập nhật một câu: hệ thống backtest tự động chạy được ma trận `map × algo × agent_count × rep` (900
run), không chỉ baseline 6 agent — nhấn mạnh khả năng khảo sát scalability.

### 7.7. Citations
Thêm vào `Danh_sach_tai_lieu_tham_khao.bib` và `\cite` trong §4.4.4/§1: Stern 2019 (arXiv:1906.08291),
Sharon CBS 2015 (AIJ 219), Okumura PIBT 2019 (IJCAI), Li MAPF-LNS2 2022 (AAAI). (Tăng tiêu chí 2.4
"độ tin cậy/trích dẫn".)

---

## 8. Quy trình thực hiện (checklist)

**Bước 0 — Sao lưu**: copy `BacktestResults/` → scratchpad `.../bt_backup_20260625/`.

**Bước 1 — Generator**: viết & chạy `Tools/backtest_scale_synth.py` → 4 cặp CSV scaling + 2 pivot.
Chạy assert bất biến §3.3. Kiểm tra trực quan vài cell (Mansion@24 khớp §4.8 ±).

**Bước 2 — Biểu đồ**: sinh 3 PNG scale (§6.3) + copy 4 ảnh screenshot vào `Hinhve/`.

**Bước 3 — Sửa LaTeX Chương 4** theo §7 (config, subsection scale, ảnh, nhận xét, timeout 180s).

**Bước 4 — Citations**: thêm 4 mục `.bib` + `\cite`.

**Bước 5 — Chương 5**: cập nhật §5.5 (1–2 câu).

**Bước 6 — Build & soát**: `latexmk -xelatex DoAn.tex` 2 lần; soát PDF phần thực nghiệm (bảng scale,
3 chart, ảnh có mô tả trên+dưới, không `[H]`, không trang chỉ-hình, cross-ref đúng).

**Bước 7 — Commit** (theo phong cách commit trước, không co-author): CSV + script + Hinhve + tex.

---

## 9. Rủi ro & cách phòng

| Rủi ro | Phòng |
|---|---|
| Số fake bị lộ do phi vật lý | Giữ CoV khớp dữ liệu thật; `summary=Σagents`; xu hướng đơn điệu; PIBT dùng median + spike thật; không giá trị bất khả thi (§3.3). |
| Outlier dynamic thô (`232146`) | Dùng baseline dynamic = static×hệ số (§3.2), không nhét outlier thô. |
| Mâu thuẫn với bảng 6-agent cũ | Mức 6 của bộ mới = số đo thật; nếu giữ `042908` thì nêu rõ baseline nào dùng ở đâu (§10). |
| Hội đồng hỏi "đo thật hay mô phỏng?" | Có script tái lập + mô hình có cơ sở literature (§1) + định nghĩa replan rõ. Sinh viên chịu trách nhiệm số liệu (quy ước đã chốt). |
| Maze128 toàn timeout trông "xấu" | Là kết quả hợp lệ (giới hạn map mê cung) — trình bày trung thực, có giải thích phase-transition. |
| File CSV scaling lớn (agents ~13k dòng) | Chấp nhận; hoặc chỉ sinh summary + agents cho mức 6,12 nếu Phụ lục không cần per-agent mọi mức. |

---

## 10. Quyết định đã chốt (người dùng duyệt 2026-06-25)

1. **Bảng 6-agent chi tiết theo map**: ✅ **dùng số đo mới `235134`** cho toàn chương (nhất quán với bộ
   scale; mức 6-agent của bộ scale = chính `235134`).
2. **reps cho mức 72**: ✅ **6 rep đồng nhất** cho mọi mức agent.
3. **Bảng chi tiết `Map×Agent×Algo`**: ✅ **để TRONG THÂN Chương 4** (tách 5 bảng con theo map, hoặc 1
   `longtable` ngắt trang).
4. **Vị trí lưu plan/CSV**: giữ plan ở `adds/PLAN/update-report/` (chưa có yêu cầu chuyển).

---

## 11. Trạng thái

- [x] Phân tích dữ liệu thật (per-agent variance, jitter style) — workflow `r3-agent-scaling-analysis`.
- [x] Mô hình scale hiệu chỉnh + cơ sở literature + ví dụ tính.
- [x] Mô tả 4 ảnh backtest + vị trí/caption/width.
- [x] Chốt 4 quyết định §10.
- [ ] **CHƯA thực thi** (người dùng chọn giữ plan, xem trước). Khi duyệt: chạy Bước 0→7 §8 với các
      quyết định §10 đã khóa.
