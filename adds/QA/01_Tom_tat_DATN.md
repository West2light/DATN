# Câu hỏi 1 — Trình bày tóm tắt ĐATN (4–6 dòng, không gạch đầu dòng)

> Mục đích của thầy: hiểu nhanh bạn làm gì, ứng dụng phục vụ mục đích gì, chạy trên nền tảng nào, có gì đặc biệt so với cái khác.

## Đánh giá câu trả lời gốc của bạn

Câu gốc đúng trọng tâm (MAPF → game, PIBT, cảm hứng từ LoRR/Team No Man's Sky, so sánh A* vs PIBT C# vs PIBT TCP, multiplayer). Tuy nhiên còn thiếu và sai vài chỗ so với quyển ĐATN:

**Thiếu:**
1. **Nền tảng cụ thể** — thầy hỏi thẳng "chạy trên nền tảng nào" mà câu gốc chỉ nói "về Unity". Cần nói rõ: game **2D góc nhìn từ trên xuống trên Unity Engine (URP)**.
2. **Bản đồ chuẩn** — dùng **bản đồ chuẩn MovingAI MAPF benchmark** (đây là điểm khiến kết quả so sánh được với cộng đồng nghiên cứu — một "cái đặc biệt" nên nêu).
3. **Quy mô thực nghiệm** — hệ thống **backtest tự động**: 5 bản đồ × 3 thuật toán × 5 mức agent (6/12/24/36/72), môi trường tĩnh + chướng ngại động, tổng **900 run**, xuất CSV. Đây là đóng góp định lượng, rất nên chốt một câu.
4. (Tùy chọn, nếu còn chỗ) cải tiến **EPIBT** để khử hiện tượng xe tăng xoay giật khi chuyển lời giải lưới thành chuyển động vật lý.

**Cần sửa cho chính xác:**
- Tên giải: **The League of Robot Runners (LoRR) 2024, do Amazon Robotics tài trợ**; Team No Man's Sky đoạt **Line Honours (nhất toàn đoàn)** và nhất cả ba bảng Combined/Planner/Scheduler. (Câu gốc viết "The LoRR 2024" hơi thừa chữ "The" và thiếu Amazon Robotics.)
- Ý cốt lõi cần bật rõ: các cài đặt đoạt giải chỉ là **máy chủ C++ chạy benchmark, không chơi/tương tác được** → đóng góp của bạn là **đưa vào Unity cho trực quan và chơi được** ("mang từ lý thuyết ra thực tế" — giữ ý này, tốt).
- Lỗi diễn đạt nhỏ: "cùng chơi cùng", "trong local LAN", thiếu dấu cách sau dấu chấm.

## Bản chuẩn hóa (khuyến nghị — ~6 dòng, không gạch đầu dòng)

Đồ án nghiên cứu và ứng dụng bài toán tìm đường đa tác nhân (Multi-Agent Pathfinding — MAPF) vào một game bắn xe tăng 2D góc nhìn từ trên xuống, nhiều người chơi, xây dựng trên Unity Engine và sử dụng bản đồ chuẩn MovingAI MAPF benchmark. Trọng tâm là thuật toán PIBT (Priority Inheritance with Backtracking), lấy cảm hứng từ lời giải đoạt giải nhất toàn đoàn (Line Honours) của Team No Man's Sky tại cuộc thi The League of Robot Runners 2024 do Amazon Robotics tài trợ — vốn chỉ tồn tại dưới dạng máy chủ C++ chạy benchmark, không thể chơi hay tương tác trực tiếp. Đồ án đưa thuật toán này từ C++ vào Unity để hiển thị trực quan và cho nhiều người cùng chơi, đồng thời triển khai ba cấp độ để so sánh khách quan: A* làm baseline, PIBT viết bằng C# ngay trong Unity, và PIBT C++ kết nối qua TCP theo mô hình client–server. Hệ thống có backtest tự động chạy trên nhiều bản đồ với cả môi trường tĩnh lẫn chướng ngại động, xuất kết quả CSV để đánh giá định lượng. Game hỗ trợ chơi nhiều người qua mạng LAN nội bộ lẫn Internet, đưa một thuật toán MAPF từ lý thuyết benchmark ra một sản phẩm chơi được thực tế.

## Bản rút gọn (nếu thầy chấm chặt đúng 4–5 dòng)

Đồ án ứng dụng bài toán tìm đường đa tác nhân (MAPF) vào game bắn xe tăng 2D nhiều người chơi trên Unity Engine, dùng bản đồ chuẩn MovingAI benchmark. Điểm đặc biệt là mang thuật toán PIBT (Priority Inheritance with Backtracking) — lấy cảm hứng từ lời giải nhất toàn đoàn của Team No Man's Sky tại The League of Robot Runners 2024, vốn chỉ chạy benchmark bằng C++ — vào Unity để trực quan hóa và chơi được. Đồ án triển khai và so sánh ba cấp độ A* (baseline), PIBT C# và PIBT C++ qua TCP client–server, kèm hệ thống backtest tự động trên môi trường tĩnh lẫn chướng ngại động, hỗ trợ chơi nhiều người qua LAN và Internet.
