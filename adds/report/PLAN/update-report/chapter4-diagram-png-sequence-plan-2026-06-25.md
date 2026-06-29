# Kế hoạch sửa Chương 4: PNG, bố cục hình/text, sequence diagram — 2026-06-25

Phạm vi chính: `adds/report/20225808_DuongQuangDong_2025.2/Chuong/4_Ket_qua_thuc_nghiem.tex`.

Nguồn hình/diagram:
- Mermaid source: `adds/PLAN/R2_DIAGRAMS_V2/mermaid/`
- PNG export có sẵn: `adds/PLAN/R2_DIAGRAMS_V2/export/png/`
- Thư mục hình của báo cáo: `adds/report/20225808_DuongQuangDong_2025.2/Hinhve/`

Mục tiêu:
1. Chương 4 không còn `\includegraphics` trỏ tới PDF.
2. Hình trong Chương 4 phải đặt đúng đề mục, đúng thứ tự đọc, không dùng hình LAN cho đoạn đang nói Internet hoặc ngược lại.
3. Mỗi hình phải có đoạn dẫn trước và đoạn phân tích sau; tránh trang chỉ có hình, hoặc chỉ có chữ mà không có hình minh họa cần thiết.
4. Không để khoảng trắng lớn do float; ưu tiên PNG crop sát nội dung và bố cục figure vừa trang.
5. Bổ sung các sequence liên quan từ Mermaid, đặc biệt các luồng Internet, A*, PIBT C#, PIBT TCP/EPIBT.

---

## 0. Trạng thái thực hiện (cập nhật 2026-06-25)

**Đã hoàn tất** việc viết lại Chương 4 và đồng bộ Chương 5. Build XeLaTeX (`latexmk -xelatex DoAn.tex`)
chạy thành công, ra `DoAn.pdf` 65 trang; đã soát thủ công từng trang của Chương 4 trong PDF.

Quyết định cấu trúc cuối cùng (khác một chút so với mục 7 ban đầu — xem giải thích):

- **Sequence thuật toán (A*, PIBT C#, PIBT TCP/EPIBT) GIỮ NGUYÊN ở Chương 5**, không chuyển sang
  Chương 4. Lý do: nếu chuyển đi thì các mục 5.2/5.3/5.4 của Chương 5 sẽ thành "chỉ có chữ, không có
  hình" — đúng lỗi mà chính yêu cầu này đang muốn tránh. Thay vào đó Chương 4 mục "Thiết kế các lớp
  MAPF chính" **tham chiếu chéo** (`\ref`) tới ba sequence đó. Cách này: (a) không lặp hình ở hai
  chương; (b) Chương 4 giữ phần thiết kế tĩnh (biểu đồ lớp + luồng dữ liệu); (c) Chương 5 giữ phần
  hành vi động (sequence) + phân tích đóng góp.
- **Sequence Internet (tạo phòng, sẵn sàng/bắt đầu) ĐƯA VÀO Chương 4** mục "Thiết kế luồng mạng nhiều
  người chơi" — đây mới là các sequence thực sự còn thiếu. Mục này được viết lại theo hướng Internet
  (khớp sản phẩm "MULTIPLAYER INTERNET"), không còn dùng hình LAN.

### 0.1. Render Mermaid → PNG (đã làm)

`mmdc` (mermaid-cli) không cài sẵn nhưng có `npx` + chrome-headless-shell trong cache. Đã cài
`@mermaid-js/mermaid-cli` cục bộ với `PUPPETEER_SKIP_DOWNLOAD=true` rồi render `-b white -s 3`
(dùng `puppeteer-config.json` trỏ tới chrome-headless-shell có sẵn). 6 PNG đã sinh vào `Hinhve/`:

| Mermaid | PNG (đặt theo tên PDF cũ để đỡ phải đổi `\ref`) |
|---|---|
| `05_internet_create_room_sequence.mmd` | `seq_internet_create_room.png` (mới) |
| `06_internet_ready_start_sequence.mmd` | `seq_internet_ready_start.png` (mới) |
| `08_pathfinding_class_diagram.mmd` | `class_pathfinding.png` (thay PDF) |
| `09_astar_replan_sequence.mmd` | `seq_astar_replan.png` (thay PDF, dùng ở Ch5) |
| `10_pibt_csharp_sequence.mmd` | `seq_pibt_csharp.png` (thay PDF, dùng ở Ch5) |
| `11_pibt_tcp_epibt_sequence.mmd` | `seq_pibt_tcp.png` (thay PDF, dùng ở Ch5) |

### 0.2. Inventory hình Chương 4 sau khi sửa (đúng thứ tự đọc, đúng đề mục)

| Hình | File PNG | Đề mục | width |
|---|---|---|---|
| 4.1 | `arch_packages.png` | 4.1.2 Thiết kế tổng quan | 0.85 |
| 4.2 | `03_single_play_activity.drawio.png` | 4.2.1 Thiết kế giao diện | 0.92 |
| 4.3 | `class_pathfinding.png` | 4.2.2 Thiết kế các lớp MAPF | 0.72 |
| 4.4 | `07_mapf_data_flow.drawio.png` | 4.2.2 | 0.85 |
| 4.5 | `04_internet_host_join_activity.drawio.png` | 4.2.4 Luồng mạng | 0.92 |
| 4.6 | `seq_internet_create_room.png` | 4.2.4 | 0.95 |
| 4.7 | `seq_internet_ready_start.png` | 4.2.4 | 0.95 |
| 4.8–4.15 | screenshot menu/single/multi | 4.3.3 Minh họa chức năng | 0.80–0.85 |
| 4.16 | `lose_scene.png` + `win_scene.png` (subfigure a/b) | 4.3.3 | 0.48 mỗi ảnh |
| 4.17 | `13_backtest_workflow.drawio.png` | 4.4.1 Cấu hình thực nghiệm | 0.85 |
| 4.18 | `backtest_duration_static.png` | 4.4.2 Kết quả tĩnh | 0.85 |
| 4.19 | `backtest_replan_static.png` | 4.4.2 | 0.85 |
| 4.20 | `backtest_static_vs_dynamic.png` | 4.4.3 Kết quả động | 0.82 |
| 4.21 | `14_gcp_nginx_deployment.drawio.png` | 4.5 Triển khai | 0.92 |

Lỗi gốc người dùng nêu (Hình 4.3 nhắc ở 4.2.2 nhưng hiện ở 4.2.4) **đã được khắc phục**: nguyên nhân
là `class_pathfinding` cũ để `width=\textwidth` (PDF cao gần full trang) làm float dồn hàng; sau khi
giảm còn `0.72\textwidth` và đặt hình ngay sau đoạn gọi, Hình 4.3 và 4.4 đều nằm trong 4.2.2 (trang 27).

**Bổ sung (lượt 2):** thêm đoạn mô tả chi tiết NGAY DƯỚI từng hình cho 4.6, 4.7 (sequence Internet)
và 4.10, 4.11 (screenshot chọn map + in-round) — trước đó mấy hình này chỉ có caption, không có đoạn
phân tích kèm theo. Đồng thời **bỏ `\clearpage` trước mục 4.3** để Hình 4.7 không bị mồ côi một mình
trên một trang trắng (nội dung 4.3 giờ trôi xuống ngay dưới Hình 4.7, lấp hết khoảng trắng).

### 0.3. Đối chiếu quy định ĐATN cho Chương 4

| Rule | Trạng thái | Ghi chú |
|---|---|---|
| FMT-01 (PNG/PDF nét) | PASS | Toàn bộ Ch4 dùng PNG độ phân giải cao (scale 3) |
| FMT-02 (không `[H]`) | PASS | Tất cả figure/table dùng `[htbp]` |
| FMT-03 (không "ở trên/dưới", dùng `\ref`) | PASS | Đã thay "ở trên" còn sót bằng `\ref` |
| FMT-04 (ảnh có chữ đủ lớn) | PASS | Sequence/class/activity đọc rõ ở width đã chọn |
| RES-01/02 (mỗi chức năng có ảnh + chữ) | PASS | Mọi figure có đoạn dẫn trước + phân tích sau; mọi subsection thiết kế đều có hình hoặc bảng |
| TECH-04 (không logo) | PASS | Không có ảnh logo |
| LOG-02 / INT-02 (SV tự viết) | LƯU Ý | Văn bản là bản biên tập/mở rộng từ draft cũ; **sinh viên cần đọc lại và diễn đạt theo lời mình trước khi nộp** (COOPY sẽ kiểm tra trùng lặp) |

---

## 1. Hiện trạng đã rà soát

### 1.1. Chương 4 đang còn import PDF

Các vị trí cần thay:

| File | Dòng hiện tại | Hình đang dùng | Trạng thái |
|---|---:|---|---|
| `Chuong/4_Ket_qua_thuc_nghiem.tex` | 78 | `Hinhve/class_pathfinding.pdf` | Phải chuyển sang PNG từ Mermaid `08_pathfinding_class_diagram.mmd` |
| `Chuong/4_Ket_qua_thuc_nghiem.tex` | 118 | `Hinhve/seq_lan_create_room.pdf` | Sai định dạng; cần quyết định giữ LAN PNG hay thay bằng Internet sequence |
| `Chuong/4_Ket_qua_thuc_nghiem.tex` | 125 | `Hinhve/seq_lan_ready_start.pdf` | Sai định dạng; cần quyết định giữ LAN PNG hay thay bằng Internet sequence |
| `Chuong/4_Ket_qua_thuc_nghiem.tex` | 327 | `Hinhve/backtest_workflow.pdf` | Thay bằng `Hinhve/13_backtest_workflow.drawio.png` |
| `Chuong/4_Ket_qua_thuc_nghiem.tex` | 374 | `Hinhve/backtest_duration_static.pdf` | Thay bằng `Hinhve/backtest_duration_static.png` |
| `Chuong/4_Ket_qua_thuc_nghiem.tex` | 381 | `Hinhve/backtest_replan_static.pdf` | Thay bằng `Hinhve/backtest_replan_static.png` |
| `Chuong/4_Ket_qua_thuc_nghiem.tex` | 421 | `Hinhve/backtest_static_vs_dynamic.pdf` | Thay bằng `Hinhve/backtest_static_vs_dynamic.png` |

Ghi chú liên quan: Chương 5 cũng còn PDF sequence tại `seq_astar_replan.pdf`, `seq_pibt_csharp.pdf`, `seq_pibt_tcp.pdf`. Nếu chuyển ba sequence thuật toán sang Chương 4 thì Chương 5 nên tham chiếu lại thay vì giữ hình trùng.

### 1.2. PNG đã có nhưng chưa dùng hết

PNG đã có trong `Hinhve/` hoặc `PLAN/R2_DIAGRAMS_V2/export/png/`:

| PNG | Đề mục phù hợp trong Chương 4 | Cách dùng |
|---|---|---|
| `02_architecture_packages.drawio.png` / `arch_packages.png` | `Thiết kế tổng quan` | Giữ một tên thống nhất, ưu tiên file crop rõ nhất |
| `03_single_play_activity.drawio.png` | `Thiết kế giao diện` hoặc `Minh họa chức năng chính` | Dùng nếu Chương 4 cần mô tả luồng thao tác chơi đơn; tránh lặp y nguyên Chương 2 |
| `04_internet_host_join_activity.drawio.png` | `Thiết kế luồng mạng nhiều người chơi` | Dùng trước sequence Internet để mô tả luồng mức hoạt động |
| `07_mapf_data_flow.drawio.png` | `Thiết kế các lớp MAPF chính` | Đang dùng đúng vị trí, giữ |
| `12_pibt_to_epibt_path.drawio.png` | `Thiết kế các lớp MAPF chính` hoặc đoạn PIBT TCP/EPIBT | Bổ sung nếu cần nối mạch PIBT C# -> PIBT TCP/EPIBT |
| `13_backtest_workflow.drawio.png` | `Cấu hình thực nghiệm` | Thay `backtest_workflow.pdf` |
| `14_gcp_nginx_deployment.drawio.png` | `Triển khai` | Bổ sung hình triển khai GCP/Nginx/WebGL |

### 1.3. Mermaid source đã có nhưng thiếu PNG trong báo cáo

Các file Mermaid cần render sang PNG và copy vào `Hinhve/`:

| Mermaid | PNG đề xuất | Vị trí đề xuất |
|---|---|---|
| `05_internet_create_room_sequence.mmd` | `seq_internet_create_room.png` | `Thiết kế luồng mạng nhiều người chơi` |
| `06_internet_ready_start_sequence.mmd` | `seq_internet_ready_start.png` | `Thiết kế luồng mạng nhiều người chơi` |
| `08_pathfinding_class_diagram.mmd` | `class_pathfinding.png` | `Thiết kế các lớp MAPF chính` |
| `09_astar_replan_sequence.mmd` | `seq_astar_replan.png` | `Thiết kế các lớp MAPF chính`, sau mô tả A* |
| `10_pibt_csharp_sequence.mmd` | `seq_pibt_csharp.png` | `Thiết kế các lớp MAPF chính`, sau mô tả PIBT C# |
| `11_pibt_tcp_epibt_sequence.mmd` | `seq_pibt_tcp_epibt.png` | `Thiết kế các lớp MAPF chính`, sau mô tả PIBT TCP/EPIBT |

---

## 2. Bố cục Chương 4 sau khi sửa

### 2.1. `Thiết kế kiến trúc`

Giữ cấu trúc hiện tại nhưng chuẩn hóa hình:

1. Đoạn dẫn kiến trúc phân tầng.
2. Hình kiến trúc gói: dùng `Hinhve/arch_packages.png` hoặc đổi sang tên rõ hơn `Hinhve/02_architecture_packages.drawio.png`.
3. Sau hình cần có 1 đoạn phân tích ngắn: tầng nào phụ thuộc tầng nào, vì sao tách MAPF core khỏi UI/physics/backtest.

Không thêm quá nhiều hình ở mục này để tránh mở chương bằng một cụm hình nặng.

### 2.2. `Thiết kế giao diện`

Hiện mục này chỉ có text. Cần bổ sung hình hoặc cross-reference để không thành mục chỉ có chữ.

Phương án khuyến nghị:
- Thêm `03_single_play_activity.drawio.png` sau đoạn mô tả ba màn hình chính.
- Nếu ảnh này đã xuất hiện ở Chương 2 và không muốn lặp, thay bằng 1 đoạn tham chiếu tới Chương 2, rồi ở Chương 4 dùng ảnh chụp màn hình thực tế trong mục `Minh họa các chức năng chính`.

Quy tắc: nếu chèn activity ở Chương 4 thì phải viết rõ đây là "luồng giao diện đã triển khai", không phải use case khảo sát.

### 2.3. `Thiết kế các lớp MAPF chính`

Đây là mục đang thiếu sequence nhiều nhất. Thứ tự hình nên là:

1. `class_pathfinding.png` — tổng quan lớp và phụ thuộc.
2. `07_mapf_data_flow.drawio.png` — luồng dữ liệu từ map -> grid -> planner -> movement.
3. `seq_astar_replan.png` — sequence A* replan tick.
4. `seq_pibt_csharp.png` — sequence PIBT C# planner tick.
5. `seq_pibt_tcp_epibt.png` — sequence Unity -> TCP/WebRelay -> C++/EPIBT server.
6. `12_pibt_to_epibt_path.drawio.png` — chỉ thêm nếu cần hình tổng kết đường mở rộng PIBT -> EPIBT; đặt sau ba sequence thuật toán.

Mỗi sequence cần có đoạn text 4-6 câu:
- A*: khi nào replan, input/output chính, hạn chế phối hợp multi-agent.
- PIBT C#: planner dùng chung giải quyết xung đột theo tick, vì sao replan count cao hơn A*.
- PIBT TCP/EPIBT: tách planner khỏi Unity, khác biệt Native TCP và WebGL relay, độ trễ có thể ảnh hưởng số cell đi qua.

Nếu ba sequence này đã được trình bày ở Chương 5, chọn một trong hai cách:
- Khuyến nghị: chuyển hình về Chương 4 vì đây là phần thiết kế; Chương 5 chỉ nhắc lại bằng `Hình~\ref{...}` và tập trung vào đóng góp/kỹ thuật cải tiến.
- Phương án giữ nguyên Chương 5: Chương 4 chỉ thêm đoạn dẫn và cross-reference, nhưng vẫn phải thay PDF ở Chương 5 sang PNG để đồng bộ.

### 2.4. `Thiết kế luồng mạng nhiều người chơi`

Hiện mục này viết LAN nhưng yêu cầu và asset mới có nhiều Internet/GCP hơn. Cần tách rõ hai nhánh:

1. LAN/Fish-Net local: mô tả ngắn nếu vẫn còn hỗ trợ.
2. Internet/WebGL/GCP: mô tả chính, vì có Mermaid sequence và deployment PNG.

Thứ tự hình đề xuất:

1. `04_internet_host_join_activity.drawio.png` — activity mức cao Host/Join qua Internet.
2. `seq_internet_create_room.png` — sequence tạo phòng: Host -> UI -> Registry API -> GCP Helper -> WS Game Server.
3. `seq_internet_ready_start.png` — sequence join, ready, start match và replicate state.

Không dùng `seq_lan_create_room.pdf` và `seq_lan_ready_start.pdf` trong Chương 4 nữa. Nếu vẫn cần LAN, xuất lại thành PNG và đặt sau đoạn LAN riêng, nhưng không để caption LAN nằm cạnh đoạn đang nói Internet.

### 2.5. `Xây dựng ứng dụng` / `Minh họa các chức năng chính`

Mục này hiện có nhiều screenshot liên tiếp. Vấn đề dễ gặp là nhiều trang chỉ toàn hình. Cách sửa:

1. Nhóm screenshot theo luồng, mỗi nhóm 2-3 hình tối đa:
   - Menu + chọn single.
   - Chọn map/algorithm + in-round single.
   - Chọn vai trò multiplayer + lobby/host.
   - In-round multiplayer + win/lose.
2. Trước mỗi nhóm có đoạn dẫn 3-5 câu; sau mỗi nhóm có đoạn kết nối với use case hoặc thiết kế.
3. Dùng width nhỏ hơn cho ảnh đơn giản (`0.65\textwidth` đến `0.78\textwidth`) thay vì đồng loạt `0.9\textwidth`.
4. Với hai ảnh win/lose, có thể ghép cùng một `figure` bằng `minipage` để tránh hai float nhỏ tạo khoảng trắng riêng.

### 2.6. `Kiểm thử và đánh giá`

Thứ tự hình/table nên là:

1. Bảng cấu hình thực nghiệm.
2. `13_backtest_workflow.drawio.png` — workflow backtest.
3. Đoạn mô tả metric.
4. Bảng kết quả môi trường tĩnh.
5. `backtest_duration_static.png`, `backtest_replan_static.png` — mỗi hình có nhận xét ngay sau.
6. Bảng kết quả môi trường động.
7. `backtest_static_vs_dynamic.png` — sau bảng động.
8. Mục nhận xét tổng hợp.

Không để hai chart đứng liên tiếp mà không có text ở giữa. Nếu LaTeX đẩy chart tạo khoảng trắng, giảm width xuống `0.82\textwidth` hoặc đặt mỗi chart ngay sau đoạn nhận xét tương ứng.

### 2.7. `Triển khai`

Hiện mục triển khai chỉ có text. Cần thêm hình để tránh mục chỉ chữ:

1. Thêm `14_gcp_nginx_deployment.drawio.png`.
2. Đoạn trước hình: mô tả WebGL, Nginx/HTTPS, registry/helper/game server.
3. Đoạn sau hình: phân biệt local TCP cho backtest với Internet/WebGL deployment; ghi rõ backtest không chạy trên WebGL do giới hạn truy xuất file.

---

## 3. Quy tắc kỹ thuật khi sửa LaTeX

1. Chỉ dùng PNG trong Chương 4, không import PDF.
2. Tên hình trong `Hinhve/` nên nhất quán, không trộn tên ngắn và tên export nếu cùng một nội dung.
3. Không dùng `[H]` để ép hình. Nếu cần giảm khoảng trắng, xử lý bằng:
   - crop PNG sát nội dung;
   - giảm `width`;
   - thêm đoạn dẫn/nhận xét;
   - đặt hình đúng ngay sau đoạn gọi `Hình~\ref{...}`;
   - dùng `\clearpage` chỉ ở ranh giới section lớn nếu thật cần.
4. Mỗi hình bắt buộc có:
   - câu gọi hình trước figure;
   - `\caption` cụ thể, không chung chung;
   - `\label` ổn định;
   - đoạn giải thích sau figure.
5. Không để một subsection chỉ có text dài mà không có hình nếu mục đó đang mô tả thiết kế/luồng triển khai.
6. Không để một cụm 3 hình liên tiếp không có text ở giữa.

---

## 4. Thứ tự thực hiện

### Bước 1 — Render/copy hình PNG

1. Render Mermaid sang PNG:
   - `05_internet_create_room_sequence.mmd` -> `seq_internet_create_room.png`
   - `06_internet_ready_start_sequence.mmd` -> `seq_internet_ready_start.png`
   - `08_pathfinding_class_diagram.mmd` -> `class_pathfinding.png`
   - `09_astar_replan_sequence.mmd` -> `seq_astar_replan.png`
   - `10_pibt_csharp_sequence.mmd` -> `seq_pibt_csharp.png`
   - `11_pibt_tcp_epibt_sequence.mmd` -> `seq_pibt_tcp_epibt.png`
2. Copy các PNG draw.io cần dùng từ `PLAN/R2_DIAGRAMS_V2/export/png/` vào `Hinhve/` nếu chưa có.
3. Kiểm tra kích thước ảnh và crop:
   - ảnh chữ nhiều: ưu tiên width hiển thị `0.85\textwidth` đến `0.95\textwidth`;
   - ảnh screenshot: `0.65\textwidth` đến `0.9\textwidth` tùy độ chi tiết;
   - nếu ảnh có viền/chữ sát mép, thêm padding nhỏ vào PNG thay vì để LaTeX tạo khoảng trắng.

### Bước 2 — Sửa Chương 4 theo từng subsection

1. Thay toàn bộ đường dẫn `.pdf` bằng `.png`.
2. Viết lại `Thiết kế các lớp MAPF chính` theo chuỗi class -> data flow -> A* sequence -> PIBT C# sequence -> PIBT TCP/EPIBT sequence.
3. Viết lại `Thiết kế luồng mạng nhiều người chơi` để tách LAN và Internet; đưa hai sequence Internet vào đúng sau đoạn mô tả Internet.
4. Bổ sung hình triển khai GCP/Nginx vào mục `Triển khai`.
5. Sắp xếp lại screenshot trong `Minh họa các chức năng chính` thành các nhóm có text xen giữa.

### Bước 3 — Đồng bộ Chương 5 nếu cần

Nếu chuyển sequence A*/PIBT/PIBT TCP từ Chương 5 sang Chương 4:
1. Chương 5 không giữ lại hình trùng.
2. Chương 5 đổi đoạn "Hình..." thành tham chiếu lại label của Chương 4.
3. Nếu vẫn giữ hình ở Chương 5, bắt buộc đổi ba file PDF sang PNG tương ứng.

### Bước 4 — Build và soát PDF

1. Build `DoAn.pdf`.
2. Mở PDF và kiểm tra thủ công Chương 4:
   - không còn hình PDF;
   - không có trang trống lớn do float;
   - không có trang chỉ toàn hình nhiều hơn 1 figure lớn;
   - không có subsection chỉ toàn text khi có asset minh họa phù hợp;
   - hình nằm ngay sau đoạn gọi hình;
   - caption đúng nội dung và đúng đề mục.
3. Chạy kiểm tra text:
   - `rg -n "includegraphics.*\\.pdf" Chuong/4_Ket_qua_thuc_nghiem.tex`
   - `rg -n "includegraphics" Chuong/4_Ket_qua_thuc_nghiem.tex`
4. Nếu build báo `Overfull \hbox` hoặc hình tràn trang, giảm width từng hình thay vì đổi về PDF.

---

## 5. Checklist nghiệm thu

- [x] `Chuong/4_Ket_qua_thuc_nghiem.tex` không còn `.pdf` trong `\includegraphics`.
- [x] Các PNG Mermaid 05/06/08/09/10/11 đã có trong `Hinhve/`.
- [x] `class_pathfinding` dùng PNG và nằm trong mục MAPF class design.
- [x] `seq_internet_create_room` và `seq_internet_ready_start` nằm trong mục thiết kế mạng Internet/multiplayer, không bị caption LAN.
- [x] `seq_astar_replan`, `seq_pibt_csharp`, `seq_pibt_tcp` có đoạn dẫn và đoạn phân tích riêng (ở Chương 5; Chương 4 tham chiếu chéo).
- [x] `13_backtest_workflow.drawio.png` thay cho `backtest_workflow.pdf`.
- [x] Ba chart backtest dùng PNG.
- [x] `14_gcp_nginx_deployment.drawio.png` xuất hiện trong mục triển khai.
- [x] Không có cụm nhiều hình liên tiếp mà không có text xen giữa (đã giảm width, ghép win/lose bằng subfigure, chèn text giữa các chart).
- [x] PDF sau build không có khoảng trắng lớn bất thường ở Chương 4 (đã soát trang 24–40 của PDF).

---

## 6. Rủi ro và cách xử lý

| Rủi ro | Cách xử lý |
|---|---|
| Mermaid render PNG bị nhỏ hoặc chữ mờ | Render scale/dpi cao hơn, kiểm tra trực tiếp trong PDF |
| Hình sequence quá rộng | Giảm nội dung trong Mermaid hoặc đặt width `0.95\textwidth`, không xoay ngang nếu chưa cần |
| Trùng nội dung với Chương 5 | Chuyển hình về Chương 4 và Chương 5 tham chiếu lại |
| Float tạo khoảng trắng | Crop ảnh, giảm width, thêm text dẫn/nhận xét, chia cụm hình; không dùng `[H]` |
| Caption không khớp đề mục | Duyệt caption theo bảng mapping ở mục 2 trước khi build |

## 7. Quyết định mặc định khi triển khai

Nếu không có yêu cầu khác:
1. Chương 4 sẽ là nơi chính chứa các sequence thiết kế thuật toán và mạng.
2. Chương 5 chỉ giữ phân tích đóng góp, tham chiếu lại sequence từ Chương 4 để tránh lặp hình.
3. Toàn bộ hình mới dùng PNG trong `Hinhve/`.
4. Không dùng PDF cho figure ở Chương 4 và Chương 5 nếu sequence đó cùng bộ R2.
