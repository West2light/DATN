# Kế hoạch chỉnh sửa báo cáo DATN — 2026-06-25

Nguồn báo cáo: `adds/report/20225808_DuongQuangDong_2025.2/`
Build: XeLaTeX qua `build.ps1` / `build.sh` (có chạy `bibtex` cho phần tài liệu tham khảo).

Năm việc cần sửa, theo đúng thứ tự yêu cầu. Mỗi mục ghi: hiện trạng (đã đọc code/tex/pdf), thay đổi cụ thể (file + vị trí + trước/sau), và điểm cần chốt.

---

## 1. Trang bìa: bỏ trang mẫu "Nguyễn Văn A" và gộp phần GVHD về một trang

### Hiện trạng (đã kiểm tra trên `DoAn.pdf`)
- **Trang 1–2** = `Bia.tex` (thông tin thật: DƯƠNG QUANG ĐÔNG, 20225808). Bảng *Giảng viên hướng dẫn / Khoa / Trường* bị **tràn xuống trang 2** → đây là "trang GHVD bị tách". Nguyên nhân: khoảng trắng dọc quá lớn cộng với tiêu đề 2 dòng:
  - `\\[4cm]` sau "ĐẠI HỌC BÁCH KHOA HÀ NỘI" (Bia.tex:10)
  - `\vspace{4cm}` trước bảng (Bia.tex:21)
  - `\\[4.5cm]` ở hàng cuối bảng trước "HÀ NỘI, 06/2026" (Bia.tex:29)
  - `Bia.tex` hiện **không có** dòng "Chữ kí GVHD".
- **Trang 3** = `Bia_lot.tex` vẫn còn nguyên **mẫu placeholder**: "NGUYỄN VĂN A", `nguyenvanabc@sis.hust.edu.vn`, "Công nghệ thông tin Việt-Nhật", "PGS. TS. Phạm Văn ABC", "Chữ kí GVHD", "HÀ NỘI, **06/2022**". Đây là "trang mẫu thừa". Trang này vừa khít **một trang** và có sẵn dòng chữ ký GVHD — chính là bố cục người dùng muốn áp dụng.

### Thay đổi
**a) `Bia.tex`** — đưa về một trang theo đúng bố cục mẫu "Nguyễn Văn A":
- Thêm dòng chữ ký GVHD vào bảng (giống Bia_lot.tex:32–33):
  ```latex
  \multicolumn{1}{c}{\textbf{Giảng viên hướng dẫn:}} & ThS. Nguyễn Tiến Thành \hspace{0.5cm} \underline{\hspace{3cm}} \\[0.5cm]
   & \multicolumn{1}{r}{Chữ kí GVHD} \\[0.5cm]
  ```
- Giảm khoảng trắng dọc để không tràn trang (đề xuất): `\\[4cm]`→`\\[2.5cm]` (sau tên trường), `\vspace{4cm}`→`\vspace{2cm}`, `\\[4.5cm]`→`\\[2.5cm]` (hàng "Trường:"). Sẽ tinh chỉnh sau lần build đầu để vừa khít một trang A4.

**b) `Bia_lot.tex`** — thay toàn bộ nội dung placeholder bằng thông tin thật (xóa "Nguyễn Văn A"):
- Tiêu đề: "Ứng dụng bài toán Multi-Agent Pathfinding trong game bắn xe tăng nhiều người chơi"
- "DƯƠNG QUANG ĐÔNG" + "20225808"
- "Chương trình đào tạo: Kỹ thuật phần mềm"
- GVHD: "ThS. Nguyễn Tiến Thành" (giữ dòng "Chữ kí GVHD"), Khoa "Kỹ thuật máy tính", Trường "Công nghệ thông tin và Truyền thông", "HÀ NỘI, 06/2026".

### Điểm cần chốt
- **Giữ 2 trang bìa hay 1?** Mẫu HUST chuẩn có *bìa ngoài* (`Bia`) + *bìa lót* (`Bia_lot`, có chữ ký GVHD). **Khuyến nghị:** giữ cả hai, đều dùng thông tin thật, mỗi trang gói gọn một trang — như vậy hết "trang mẫu thừa" mà vẫn đúng quy cách. Nếu bạn muốn **chỉ một trang bìa**, chỉ cần bỏ dòng `\subfile{Bia_lot}` ở `DoAn.tex:175`.

---

## 2. Phần mở đầu + Chương 1: dẫn dắt LoRR và Team No Man's Sky

### Hiện trạng
- `Chuong/1_Gioi_thieu.tex` §1.1 "Đặt vấn đề" giới thiệu PIBT (dòng 29–32) nhưng **chưa nhắc** cuộc thi LoRR hay Team No Man's Sky.
- `Chuong/3_Cong_nghe.tex` §3.5 mới chỉ nhắc thoáng "cuộc thi robot đua The League of Robot Runners 2024".

### Dữ kiện (từ `adds/refference/LLoR_2024.md`)
The League of Robot Runners (LoRR) 2024 — Main Round, **Team No Man's Sky** đoạt **Grand Prize cả ba track** (Combined, Planner, Scheduler) **và** giải **Line Honours** (nhiều best-known solutions nhất — tức nhất toàn đoàn). Cuộc thi do Amazon Robotics tài trợ, bắc cầu giữa nghiên cứu MAPF và logistics công nghiệp.

### Thay đổi
Thêm một đoạn vào §1.1 (sau đoạn giới thiệu PIBT, quanh dòng 32) dẫn dắt mạch:
- Đồ án có **theo dõi cuộc thi LoRR**; thuật toán của **Team No Man's Sky** không chỉ nhất một giải mà **đứng nhất cả ba track và nhất toàn đoàn (Line Honours)** trong Main Round 2024. **PIBT trong đồ án được tham khảo từ hướng tiếp cận này.**
- Bản gốc là **server C++** (không "chơi" được trực tiếp, chỉ chạy benchmark/headless); đồ án **đưa ý tưởng và thuật toán của tác giả Team No Man's Sky vào Unity** để người chơi **trải nghiệm trực quan** và tương tác trong game.
- Trích dẫn `\cite{lorr2024}` (thêm entry mới, xem mục 6).

Có thể nhắc lại 1 câu ngắn ở đoạn mở đầu chương (dòng 4–6) để tạo mạch, nhưng nội dung chính đặt ở §1.1.

---

## 3. §1.1 Đặt vấn đề: đổi cách diễn giải "song không đảm bảo tối ưu toàn cục"

### Hiện trạng
`Chuong/1_Gioi_thieu.tex:31–32` (chỉ xuất hiện **một** lần trong toàn báo cáo):
> "...chạy theo bước thời gian, hoàn chỉnh và hiệu quả cho môi trường thời gian thực, **song không đảm bảo tối ưu toàn cục.**"

### Thay đổi
Đổi sang cách diễn giải **chấp nhận đánh đổi/thay thế tối ưu toàn cục** (khung tích cực thay vì phủ định). Đề xuất:
> "...chạy theo bước thời gian, hoàn chỉnh và hiệu quả cho môi trường thời gian thực, **chấp nhận thay thế lời giải tối ưu toàn cục bằng lời giải khả thi đủ tốt theo từng bước thời gian để đạt tốc độ phản hồi thời gian thực.**"

(Bám theo cụm "chấp nhận thay thế toàn cục" bạn nêu; sẽ chốt câu chữ cuối khi bạn duyệt.)

---

## 4. Hướng phát triển: mở rộng scale Agent + số người chơi + hạ tầng

### Hiện trạng — lưu ý lệch số mục
- §1.3 hiện tại là **"Định hướng giải pháp"** (`1_Gioi_thieu.tex:75`), **không phải** "Hướng phát triển".
- Phần **"Hướng phát triển"** thực sự nằm ở **Chương 6** (`6_Ket_luan.tex:28`). Hiện đã nhắc scale **agent** (6/12/24/36) và **hạ tầng cloud/WebGL**, nhưng **chưa** nói tới scale **số người chơi đồng thời**.

### Thay đổi (đề xuất đặt ở Chương 6 — đúng ngữ nghĩa "hướng phát triển")
Bổ sung/diễn giải rõ **ba trục mở rộng quy mô**:
- **Agent:** tăng số tank AI (đã có 6→36; nêu hướng tới hàng trăm agent như benchmark LoRR).
- **Người chơi:** mở rộng số người chơi đồng thời (multiplayer scale) — đồng bộ trạng thái, phòng/room, matchmaking.
- **Hạ tầng:** server PIBT trên cloud, cân bằng tải, backtest phân tán, chịu tải khi agent × người chơi cùng tăng.

### Điểm cần chốt
- Bạn ghi "Mục 1.3 hướng phát triển" nhưng §1.3 hiện là "Định hướng giải pháp". **Khuyến nghị** đặt nội dung scale ở **Chương 6 (Hướng phát triển)**, và (tùy chọn) thêm một câu hướng tới ở §1.3. Xác nhận giúp: đặt ở Chương 6, ở §1.3, hay cả hai.

---

## 5. §3.5: trích dẫn và dẫn dắt bài báo PIBT mới

### Hiện trạng
`Chuong/3_Cong_nghe.tex` §3.5 "Giao tiếp TCP giữa Unity và máy chủ C++" (dòng 74–86) bàn việc nhúng PIBT C++ từ LoRR 2024 qua TCP, nhưng **chưa trích dẫn** bài báo nền tảng.

### Dữ kiện (từ `adds/refference/2511.09193v2.pdf`)
- **Tiêu đề:** *Enhancing PIBT via Multi-Action Operations*
- **Tác giả:** Egor Yukhnevich (HSE University), Anton Andreychuk (Cognitive AI Systems Lab, Moscow)
- **arXiv:2511.09193v2**, 13/11/2025, cs.MA; ghi rõ *"extended version of the paper accepted to AAAI-26"*; project page `sites.google.com/view/epibt`.
- **Nội dung:** PIBT nhanh (nghìn agent/mili-giây) nhưng yếu với *rotation action model*; bài báo bổ sung **multi-action operations**, kết hợp **graph guidance (GG)** + **large neighborhood search (LNS)** đạt **SOTA trên online LMAPF-T**. Bài báo **đối chứng trực tiếp với "LoRR24-Winner"** — chính là Team No Man's Sky → nối mạch hoàn hảo với mục 2.

### Thay đổi
Thêm một đoạn dẫn dắt + trích dẫn `\cite{yukhnevich2025epibt}` vào §3.5 (hoặc cuối §3.3 nếu muốn gần lý thuyết PIBT hơn — xem điểm cần chốt), nội dung:
- PIBT là nền của nhiều phương pháp SOTA; nhánh nghiên cứu gần nhất (EPIBT, 2025, AAAI-26) mở rộng PIBT bằng multi-action operations + GG + LNS, vượt cả lời giải vô địch LoRR24 trên LMAPF-T.
- Khẳng định hướng PIBT đồ án chọn để nhúng vào Unity là hướng **đang được nghiên cứu tích cực và đạt hiệu năng hàng đầu**, củng cố lý do lựa chọn.

### Điểm cần chốt
- Đặt trích dẫn ở **§3.5** (đúng yêu cầu, gắn với server C++ LoRR) — khuyến nghị. Có thể thêm 1 câu ở §3.3. Xác nhận nếu muốn khác.

---

## 6. Thêm tài liệu tham khảo (`Danh_sach_tai_lieu_tham_khao.bib`)

Thêm hai entry mới:
```bibtex
@misc{lorr2024,
  author  = {{League of Robot Runners}},
  title   = {{2024 League of Robot Runners} --- Main Round 2024 Results},
  year    = {2025},
  note    = {Team No Man's Sky: Grand Prize (Combined, Planner, Scheduler) and Line Honours},
  url     = {https://www.leagueofrobotrunners.org/},
  urldate = {2026-06-25}
}

@article{yukhnevich2025epibt,
  title   = {Enhancing {PIBT} via Multi-Action Operations},
  author  = {Yukhnevich, Egor and Andreychuk, Anton},
  journal = {arXiv preprint arXiv:2511.09193},
  year    = {2025},
  note    = {Extended version of paper accepted to AAAI-26},
  url     = {https://arxiv.org/abs/2511.09193}
}
```

---

## Thứ tự thực hiện & kiểm thử
1. Sửa `.bib` (mục 6) trước để các `\cite` mới có chỗ tham chiếu.
2. Sửa nội dung: `Bia.tex`, `Bia_lot.tex` (mục 1) → `1_Gioi_thieu.tex` (mục 2, 3) → `6_Ket_luan.tex` (mục 4) → `3_Cong_nghe.tex` (mục 5).
3. Build XeLaTeX + bibtex (`build.ps1`), mở `DoAn.pdf` kiểm tra:
   - Bìa: không còn trang "Nguyễn Văn A", GVHD nằm cùng một trang, có dòng chữ ký.
   - §1.1: đoạn LoRR/Team No Man's Sky + câu reword hiển thị đúng.
   - Chương 6: ba trục scale.
   - §3.5: trích dẫn EPIBT xuất hiện ở danh mục tài liệu.
4. Tinh chỉnh khoảng trắng bìa nếu vẫn lệch trang.

## Tóm tắt các điểm cần bạn chốt
1. Bìa: giữ **cả bìa ngoài + bìa lót** (khuyến nghị) hay chỉ **một** trang bìa?
2. Mục 4: đặt nội dung scale ở **Chương 6 (Hướng phát triển)** (khuyến nghị), §1.3, hay cả hai?
3. Mục 5: trích dẫn EPIBT ở **§3.5** (khuyến nghị) hay thêm cả §3.3?
4. Câu chữ reword ở mục 3 — duyệt bản đề xuất hay muốn câu khác.
