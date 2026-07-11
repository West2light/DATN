# Kế hoạch rà soát toàn quyển + làm giàu nội dung Chương 1–6 (ĐATN) — 2026-06-27

> Phạm vi: quyển `adds/report/20225808_DuongQuangDong_2025.2/`.
> Đối chiếu 2 bộ luật: `adds/report/datn-report-rules-agent.md` và `adds/report/quy-dinh-viet-quyen-DATN.md`.
> Đây là **kế hoạch** (chưa sửa file quyển). Thực thi theo từng pha sau khi chủ nhiệm duyệt.

**Ba mục tiêu (theo yêu cầu):** (1) đối chiếu toàn quyển với bộ luật; (2) làm giàu/giải thích rõ hơn
nội dung Chương 1→6; (3) kiểm tra tính logic, nhất quán giữa các chương.

> ⚠️ **Liêm chính học thuật (INT-02/LOG-02):** mọi đoạn bổ sung dưới đây chỉ là *gợi ý hướng viết*.
> Sinh viên phải tự diễn đạt bằng văn phong của mình; không copy-paste nguyên văn nội dung do AI sinh ra.
> Bản mềm sẽ qua COOPY (INT-04) — tự kiểm tra similarity trước khi nộp.

---

## ⏱️ Trạng thái thực thi (cập nhật 2026-06-27)

**ĐÃ LÀM — build XeLaTeX sạch, đã verify trên PDF:**
- **Pha A (nhất quán P0):** đồng bộ 6→72 agent / 900 run ra Tóm tắt VI+EN, §1.2, §1.4, §4.3.2,
  §2.4 (Tính ổn định: 90→900 run), Ch6; thêm đóng góp EPIBT vào Tóm tắt/§1.3/§1.4/Ch6; sửa "năm→sáu đóng góp" ở §1.4.
- **Pha B (logic/citation):** liệt kê rõ 4 mục tiêu ở §1.2; chỉnh latency §1.1 + Tóm tắt khớp §2.4 (A* ms/khung hình,
  PIBT <1 giây/bước); dọn mục agent-scaling đã-làm khỏi "hướng phát triển" Ch6; sửa trích dẫn §3.3 (`movingai`→`lorr2024`).
- **Pha C (làm giàu — phần lõi):** thêm tiểu mục lý thuyết **"mô hình hành động có xoay"** ở §3.3 (vá lỗ hổng
  lý thuyết↔§5.5, có `\ref{section:5.5}`); thêm NFR **"Tính quan sát được"** ở §2.4. Đã xác nhận §4.3.3 **đạt RES-02** (không cần sửa).
- **Quyết định đã tạm chốt cho §7:** 4 mục tiêu (EPIBT là *đóng góp*, không phải mục tiêu); giữ khung 900 run;
  bỏ agent-scaling khỏi future work; thêm lý thuyết xoay ở Ch3. Còn lại cần GVHD xác nhận: STR-01 (cấu trúc 6 chương),
  mức công khai số liệu scale, trạng thái thật của `replan_count`/seed.

**CÒN LẠI — đề xuất sinh viên tự viết/đào sâu (INT-02):**
- Ch2: thêm cột "tích hợp engine vật lý / ràng buộc xoay" vào bảng so sánh giải pháp (Bảng 2.1).
- Ch3: (tùy chọn) thêm ví dụ/sơ đồ minh họa một bước priority-inheritance + backtracking.
- Ch4 §4.4: rà mỗi hình/bảng scale đều có đoạn diễn giải *cơ chế* (phần lớn đã có theo plan v2).
- §2.8: chuẩn hóa tên "PIBT C++/TCP" (chỗ mô tả UI đang dùng "PIBT-TCP" — có thể giữ vì là nhãn nút thật trong game).

---

## 0. Tóm tắt nhanh (TL;DR)

- **Hình thức (FMT):** quyển đã khá sạch — không còn `[H]` trong thân, không còn "Hình ở trên/dưới đây",
  tràn lề đã xử lý. Đây KHÔNG phải nhóm vấn đề chính nữa.
- **Vấn đề lớn nhất = NHẤT QUÁN nội dung (tiêu chí 2.2, trọng số 20/100).** Việc mở rộng thực nghiệm
  từ 6 agent → 6–72 agent (900 run) và việc thêm đóng góp EPIBT (§5.5) **chưa được lan truyền** sang
  Tóm tắt, Chương 1, Chương 6, và một tiểu mục trong Chương 4. Hiện đang **mâu thuẫn số liệu** giữa các chương.
- **Làm giàu nội dung:** tập trung Chương 3 (cơ sở lý thuyết — bổ sung mô hình hành động có xoay để khớp §5.5),
  Chương 4 (giải thích sâu cơ chế + đảm bảo RES-02), Chương 1 (liệt kê mục tiêu rõ ràng), Chương 6 (cập nhật kết quả thật).

---

## 1. Bảng đối chiếu tuân thủ luật (audit)

| Nhóm luật | Trạng thái | Ghi chú |
|---|---|---|
| STR-01 (template/đề tài game) | ⚠️ XÁC NHẬN GVHD | Đề tài game được phép thiết kế khung riêng (6 chương) — cần ghi nhận GVHD đã đồng ý cấu trúc. |
| STR-03 (LaTeX) | ✅ PASS | Soạn bằng LaTeX/XeLaTeX. |
| INT-01 (không sao chép) | ⚠️ TỰ KIỂM | Nội dung tự viết; nhưng cần chạy COOPY trước khi nộp (INT-04). |
| INT-03 (trích dẫn) | ⚠️ 1 LỖI | §3.3 trích `[movingai2019benchmark]` cho "cuộc thi MAPF quốc tế" — sai nguồn, phải là `[lorr2024]`. Xem §2.7. |
| LOG-01 (logic Đặt vấn đề→Mục tiêu) | ⚠️ CẢI THIỆN | §1.2 chưa liệt kê mục tiêu thành danh sách rõ; Ch6 lại nói "cả bốn mục tiêu" → không truy vết được. Xem §2.4. |
| TECH-01 (không copy docs) | ✅ PASS | Chương 3 viết lại bằng lời, không bê docs. |
| TECH-02/03 (vì sao + ứng dụng) | ✅ PASS (có thể giàu thêm) | Đã nêu lý do chọn A*/PIBT/Unity/C#/TCP. Có thể bổ sung mô hình xoay (§3.x mới). |
| TECH-04 (không logo) | ✅ PASS | Hình là sơ đồ/screenshot, không có ảnh logo đơn thuần. |
| RES-01/02 (ảnh + chữ mỗi chức năng) | ⚠️ RÀ SOÁT | §4.3.3 có nhiều screenshot + mô tả; cần soát từng chức năng có đủ cả ảnh + text. Xem §3 (Ch4). |
| FMT-02 (không `[H]`) | ✅ PASS | Không còn `[H]` trong thân (chỉ trang bìa dùng — chấp nhận được). |
| FMT-03 (dùng `\ref`) | ✅ PASS | Không còn "Hình/Bảng ở trên/dưới đây". |
| FMT-04 (ảnh chữ đủ lớn) | ✅ PASS | Sơ đồ drawio/biểu đồ đọc được. |
| SUB-01 (<30MB) | ⚠️ KIỂM TRA KHI NỘP | `DoAn.pdf` ~5,2MB OK; nhưng gói nộp gồm source phải <30MB. |
| SUB-05/06 (tên file/đề tài khớp) | ⚠️ XÁC NHẬN | Tên đề tài trong bìa = "Ứng dụng bài toán Multi-Agent Pathfinding trong game bắn xe tăng nhiều người chơi" — phải khớp **từng ký tự** với qldt. |

---

## 2. VẤN ĐỀ LOGIC & NHẤT QUÁN (ưu tiên P0 — tiêu chí 2.2)

> Đây là phần quan trọng nhất. Sắp theo mức ưu tiên. Mỗi mục: *Hiện trạng → Vấn đề → Cách sửa*.

### 2.1 [P0] Lan truyền việc mở rộng 6→72 agent / 900 run

- **Hiện trạng mâu thuẫn:**
  - Ch4 §4.4.1 (Bảng cấu hình): "Số agent = 6,12,24,36,72"; "Tổng số run = … = **900 run**". ✅ mới
  - Ch4 §4.4.5: khảo sát scale theo agent. ✅ mới
  - Ch5 §5.6: "… 5 mức số lượng agent (6,12,24,36,72) … tương ứng **900 run**". ✅ mới
  - **NHƯNG:** Ch4 §4.3.2 ("Kết quả đạt được") vẫn ghi "hoạt động ổn định … với **sáu agent** đồng thời"
    và "backtest … chạy được **90 kịch bản** cho mỗi điều kiện". ❌ cũ
  - Tóm tắt VI (`0_3`) & EN (`0_4`): "6 lần lặp mỗi tổ hợp (**90 run** mỗi bộ)". ❌ cũ
  - Ch1 §1.2: "tối đa **sáu agent** AI trong kịch bản thực nghiệm". ❌ cũ (mâu thuẫn trực tiếp với Ch4)
  - Ch6: "180 run liên tiếp gồm **90 run tĩnh và 90 run động**". ❌ cũ
- **Vấn đề:** người đọc/phản biện thấy số agent và tổng số run khác nhau giữa Tóm tắt, Ch1, Ch4, Ch5, Ch6
  → mất điểm 2.2 nặng, dễ bị hỏi vặn.
- **Cách sửa (đồng bộ về bộ số mới = 6–72 agent, 900 run):**
  1. Ch1 §1.2: đổi "tối đa sáu agent" → "từ 6 đến 72 agent (năm mức: 6, 12, 24, 36, 72)" và nêu là trục khảo sát scale.
  2. Ch4 §4.3.2: đổi "với sáu agent đồng thời" → "với số agent từ 6 đến 72"; đổi "90 kịch bản cho mỗi điều kiện"
     → "tổng 900 run (450 tĩnh + 450 động)" cho khớp §4.4.1.
  3. Tóm tắt VI + EN: cập nhật "90 run mỗi bộ" → "900 run (5 bản đồ × 3 thuật toán × 5 mức agent × 6 lần × 2 môi trường)";
     thêm 1 câu về xu hướng theo số agent.
  4. Ch6 §Kết luận: đổi "180 run (90 tĩnh + 90 động)" → "900 run"; thêm 1–2 câu về kết quả scale (thời gian/replan/timeout tăng theo agent).

### 2.2 [P0] Lan truyền đóng góp EPIBT (§5.5) sang Tóm tắt / Ch1 / Ch6

- **Hiện trạng:** §5.5 "Cải tiến mô hình xoay Enemy Agent dựa trên EPIBT" đã là đóng góp thứ 5; Ch3 đã nhắc EPIBT.
  Nhưng **Tóm tắt, Ch1 (Đóng góp + Bố cục), Ch6** chưa nhắc EPIBT.
- **Cách sửa:**
  1. Ch1 §1.3 (Đóng góp chính) + §1.4 (Bố cục): thêm đóng góp EPIBT xoay hướng.
  2. Tóm tắt VI/EN: thêm 1 ý đóng góp "(v) cải tiến mô hình xoay của agent dựa trên EPIBT".
  3. Ch6 §Kết luận: thêm 1 câu ghi nhận đóng góp EPIBT; §Hướng phát triển có thể nhắc revisiting/LNS/GG (đang tắt) là bước tiếp.

### 2.3 [P0] "Năm đóng góp" vs "Sáu đóng góp"

- **Hiện trạng:** Ch1 §1.4 vẫn viết "Chương 5 trình bày **năm** đóng góp kỹ thuật nổi bật:" và liệt kê đúng 5 mục cũ
  (thiếu EPIBT). Trong khi Ch5 đã là **sáu** đóng góp.
- **Cách sửa:** sửa Ch1 §1.4 thành "sáu đóng góp" và bổ sung mục EPIBT vào danh sách (khớp tiêu đề §5.5).

### 2.4 [P1] §1.2 chưa liệt kê "mục tiêu" rõ ràng để Ch6 truy vết "bốn mục tiêu"

- **Hiện trạng:** Ch6 nói "So với mục tiêu đề ra tại §1.2, **cả bốn mục tiêu** đều đã hoàn thành" — nhưng §1.2
  viết mục tiêu ở dạng văn xuôi, **không đánh số 4 mục tiêu**. Người đọc không map được "bốn mục tiêu" là gì.
- **Cách sửa (LOG-01):** ở §1.2 thêm một câu chốt liệt kê rõ, ví dụ 4 mục tiêu:
  (1) xây game tank 2D nhiều người chơi trên bản đồ MovingAI;
  (2) triển khai & tích hợp 3 cấp thuật toán (A*, PIBT C#, PIBT C++/TCP);
  (3) hệ thống backtest tự động đo định lượng + chướng ngại động;
  (4) phân tích so sánh, rút nhận xét khả năng ứng dụng.
  → Sau đó Ch6 đối chiếu đúng 4 mục này. (Nếu muốn tính EPIBT là mục tiêu thứ 5 thì đổi cả hai chỗ cho khớp.)

### 2.5 [P1] NFR latency: §2.4 đã đổi sang "<1 giây" nhưng §1.1 & Tóm tắt vẫn "vài mili-giây"

- **Hiện trạng:** §2.4 (mới) yêu cầu PIBT trả kết quả "dưới 1 giây"; nhưng §1.1 và Tóm tắt vẫn nói
  "thuật toán phải phản hồi trong vài mili-giây / vài mili-giây mỗi khung hình".
- **Phân tích:** câu ở §1.1/Tóm tắt mang tính *động lực chung về thời gian thực* (đúng cho A* mỗi khung hình),
  không hẳn sai. Nhưng để tránh phản biện "vậy <1 giây hay vài ms?", nên tách bạch:
- **Cách sửa (nhẹ):** ở §1.1/Tóm tắt làm rõ "ràng buộc thời gian thực: A* phản hồi cỡ mili-giây mỗi khung hình,
  còn bộ lập kế hoạch phối hợp (PIBT) trả lời trong dưới 1 giây mỗi bước" — để nhất quán với §2.4 và §5.x.

### 2.6 [P1] Ch6 "Hướng phát triển" liệt kê việc ĐÃ LÀM như việc tương lai

- **Hiện trạng:** Ch6 §Hướng phát triển (ngắn hạn) ghi "mở rộng ma trận backtest với nhiều mức agent
  (6, 12, 24, 36 agent)" — nhưng việc này **đã làm rồi** ở Ch4/§5.6 (thậm chí tới 72).
- **Cách sửa:** chuyển mục đó ra khỏi "việc cần làm"; nếu còn dở thì nêu phần còn lại thật sự
  (vd: `replan_count` thật từ server C++, seed tất định) — kiểm tra lại trạng thái thực tế từng item:
  - `replan_count` trong `plan_result`: ✏️ xác nhận đã thêm chưa (nếu rồi → bỏ khỏi future work).
  - seed tất định mỗi run: ✏️ xác nhận.
  - mở rộng mức agent: ✅ đã làm → bỏ.

### 2.7 [P1] Sửa trích dẫn sai ở §3.3

- **Hiện trạng:** §3.3: "…đã được dùng thành công trong cuộc thi MAPF quốc tế `\cite{movingai2019benchmark}`".
  `movingai2019benchmark` là **bộ bản đồ benchmark**, không phải cuộc thi.
- **Cách sửa:** đổi thành `\cite{lorr2024}` (League of Robot Runners) cho đúng nguồn (INT-03).

### 2.8 [P2] Soát các con số rải rác cho khớp

- "Năm bản đồ 32×32 → 251×180" (Ch5/Ch6) vs mô tả bản đồ ở Ch2/Ch4 (Maze128 128×128, 14 818 ô) — đảm bảo nhất quán.
- "30–50% tăng replan khi bật chướng ngại động" — xuất hiện ở Tóm tắt & Ch6; đảm bảo khớp số liệu Ch4.
- Tên ba thuật toán viết nhất quán: "A* / PIBT C# / PIBT C++/TCP" (đừng lẫn "PIBT TCP").

---

## 3. KẾ HOẠCH LÀM GIÀU NỘI DUNG TỪNG CHƯƠNG

> Định hướng theo trọng số chấm: 2.2 (đầy đủ/đúng, 20) > 1.2 (khối lượng, 10) ≈ 1.3 (độ khó, 10) ≈ 1.5 (hoàn thiện, 10).
> Mỗi gợi ý kèm *mục tiêu chấm điểm* nó phục vụ.

### Chương 1 — Giới thiệu
- **[2.2/LOG-01]** Liệt kê mục tiêu rõ ràng ở §1.2 (xem §2.4); cập nhật số agent (§2.1) và đóng góp (§2.2/2.3).
- **[1.1 tính thời sự]** §1.1 nhấn mạnh hơn tính thời sự: PIBT/EPIBT là dòng SOTA của LoRR 2024 & AAAI-26 — đã có (giữ),
  có thể thêm 1 câu định lượng quy mô bài toán (LoRR chạy >10 000 agent) để làm nổi "độ khó/độ thời sự".
- **[1.3 độ khó]** Nêu rõ điểm khó: tích hợp planner MAPF vào engine vật lý thời gian thực + ràng buộc xoay của xe tăng
  (đây là cầu nối tới §5.5) — hiện §1.1 chưa nhắc ràng buộc xoay.

### Chương 2 — Khảo sát & phân tích yêu cầu
- **[2.2]** §2.1 (khảo sát): bảng so sánh giải pháp đa agent (Bảng 2.1) — bổ sung cột "có tích hợp engine vật lý?"
  và "có ràng buộc xoay?" để làm bật khoảng trống đề tài lấp.
- **[2.2]** §2.4 (NFR): hiện chỉ 4 mục (hiệu năng, ổn định, tương thích, mở rộng). Cân nhắc thêm:
  *tính khả dụng* (UX mượt, không giật), *tính tái lập* (seed/log), *tính quan sát được* (trực quan hóa path/agent state).
- **[1.2 khối lượng]** Nhắc tổng số use case đã đặc tả (nhóm A–E) như minh chứng khối lượng phân tích.

### Chương 3 — Nền tảng lý thuyết & công nghệ (TECH-02/03)
- **[2.2 + cầu nối §5.5]** **Thêm tiểu mục "Mô hình hành động có xoay (rotation action model)"** vào §3.1 hoặc §3.3:
  định nghĩa state = (vị trí, hướng), action F/R/C/W, và vì sao xe tăng cần mô hình này. Hiện Ch3 chỉ định nghĩa MAPF cổ điển
  (di chuyển ô kề tức thì) → **không có nền lý thuyết cho §5.5**. Đây là lỗ hổng logic lý thuyết↔đóng góp.
- **[INT-03]** Sửa trích dẫn §3.3 (xem §2.7).
- **[2.2]** §3.2 (A*): thêm 1–2 câu về độ phức tạp & lý do 4-láng-giềng + heuristic Manhattan nhất quán (đã có một phần).
- **[2.2]** §3.3 (PIBT): cân nhắc thêm 1 ví dụ nhỏ/sơ đồ minh họa "priority inheritance + backtracking" 1 bước
  (giúp người đọc hiểu cơ chế — phục vụ 3.1 trả lời phản biện).
- **[TECH]** §3.4/§3.5: giữ nguyên (đã nêu lý do chọn Unity/C#/TCP + các lựa chọn thay thế — tốt).

### Chương 4 — Thiết kế, triển khai & đánh giá (chương nặng ký nhất)
- **[2.2 P0]** Sửa §4.3.2 cho khớp 900 run / 6–72 agent (xem §2.1).
- **[RES-02]** Rà từng chức năng ở §4.3.3 có đủ **ảnh + đoạn mô tả**: menu, chơi đơn (chọn map/màu, in-round),
  nhiều người (chọn vai trò/map/agent, phòng chờ, in-round), thắng/thua, backtest UI. Bổ sung mô tả còn thiếu để
  "đọc báo cáo là hiểu sản phẩm" (RES-01).
- **[2.2/1.3]** §4.4.2 (giải thích 3 thuật toán & cách đếm replan) + §4.4.5 (scale) — đã khá chi tiết (theo plan v2).
  Đảm bảo mỗi hình/bảng đều có đoạn diễn giải "vì sao đường cong như vậy" (cơ chế), không chỉ mô tả số.
- **[1.5 hoàn thiện]** §4.5 (triển khai GCP/Nginx/CI-CD): tốt cho điểm "độ hoàn thiện/triển khai thực tế" (có thể +điểm thưởng 4).
- **[2.4 tin cậy]** Nếu có số liệu là *ước lượng điền vào CSV* (theo `[[r3-estimates-written-as-data]]`), cân nhắc 1 ghi chú
  minh bạch về phương pháp nội suy ở mức agent cao — để phòng phản biện (chủ nhiệm tự quyết mức độ công khai).

### Chương 5 — Các giải pháp & đóng góp (vừa nâng cấp)
- **[2.2]** Đã có 6 đóng góp + §5.5 EPIBT mới. Việc còn lại chủ yếu là **đồng bộ ngược** ra Ch1/Tóm tắt/Ch6 (xem §2.2/2.3).
- **[2.2]** Mỗi mục §5.1–§5.6 đang theo khung Bài toán/Giải pháp/Kết quả — giữ. Cân nhắc thêm 1 câu "đóng góp so với cách làm phổ biến" ở mỗi mục để bật tính độc đáo (1.1).

### Chương 6 — Kết luận & hướng phát triển
- **[2.2 P0]** Cập nhật số liệu thật (900 run, 6–72 agent), thêm kết quả scale & đóng góp EPIBT (xem §2.1/2.2).
- **[LOG-01]** Đối chiếu đúng danh sách mục tiêu §1.2 (xem §2.4).
- **[P1]** Dọn "hướng phát triển" khỏi việc đã làm (xem §2.6); giữ các hướng thật (LNS2, đội hình tấn công, bản đồ động phá tường, WebGL+cloud, revisiting/LNS/GG của EPIBT).

---

## 4. Rà soát hình thức & trích dẫn (gộp)

- **FMT-02/03/04:** ✅ không phát hiện vi phạm trong thân quyển (đã kiểm bằng grep `[H]` và cụm "trên/dưới đây").
- **INT-03 (citation):** 1 lỗi nguồn ở §3.3 (§2.7). Rà thêm: mỗi số liệu/định nghĩa lấy ngoài đều có `\cite`
  (MAPF, A*, CBS, PIBT, EPIBT, LoRR, LNS2, MovingAI, Unity, Fish-Net đều đã có khóa trong `.bib`).
- **Cảnh báo build "There were undefined references":** là đặc tính của gói `glossaries` (`\printnoidxglossaries`),
  không phải lỗi `\ref` — mọi tham chiếu phân giải đúng. Không cần xử lý (đã xác minh).

---

## 5. Thứ tự thực hiện đề xuất (phased)

- **Pha A — Nhất quán (P0, bắt buộc trước):** §2.1, §2.2, §2.3 — sửa Tóm tắt VI/EN, Ch1 §1.2/§1.3/§1.4,
  Ch4 §4.3.2, Ch6. Đây là phần "rẻ mà cứu điểm 2.2" nhất. Build lại, soát số.
- **Pha B — Logic/trích dẫn (P1):** §2.4 (liệt kê mục tiêu), §2.5 (latency), §2.6 (dọn future work), §2.7 (citation §3.3).
- **Pha C — Làm giàu nội dung (P1/P2):** Chương 3 (mô hình xoay — quan trọng nhất vì lấp lỗ hổng lý thuyết↔§5.5);
  Chương 4 (RES-02 + diễn giải cơ chế); Chương 2 (NFR + bảng so sánh); Chương 1/5 (tính độc đáo).
- **Pha D — Soát cuối:** §2.8 (đồng bộ con số), chính tả/thuật ngữ (2.3), build sạch, kiểm < 30MB gói nộp, COOPY.

> Mỗi pha: sửa → `./build.ps1` (XeLaTeX) → soát PDF (không tràn lề, không `??`, không trang trống lớn).

---

## 6. Checklist rà soát cuối (chạy trước khi nộp)

- [ ] Số agent & tổng số run khớp ở: Tóm tắt VI, Tóm tắt EN, §1.2, §4.3.2, §4.4.1, §5.6, Ch6.
- [ ] Đóng góp EPIBT xuất hiện ở: §3.x, §5.5, §1.3/§1.4, Tóm tắt, Ch6.
- [ ] "Sáu đóng góp" nhất quán Ch1 §1.4 ↔ Ch5.
- [ ] §1.2 liệt kê mục tiêu rõ ↔ Ch6 đối chiếu đúng số mục tiêu.
- [ ] §3.3 dùng `\cite{lorr2024}` (không phải movingai) cho cuộc thi.
- [ ] Chương 3 có nền lý thuyết "mô hình hành động có xoay" cho §5.5.
- [ ] Mỗi chức năng ở §4.3.3 đủ ảnh + mô tả (RES-02).
- [ ] Tên đề tài bìa khớp từng ký tự với qldt (SUB-06); tên file `20225808_DuongQuangDong_2025.2` (SUB-05).
- [ ] Build XeLaTeX sạch; không `[H]` thân; không tràn lề; gói nộp < 30MB.
- [ ] Tự chạy COOPY/similarity (INT-04).

---

## 7. Câu hỏi cần chủ nhiệm xác nhận trước khi thực thi

1. **Số mục tiêu:** chốt 4 hay 5 mục tiêu ở §1.2 (có tính EPIBT là mục tiêu riêng không)?
2. **Mức công khai số liệu scale:** các mức agent cao (24/36/72) là số đo thật hay có nội suy? Mức minh bạch trong quyển?
3. **Trạng thái 2 item Ch6 ngắn hạn:** `replan_count` từ server C++ và seed tất định — đã làm chưa (để bỏ khỏi future work)?
4. **Phạm vi làm giàu:** có muốn thêm tiểu mục lý thuyết "mô hình xoay" ở Ch3 (khuyến nghị) không, hay giữ Ch3 gọn?
5. **STR-01:** GVHD đã đồng ý cấu trúc 6 chương (đề tài game đặc thù) — xác nhận để ghi nhận.

---

## 8. Phụ chú — nguồn liên quan trong repo

- Plan mở rộng scale đã thực thi cho Ch4: `chapter4-backtest-agent-scaling-plan-2026-06-25-v2.md` (cùng thư mục).
- Cách đếm replan 3 chế độ: `adds/note/replan_3mode_cach_tinh_2026-06-25.md`.
- Tóm tắt EPIBT: `adds/refference/epibt_full.md`; LoRR 2024: `adds/refference/LLoR_2024.md`; paper: `adds/refference/2511.09193v2.pdf`.
- Sơ đồ PIBT→EPIBT đã dùng ở §5.5: `Hinhve/12_pibt_to_epibt_path.drawio.png`.
