# Kế hoạch R7 - Rà soát, sửa lỗi trình bày và đối chiếu quy định ĐATN

Ngày lập: 2026-06-24
Repo/nhánh: `F:\DATN`, branch `report`
Đối tượng: `adds/report/20225808_DuongQuangDong_2025.2/` (PDF hiện tại: 50 trang, build XeLaTeX OK)
Quy định đối chiếu: `adds/report/datn-report-rules-agent.md`, `adds/report/quy-dinh-viet-quyen-DATN.md`
Nguồn diagram: `adds/PLAN/R2_DIAGRAMS_V2/` (drawio đã export, mermaid CHƯA export)

## 0. Tóm tắt nhanh

PDF build được nhưng còn nhiều lỗi trình bày và sót nội dung template. Sáu nhóm việc:

| Nhóm | Vấn đề | Mức độ |
| --- | --- | --- |
| A | Nội dung template thừa (chương "Lưu ý TLTK", Phụ lục A/B hướng dẫn viết) | Nặng |
| B | Trang trắng + danh mục nằm trang chẵn + bố cục front matter | Trung bình |
| C | Thiếu 6 diagram mermaid (class + sequence) chưa export, chưa chèn | Nặng (nội dung) |
| D | Lạm dụng liệt kê; phải chuyển sang đoạn văn (trừ các bước tuần tự) | Nặng (văn phong) |
| E | Lệch căn chỉnh (bảng rộng, thụt đầu mục, figure) | Trung bình |
| F | Đối chiếu checklist quy định (logo, citation, tên đề tài, dung lượng) | Bắt buộc |

## 1. Mục tiêu

1. Xóa sạch nội dung template không liên quan để quyển chỉ còn nội dung thật của đề tài.
2. Sửa lỗi phân trang: không còn trang trắng vô nghĩa, danh mục hình/bảng đặt hợp lý.
3. Bổ sung 6 diagram mermaid còn thiếu (class diagram + các sequence diagram).
4. Viết lại các đoạn đang liệt kê thành văn xuôi, chỉ giữ liệt kê cho quy trình theo bước.
5. Sửa căn chỉnh bảng/hình/tiêu đề.
6. Chạy hết checklist trong 2 file quy định, đảm bảo PASS.

## 2. Nhóm A - Xóa nội dung template thừa

### A1. Chương "MỘT SỐ LƯU Ý VỀ TÀI LIỆU THAM KHẢO" (template, phải xóa)

- Vị trí: `DoAn.tex` dòng 306-308 (`\chapter*{...}` + `\subfile{Chuong/7_Luu_y_tai_lieu_tham_khao}`).
- Hiện trạng: file `7_Luu_y_tai_lieu_tham_khao.tex` toàn văn hướng dẫn template, có placeholder `<...>` và các `\cite{hovy1993automated}`, `\cite{peterson2007computer}`, `\cite{NguyenThucHai}` trỏ tới bib đã bị xóa → đây là chương rác xuất hiện ngay trước Tài liệu tham khảo (trang 41-42 PDF).
- Cách sửa: xóa 3 dòng include chương này trong `DoAn.tex`; có thể xóa luôn file `7_Luu_y_tai_lieu_tham_khao.tex`.
- Hệ quả tốt: dọn luôn các citation undefined gây 9 warning bibtex.

### A2. Phụ lục A "HƯỚNG DẪN VIẾT ĐỒ ÁN TỐT NGHIỆP" (template, phải xóa)

- Vị trí: `DoAn.tex` dòng 333-334; file `Phu_luc_A.tex` (trang 45-49 PDF).
- Hiện trạng: toàn bộ là hướng dẫn viết của trường (quy định chung, ngành học, ví dụ bullet, ví dụ bảng, công thức mẫu), kèm 3 ảnh `IoT.png`, `Bia.PNG`, `GayBia.png`.
  - `IoT.png` vi phạm RULE TECH-04 (ảnh mang tính logo/minh họa chung).
  - Dùng `\cite{scott2013sdn}`, `\cite{ashton2009internet}` (bib đã xóa) → undefined.
- Cách sửa (chọn 1):
  - **(A2a, khuyến nghị) Xóa hẳn Phụ lục A** và thay bằng phụ lục có giá trị: bảng kết quả backtest đầy đủ per-agent (từ `backtest_agents_*.csv`) hoặc giao thức JSON TCP (`hello`/`plan_step`/`plan_result`).
  - (A2b) Nếu muốn giữ khung phụ lục, xóa toàn bộ text hướng dẫn + 3 ảnh template, viết nội dung phụ lục thật.

### A3. Phụ lục B "ĐẶC TẢ USE CASE" (template, xử lý)

- Vị trí: `DoAn.tex` dòng 336-337; file `Phu_luc_B.tex` (trang 50 PDF).
- Hiện trạng: text hướng dẫn template về đặc tả use case.
- Cách sửa: vì 3 use case chính (UC1, UC3, UC4) đã đặc tả trong Chương 2, hoặc (i) xóa Phụ lục B, hoặc (ii) chuyển các use case phụ (UC2 chơi LAN, UC5 xem kết quả) xuống đây để Chương 2 gọn hơn.

### A4. Dọn các file front-matter template chưa dùng

- Kiểm tra và làm sạch nếu còn text hướng dẫn: `0_5_Danh_muc_viet_tat.tex`, `0_6_Thuat_ngu.tex`, `Tu_viet_tat.tex`, `Bia_lot.tex`, `0_2_Loi_cam_on.tex`.
- `Tu_viet_tat.tex` (glossary/acronym): điền các từ viết tắt thật của đề tài (MAPF, PIBT, A*, TCP, LNS2, URP, CSV, LOS, HUD...).

## 3. Nhóm B - Trang trắng và bố cục front matter

### Hiện trạng (đánh số trang theo PDF 50 trang)

- Trang 10: TRẮNG hoàn toàn (chèn bởi `\cleardoublepage` sau Mục lục, dòng 224 `DoAn.tex`).
- Trang 12: DANH MỤC BẢNG BIỂU rơi vào **trang chẵn**.
- Trang 44: gần trắng (trang ngăn `\appendixpage`, chỉ có chữ "PHỤ LỤC").

### Nguyên nhân

Tài liệu để chế độ `twoside` (dòng 1 `DoAn.tex`), `\cleardoublepage` ép nội dung sang trang lẻ nên sinh trang trắng verso. Vì quyển chỉ nộp bản mềm (RULE SUB-07, không in 2 mặt), trang trắng không cần thiết.

### Cách sửa (chọn 1)

- **(B1, khuyến nghị) Chuyển sang `oneside`**: đổi `\documentclass[...,twoside]` thành `oneside`. Khi đó không còn trang verso trắng; danh mục hình/bảng chảy liền mạch. Lề trái 3.5cm vẫn giữ (không mirror), phù hợp bản mềm. Kiểm tra lại header/footer `fancyhdr` (đang dùng `[RE,LO]`) cho hợp `oneside`.
- (B2) Giữ `twoside` nhưng thay các `\cleardoublepage` thừa ở front matter bằng `\clearpage`, và chủ động bố trí để Mục lục / Danh mục hình / Danh mục bảng / Danh mục thuật ngữ mỗi mục bắt đầu trang mới gọn, không chèn trang trắng.
- Sau khi sửa, build lại và xác nhận: không còn trang `[0 ký tự]`, danh mục hình/bảng không kẹt lẻ loi ở trang chẵn cuối.

## 4. Nhóm C - Bổ sung 6 diagram mermaid còn thiếu

### Diagram chưa export (chỉ có `.mmd`, chưa có PDF/PNG)

| File mermaid | Loại | Chương dùng |
| --- | --- | --- |
| `08_pathfinding_class_diagram.mmd` | Class diagram | Ch4 - Thiết kế lớp |
| `09_astar_replan_sequence.mmd` | Sequence | Ch4/Ch5 - A* replan |
| `10_pibt_csharp_sequence.mmd` | Sequence | Ch4/Ch5 - PIBT C# |
| `11_pibt_tcp_epibt_sequence.mmd` | Sequence | Ch4/Ch5 - PIBT TCP |
| `05_internet_create_room_sequence.mmd` | Sequence | Ch2 - tạo phòng LAN |
| `06_internet_ready_start_sequence.mmd` | Sequence | Ch2 - ready/start LAN |

### Cách export (mmdc chưa cài, npx có sẵn — npm 11.13)

```bash
cd adds/PLAN/R2_DIAGRAMS_V2/mermaid
for f in *.mmd; do
  npx -y @mermaid-js/mermaid-cli -i "$f" -o "../export/pdf/${f%.mmd}.pdf" -b transparent
done
```

- Lần đầu npx sẽ tải `@mermaid-js/mermaid-cli` + Chromium (puppeteer), cần mạng. Nếu tải Chromium lỗi, fallback: mở từng `.mmd` trên https://mermaid.live rồi export PDF/SVG thủ công.
- Sau export, copy sang `Hinhve/` với tên gọn: `class_pathfinding.pdf`, `seq_astar_replan.pdf`, `seq_pibt_csharp.pdf`, `seq_pibt_tcp.pdf`, `seq_lan_create_room.pdf`, `seq_lan_ready_start.pdf`.

### Chèn vào báo cáo

- Ch4 Thiết kế lớp: thêm class diagram `class_pathfinding.pdf` (thay vì chỉ mô tả 3 lớp bằng chữ).
- Ch4 hoặc Ch5: thêm 3 sequence diagram A*/PIBT/PIBT-TCP minh họa luồng replan/tick.
- Ch2: thêm 2 sequence diagram LAN (đang chỉ có activity diagram).
- Mỗi hình: `[htbp]`, `\caption`, `\label`, `\ref` trong text, ảnh đủ lớn để đọc nhãn (RULE FMT-04).

## 5. Nhóm D - Chuyển liệt kê sang đoạn văn

### Nguyên tắc (theo yêu cầu)

Chỉ giữ liệt kê khi nội dung là **các bước tuần tự** (quy trình, luồng sự kiện use case, thứ tự chạy). Mọi liệt kê dạng phân loại/đặc điểm/nhận xét phải viết thành đoạn văn.

### Hiện trạng số khối liệt kê

| Chương | itemize | enumerate | Ghi chú |
| --- | ---: | ---: | --- |
| 1 Giới thiệu | 0 | 2 | "3 nhóm sản phẩm" + "mục tiêu" → chuyển đoạn văn |
| 2 Khảo sát | 1 | 0 | "2 tác nhân" → đoạn; giữ luồng sự kiện trong bảng UC |
| 3 Công nghệ | 1 | 0 | xem lại, ưu tiên đoạn |
| 4 Thiết kế/Đánh giá | 5 | 3 | **nặng nhất** — xử lý kỹ |
| 5 Giải pháp | 0 | 0 | đã dùng đoạn (Bài toán/Giải pháp/Kết quả) — giữ |
| 6 Kết luận | 0 | 3 | "kết quả chính", "hướng ngắn hạn", "hướng dài hạn" → đoạn |

### Hướng xử lý cụ thể

- **Giữ liệt kê** (hợp lệ vì là bước tuần tự): luồng sự kiện trong 3 bảng đặc tả use case (Ch2); thứ tự boot/scenario nếu mô tả theo bước.
- **Chuyển sang đoạn văn**:
  - Ch1: 3 nhóm sản phẩm/nghiên cứu liên quan; 4 mục tiêu (viết thành 1-2 đoạn dẫn dắt nhân-quả theo LOG-01).
  - Ch4: 5 tầng kiến trúc, 3 biến thể lớp, 2 dạng CSDL, 3 màn hình giao diện, 5 điểm "kết quả đạt được", 5 điểm "nhận xét đánh giá" → viết thành đoạn (mỗi nhận xét là một đoạn phân tích, dẫn số liệu từ bảng).
  - Ch6: kết quả chính, hướng phát triển → viết đoạn; nếu thật cần thì tối đa một liệt kê ngắn cho "hướng phát triển".
- Sau khi viết lại, đếm lại: mục tiêu mỗi chương ≤ 1 khối liệt kê (trừ Ch2 có các bảng UC).

## 6. Nhóm E - Lệch căn chỉnh

- Build hiện tại: 0 Overfull \hbox (không tràn lề ngang nghiêm trọng), 26 Underfull (chủ yếu do `\\` và `\cleardoublepage`, mức nhẹ).
- Cần rà mắt trên PDF:
  - Bảng đặc tả use case dùng `p{10cm}` + cột nhãn: kiểm tra có lệch/đụng lề phải không; nếu cần đổi `p{9.5cm}` hoặc dùng `tabularx` với `\textwidth`.
  - Bảng kết quả static/dynamic (6 cột): căn giữa trang, cân nhắc `\small` đã có; kiểm tra cân đối.
  - Tiêu đề mục: `titlespacing` đang thụt `\subsection` 30pt, `\subsubsection` 50pt — nhìn có thể "lệch"; cân nhắc giảm về 0-15pt cho cân với heading chương căn giữa.
  - Hình: đảm bảo `\centering` và `width` ≤ `\textwidth`; class/sequence diagram dài có thể cần `width=\textwidth` hoặc xoay ngang (`pdflscape`).
- Trang bìa: kiểm tra căn giữa các dòng, khoảng cách GVHD/Khoa/Trường.

## 7. Nhóm F - Đối chiếu checklist quy định

Chạy qua `AGENT_VALIDATION_CHECKLIST` trong `datn-report-rules-agent.md`:

| Rule | Nội dung | Trạng thái cần đạt |
| --- | --- | --- |
| FMT-02 | Không `[H]` | Đã đạt ở Ch1-6 (đã kiểm). Kiểm nốt phụ lục sau khi dọn |
| FMT-03 | Không "hình trên/dưới đây" | Còn 1 chỗ trong `Phu_luc_A` ("như hình dưới đây") → mất khi xóa phụ lục |
| FMT-04 | Ảnh có chữ đủ lớn | Kiểm class/sequence diagram sau khi chèn |
| TECH-04 | Không ảnh logo | Xóa `IoT.png` ở Phụ lục A (Nhóm A2) |
| INT-03 | Mọi claim ngoài có citation | Đã thêm bib MAPF; rà số liệu/định nghĩa Ch1-3 |
| SUB-06 | Tên đề tài khớp qldt | Đối chiếu chính xác với tên trên qldt (hiện bìa: "Ứng dụng bài toán Multi-Agent Pathfinding trong game bắn xe tăng nhiều người chơi") |
| SUB-05 | Tên file `MSSV_Hoten_Ky` | Đặt `20225808_DuongQuangDong_20242` (xác nhận mã kỳ) |
| SUB-01 | < 30MB | PDF hiện 0.87MB — đạt |
| RES-02 | Mỗi chức năng có ảnh + chữ | Chương 4 "Minh họa chức năng" hiện thiếu screenshot gameplay → cân nhắc bổ sung ảnh chụp màn hình thật |

Lưu ý liêm chính (INT-01/02): nội dung do sinh viên tự đọc lại và diễn đạt; bản mềm sẽ qua COOPY. Cần tự kiểm similarity trước khi nộp.

## 8. Thứ tự thực hiện đề xuất

1. **Nhóm A** (xóa template) trước — giảm ngay số trang thừa và dọn citation lỗi.
2. **Nhóm C** (export + chèn mermaid) — bổ sung nội dung diagram.
3. **Nhóm D** (viết lại đoạn văn) — phần tốn công nhất, làm theo từng chương 1→6.
4. **Nhóm B** (phân trang/oneside) — làm sau khi nội dung ổn định để không phải canh lại nhiều lần.
5. **Nhóm E** (căn chỉnh) — tinh chỉnh cuối.
6. **Nhóm F** (checklist) — rà soát lần cuối, build sạch, đối chiếu tên đề tài/tên file.

Mỗi nhóm xong build lại XeLaTeX (+ bibtex) và kiểm tra PDF.

## 9. Definition of Done

- [x] Không còn chương/phụ lục nội dung template (đã xóa chương "Lưu ý TLTK" + file; Phụ lục A → "Kết quả backtest chi tiết", Phụ lục B → "Đặc tả use case bổ sung").
- [x] 0 trang trắng vô nghĩa trong front matter; danh mục hình/bảng/thuật ngữ chảy liền (chuyển `oneside`). Còn 1 trang ngăn "PHỤ LỤC" (divider hợp lệ, không phải trang trắng).
- [x] 6 diagram mermaid đã export (chrome-headless-shell + puppeteer-config) và chèn: class diagram + 2 sequence LAN ở Ch4, 3 sequence A*/PIBT/TCP ở Ch5; đều có caption/label/ref.
- [x] 0 khối liệt kê trong cả 6 chương (đã viết thành đoạn văn); chỉ giữ luồng sự kiện trong bảng use case.
- [x] 0 Overfull/Underfull \hbox; render kiểm tra class + sequence diagram vừa trang, đọc rõ.
- [x] bibtex 0 warning; 0 trang có `??` (mọi ref resolve).
- [x] Việt hóa nhãn: caption "Hình"/"Bảng", header "CHƯƠNG" (sửa hồi quy do bỏ gói `vietnam`).
- [x] Build XeLaTeX sạch; PDF 49 trang, 0.6MB (< 30MB).
- [ ] **Cần người dùng xác nhận**: tên đề tài khớp chính xác qldt; mã kỳ học cho tên file nộp `20225808_DuongQuangDong_<kỳ>`; bổ sung screenshot gameplay thật cho RES-02 (mục "Minh họa chức năng").

## 10. Rủi ro

- Export mermaid qua npx cần tải Chromium (~mạng + thời gian); có fallback mermaid.live.
- Chuyển `twoside`→`oneside` ảnh hưởng header/footer và đánh số — cần build kiểm tra lại toàn bộ.
- Viết lại đoạn văn nhiều → phải giữ đúng số liệu đã chốt (không đổi nội dung kỹ thuật, chỉ đổi cách trình bày).
- Bổ sung screenshot gameplay (RES-02) cần chụp từ Unity — nếu chưa có, ghi nhận là việc cần làm trước khi nộp.
