# Plan ôn tập & chuẩn bị phản biện ĐATN

> Mục tiêu: (1) hiểu lại project từ con số 0 theo đúng thứ tự, (2) chuẩn bị đầy đủ theo yêu cầu của thầy để **không bị "vỗ tới chết"** hôm phản biện.

---

## PHẦN 0 — Mô hình tư duy 30 giây (đọc cái này trước tiên)

Project là **1 game xe tăng 2D (Unity)** trong đó nhiều xe tăng địch (agent AI) phải tự tìm đường tới **Eagle base** mà **không đâm nhau**. Đó là bài toán **MAPF**. Bạn làm **3 phiên bản "bộ não" tìm đường** cho lũ địch đó, rồi **so sánh** chúng:

| Mode | "Bộ não" (planner) | Agent điều khiển | Đặc điểm |
|------|--------------------|------------------|----------|
| A* (baseline) | `GridAStarPathfinder` | `GridEnemyAgent` | Mỗi con tự tìm đường, KHÔNG tránh nhau ở tầng lập kế hoạch |
| PIBT C# | `PIBTPlanner` | `GridEnemyAgentPIBT` | Phối hợp đa agent tránh va chạm, chạy ngay trong Unity |
| PIBT TCP | `PIBTTcpClient` → server C++ | `GridEnemyAgentPIBT_TCP` | Gửi bài toán qua TCP cho server C++ giải, nhận đường về |

Cả 3 mode **dùng chung**: `MapLoader` (bản đồ) + `TankController` (điều khiển vật lý). Đó là điểm mấu chốt để so sánh công bằng. `BacktestRunner` chạy tự động cả 3 mode trên nhiều bản đồ rồi xuất CSV. Multiplayer nằm riêng ở thư mục `Multiplayer/`.

**Nếu chỉ nhớ 1 câu:** *"3 bộ não tìm đường (A*, PIBT C#, PIBT-qua-TCP-C++) cắm vào cùng 1 thân xe tăng trên cùng 1 bản đồ, rồi đo xem con nào đi tốt hơn."*

---

## PHẦN 1 — Thứ tự đọc code (đừng đọc lung tung, đi theo luồng dữ liệu)

Đọc từ **nhỏ → lớn**, theo đúng dòng chảy: *bản đồ → điều khiển → tìm đường → agent → bootstrap → backtest → multiplayer*. Với mỗi file, chỉ cần trả lời được **"file này INPUT gì, OUTPUT gì"**, chưa cần hiểu từng dòng.

### Chặng 1 — Điều khiển xe (dễ nhất, để lấy đà) · ~1 giờ
1. `TankController.cs` (49 dòng) — cửa ngõ điều khiển. Chỉ có `HandleMoveBody`, `HandleTurretMovement`, `HandleShoot`. **Cả người chơi lẫn AI đều gọi 3 hàm này.** Hiểu được đây là hiểu 50% cách xe chạy.
2. `TankMover.cs` — vật lý Rigidbody2D: tăng tốc, xoay thân.
3. `Turret.cs` + `AimTurret.cs` + `Bullet.cs` + `Damagable.cs` — bắn và nhận sát thương. Sát thương 1 chiều: `Bullet` chạm → `Damagable.Hit()`.
4. `PlayerInput.cs` — cách người chơi bơm input vào `TankController` (để đối chiếu: AI cũng bơm y hệt).

> Chốt chặng 1: bạn hiểu "thân xe" hoạt động thế nào, độc lập với việc ai điều khiển.

### Chặng 2 — Bản đồ & toạ độ (nền móng) · ~1–2 giờ
5. `MapLoader.cs` (764 dòng) — **file quan trọng nhất, đọc kỹ**. Có sẵn tài liệu giải thích ở `Assets/Docs/MapLoader.cs.md` — **đọc file .md đó song song**. Chỉ cần nắm 5 hàm public: `IsWalkable(cell)`, `CellToWorld(cell)`, `WorldToCell(pos)`, `TryFindWalkableNear(...)`, và cách nó đọc file `.map`. Toạ độ ô: gốc trên-trái, Y tăng xuống dưới.
6. Mở 1 file `Assets/MapData/*.map` bằng text editor — xem `.` (đi được) và `@` (tường). Hiểu format này là hiểu "dữ liệu đầu vào" của cả đồ án.

> Chốt chặng 2: bạn hiểu bản đồ biến từ text `.map` thành lưới ô và toạ độ world ra sao.

### Chặng 3 — 3 bộ não tìm đường · ~half day (phần lõi luận văn)
Đọc theo cặp *planner ↔ agent*:

7. **A*:** `GridAStarPathfinder.cs` (317) — thuật toán A* 4 hướng, heuristic Manhattan. Rồi `GridEnemyAgent.cs` (944) — agent gọi A*, đi theo waypoint, replan định kỳ, chuyển sang bắn khi thấy mục tiêu. Xem cấu trúc hàm: `Update` → `ReplanPath` → `FollowPath`. **Đây là con "đơn giản nhất", đọc trước để làm mốc.**
8. **PIBT C#:** `PIBTPlanner.cs` (377) — trái tim luận văn. PIBT = ưu tiên + kế thừa ưu tiên + backtracking để nhiều agent không tranh 1 ô. Rồi `GridEnemyAgentPIBT.cs` (863). Đối chiếu với `GridEnemyAgent` để thấy **khác biệt duy nhất là bộ não phối hợp**.
9. **PIBT TCP:** `PIBTTcpClient.cs` (501) — client TCP: đóng gói bài toán, gửi sang server C++, nhận đường về. Rồi `GridEnemyAgentPIBT_TCP.cs` (299, ngắn nhất vì đẩy việc nặng sang server).

> Server C++ nằm ngoài repo Unity: `//wsl.localhost/Ubuntu/home/lenovo/projectY/Server-PIBT-TeamNoMan-sSky`. **Cần biết đường dẫn này để hôm demo bật server lên trước.**

### Chặng 4 — Lắp ráp cảnh (ai spawn ra ai) · ~1 giờ
10. `MapTankTestBootstrap.cs` — dựng map + spawn người chơi + camera.
11. `MapScenarioBootstrap.cs` (724) và 2 biến thể `...PIBT.cs`, `...PIBT_TCP.cs` — spawn Eagle + spawn lũ địch, gắn đúng loại agent. Đọc để trả lời "khi bấm Play thì cái gì chạy trước".

### Chặng 5 — Backtest (phần số liệu để bảo vệ kết quả) · ~2 giờ
12. `Backtest/BacktestData.cs` (20 dòng) — các trường số liệu ghi ra CSV. Đọc trước để biết "đo cái gì".
13. `Backtest/BacktestRunner.cs` (896) — vòng lặp chạy tự động: `BuildJobs` → `RunJob` → `RecordRun` → `ExportCSV`. Xem 3 hàm `CollectAgent` / `CollectAgentLns2` / `CollectAgentTcp` để biết mỗi mode nộp số liệu gì.
14. `Backtest/DynamicObstacleSpawner.cs` — chướng ngại động (thả thùng cản đường để test khả năng tái hoạch định).

### Chặng 6 — Multiplayer (đọc lướt, chỉ cần biết đường đi) · ~1 giờ
15. `Multiplayer/LanLobbyController.cs`, `LanDiscovery.cs`, `LanSessionManager.cs` — luồng LAN.
16. `Multiplayer/RelayManager.cs`, `InternetSessionClient.cs` — luồng qua Internet.
> Chỉ cần trả lời được "host tạo phòng → client tìm/join thế nào", không cần thuộc từng dòng.

**Tổng thời gian đọc hiểu: ~2–3 buổi.** Nếu gấp, ưu tiên Chặng 1 → 2 → 3 (bỏ qua chi tiết 5, 6).

---

## PHẦN 2 — Checklist nộp & mang đi theo yêu cầu của thầy

| # | Yêu cầu của thầy | Trạng thái cần đạt | Việc phải làm |
|---|------------------|--------------------|----------------|
| 1 | Bản mềm ĐATN (PDF) | ✅ Có `DoAn.pdf` | Build lại bản mới nhất, kiểm tra không lỗi tràn trang; copy ra USB |
| 2 | Phiếu giao nhiệm vụ ĐATN (PDF) | ⚠️ Kiểm tra | Xin/scan phiếu có chữ ký thầy hướng dẫn, xuất PDF |
| 3 | Tóm tắt 4–6 dòng, không gạch đầu dòng | ✅ Đã có | Dùng bản ở `adds/QA/01_Tom_tat_DATN.md` |
| 4 | Link sản phẩm / code (nếu đã triển khai) | ⚠️ | Nếu có repo GitHub/GitLab: đính link. Nếu chưa deploy public: ghi "chạy local, mã nguồn kèm theo" |
| 5 | **Mã nguồn + chương trình chạy sẵn dữ liệu** | ❗ Quan trọng | Xem PHẦN 3 |
| 6 | Dữ liệu back-up phòng lỗi | ❗ Quan trọng | Xem PHẦN 3 |
| 7 | KHÔNG cần slide | — | Không làm slide, tập trung vào demo chạy được |

---

## PHẦN 3 — Chuẩn bị DEMO (điểm sống còn: "đừng để bắt đầu mới nhập dữ liệu")

Ý thầy: hôm phản biện **mở lên là chạy được ngay**, không loay hoay cấu hình. Cần chuẩn bị:

1. **Unity project mở sẵn, đúng scene.** Mở trước cả 2 scene: `Assets/Scenes/MapF_TankTest.unity` (A*) và `MapF_TankTest_PIBT.unity`. Test bấm Play cả hai chạy mượt.
2. **Server C++ (mode TCP) bật sẵn.** WSL Ubuntu, chạy server ở `//wsl.localhost/Ubuntu/home/lenovo/projectY/Server-PIBT-TeamNoMan-sSky` **trước** khi demo. Nếu server không lên, mode TCP sẽ treo → chuẩn bị câu nói lùi: "mode TCP cần server C++, em đã có sẵn kết quả backtest offline nếu server lỗi".
3. **Dữ liệu backtest có sẵn (CSV + biểu đồ).** Đừng để chạy 900 run tại chỗ (mất rất lâu). Xuất trước file CSV/HTML/PNG kết quả và để trong 1 thư mục `demo_results/`. Khi thầy hỏi số liệu → mở file, không chạy lại.
4. **Bản đồ `.map` đã nằm sẵn** trong `Assets/MapData/` — kiểm tra đủ 5 bản đồ dùng trong luận văn.
5. **Kịch bản demo 3 phút** (tập trước, bấm theo thứ tự):
   - Bấm Play scene A* → chỉ cho thầy thấy địch tìm đường tới Eagle, thỉnh thoảng kẹt/đâm nhau.
   - Chuyển scene PIBT → chỉ cho thấy địch phối hợp mượt hơn, tránh nhau.
   - (Nếu server ok) bật mode TCP → giải thích "cùng thuật toán nhưng chạy trên server C++".
   - Mở file CSV/biểu đồ backtest → chỉ số liệu so sánh.
   - (Nếu kịp) demo multiplayer LAN 2 máy hoặc 2 instance.
6. **BACK-UP (thầy nhấn mạnh):**
   - Copy **toàn bộ project** ra ổ ngoài/USB (zip lại, đã có sẵn `20225808_DuongQuangDong_2025.2.zip`).
   - Copy riêng thư mục `Assets/MapData/` + `demo_results/` (CSV) ra USB.
   - **Quay 1 video màn hình** demo chạy đúng của cả 3 mode + multiplayer. Nếu hôm đó máy lỗi/máy chiếu trục trặc → mở video. Đây là "phao cứu sinh".
   - Push code lên GitHub/GitLab private làm bản back-up đám mây.

---

## PHẦN 4 — Câu hỏi thầy hay hỏi & CHỖ tìm câu trả lời trong code

Chuẩn bị sẵn, vì đây là chỗ dễ bị "vỗ":

1. **"PIBT khác A* ở điểm nào?"** → A* mỗi agent tìm đường độc lập, không biết agent khác; PIBT phân ưu tiên, agent ưu tiên cao "ép" agent thấp nhường ô (priority inheritance), kẹt thì backtracking. Code: so `GridAStarPathfinder` vs `PIBTPlanner`.
2. **"Tại sao không dùng CBS cho tối ưu?"** → CBS tối ưu toàn cục nhưng độ phức tạp tăng theo hàm mũ số agent, không kịp thời gian thực; PIBT đổi tối ưu lấy tốc độ (đủ tốt mỗi bước). (Đã viết trong Chương 1 luận văn.)
3. **"Sao phải làm cả bản TCP C++, C# chưa đủ à?"** → Bản đoạt giải LoRR là C++ hiệu năng cao; TCP cho phép tái dùng đúng code server đó thay vì viết lại, và chứng minh kiến trúc client-server mở rộng được (đưa lên cloud). Code: `PIBTTcpClient.cs`.
4. **"Đo cái gì, so sánh thế nào?"** → mở `Backtest/BacktestData.cs` liệt kê các chỉ số (số lần replan, kẹt, phá Eagle...). 900 run = 5 map × 3 thuật toán × 5 mức agent × 6 lần lặp × (tĩnh + động).
5. **"Xe tăng có hướng thân, sao đi mượt được?"** → cải tiến **EPIBT** khử xoay giật; lời giải lưới rời rạc được chuyển thành chuyển động vật lý qua ngưỡng dot-product trong `FollowPath()`. (Mục 5.5 luận văn.)
6. **"Multiplayer chạy thật không?"** → chuẩn bị sẵn 2 instance/2 máy LAN. Nếu Internet chưa test kỹ thì nói "LAN chạy ổn định, Internet qua Relay đã dựng nhưng còn tuỳ mạng" — **đừng khẳng định chắc cái mình chưa test**.

> Nguyên tắc vàng khi phản biện: cái gì chạy được thì demo thẳng; cái gì chưa chắc thì nói trung thực "đã làm tới đâu" + có số liệu/video backup. Thầy đánh giá sự hiểu và trung thực hơn là đánh giá bạn thuộc lòng.

---

## PHẦN 5 — Lịch ôn 3 ngày (gợi ý)

- **Ngày 1:** PHẦN 0 + Chặng 1, 2 (điều khiển + bản đồ). Bấm Play thử, nghịch cho quen.
- **Ngày 2:** Chặng 3 (3 bộ não) — phần lõi. Đối chiếu A* vs PIBT. Chuẩn bị trả lời câu 1–3 PHẦN 4.
- **Ngày 3:** Chặng 4, 5, 6 lướt + dựng bộ demo & backup PHẦN 3 + tập kịch bản demo 3 phút 2–3 lần.
