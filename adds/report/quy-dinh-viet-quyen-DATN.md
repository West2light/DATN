# Quy định viết quyển Đồ án tốt nghiệp (ĐATN)

> Tổng hợp từ slide "Một số lưu ý trong quá trình chuẩn bị và triển khai ĐATN" — TS. Trịnh Thành Trung, Trường CNTT&TT, Đại học Bách khoa Hà Nội — Kỳ 2025.2

---

## 1. Cấu trúc quyển báo cáo

- **Tham khảo template** chính thức của trường (Template và hướng dẫn viết quyển đồ án tốt nghiệp).
- Nên tuân thủ theo cấu trúc đã được cung cấp. Tuy nhiên, với các đề tài đặc thù (ví dụ: đề tài nghiên cứu, đề tài game...), sinh viên **có thể thiết kế khung báo cáo cho phù hợp**.
  - ⚠️ **QUAN TRỌNG: Việc thay đổi cấu trúc phải được GVHD đồng ý trước.**
- Format trình bày (ví dụ: dùng itemize hay không) có thể linh hoạt thay đổi để phù hợp với nội dung.
- **Bắt buộc sử dụng LaTeX** để soạn thảo (xem thêm "Hướng dẫn viết đồ án bằng LaTeX").

## 2. Liêm chính học thuật — Không sao chép

- **Không được sao chép nội dung từ bất kỳ nguồn nào**, kể cả khi đã dịch từ tiếng Anh sang tiếng Việt.
- Sinh viên phải **tự viết toàn bộ** nội dung quyển ĐATN bằng văn phong của chính mình.
- Nếu có tham khảo tài liệu nào, **bắt buộc phải trích dẫn nguồn (citation)** đầy đủ.
- Quyển đồ án sẽ được **kiểm tra trùng lặp nội dung bằng hệ thống COOPY** (tích hợp sẵn trong hệ thống Quản lý đào tạo - qldt) trước khi nộp.

## 3. Nội dung phải có tính logic

- Các phần trong báo cáo phải liên kết chặt chẽ, mạch lạc với nhau.
  - Ví dụ: nếu phần "Đặt vấn đề" trình bày theo dạng "Vì... [lý do]", thì phần "Mục tiêu" cần tiếp nối logic đó theo kiểu "...cho nên [mục tiêu đặt ra]".
- **Không thể phó mặc nội dung cho AI viết hộ** — đây là sản phẩm trí tuệ và công sức của chính sinh viên.

## 4. Phần Cơ sở lý thuyết / Công nghệ sử dụng

- **Không** chép nguyên tài liệu hướng dẫn (docs) hay tài liệu lập trình vào báo cáo.
- Trọng tâm cần làm rõ là: **vì sao lựa chọn lý thuyết/công nghệ này** cho đồ án của mình (lý do, sự phù hợp).
- Cần chỉ ra rõ **sự liên quan và cách ứng dụng cụ thể** của lý thuyết/công nghệ đó vào đồ án.
- **Không** đưa ảnh logo (của công nghệ, công ty, framework...) vào báo cáo.

## 5. Phần Kết quả đạt được

- Nguyên tắc: **người đọc chỉ cần đọc báo cáo là phải hiểu được sản phẩm của đồ án**, không cần xem demo trực tiếp mới hiểu.
- Mỗi chức năng của sản phẩm cần được trình bày bằng **cả ảnh minh họa sản phẩm và nội dung mô tả bằng chữ** (không chỉ ảnh, không chỉ chữ).

## 6. Trình bày văn bản (LaTeX)

### Công cụ soạn thảo
- Có thể dùng công cụ trực quan như **Overleaf**.
- Hoặc dùng công cụ online khác rồi copy code (công thức toán, bảng biểu...) vào LaTeX.

### Hình ảnh, sơ đồ (diagram)
- Xuất ảnh ở **độ phân giải lớn** dưới định dạng PNG, hoặc PDF (nếu được).
- LaTeX hỗ trợ `\includegraphics` với file PDF — nên ưu tiên dùng PDF cho hình vẽ vector để giữ chất lượng nét.

### Khoảng trắng trong trang
Tránh để trang bị trống nhiều do hình/bảng tự "nhảy" vị trí. Hai cách xử lý:
1. Chủ động viết thêm nội dung hoặc thêm ảnh để lấp chỗ trống, **hoặc**
2. **Không sử dụng thẻ `[H]`** ép vị trí hình ảnh — để LaTeX tự sắp xếp hình/bảng (thường trôi về đầu/cuối trang một cách tự nhiên).

### Tham chiếu hình/bảng
- **Không viết** kiểu "Hình ở dưới/ở trên đây..." (vì vị trí có thể thay đổi khi build lại).
- **Dùng lệnh `\ref`** để tham chiếu chính xác đến số thứ tự hình/bảng/mục khi cần nhắc tới.

### Kích thước hình ảnh
- **Ảnh có chứa chữ/text** (ví dụ: ảnh code, ảnh sơ đồ có nhãn): phải đủ lớn để **đọc được rõ ràng**.
- **Ảnh chụp giao diện (UI screenshot)**: không cần phóng quá to, vừa đủ minh họa.

## 7. Giới hạn dung lượng nộp bài

- **Tổng dung lượng đồ án nộp lên hệ thống phải nhỏ hơn 30MB.**
- Mục đích của việc nộp: làm **minh chứng** cho công việc đã thực hiện, không phải nộp toàn bộ dự án.
- **Quan trọng nhất là mã nguồn (source code) do chính sinh viên tự viết.**
- Đối với các thư viện/asset có dung lượng lớn (không phải code tự viết): 
  - Viết tài liệu hướng dẫn cách thiết lập môi trường (README/setup guide), và/hoặc
  - Upload riêng lên Google Drive / OneDrive và để link tham khảo.

## 8. Cập nhật bản mềm sau khi nộp

- Có thể cập nhật báo cáo khi được **GV phản biện hoặc Hội đồng yêu cầu** chỉnh sửa/bổ sung.
- Thời hạn cho phép cập nhật: **trong vòng 2 tuần sau khi bảo vệ**.
- Khi cập nhật, cần **liên hệ GVHD để thay đổi trạng thái đồ án** trên hệ thống qldt.

## 9. Quy trình & mốc thời gian nộp quyển (tham khảo)

| Bước | Ghi chú |
|---|---|
| Up đồ án lên hệ thống qldt | **Không** nộp bản cứng |
| Tên file | Định dạng: `MSSV_Họ và tên SV_Kỳ học` (VD: `20201234_NguyenVanA_20232`) |
| Cập nhật lại file | Nếu nộp lại, đổi tên file kiểu `..._V1`, `..._V2`... |
| Tên đồ án | Phải khớp **hoàn toàn** giữa hệ thống qldt và trong quyển báo cáo |
| Kiểm tra trùng lặp | Dùng hệ thống COOPY tích hợp trong qldt, kiểm tra cả bản tiếng Anh và tiếng Việt |
| Ký số | GVHD thực hiện ký số trên hệ thống theo quy định của trường |
| Mã nguồn quá lớn | Nếu kích thước vượt 30MB, liên hệ bộ phận thư viện để được hỗ trợ giảm dung lượng |

## 10. Tiêu chí chấm điểm liên quan đến quyển báo cáo (trọng số trong tổng điểm 100)

| Tiêu chí | Điểm tối đa |
|---|---|
| **1. Chất lượng sản phẩm** | 40 |
| 1.1 Tính độc đáo / tính thời sự của đề tài | 5 |
| 1.2 Quy mô, khối lượng công việc đã thực hiện | 10 |
| 1.3 Độ khó, độ phức tạp của vấn đề | 10 |
| 1.4 Khả năng ứng dụng / giá trị khoa học | 5 |
| 1.5 Độ hoàn thiện của sản phẩm | 10 |
| **2. Chất lượng báo cáo** | 40 |
| 2.1 Tính hợp lý của bố cục | 5 |
| 2.2 Tính đầy đủ, đúng đắn của nội dung | 20 |
| 2.3 Văn phong, hình thức trình bày (chính tả, hình vẽ, bảng biểu, thuật ngữ) | 10 |
| 2.4 Mức độ tin cậy nội dung (đầy đủ tài liệu tham khảo, trích dẫn) | 5 |
| **3. Kết quả kiểm tra sau phản biện** | 20 |
| 3.1 Tính hợp lý, đúng đắn khi trả lời câu hỏi phản biện | 10 |
| 3.2 Kỹ năng trình bày, demo sản phẩm | 10 |
| **4. Điểm thưởng** (tác giả chính công bố khoa học liên quan, hoặc sản phẩm đã triển khai thực tế thành công) | +5 |

> Lưu ý: Tiêu chí 2.2 (tính đầy đủ và đúng đắn về nội dung) chiếm trọng số cao nhất (20/100 điểm) — đây cũng là tiêu chí liên quan trực tiếp đến chất lượng viết quyển báo cáo.

---

*Tài liệu này được tổng hợp lại từ slide thuyết trình gốc, chỉ mang tính tham khảo. Sinh viên nên đối chiếu với văn bản chính thức và hướng dẫn từ GVHD/Khoa để đảm bảo tính cập nhật.*
