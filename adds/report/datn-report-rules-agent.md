# RULESET: DATN_REPORT_WRITING_COMPLIANCE
# Scope: Quy tắc viết quyển Đồ án tốt nghiệp (ĐATN) - Trường CNTT&TT, ĐHBK Hà Nội
# Source: Slide "Một số lưu ý trong quá trình chuẩn bị và triển khai ĐATN", Kỳ 2025.2
# Purpose: Machine-readable ruleset for an agent to validate, review, or assist in
#          writing/editing a DATN (graduation thesis) report.
# Usage: Each rule has a unique ID. Agent should check generated/reviewed content
#        against each applicable rule and report PASS/FAIL/WARN with rule ID reference.

---

## RULE_GROUP: STRUCTURE

### RULE STR-01
- statement: Quyển báo cáo PHẢI dựa trên template chính thức của trường (do trường cung cấp).
- severity: REQUIRED
- exception: Đề tài đặc thù (nghiên cứu, game, ...) được phép thiết kế lại khung báo cáo.
- exception_condition: "phải được GVHD đồng ý trước khi áp dụng cấu trúc khác"
- agent_action: Nếu nội dung lệch khỏi template chuẩn, hỏi/ghi chú "cần xác nhận GVHD đã đồng ý cấu trúc thay thế".

### RULE STR-02
- statement: Format trình bày con (itemize, bullet, numbering...) có thể thay đổi linh hoạt để phù hợp nội dung.
- severity: OPTIONAL
- agent_action: Không cảnh báo nếu format khác template, miễn cấu trúc chương/mục chính vẫn hợp lý.

### RULE STR-03
- statement: Báo cáo PHẢI được soạn bằng LaTeX.
- severity: REQUIRED
- agent_action: Nếu input là định dạng khác (Word, Google Docs thuần), gắn cờ WARN "không đúng công cụ soạn thảo quy định".
- reference: "Hướng dẫn viết đồ án bằng LaTeX" (tài liệu trường cung cấp)

---

## RULE_GROUP: ACADEMIC_INTEGRITY

### RULE INT-01
- statement: KHÔNG được sao chép nguyên văn nội dung từ bất kỳ nguồn nào.
- severity: CRITICAL
- applies_to: ["toàn bộ quyển báo cáo"]
- note: "dù là dịch từ tiếng Anh sang tiếng Việt cũng tính là sao chép nếu không trích dẫn"
- agent_action: Nếu phát hiện đoạn văn giống cao với nguồn bên ngoài (paraphrase tối thiểu), gắn cờ CRITICAL_FAIL.

### RULE INT-02
- statement: Sinh viên PHẢI tự viết toàn bộ nội dung ĐATN.
- severity: CRITICAL
- agent_action: Khi hỗ trợ sinh viên, đóng vai trò gợi ý/chỉnh sửa/phản hồi — KHÔNG tạo ra toàn văn nội dung để sinh viên nộp nguyên xi mà không qua chỉnh sửa của chính họ. Nhắc nhở người dùng về rủi ro liêm chính học thuật nếu họ có ý định nộp nguyên văn nội dung do AI tạo.

### RULE INT-03
- statement: Mọi nội dung tham khảo từ nguồn khác PHẢI có trích dẫn (citation) đầy đủ.
- severity: REQUIRED
- agent_action: Khi review, kiểm tra mỗi claim/số liệu/định nghĩa lấy từ bên ngoài có citation đi kèm hay không.

### RULE INT-04
- statement: Bản mềm báo cáo sẽ bị kiểm tra trùng lặp bằng hệ thống COOPY (tích hợp trong qldt) trước khi nộp, kiểm tra cả bản tiếng Anh và tiếng Việt.
- severity: SYSTEM_CHECK
- agent_action: Thông báo cho người dùng rằng việc kiểm tra trùng lặp là bắt buộc và tự động; khuyến nghị tự kiểm tra similarity trước khi nộp chính thức.

---

## RULE_GROUP: CONTENT_LOGIC

### RULE LOG-01
- statement: Nội dung các phần PHẢI có tính logic, liên kết chặt chẽ với nhau.
- severity: REQUIRED
- example: |
    Nếu "Đặt vấn đề" viết dạng "Vì [lý do X]..."
    thì "Mục tiêu" phải tiếp nối dạng "...cho nên [mục tiêu Y]"
    => X và Y phải có quan hệ nhân-quả rõ ràng, không rời rạc.
- agent_action: Khi review, kiểm tra xem phần "Mục tiêu" có logic phái sinh trực tiếp từ phần "Đặt vấn đề"/"Lý do chọn đề tài" hay không.

### RULE LOG-02
- statement: KHÔNG được "phó mặc" nội dung báo cáo cho AI viết hộ toàn bộ.
- severity: CRITICAL
- agent_action: Agent hỗ trợ viết PHẢI giữ vai trò cố vấn/biên tập, khuyến khích sinh viên tự diễn đạt ý tưởng bằng lời văn của mình; tránh tạo toàn bộ chương/mục hoàn chỉnh để sinh viên copy-paste trực tiếp.

---

## RULE_GROUP: THEORY_TECH_SECTION

### RULE TECH-01
- statement: KHÔNG đưa nguyên tài liệu hướng dẫn / tài liệu lập trình (docs, manual) vào báo cáo.
- severity: REQUIRED
- agent_action: Nếu nội dung phần "Cơ sở lý thuyết/Công nghệ sử dụng" có dấu hiệu copy nguyên văn từ documentation chính thức (cú pháp, cấu trúc giống hệt docs), gắn cờ WARN.

### RULE TECH-02
- statement: Trọng tâm phần lý thuyết/công nghệ PHẢI là LÝ DO LỰA CHỌN (why), không phải mô tả thuần kỹ thuật (what/how chung chung).
- severity: REQUIRED
- agent_action: Kiểm tra mỗi công nghệ/lý thuyết được đề cập có đi kèm giải thích "vì sao chọn dùng trong đồ án này" hay không.

### RULE TECH-03
- statement: PHẢI chỉ rõ mối liên quan và cách ứng dụng cụ thể của lý thuyết/công nghệ vào chính đồ án của sinh viên.
- severity: REQUIRED

### RULE TECH-04
- statement: KHÔNG được chèn ảnh logo (công nghệ, công ty, framework, thương hiệu...) vào báo cáo.
- severity: REQUIRED
- agent_action: Khi review hình ảnh trong báo cáo, gắn cờ FAIL nếu phát hiện ảnh logo đơn thuần (không phải screenshot có ý nghĩa minh họa).

---

## RULE_GROUP: RESULTS_SECTION

### RULE RES-01
- statement: Người đọc CHỈ cần đọc báo cáo (không cần xem demo) là phải hiểu được sản phẩm của đồ án.
- severity: REQUIRED
- agent_action: Đánh giá tính tự-đầy-đủ (self-contained) của phần "Kết quả đạt được": mô tả chức năng có đủ chi tiết để người không xem demo vẫn hiểu không.

### RULE RES-02
- statement: Mỗi chức năng PHẢI được trình bày bằng CẢ hai: (a) ảnh minh họa sản phẩm VÀ (b) nội dung mô tả bằng văn bản.
- severity: REQUIRED
- agent_action: Kiểm tra từng chức năng được liệt kê có đủ cả ảnh + text mô tả không. Gắn cờ FAIL nếu chỉ có một trong hai.

---

## RULE_GROUP: LATEX_FORMATTING

### TOOLING
- recommended_tools: ["Overleaf (trực quan)", "công cụ online tạo công thức/bảng rồi copy code vào LaTeX"]

### RULE FMT-01
- statement: Hình ảnh/sơ đồ (diagram) PHẢI xuất ở định dạng PNG độ phân giải lớn, hoặc PDF nếu có thể.
- severity: REQUIRED
- preferred_format: "PDF (vector, giữ chất lượng nét khi includegraphics)"
- agent_action: Gắn cờ WARN nếu phát hiện ảnh độ phân giải thấp/pixelated được đề cập hoặc đính kèm.

### RULE FMT-02
- statement: KHÔNG dùng thẻ ép vị trí `[H]` cho hình/bảng trong LaTeX.
- severity: REQUIRED
- rationale: "Thẻ [H] ép hình/bảng vào đúng vị trí code, dễ gây khoảng trắng lớn không mong muốn."
- alternative_action: "Chủ động viết thêm text/thêm ảnh để lấp khoảng trống, để LaTeX tự sắp xếp float tự nhiên."
- agent_action: Khi review code LaTeX, nếu tìm thấy `\begin{figure}[H]` hoặc `\begin{table}[H]`, gắn cờ FAIL và đề xuất bỏ `[H]`.

### RULE FMT-03
- statement: KHÔNG dùng cụm từ định vị tuyệt đối kiểu "Hình ở dưới/ở trên đây..." khi nhắc đến hình/bảng/mục.
- severity: REQUIRED
- required_alternative: "Sử dụng lệnh `\\ref{...}` để tham chiếu số thứ tự chính xác."
- agent_action: Quét text tìm các cụm "hình dưới đây", "bảng trên đây", "ở trên", "ở dưới" gắn với hình/bảng → gắn cờ FAIL, đề xuất thay bằng `\ref`.

### RULE FMT-04
- statement: Ảnh có chứa văn bản/chữ (screenshot code, sơ đồ có nhãn chữ) PHẢI đủ lớn để đọc được rõ ràng.
- severity: REQUIRED
- check: "kích thước hiển thị đủ để chữ trong ảnh legible khi in/xem PDF"

### RULE FMT-05
- statement: Ảnh chụp giao diện (UI screenshot) thuần túy KHÔNG cần phóng to quá mức.
- severity: GUIDANCE
- rationale: "tránh lãng phí không gian trang khi nội dung ảnh không cần độ chi tiết cao"

---

## RULE_GROUP: SUBMISSION_CONSTRAINTS

### RULE SUB-01
- statement: Tổng dung lượng file đồ án nộp PHẢI nhỏ hơn 30MB.
- severity: HARD_LIMIT
- value: "< 30 MB"
- purpose: "làm minh chứng (evidence) cho công việc đã thực hiện, không phải nộp toàn bộ project"
- agent_action: Nếu hỗ trợ đóng gói nộp bài, kiểm tra tổng dung lượng < 30MB trước khi xác nhận hoàn tất.

### RULE SUB-02
- statement: Thành phần QUAN TRỌNG NHẤT trong gói nộp là mã nguồn (source code) DO SINH VIÊN TỰ VIẾT.
- severity: REQUIRED
- priority: HIGHEST

### RULE SUB-03
- statement: Thư viện/asset dung lượng lớn (không phải code tự viết) KHÔNG bắt buộc nộp trực tiếp.
- severity: OPTIONAL
- alternative_actions:
    - "Viết tài liệu hướng dẫn thiết lập môi trường (README/setup guide)"
    - "Upload riêng lên Google Drive/OneDrive và đính kèm link"

### RULE SUB-04
- statement: Nếu dung lượng vượt 30MB do kích thước file quyển (PDF) quá lớn, cần liên hệ bộ phận thư viện để được hỗ trợ giảm kích thước.
- severity: PROCEDURAL

### RULE SUB-05
- statement: Tên file nộp PHẢI theo định dạng: `MSSV_HọVàTênSV_KỳHọc`
- severity: REQUIRED
- example: "20201234_NguyenVanA_20232"
- versioning_rule: "Nếu cập nhật file mà hệ thống không cho ghi đè, thêm hậu tố _V1, _V2... (VD: 20201234_NguyenVanA_20232_V1)"

### RULE SUB-06
- statement: Tên đề tài/đồ án trên hệ thống qldt và trong quyển báo cáo PHẢI trùng khớp hoàn toàn (chính xác từng ký tự).
- severity: REQUIRED
- agent_action: So khớp chuỗi tên đề tài giữa metadata hệ thống và trang bìa/tiêu đề báo cáo; gắn cờ FAIL nếu có sai khác (kể cả dấu câu, viết hoa/thường).

### RULE SUB-07
- statement: KHÔNG nộp bản cứng (bản in giấy); chỉ up bản mềm lên hệ thống qldt.
- severity: REQUIRED

### RULE SUB-08
- statement: GVHD thực hiện ký số trên hệ thống theo quy định của trường (sinh viên không tự ký).
- severity: PROCEDURAL

---

## RULE_GROUP: POST_DEFENSE_UPDATE

### RULE UPD-01
- statement: Có thể cập nhật bản mềm báo cáo nếu được Phản biện hoặc Hội đồng yêu cầu chỉnh sửa.
- severity: CONDITIONAL
- trigger: "yêu cầu chính thức từ GV phản biện hoặc Hội đồng bảo vệ"

### RULE UPD-02
- statement: Thời hạn cập nhật báo cáo sau bảo vệ là TỐI ĐA 2 TUẦN kể từ ngày bảo vệ.
- severity: HARD_DEADLINE
- value: "2 weeks post-defense"

### RULE UPD-03
- statement: Khi cập nhật, PHẢI liên hệ GVHD để GVHD thay đổi trạng thái đồ án trên hệ thống qldt.
- severity: PROCEDURAL

---

## RULE_GROUP: GRADING_CRITERIA (context for content prioritization)
# Dùng để agent ưu tiên mức độ đầu tư khi hỗ trợ/review nội dung — phần có trọng số cao hơn cần được chú trọng kỹ hơn.

| criterion_id | name | max_score | weight_in_100 |
|---|---|---|---|
| 1   | Chất lượng sản phẩm (tổng)                          | -   | 40 |
| 1.1 | Tính độc đáo / tính thời sự của đề tài               | 10  | 5  |
| 1.2 | Quy mô, khối lượng công việc đã thực hiện            | 10  | 10 |
| 1.3 | Độ khó, độ phức tạp của vấn đề                       | 10  | 10 |
| 1.4 | Khả năng ứng dụng / giá trị khoa học                 | 10  | 5  |
| 1.5 | Độ hoàn thiện của sản phẩm                           | 10  | 10 |
| 2   | Chất lượng báo cáo (tổng)                            | -   | 40 |
| 2.1 | Tính hợp lý của bố cục                               | 10  | 5  |
| 2.2 | Tính đầy đủ và đúng đắn của nội dung                 | 10  | 20 |
| 2.3 | Văn phong, hình thức trình bày                       | 10  | 10 |
| 2.4 | Mức độ tin cậy nội dung (trích dẫn, tài liệu tham khảo) | 10 | 5 |
| 3   | Kết quả kiểm tra sau phản biện (tổng)                | -   | 20 |
| 3.1 | Trả lời câu hỏi phản biện đúng/đủ                    | 10  | 10 |
| 3.2 | Kỹ năng trình bày, demo sản phẩm                     | 10  | 10 |
| 4   | Điểm thưởng (công bố khoa học / sản phẩm triển khai thực tế) | - | +5 (bonus) |

- highest_weight_single_item: "2.2 Tính đầy đủ và đúng đắn của nội dung (weight=20)"
- agent_priority_note: "Khi hỗ trợ review/viết báo cáo, ưu tiên cao nhất cho tính đầy đủ & đúng đắn nội dung (2.2), sau đó là khối lượng công việc (1.2), độ phức tạp (1.3), và độ hoàn thiện sản phẩm (1.5)."

---

## AGENT_VALIDATION_CHECKLIST
# Quick-reference checklist để agent chạy qua khi review một bản thảo ĐATN

1. [ ] STR-01: Cấu trúc theo template, hoặc có xác nhận GVHD nếu khác?
2. [ ] STR-03: Soạn bằng LaTeX?
3. [ ] INT-01/02/03: Không sao chép nguyên văn, tự viết, có trích dẫn đầy đủ?
4. [ ] LOG-01: Đặt vấn đề → Mục tiêu có logic nhân-quả?
5. [ ] TECH-01/02/03/04: Phần lý thuyết tập trung vào "vì sao", không copy docs, không có logo?
6. [ ] RES-01/02: Mỗi chức năng có ảnh + text mô tả, tự đủ nghĩa không cần demo?
7. [ ] FMT-02: Không dùng `[H]` ép vị trí figure/table?
8. [ ] FMT-03: Không dùng "hình trên/dưới", dùng `\ref`?
9. [ ] FMT-04: Ảnh có chữ đủ lớn để đọc?
10. [ ] SUB-01: Tổng dung lượng < 30MB?
11. [ ] SUB-05/06: Tên file đúng định dạng, tên đề tài khớp giữa qldt và quyển?
12. [ ] SUB-07: Không có bản cứng, chỉ bản mềm?

---

## METADATA
- generated_from: "Slide PDF: Một số lưu ý trong quá trình chuẩn bị và triển khai ĐATN (Kỳ 2025.2)"
- institution: "Trường Công nghệ Thông tin và Truyền thông, Đại học Bách khoa Hà Nội"
- author_of_source: "TS. Trịnh Thành Trung, Phó Trưởng Văn phòng - Trường CNTT&TT"
- coverage: "Slide 14-19 (mục 'Một số lưu ý quyển báo cáo')"
- disclaimer: "Ruleset này dùng để hỗ trợ agent kiểm tra/tư vấn, không thay thế văn bản quy định chính thức của nhà trường. Khi có xung đột, văn bản gốc và hướng dẫn từ GVHD/Khoa có giá trị cao hơn."
