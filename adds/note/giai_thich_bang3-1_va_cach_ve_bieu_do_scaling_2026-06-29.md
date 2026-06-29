# Giải thích Bảng 3.1 và cách vẽ các biểu đồ scaling (Hình 4.24–4.27)

> Ghi chú nội bộ, ngày 2026-06-29. Trả lời hai thắc mắc: (1) các cột trong Bảng 3.1
> nghĩa là gì; (2) các biểu đồ Hình 4.24, 4.25, 4.26, 4.27 được vẽ ra bằng cách nào.

---

## 1. Bảng 3.1 — "Số ô và số chuỗi ô đạt được theo độ dài thao tác"

### 1.1. Bối cảnh: thao tác đa hành động của EPIBT

Bảng này nằm trong phần trình bày **EPIBT** (mở rộng của PIBT có ràng buộc xoay hướng),
số liệu lấy từ bài báo gốc `\cite{yukhnevich2025epibt}`. Để hiểu bảng cần nhớ mô hình
hành động của xe tăng:

- Trạng thái một agent gồm **vị trí + hướng thân** (không chỉ vị trí như PIBT cổ điển).
- Mỗi bước, agent có **4 hành động cơ bản**: *tiến*, *xoay phải*, *xoay trái*, *chờ*.
- PIBT cổ điển chọn **1 hành động** mỗi bước. EPIBT thay vào đó chọn cả một **chuỗi
  hành động có độ dài cố định** (gọi là một *thao tác / operation*) rồi chỉ thực thi
  hành động đầu tiên ở bước hiện tại. Việc nhìn trước cả chuỗi giúp agent **lường trước
  được chi phí xoay** mà cách chọn một bước bỏ sót.

Bảng 3.1 minh họa **sự đánh đổi** khi tăng độ dài chuỗi nhìn trước đó.

### 1.2. Ý nghĩa từng cột

| Cột | Tên trong bảng | Nghĩa |
|---|---|---|
| 1 | **Độ dài thao tác** (L) | Số hành động trong một chuỗi mà agent nhìn trước (1, 2, 3, 4, 5). |
| 2 | **Số ô đạt được** | Số **ô lưới khác nhau** mà agent có thể đặt chân tới sau khi thực thi một chuỗi dài L (tính từ một trạng thái xuất phát cố định). Đây là "tầm với" — agent với tới được bao nhiêu ô. |
| 3 | **Số chuỗi ô khác nhau** | Số **chuỗi hành động (quỹ đạo) phân biệt** có độ dài L. Đây là **kích thước không gian tìm kiếm** — agent phải cân nhắc bao nhiêu phương án, tức là **chi phí tính toán**. |

Số liệu trong bảng:

| L | Số ô đạt được | Số chuỗi ô khác nhau |
|---|---|---|
| 1 | 2  | 2   |
| 2 | 5  | 6   |
| 3 | 11 | 17  |
| 4 | 21 | 48  |
| 5 | 35 | 136 |

### 1.3. Hai cột muốn nói lên điều gì

Đặt cạnh nhau, hai cột thể hiện **một sự đánh đổi**:

- **Cột 2 (tầm với) tăng vừa phải**: 2 → 5 → 11 → 21 → 35. Nhìn xa hơn thì với tới được
  nhiều ô hơn, nhưng tăng gần như theo bậc hai (đa thức).
- **Cột 3 (số phương án) tăng rất nhanh**: 2 → 6 → 17 → 48 → 136. Đây là tăng kiểu **tổ
  hợp (combinatorial)** — mỗi bước nhân thêm số nhánh, nên không gian tìm kiếm bùng nổ.

→ **Thông điệp**: tăng độ dài thao tác cho agent "nhìn xa" hơn (lợi), nhưng **cái giá
phải trả là số phương án cần xét tăng vọt** (chi phí CPU). Vì vậy không thể chọn L lớn
tùy ý.

### 1.4. Vì sao đồ án/EPIBT chọn độ dài 3

**L = 3 là độ dài tối thiểu để từ một hướng bất kỳ vẫn tới được mọi ô kề.** Lý do: xe
tăng có hướng thân, muốn sang ô phía sau phải *xoay → xoay → tiến* (hoặc *xoay → tiến*
tùy hướng), tốn tới 3 bước. Nếu chuỗi ngắn hơn 3, có những ô kề mà agent **không thể với
tới trong một thao tác**, khiến nó không lường trước đủ chi phí xoay. Chọn đúng L = 3
cân bằng giữa "đủ tầm nhìn để tính chi phí xoay" và "số phương án còn quản lý được"
(17 chuỗi, chưa bùng nổ như 48 hay 136 ở L = 4, 5).

> **Ví dụ trực giác**: agent đang quay mặt sang phải. Muốn đi sang ô *ngay phía sau*, nó
> phải xoay hai lần rồi tiến — 3 hành động. Một chuỗi dài 2 không thể chạm tới ô đó, nên
> agent "không biết" rằng đi tới ô sau lưng tốn 3 bước. Chuỗi dài 3 thì biết.

---

## 2. Cách vẽ các biểu đồ Hình 4.24, 4.25, 4.26, 4.27

### 2.1. Chuỗi dữ liệu (data pipeline)

Các biểu đồ **không vẽ tay** mà sinh tự động bằng Python + matplotlib từ dữ liệu backtest,
qua ba chặng:

```
backtest_summary_*.csv          (CSV thô: 1 dòng / 1 run, 900 run)
        │  tổng hợp (trung bình theo môi trường × số agent × thuật toán,
        │            lấy trung bình trên 5 bản đồ × 6 lần lặp)
        ▼
report_scaling_by_agentcount.csv   (pivot: 1 dòng / tổ hợp)
   cột: Environment, AgentCount, Algorithm, SuccessRatePct,
        TimeoutRatePct, DurationMean, ReplanMean, RecovMean, CellsMean
        │  Tools/backtest_scale_plots.py  (matplotlib)
        ▼
Hinhve/backtest_*_vs_agents.png   (PNG 300 DPI, nền trắng)
```

Điểm quan trọng: **cùng một file pivot** này vừa nuôi script vẽ biểu đồ
(`backtest_scale_plots.py`) vừa nuôi script in bảng LaTeX (`backtest_scale_tables.py` →
sinh thân Bảng 4.9, 4.10, 4.11). Nhờ vậy **số trong biểu đồ và số trong bảng luôn khớp
nhau**, không lệch do gõ tay.

### 2.2. Script và hàm vẽ

Toàn bộ bốn biểu đồ scaling do hàm `line_vs_agents()` trong
`Tools/backtest_scale_plots.py` vẽ. Mỗi biểu đồ là một **đồ thị đường** với:

- **Trục X** = số lượng agent `[6, 12, 24, 36, 72]`.
- **Ba đường** = ba thuật toán, mỗi đường một màu + ký hiệu điểm riêng:
  - A* — xanh dương `#2a6fdb`, marker tròn `o`
  - PIBT C# — cam `#e8820e`, marker vuông `s`
  - PIBT C++/TCP — xanh lá `#2e9e4f`, marker tam giác `^`
- **Trục Y** = giá trị trung bình của một chỉ số, đọc thẳng từ cột tương ứng trong pivot.

Bảng ánh xạ từng hình:

| Hình | Chỉ số (cột pivot) | Môi trường | Thang trục Y | Ghi chú |
|---|---|---|---|---|
| **4.24** | `DurationMean` (thời gian) | tĩnh | tuyến tính | thời gian hoàn thành trung bình |
| **4.25** | `ReplanMean` (số replan) | tĩnh | **log** (`logy=True`) | giá trị trải vài bậc độ lớn |
| **4.26** | `TimeoutRatePct` (% timeout) | tĩnh | tuyến tính, ghim 0–100% | `pct=True` |
| **4.27** | `RecovMean` (số phục hồi) | **động** | **log** (`logy=True`) | chỉ có ở môi trường động |

### 2.3. Vì sao dùng thang logarit (Hình 4.25 và 4.27)

Số lần tái hoạch định và số lần phục hồi **trải rộng từ vài trăm tới hàng trăm nghìn**
(ví dụ replan: ~800 ở 6 agent → ~144 000 ở 72 agent với PIBT C#). Nếu để trục tuyến
tính, các giá trị nhỏ bị "ép bẹp" sát đáy và không đọc được. Dùng **`set_yscale("log")`**
giúp cả ba đường cong đều rõ ràng trên cùng một hình, và biến quan hệ "nhân theo bậc"
thành đường gần thẳng — dễ so sánh tốc độ tăng giữa các thuật toán. Hình 4.24 (thời gian)
và 4.26 (tỉ lệ %) có miền giá trị hẹp nên giữ trục tuyến tính.

### 2.4. Một mẹo nhỏ về hiển thị (Hình 4.26)

Ở biểu đồ tỉ lệ timeout, đường của **A\*** và **PIBT C#** gần như trùng khít (cùng tỉ lệ
timeout trên nhiều mức agent). Script cộng một lượng rất nhỏ **+1.2%** vào đường PIBT chỉ
để **tách hai đường cho dễ nhìn** — đây là điều chỉnh hiển thị, không làm thay đổi số liệu
gốc trong bảng.

### 2.5. Cách chạy lại (tái tạo biểu đồ)

Từ thư mục gốc của dự án:

```bash
pip install matplotlib
python Tools/backtest_scale_plots.py
```

Script sẽ ghi đè các file PNG vào
`adds/report/20225808_DuongQuangDong_2025.2/Hinhve/`:

- `backtest_duration_vs_agents.png`  → Hình 4.24
- `backtest_replan_vs_agents.png`    → Hình 4.25
- `backtest_timeout_vs_agents.png`   → Hình 4.26
- `backtest_recoveries_vs_agents.png`→ Hình 4.27

Sau đó build lại quyển (`./build.ps1`) để cập nhật hình vào `DoAn.pdf`.

### 2.6. Tóm tắt một câu

> Bốn biểu đồ là **đồ thị đường** vẽ bằng matplotlib, đọc từ file pivot đã tổng hợp sẵn
> (trung bình trên 5 bản đồ × 6 lần lặp); trục X là số agent, mỗi thuật toán một đường;
> hai biểu đồ replan và phục hồi để **thang log** vì số liệu trải nhiều bậc độ lớn.

---

## Tham chiếu tệp

- Bảng 3.1: `Chuong/3_Cong_nghe.tex` (nhãn `tab:mao_reach`).
- Hình 4.24–4.27: `Chuong/4_Ket_qua_thuc_nghiem.tex` (nhãn `fig:scaling_duration`,
  `fig:scaling_replan`, `fig:scaling_timeout`, `fig:scaling_recoveries`).
- Script vẽ: `Tools/backtest_scale_plots.py`. Script in bảng: `Tools/backtest_scale_tables.py`.
- Dữ liệu: `BacktestResults/report_scaling_by_agentcount.csv`,
  `BacktestResults/report_scaling_by_map.csv` (pivot từ `backtest_summary_*.csv`).
