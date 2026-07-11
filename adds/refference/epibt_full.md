# Tóm tắt cải tiến PIBT/EPIBT và pseudocode các giải thuật

**Nguồn chính:** *Enhancing PIBT via Multi-Action Operations* — Egor Yukhnevich, Anton Andreychuk, arXiv:2511.09193v2. Extended version của bài được chấp nhận tại AAAI-26.

Tài liệu này tổng hợp toàn bộ nội dung quan trọng của bài báo về **Enhanced PIBT (EPIBT)**, bao gồm: phân tích vấn đề, ba cải tiến lõi, pseudocode đầy đủ, phân tích lý thuyết, và kết quả thực nghiệm trên cả hai setting LMAPF-T và LMAPF.

---

## 1. Bối cảnh bài toán

### 1.1. Định nghĩa bài toán: online LMAPF-T

Bài toán được xét là **online Lifelong MAPF with Time budget and rotation action model (online LMAPF-T)**:

- Môi trường là lưới 4-neighbor. Tập ô đi được ký hiệu là `V ⊆ ℤ²`, tập cạnh `E` gồm các cặp ô kề nhau đều đi được.
- Mỗi agent `k` có **state** gồm vị trí `(i, j)` và **hướng** `o ∈ {north, east, south, west}`: `s_k = ((i, j), o)`.
- Agent chỉ biết goal hiện tại `g_k`; khi tới goal thì được gán goal mới ngay lập tức bởi một module bên ngoài.
- Mỗi timestep, agent chọn một trong bốn action:
  - `F`: di chuyển về phía trước
  - `R`: quay phải 90°
  - `C`: quay trái 90°
  - `W`: đứng yên
- Va chạm gồm hai loại:
  - **Vertex conflict:** hai agent cùng chiếm một ô tại cùng timestep.
  - **Edge conflict:** hai agent đi ngược chiều qua cùng cạnh trong cùng timestep.
- **Mục tiêu:** tối đa hóa **throughput** — số goal hoàn thành trung bình trên mỗi timestep trong giới hạn `T` timestep.
- **Time budget:** solver phải trả về action của tất cả agent trong ngân sách thời gian nghiêm ngặt, trong bài báo là **1 giây/timestep**. Nếu vượt, tất cả agent bị delay 1 timestep cho mỗi giây bị dư. Ngân sách **không tích lũy** qua các bước.

### 1.2. Kết nối với League of Robot Runners (LoRR)

Bài toán được xét **đồng nhất với bài toán của cuộc thi League of Robot Runners (LoRR)** — được tài trợ bởi Amazon Robotics, kết nối nghiên cứu nền tảng với ứng dụng công nghiệp như kho hàng tự động, logistics và sản xuất tiên tiến.

Các phiên bản cạnh tranh của LoRR:

| Phiên bản | Người thắng | Phương pháp |
|-----------|-------------|-------------|
| LoRR23 | WPPL (Jiang et al. 2024) | Windowed PIBT + LNS + Graph Guidance |
| LoRR24 | LoRR24-Winner | EPIBT variant (xem mục 12.3) |

Bài báo không xét phần gán task; giả định task assignment do module bên ngoài xử lý.

---

## 2. Vấn đề của PIBT gốc

**PIBT (Priority Inheritance with Backtracking)** là solver rule-based, nổi bật vì tốc độ xử lý hàng nghìn agent trong vài millisecond.

**Cơ chế PIBT gốc:**

1. Gán priority cho agent dựa trên khoảng cách tới goal.
2. Agent có priority cao chọn action trước.
3. Nếu agent priority cao muốn đi vào ô đang bị agent `l` chiếm, agent `l` **inherit priority** và bị đẩy ra.
4. Nếu chuỗi đẩy thất bại, dùng backtracking để thử action khác.

**Điều kiện đảm bảo tính đúng đắn:** với mọi cặp node kề nhau trong graph, tồn tại một simple cycle `C` chứa cả hai node đó với `|C| ≥ 3`. Điều này đảm bảo agent priority cao luôn có thể đẩy agent khác ra khỏi ô mong muốn.

**Giới hạn khi dùng với rotation action model:**

PIBT gốc giả định rằng tại mỗi timestep, agent có priority cao nhất luôn có thể tiến gần goal hơn vì agent đang chặn có thể rời ô ngay bước kế tiếp. Giả định này **không còn đúng** trong rotation model:

- Agent đang chặn có thể phải quay 1–2 lần trước khi rời ô.
- PIBT 1-step không thấy trước được các bước quay này → dễ bị kẹt, throughput thấp.

---

## 3. Các solver liên quan: Causal PIBT và winPIBT

Trước EPIBT, có hai hướng tiếp cận đã được thử để xử lý vấn đề trên.

### 3.1. Causal PIBT

**Causal PIBT** (Okumura, Tamura, Défago 2021) xét quan hệ nhân quả giữa action của các agent. Ban đầu được thiết kế cho **môi trường stochastic** — nơi mỗi action có thể bị trì hoãn với một xác suất nào đó. Trong bối cảnh rotation model, các action quay được diễn giải là **trì hoãn có tính xác định** (rotation luôn tốn đúng 1 timestep).

Hạn chế: xử lý delay theo sự kiện (event-based) vẫn bị giới hạn bởi tầm nhìn 1 timestep.

### 3.2. winPIBT

**winPIBT** (Okumura, Tamura, Défago 2019) mở rộng PIBT bằng cách lập kế hoạch `w` bước phía trước. Thay vì chọn 1 action tiếp theo, mỗi agent lập một **path** cho `w` bước bằng A*.

| | winPIBT | EPIBT |
|---|---|---|
| Window size | Lên đến 30 bước | Tối đa 5 bước |
| Cơ chế lập kế hoạch | Chạy A* cho từng agent | Dùng multi-action operations ngắn, không chạy path planning riêng |
| Mục tiêu ban đầu | Classical MAPF — giải quyết blockage ở goal | Online LMAPF-T — rotation action model |
| Hiệu quả thực tế | Vượt time budget (>1s) trên map lớn | Giữ được tốc độ của PIBT |

**Kết quả thực nghiệm (Figure 6 trong bài báo):** winPIBT hoạt động tốt trên `random-32-32-20` nhỏ, nhưng **vượt time budget 1 giây** trên tất cả các map lớn hơn → không khả thi cho online LMAPF.

---

## 4. Enhanced PIBT: ba cải tiến lõi

Bài báo đề xuất **EPIBT**, gồm ba cải tiến lõi tích hợp vào PIBT:

| # | Cải tiến | Ý tưởng | Tác dụng |
|---|---|---|---|
| 1 | **Multi-action Operations (MAO)** | Thay vì chọn 1 action, agent chọn một chuỗi action ngắn độ dài `op_len` | Nhìn trước vài timestep mà không cần path planning nặng như winPIBT |
| 2 | **Revisiting Agents** | Cho phép agent đã chọn operation được xét lại trong cùng timestep, tối đa `L` lần | Giải quyết collision phức tạp hơn, tránh agent bị khóa bởi lựa chọn ban đầu |
| 3 | **Inheritance of Operations (IO)** | Kế thừa operation từ timestep trước: bỏ action đầu, thêm `W` cuối | Giữ plan ngắn đang còn hợp lệ, giảm collision và tránh fallback về wait |

Ngoài EPIBT lõi, bài báo còn kết hợp thêm:

- **Large Neighborhood Search (LNS):** tối ưu lại operation trong phần thời gian còn lại của time budget.
- **Graph Guidance (GG):** điều chỉnh trọng số cạnh để điều hướng luồng agent, giảm tắc nghẽn ở hành lang hẹp.

---

## 5. Multi-action Operations (MAO)

### 5.1. Operation là gì?

Một **operation** là chuỗi action có độ dài cố định `op_len`:

```
op = [a_1, a_2, ..., a_op_len],  a_i ∈ {F, R, C, W}
```

Ví dụ với `op_len = 3`:

```
FWW  = tiến lên, chờ, chờ         (đến ô kề trong 1 bước, chờ 2 bước)
RFW  = quay phải, tiến lên, chờ   (đến ô bên phải trong 2 bước)
CFW  = quay trái, tiến lên, chờ   (đến ô bên trái trong 2 bước)
RRF  = quay 180°, tiến lên        (đến ô phía sau trong 3 bước)
WWW  = chờ 3 bước
```

Với `op_len = 3`, agent có thể đến bất kỳ ô kề nào bất kể hướng ban đầu — do đó **đây là độ dài tối thiểu đủ cho rotation model**.

Bảng số lượng từ bài báo (Table 1):

| Operation length | Reachable cells | Reachable states | Unique cell sequences |
|---:|---:|---:|---:|
| 1 | 2 | 4 | 2 |
| 2 | 5 | 10 | 6 |
| 3 | 11 | 23 | 17 |
| 4 | 21 | 48 | 48 |
| 5 | 35 | 88 | 136 |

### 5.2. Giảm số lượng operation cần xét

Tối đa có `4^op_len` chuỗi action, nhưng EPIBT rút gọn đáng kể:

1. **Loại bỏ rotation dư thừa:** ví dụ `RRC` ≡ `L` (quay trái 1 lần).
2. **Không xét operation kết thúc bằng rotation:** rotation cuối không đổi vị trí. Thay vào đó, các operation như `FFR`, `FFC`, `FFW` được **gộp thành `FFW` duy nhất**, với h-value được tính theo **heading tốt nhất trong số các heading có thể đạt được**.
3. **Phân biệt theo chuỗi ô chiếm giữ (cell sequence):** do có agent khác trong không gian, cần xét chiều thời gian — `W` giúp agent đến cùng một state **muộn hơn** để né collision. Số lượng operation thực sự cần xét = số unique cell sequences (cột cuối Table 1).

### 5.3. Thứ tự xét operation

EPIBT sắp xếp operation theo trọng số:

```
w_op = h(s_k, op, g_k) * α + β_op
```

Trong đó:
- `h(s_k, op, g_k)`: khoảng cách heuristic từ state sau khi thực hiện `op` đến goal `g_k`.
- `α`: hệ số rất lớn, đảm bảo `h` là tiêu chí chính.
- `β_op`: tie-breaker khi nhiều operation có cùng `h`.

**Tie-breaking tốt nhất theo bài báo:** ưu tiên `Forward > Rotation > Wait`, tức là:

```
score(F) = 0,  score(R/C) = 1,  score(W) = 2
β_op = lexicographic score của chuỗi action trong op
```

Lý do: nếu goal ở ô kề và agent chọn `WFW` hoặc `WWF` thay vì `FWW`, agent sẽ đứng yên và bước tiếp theo có thể lại chọn wait → **tự deadlock ngay cả khi không có agent nào khác**. Phải ép agent di chuyển trước.

---

## 6. Revisiting Agents

### 6.1. Hai thách thức từ MAO

Thêm MAO tạo ra **hai thách thức mới**, cả hai đều dẫn đến cơ chế revisiting:

**Thách thức 1 — Multi-agent collision:**
Operation nhiều bước có thể gây collision với **nhiều hơn 1 agent** cùng lúc. Khi đó, priority inheritance tạo ra ambiguity vì tất cả các agent va chạm đều inherit cùng một priority và ảnh hưởng lẫn nhau. Giải pháp: **bỏ qua bất kỳ operation nào gây collision với hơn 1 agent** (line 10 Algorithm 2 trong paper).

**Thách thức 2 — Đa dạng loại collision:**
Trong PIBT gốc, mỗi agent chỉ được xét 1 lần và action đã chọn không thể thay đổi → giới hạn khả năng giải quyết collision. EPIBT cho phép agent được **revisit** — tức là được gọi lại `EPIBT_SELECT_OPERATION` thêm lần nữa để thử operation khác — giúp giải quyết tốt hơn các tình huống collision phức tạp.

### 6.2. Revisit limit L

Revisit không giới hạn có thể làm runtime tăng mạnh và vượt time budget ở scenario mật độ cao. Vì vậy EPIBT giới thiệu **giới hạn `L`** — số lần tối đa mỗi agent được revisit trong một timestep.

**Kết quả ablation (Appendix):**
- `L = 1, 2, 4`: underperform rõ rệt.
- `L ≥ 8`: kết quả rất tương đương nhau — không cần tăng `L` quá cao.
- Khuyến nghị: **`L = 10`** là giá trị cân bằng tốt giữa hiệu năng và tốc độ.

---

## 7. Inheritance of Operations (IO)

Mỗi timestep, EPIBT chạy lại từ đầu. Nếu không có inheritance, mọi agent bắt đầu với operation mặc định `WWW` (chờ toàn bộ) → nhiều collision ban đầu, thuật toán phải giải quyết nhiều hơn.

**Với IO:**
- Operation từ timestep trước đã được thiết kế collision-free cho `op_len` bước.
- Sau khi thực thi action đầu tiên, phần còn lại vẫn là plan hợp lệ.
- Kế thừa bằng cách: **bỏ action đầu, thêm `W` ở cuối** → giữ nguyên độ dài `op_len`.

**Lưu ý quan trọng:**
- IO chỉ dùng để **khởi tạo** — agent vẫn tự do chọn bất kỳ operation nào tốt hơn.
- Nếu agent không tìm được operation tốt hơn ở bước hiện tại, agent **dùng inherited operation thay vì WWW** — đây là điểm cải tiến then chốt so với PIBT gốc (khi backtrack thất bại, agent phải đứng yên).

---

## 8. Ký hiệu dùng trong pseudocode

| Ký hiệu | Ý nghĩa |
|---|---|
| `G` | Grid graph/môi trường |
| `n` | Số agent |
| `s_k` | State hiện tại của agent `k`: vị trí + hướng |
| `g_k` | Goal hiện tại của agent `k` |
| `a'_k` | Operation kế thừa từ timestep trước (inherited) |
| `a_k` | Operation được chọn ở timestep hiện tại |
| `P` | Reservation table: tập path của các agent đã được gán operation |
| `p_k` | Priority của agent `k` |
| `L` | Revisit limit: số lần tối đa agent được gọi lại trong một timestep |
| `visited_k` | Số lần `EPIBT_SELECT_OPERATION` đã được gọi cho agent `k` trong timestep hiện tại |
| `hit_k` | Cờ: agent `k` đang nằm trong nhánh đệ quy hiện tại (tránh cycle) |
| `Operations` | Tập tất cả operation ứng viên với độ dài `op_len` (đã rút gọn) |
| `getPath(s, op)` | Trả về chuỗi (state, timestep) khi thực hiện `op` từ state `s` |
| `getUsed(s, op, P)` | Trả về ID các agent sẽ collision với path của `(s, op)` trong reservation table `P` |
| `dist(s, g)` | Khoảng cách heuristic từ state `s` đến goal `g` (đã tính trước) |
| `h(s, op, g)` | Khoảng cách từ state sau khi thực hiện `op` từ `s` đến `g` (dùng heading tốt nhất) |

---

## 9. Xây dựng tập operation ứng viên

*Pseudocode dưới đây được rút ra từ mô tả Section MAO trong bài báo; không phải algorithm đánh số trong paper.*

```
Algorithm BUILD_OPERATIONS(op_len)
Input : op_len
Output: Operations (tập operation đã rút gọn)

 1: RawOps ← tất cả chuỗi action độ dài op_len trên {F, R, C, W}
 2: Operations ← ∅

 3: for op in RawOps do
 4:     if op có rotation dư thừa then
 5:         continue
 6:     if action cuối của op là R hoặc C then
 7:         continue
 8:
 9:     cell_seq ← chuỗi ô được chiếm khi thực hiện op
10:     if đã có op_old trong Operations với cùng cell_seq then
11:         // Gộp: giữ một operation đại diện
12:         // Khi tính h-value, dùng heading tốt nhất trong số heading đạt được
13:         continue
14:     else
15:         Operations ← Operations ∪ {op}
16:
17: return Operations
```

**Sắp xếp operation cho agent `k`:**

```
Algorithm SORT_OPERATIONS(k, Operations)
Input : agent k, tập Operations
Output: Operations được sắp xếp tăng dần theo w_op

1: for op in Operations do
2:     h_val ← h(s_k, op, g_k)
3:     β    ← lexicographic score của op (F=0, R/C=1, W=2)
4:     w_op ← α * h_val + β

5: return sort(Operations, key = w_op)
```

---

## 10. Kế thừa operation

*Pseudocode dưới đây được rút ra từ mô tả IO trong bài báo; không phải algorithm đánh số trong paper.*

```
Algorithm INHERIT_OPERATIONS(prev_operations, op_len)
Input : {a_prev_1, ..., a_prev_n}, op_len
Output: {a'_1, ..., a'_n}

1: for each agent k do
2:     a'_k ← a_prev_k[2 .. op_len]   // bỏ action đầu tiên
3:     a'_k ← a'_k + [W]              // thêm W ở cuối

4: return {a'_1, ..., a'_n}
```

**Lưu ý:** ở timestep đầu tiên (chưa có operation nào), khởi tạo `a'_k = [W, W, ..., W]` (op_len lần W) cho mọi agent `k`. Đây là operation collision-free mặc định.

---

## 11. Algorithm P1 — EPIBT Main Loop

*Đây là **Algorithm 1** trong bài báo (ký hiệu P1 để phân biệt với các pseudocode xây dựng ở trên).*

```
Algorithm EPIBT_MAIN_LOOP                                         [Paper: Algorithm 1]
Input :
    G                          // grid graph
    {s_1, ..., s_n}            // current states
    {g_1, ..., g_n}            // current goals
    {a'_1, ..., a'_n}          // inherited operations
    L                          // revisit limit
Output:
    {a_1, ..., a_n}            // selected operations

 1: for k = 1 to n do
 2:     a_k     ← a'_k                        // khởi tạo bằng inherited operation
 3:     visited_k ← 0
 4:     hit_k   ← 0

 5: P ← ∅
 6: for k = 1 to n do
 7:     P ← P ∪ getPath(s_k, a_k)            // reserve path của inherited operations

 8: for k = 1 to n do
 9:     p_k ← dist(s_k, g_k)                 // tính priority

10: Agents ← sort {1, ..., n} theo priority p_k (giảm dần: xa goal = priority cao hơn)

11: for k in Agents do
12:     if visited_k ≠ 0 then
13:         continue                          // đã được gọi trong đệ quy trước, bỏ qua

14:     P ← P \ getPath(s_k, a_k)           // tạm xóa path cũ khỏi reservation

15:     if EPIBT_SELECT_OPERATION(k, p_k) = failed then
16:         P ← P ∪ getPath(s_k, a'_k)      // fallback: dùng lại inherited operation
17:         a_k ← a'_k

18: return {a_1, ..., a_n}
```

**Ghi chú:**
- Tất cả agent bắt đầu với inherited operation → P đã có path hợp lệ ban đầu.
- Agent được duyệt theo priority (xa goal trước), nhưng nếu đã được xử lý trong đệ quy thì bỏ qua.
- Nếu không tìm được operation tốt hơn, agent **giữ inherited operation** (không fallback về WWW).

---

## 12. Algorithm P2 — EPIBT Operation Selection Procedure

*Đây là **Algorithm 2** trong bài báo (ký hiệu P2).*

```
Algorithm EPIBT_SELECT_OPERATION                                   [Paper: Algorithm 2]
Input :
    k           // agent đang được xử lý
    p           // priority đang được inherit
Global (đọc và ghi):
    G, Operations, P
    {s_k}, {g_k}, {a_k}, {a'_k}
    {p_k}, {visited_k}, {hit_k}
    L
Output:
    success hoặc failed

 1: OP ← SORT_OPERATIONS(k, Operations)     // sắp xếp theo w_op tăng dần

 2: visited_k ← visited_k + 1
 3: hit_k     ← 1                           // đánh dấu: k đang trong nhánh đệ quy hiện tại

 4: for op in OP do
 5:     path_k ← getPath(s_k, op)

 6:     if path_k nằm ngoài G (vào chướng ngại/out-of-bound) then
 7:         continue

 8:     U ← getUsed(s_k, op, P)             // các agent sẽ collision với path_k

 9:     // CASE 1: không collision với ai → nhận ngay
10:     if U = ∅ then
11:         a_k   ← op
12:         hit_k ← 0
13:         P     ← P ∪ path_k
14:         return success

15:     // CASE 2: collision với nhiều hơn 1 agent → bỏ qua để tránh ambiguity
16:     if |U| > 1 then
17:         continue

18:     // CASE 3: collision với đúng 1 agent l → thử đẩy l
19:     l ← phần tử duy nhất trong U

20:     // Không đệ quy nếu l đang trong cùng nhánh, đã hết revisit limit, hoặc priority >= p
21:     if hit_l = 1 or visited_l ≥ L or p_l ≤ p then
22:         continue

23:     old_path_l ← getPath(s_l, a_l)

24:     P  ← P \ old_path_l                 // tạm xóa path của l
25:     P  ← P ∪ path_k                     // reserve path của k theo op
26:     a_k ← op

27:     if EPIBT_SELECT_OPERATION(l, p) = success then
28:         hit_k ← 0
29:         return success                   // l đã nhường, k giữ op

30:     // Backtrack nếu l không tìm được operation mới
31:     P  ← P \ path_k
32:     P  ← P ∪ old_path_l

33: // Mọi operation đều thất bại → fallback về inherited operation
34: a_k   ← a'_k
35: hit_k ← 0
36: return failed
```

**Logic quan trọng cần nhớ:**

1. Thử operation theo thứ tự `w_op` tăng dần (gần goal + prefer move trước).
2. Operation ra ngoài grid/vào chướng ngại bị bỏ qua.
3. **Không collision** → nhận ngay.
4. **Collision với >1 agent** → bỏ qua (tránh ambiguity trong priority inheritance).
5. **Collision với đúng 1 agent `l`** → thử đẩy đệ quy, nhưng chỉ khi `l` chưa ở trong nhánh hiện tại, chưa hết revisit limit, và có priority thấp hơn.
6. Nếu đệ quy thất bại → backtrack reservation table và thử operation khác.
7. Nếu tất cả thất bại → agent **giữ inherited operation `a'_k`**, không đứng yên hoàn toàn.

---

## 13. Large Neighborhood Search (LNS)

*Bài báo mô tả LNS trong text, không đưa pseudocode đánh số riêng. Pseudocode dưới đây được tổng hợp từ mô tả.*

```
Algorithm EPIBT_WITH_LNS
Input :
    G, {s_k}, {g_k}, {a'_k}, L
    time_budget
Output:
    {a_k} được tối ưu hóa

 1: {a_k}        ← EPIBT_MAIN_LOOP(G, {s_k}, {g_k}, {a'_k}, L)
 2: best_solution ← {a_k}
 3: best_score    ← LNS_METRIC(best_solution)

 4: while remaining_time() > 0 do
 5:     k ← chọn ngẫu nhiên một agent

 6:     candidate ← copy(best_solution)
 7:     P         ← reservation table của candidate
 8:     P         ← P \ getPath(s_k, a_k)      // xóa path của k

 9:     // Cho k priority cao nhất để override các agent khác
10:     result ← EPIBT_SELECT_OPERATION(k, p = +∞)

11:     if result = success then
12:         candidate_score ← LNS_METRIC(candidate)
13:         if candidate_score < best_score then  // cải thiện
14:             best_solution ← candidate
15:             best_score    ← candidate_score
16:         else
17:             revert thay đổi (restore path của k về cũ)

18: return best_solution
```

**LNS Metric:**

```
LNS_METRIC = Σ (w_op_k * p_k)  cho tất cả agent k
```

Trong đó `p_k` phản ánh khoảng cách tới goal (xa = priority cao), `w_op_k` là trọng số operation. Metric khuyến khích agent hoàn thành task nhanh và chuyển sang task mới.

---

## 14. Graph Guidance (GG)

GG không phải phần lõi của EPIBT, nhưng khi kết hợp với EPIBT+LNS cho kết quả rất mạnh. Cả **WPPL** và **LoRR24-Winner** đều dùng GG.

**Ý tưởng:** điều chỉnh trọng số chuyển tiếp trên graph để dẫn agent đi theo luồng hợp lý, tránh tích tụ ở hành lang hẹp.

```
Algorithm APPLY_GRAPH_GUIDANCE
Input : graph G, guidance_weight[e] cho mỗi cạnh e
Output: graph G với weighted costs

1: for each directed edge e in G do
2:     guided_cost[e] ← base_cost[e] + guidance_weight[e]

3: // Dùng guided_cost thay thế trong dist/h và khi sort operations
```

**Bằng chứng từ waiting heatmaps (Appendix, Figure 15–17):**
- Ngay cả **không có GG**, EPIBT(3)+LNS đã ít vùng đỏ/vàng (chờ nhiều) nhất trong nhóm không dùng GG.
- GG giảm mạnh bottleneck ở hành lang hẹp, đặc biệt rõ trên `Paris-1-256` và `sortation`.
- LoRR24-Winner (không có GG) phân bổ vùng chờ rải rác khắp map vì không có operation inheritance.

GG có thể được tiền xử lý trên map hoặc cập nhật động theo traffic flow (như trong Causal PIBT+traffic flow).

---

## 15. Phân tích lý thuyết

### 15.1. Điều kiện đảm bảo tính đúng đắn

EPIBT yêu cầu điều kiện graph giống PIBT gốc:

> **Điều kiện graph:** với mọi cặp node kề nhau, tồn tại một simple cycle `C` chứa cả hai node đó với `|C| ≥ 3`.

**Lưu ý:** map `random-32-32-20` **vi phạm điều kiện này** ở một số ô (cells có no cycle of size 3+), dẫn đến deadlock thường xuyên hơn trên map này.

### 15.2. Lemma 1: Agent priority cao nhất tiến gần goal trong ≤ 3 bước

**Phát biểu:** Gọi `k` là agent có priority cao nhất tại timestep `t`, và `(i, j)` là ô gần `g_k` nhất trong các ô kề `s_k`. Trong trường hợp xấu nhất, agent `k` sẽ đến `(i, j)` trong vòng `t + 3` timestep.

**Bằng chứng (tóm tắt, với `op_len = 3`):**

**Bước 1 — Không có inheritance:** tất cả agent bắt đầu với `WWW` (collision-free). Agent `k` có priority cao nhất chọn trước, các agent còn lại vẫn là `WWW`. Bốn operation sau đây bao phủ **tất cả ô kề** bất kể hướng ban đầu, và mỗi operation chỉ có **1 action di chuyển ở cuối**:

```
WWF,  CWF,  RWF,  RRF
```

Vì chỉ có 1 action di chuyển (ở cuối), mỗi operation chỉ có thể collision với **tối đa 1 agent**, và agent bị đẩy có **2 timestep dư** để quay trước khi cần rời ô. Áp dụng đệ quy priority inheritance, mọi agent bị chặn đều có thể rời ô trong ≤ 3 bước.

**Bước 2 — Với inheritance:** inherited operation vẫn collision-free. Trong trường hợp xấu nhất, agent `k` có thể **chỉ thay action cuối** của inherited operation → collision với tối đa 1 agent ở cuối operation → logic đẩy vẫn giữ nguyên.

**Kết luận:** operation inheritance **không phá vỡ** tính chất này. □

### 15.3. Proposition 1: Độ phức tạp thời gian

Với:
- `n`: số agent
- `|OP|`: số operation ứng viên
- `op_len`: độ dài operation
- `L`: revisit limit

**Độ phức tạp mỗi timestep của EPIBT:**

```
O(n * (log n  +  |OP| * log|OP|  +  L * |OP| * op_len))
```

**Giải thích:**
- `n log n`: sắp xếp agent theo priority.
- `|OP| log|OP|`: sắp xếp operation cho từng agent (thực hiện 1 lần/agent).
- `L * |OP| * op_len`: mỗi agent bị revisit tối đa `L` lần; mỗi lần xét tối đa `|OP|` operation; collision check và reservation update mỗi operation tốn `Θ(op_len)`.

**Thực tế:** với 10.000 agent, EPIBT tính xong action trong **dưới 100ms** (còn dư thời gian cho LNS trong budget 1 giây).

---

## 16. Kết quả thực nghiệm

### 16.1. Setup thực nghiệm

- **Phần cứng:** Intel Xeon Gold 6338 CPU, 256 GB RAM, **single-threaded**.
- **Time budget:** 1 giây/timestep cho mọi experiment.
- **Timestep horizon:** `T = 5,000` timestep (riêng `random-32-32-20` dùng `T = 1,000`).
- **Task assignment:** loại bỏ inter-agent influence — agent `k` nhận task theo thứ tự `k, k + total_agents, k + 2*total_agents, ...`

**Năm map thực nghiệm (từ LoRR competition + MAPF benchmark):**

| Map | Kích thước | `|V|` (ô đi được) | Số agent thử nghiệm |
|---|---|---|---|
| `random-32-32-20` | 32×32 | 819 | 100, 200, ..., 800 |
| `Paris-1-256` | 256×256 | 47,240 | 1,000, 2,000, ..., 10,000 |
| `brc202d` | 481×530 | 43,151 | 500, 1,000, ..., 5,000 |
| `sortation` | 140×500 | 54,320 | 1,000, 2,000, ..., 10,000 |
| `warehouse` | 140×500 | 38,586 | 1,000, 2,000, ..., 10,000 |

### 16.2. Ablation study (Figure 4 trong bài báo)

| Variant | Nhận xét |
|---|---|
| PIBT + MAO(3) | **Kết quả kém** vì agent không thể sửa lựa chọn khi thất bại |
| + Revisit(∞) | Cải thiện rõ rệt nhưng **vượt time budget** ở high-density |
| + Revisit(10) | Ổn định cả throughput lẫn runtime |
| + IO | Tiếp tục tăng throughput **ở mọi op_len** |
| op_len = 4, 5 | Tăng runtime; đôi khi cải thiện throughput ở map lớn nhưng **không ổn định** |

**Kết luận:** `op_len = 3` là lựa chọn tốt nhất trong hầu hết trường hợp — operation dài hơn tạo ra nhiều multi-agent collision hơn, buộc agent chọn action suboptimal để né.

### 16.3. EPIBT trên LMAPF-T (có rotation)

PIBT gốc không dùng trực tiếp được với rotation model. Nó được adapt bằng cách chỉ xét **5 operation cố định**: `FWW, RFW, CFW, RRF, WWW` — mô phỏng omnidirectional model khi di chuyển tới ô kề cần 2–3 action.

**Kết quả (Figure 7 trong bài báo):**
- EPIBT(3) **vượt trội rõ rệt** so với PIBT và Causal PIBT trên tất cả 5 map, mọi số lượng agent.
- PIBT+Revisit(10) cải thiện một phần so với PIBT gốc nhưng vẫn thua EPIBT(3) vì thiếu IO.
- Causal PIBT có runtime thấp hơn do dùng implementation được tối ưu riêng cho LMAPF-T; EPIBT implementation thiết kế tổng quát hơn.

### 16.4. EPIBT trên LMAPF (omnidirectional — không có rotation)

Trong setting này, mọi action đều dẫn đến vị trí khác nhau → số unique cell sequences là `5^op_len` và **không thể rút gọn**. `EPIBT(1)` và `EPIBT(2)` khả thi vì mọi ô kề đều đến được trong 1 bước.

**Kết quả (Figure 6 trong bài báo, 3/5 map được hiển thị):**

| Phương pháp | Kết quả |
|---|---|
| winPIBT (w=10) | Tốt trên `random-32-32-20`, nhưng **vượt time budget** trên tất cả map lớn hơn |
| PIBT gốc | Underperform trên `random-32-32-20` (deadlock vì map vi phạm điều kiện cycle ≥ 3); cạnh tranh với EPIBT(1) trên `Paris-1-256` (không có deadlock) |
| EPIBT(1) | Cải thiện so với PIBT nhờ revisiting — đặc biệt trên map có deadlock |
| EPIBT(2) | **Rõ ràng có lợi** — op_len = 2 cân bằng tốt giữa tầm nhìn và tốc độ cho omnidirectional |
| EPIBT(3) | Tăng runtime, không luôn cải thiện throughput so với EPIBT(2) |

**Kết luận cho omnidirectional:** `op_len = 2` là lựa chọn tốt nhất; tăng thêm lên 3 không ổn định và tốn thêm runtime.

### 16.5. EPIBT+LNS — So sánh state-of-the-art (Figure 1 & 5)

Các baseline được so sánh:

| Baseline | Mô tả |
|---|---|
| **WPPL** | LoRR23 winner; windowed PIBT + LNS + GG |
| **Causal PIBT + traffic flow** | LoRR24 default planner; GG động |
| **LoRR24-Winner** | **Do chính các tác giả tạo ra** cho LoRR24; là EPIBT variant **không có operation inheritance**, giới hạn revisit theo cách khác; dùng GG |

**Kết quả:**
- `EPIBT(3)+LNS+GG` đạt **normalized score 1.0 trên 3/5 map** — không phương pháp nào trong nhóm so sánh vượt được.
- Trên 2 map còn lại (`random-32-32-20` và `brc202d`), các phương pháp khác đôi khi thắng ở low-agent instances, nhưng EPIBT(3)+LNS+GG vẫn tốt hơn trong phần lớn trường hợp.
- LoRR24-Winner thua EPIBT(3)+LNS+GG chủ yếu vì thiếu operation inheritance.

---

## 17. Ablation các tham số quan trọng (Appendix)

### 17.1. Tie-breaking (Figure 9–11 trong bài báo)

**Ba chiến lược tốt nhất:** `FRW`, `FWR`, và **`RND` (random)**.

RND hoạt động tốt nhờ tính **stochastic** — mỗi timestep agent có thứ tự ưu tiên khác nhau, giúp phá deadlock tự nhiên.

**Chiến lược đặt wait/rotate trước forward đều cho kết quả tệ rõ rệt**, đặc biệt ở low-agent instances. Ví dụ minh họa:

```
Goal ở ô kề, agent cần đúng 1 action F.
Nếu chọn WFW hoặc WWF thay vì FWW:
  → agent đứng yên bước 1
  → bước tiếp theo vẫn có thể chọn wait lại
  → deadlock ngay cả khi hoàn toàn không có agent nào khác
```

Kết luận: **bắt buộc phải ép agent di chuyển** (F > R/C > W). Sự lựa chọn giữa R và W ít ảnh hưởng hơn.

### 17.2. Revisit limit (Figure 12–14 trong bài báo)

| Giá trị L | Kết quả |
|---|---|
| 1, 2, 4 | Underperform rõ rệt |
| 8, 10, 16, 25, 50 | Kết quả rất tương đương nhau |

**Kết luận:** chỉ cần `L ≥ 8` là đủ — tăng thêm không cải thiện throughput. Khuyến nghị `L = 10`.

---

## 18. Tham số khuyến nghị từ bài báo

| Thành phần | Khuyến nghị | Ghi chú |
|---|---|---|
| `op_len` cho LMAPF-T | `3` | Tốt nhất/cân bằng nhất trong hầu hết map |
| `op_len` cho LMAPF omnidirectional | `2` | op_len = 3 tăng runtime không đổi throughput |
| Revisit limit `L` | `10` | Mọi giá trị ≥ 8 đều tốt tương đương |
| Tie-breaking | `FRW` hoặc `FWR` | Random (RND) cũng tốt tương đương |
| Unlimited revisit | **Không nên** | Dễ vượt time budget ở high-density |
| Operation inheritance | **Nên bật** | Cải thiện throughput ở mọi op_len |
| LNS | Dùng thời gian còn lại sau EPIBT | Tăng đáng kể chất lượng nghiệm |
| GG | Kết hợp với EPIBT+LNS | Giảm congestion ở map có hành lang hẹp |

---

## 19. Checklist triển khai EPIBT

- [ ] State agent gồm cả **vị trí** và **hướng**.
- [ ] `getPath(s, op)` trả đủ chuỗi (cell, timestep) cho toàn bộ op — gồm cả các ô trung gian.
- [ ] Collision check gồm cả **vertex conflict** và **edge conflict** theo từng timestep.
- [ ] Tập candidate operations được **rút gọn** theo ba quy tắc (rotation dư thừa, kết thúc bằng R/C, gộp theo cell sequence).
- [ ] h-value khi gộp operations dùng **heading tốt nhất** trong số heading đạt được.
- [ ] Operations được sort theo `α * h + β` với tie-breaking ưu tiên `F > R/C > W`.
- [ ] `P` được cập nhật/backtrack **chính xác** khi đệ quy thất bại.
- [ ] Operation gây collision với **hơn 1 agent phải bị bỏ qua** (không dùng priority inheritance cho multi-agent collision).
- [ ] `visited_k` giới hạn revisit theo `L`.
- [ ] `hit_k` ngăn cycle trong cùng recursion branch.
- [ ] Khi thất bại, agent quay về `a'_k` (inherited) thay vì `WWW`.
- [ ] Timestep đầu tiên: khởi tạo `a'_k = WWW...W` (op_len lần W).
- [ ] Sau khi chọn xong operations, **chỉ thực thi action đầu tiên** của mỗi operation.
- [ ] Timestep sau: tạo inherited operation = bỏ action đầu + thêm W cuối.
- [ ] Với omnidirectional model: không thể rút gọn cell sequences (số sequences = `5^op_len`).

---

## 20. Pseudocode vòng lặp online đầy đủ

```
Algorithm ONLINE_EPIBT_PLANNER
Input :
    G
    {s_k} initial states
    {g_k} initial goals
    op_len, L, time_budget
    use_LNS  // bool
Output:
    chuỗi one-step actions cho tất cả agent theo từng timestep

 1: Operations ← BUILD_OPERATIONS(op_len)

 2: // Khởi tạo inherited operations = WWW...W (collision-free mặc định)
 3: for k = 1 to n do
 4:     a'_k ← [W] * op_len

 5: for timestep t = 1, 2, ... do
 6:     cập nhật {s_k}             // nhận state mới từ môi trường
 7:     cập nhật {g_k}             // gán goal mới nếu agent vừa hoàn thành goal

 8:     {a_k} ← EPIBT_MAIN_LOOP(G, {s_k}, {g_k}, {a'_k}, L)

 9:     if use_LNS then
10:         {a_k} ← EPIBT_WITH_LNS(G, {s_k}, {g_k}, {a'_k}, L,
11:                                  initial_solution={a_k},
12:                                  time_budget=remaining_budget())

13:     // Thực thi action đầu tiên của mỗi operation
14:     execute action a_k[1] for each agent k

15:     // Chuẩn bị inherited operations cho timestep tiếp theo
16:     for k = 1 to n do
17:         a'_k ← a_k[2 .. op_len] + [W]   // bỏ action đầu, thêm W
```

---

## 21. Tóm tắt ngắn gọn

EPIBT cải tiến PIBT bằng ba thành phần:

1. **Multi-action Operations (MAO):** thay action đơn lẻ bằng chuỗi action ngắn (`op_len` thường = 3), cho phép nhìn trước vài timestep mà không cần path planning riêng như winPIBT.

2. **Revisiting (giới hạn L):** agent có thể được gọi lại để đổi operation, giải quyết collision phức tạp hơn. Hai thách thức: (i) bỏ qua operation gây collision với >1 agent; (ii) giới hạn số lần revisit để tránh vượt time budget.

3. **Operation Inheritance (IO):** tái sử dụng operation từ bước trước (bỏ action đầu, thêm W cuối). Khi thất bại, agent dùng inherited operation thay vì đứng yên hoàn toàn.

Ba thành phần này cùng xử lý được rotation action model, nơi agent có thể cần quay nhiều bước trước khi rời ô. Khi kết hợp thêm LNS và Graph Guidance, EPIBT(3)+LNS+GG thiết lập **state-of-the-art mới cho online LMAPF-T**, vượt cả hai winner của LoRR competition trên phần lớn benchmark.
