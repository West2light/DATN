# Tổng hợp Q&A - Phân tích chuyên sâu Đồ án AI & Multiplayer
*(Được tổng hợp từ các cuộc thảo luận giải thích code)*

---

## PHẦN 1: GAMEPLAY & THÔNG SỐ (HP, SÁT THƯƠNG)

### 1. Tại sao máu xe tăng địch / người chơi lại tụt nhanh/chậm bất thường?
- **Prefab gốc (`Tank.prefab`):** Có mặc định `MaxHealth = 100`.
- **Tuy nhiên (Tính năng Variant):** Trong file `EnemyTank Variant.prefab`, `MaxHealth` bị ghi đè (override) xuống chỉ còn **2 HP**. Nếu viên đạn (`FastBulletData`) có sát thương là 5, xe sẽ nổ tung chỉ với 1 hit (5 > 2). Giống hệt game Battle City (1990) cổ điển.
- **Tại sao khi chơi thực tế lại cần 3-4 viên mới vỡ?** 
Vì trong Script Quản lý Màn chơi (`MapScenarioBootstrapPIBT.cs`), có khai báo biến `enemyMaxHealth = 20`. Khi xe tăng vừa sinh ra, Script này đã ép (ghi đè) lại máu của toàn bộ xe địch thành 20, phớt lờ thông số 2 HP trong Prefab. Phép tính: `20 HP / 5 Damage = 4 viên đạn` để tiêu diệt. (Sự tách bạch này giúp dễ dàng tăng độ khó ở các màn sau mà không cần tạo Prefab mới).

---

## PHẦN 2: TRÍ TUỆ NHÂN TẠO (AI) - GRID ENEMY AGENT

### 2. Hàm `ReplanPath`: Tại sao coi đồng đội là "Tường"?
AI mù phương hướng với nhau. Nếu 2 xe cùng lúc dùng A*, chúng sẽ chọn trùng 1 con đường ngắn nhất và đâm nhau. Để khắc phục (Tránh va chạm nghèo nàn), trước khi chạy A*, xe sẽ chụp ảnh bản đồ và đánh dấu tọa độ xe đồng đội thành "Bức tường cứng". Lúc này A* buộc phải vạch đường vòng. Nó "nghèo nàn" vì xe chỉ né dựa trên vị trí *hiện tại* của đồng đội, chứ không biết 1 giây sau đồng đội đi đâu, nên đôi khi vẫn đụng nhau.

### 3. Hàm `TryFindPathWithFallback`: "Thà xước sơn còn hơn đứng im"
Bình thường AI rất sợ tường (Bán kính né tường `clearanceRadius > 0`). Nó sẽ cự tuyệt không đi vào ngõ hẹp. Nhưng nếu ngõ hẹp là đường duy nhất?
Thuật toán sẽ "xuống nước" (Fallback): Giảm bán kính né tường về 0 (hóp bụng lại), ép A* vẽ con đường cọ sát sạt vách tường. Triết lý: Chậm chạp lết qua ngõ hẹp vẫn tốt hơn đứng chết tại chỗ.

### 4. `pendingBlockedCells` để làm gì? (Sổ tay ghi thù)
Đây là công cụ chống "Kẹt ảo" (Chạy vòng tròn vô tận trong ngõ cụt do lỗi vật lý). Nếu xe phát hiện mình chạy lòng vòng, nó sẽ lấy tọa độ của các ô đất vừa giẫm lên ném vào `pendingBlockedCells` (Giống như tự cắm biển Cấm Đi). Lần gọi A* tiếp theo, nó sẽ tránh xa các ô đất bị nguyền rủa này và tìm một ngõ rẽ hoàn toàn mới.

### 5. `FollowPath`: Logic "Vừa xoay vừa tiến" & Nhấp nhả ga (Duty Cycle)
- **Vấn đề:** Tránh xe xoay 90 độ tại chỗ rồi mới chạy (rất giật cục).
- **Dot Product & Cross Product:** Tính toán xem mũi xe chĩa thẳng mục tiêu không. (Dot=1: Tiến lút ga; Dot=0: Vuông góc).
- **Duty Cycle (Nhấp nhả ga):** Thay vì đạp ga liên tục, AI nhấp nhả (Vd: 65% thời gian tiến, 35% thời gian xoay) tạo ra cú bo cua (Drift) vòng cung cực kỳ mượt mà.
- **GetWaypointReachDistance:** Bán kính chạm đích thông minh. Khúc cua thì bắt ép cán chuẩn tâm (`0.12`), đường thẳng thì du di cho lướt qua nhanh (`0.3`).

### 6. Thang Cứu Hộ Kẹt 4 Bậc (Anti-Stuck System)
Bao gồm 3 cảm biến "bắt bệnh":
1. **Kẹt thời gian:** Chết máy đứng im (Time.time - lastProgressTime > 1.5s).
2. **Kẹt cạ tường:** Húc vào vật cản, vận tốc = 0 quá 0.4s.
3. **Kẹt không gian (Mê cung):** Lạc, chạy tốc độ cao nhưng lòng vòng tại chỗ.
Khi phát hiện kẹt, hệ thống điều trị leo thang: **(1) Ép tìm đường mới -> (2) Cài số Lùi xe -> (3) Ép vẽ đường tránh xa tường hơn -> (4) Ép liên tục**. (Hệ thống này giúp AI tự gỡ rối, đảm bảo Test không bao giờ bị nghẽn).

---

## PHẦN 3: THUẬT TOÁN PIBT PLANNER VÀ TCP C++

### 7. `_flow` là gì? (Bản đồ Giao thông Google Maps)
Thay vì A* cho mọi xe chạy dồn vào 1 hẻm, PIBT đếm lưu lượng xe chạy qua từng ngã tư (biến `_flow`). Xe đi sau nhìn vào `_flow`, thấy ngõ nào đông xe (Cảnh báo đỏ) thì tự động bẻ lái sang ngõ khác xa hơn nhưng vắng hơn. Các xe tự động tản ra mọi ngả đường mà không cần giao tiếp trực tiếp.

### 8. `AStarFlow`: Công thức Phạt tiền Giao thông
- `op_flow` (Phạt Đi Ngược Chiều): Dùng **Phép Nhân**. Đối đầu trực diện trong hẻm gây khóa cứng (Deadlock), cực kỳ thảm khốc nên phải phạt thật nặng (cấp số nhân) để cấm tuyệt đối.
- `vertex_flow` (Phạt Đi Vào Chỗ Đông): Dùng **Phép Chia**. Đi qua ngã tư đông đúc chỉ làm chậm tuyến tính, không gây chết người. Phạt nhẹ để cân nhắc (thà đi chậm tí còn hơn vòng quá xa).

### 9. Biến `budgetMs` (Frank-Wolfe)
Thuật toán tìm đường có thể tính toán mãi mãi để ra đường đi hoàn hảo. Nhưng trong game 60 FPS, tính quá lâu game sẽ đơ. `budgetMs = 15f` là "Tối hậu thư": Cho thuật toán đúng 15 mili-giây, tính tới đâu xài tới đó, lập tức trả kết quả để game chạy tiếp, chống tụt FPS.

### 10. `GetOrBuildH` (Reverse BFS) & `BuildNbrs` (Cuốn danh bạ)
- **Reverse BFS:** Thay vì đoán đường chim bay (Manhattan) bị lỗi khi dính tường, AI "đổ xô nước" từ Căn Cứ cho loang ra toàn bản đồ. Khoảng cách đo được là thật 100%. Các xe đi chung 1 hướng có thể dùng lại (Cache) kết quả nước loang này để A* đi thần tốc.
- **BuildNbrs:** Dựng sẵn danh bạ "Ngã tư này thông với ngã tư nào" lúc load game, giúp A* đang chạy nóng không cần hỏi lại `IsWalkable` 4 hướng, tối ưu tốc độ.

### 11. Dữ liệu TCP: `goalLoc` và `orientation`
- `goalLoc`: Đích đến (Căn cứ) để Server tính đường.
- `orientation`: Hướng mũi xe hiện tại. Vì xe tăng không thể "đi ngang" như con cua (Kinodynamic), Server cần biết hướng này để tính toán chi phí xoay xe trước khi đi thẳng.

### 12. Cú hích tham lam (Greedy Nudge) - ĐIỂM PHẢN BIỆN ĂN ĐIỂM
Khi hệ thống C++ Server bị nghẽn quá 4s, xe Unity tự động "nhích bừa" 1 ô về phía Căn cứ để tự giải cứu. 
👉 **Phản biện:** Hãy thừa nhận với Hội đồng đây **KHÔNG PHẢI PIBT**. Nó là một cái phao cứu sinh vật lý do sự sai lệch giữa môi trường Grid C++ và Physic Unity. Bạn đã cẩn thận ghi log `FALLBACK greedy nudge` và tách nó ra khỏi dữ liệu báo cáo để đảm bảo sự trung thực học thuật!

---

## PHẦN 4: MULTIPLAYER VÀ NETCODE (NGO)

### 13. NGO là gì?
NGO = **Netcode for GameObjects**. Thư viện Multiplayer tân tiến và chính quy nhất của Unity hiện nay (Thay thế UNET/PUN). Dùng NGO chứng minh đồ án code theo chuẩn ngành.

### 14. Lỗi tràn WebSocket & `MaxPacketQueueSize`
Bản chạy trên Web phải dùng WebSocket. Lúc vừa load map, trình duyệt đơ vài giây không đọc thư, nhưng Server vẫn vã 30 tin nhắn/giây vào hòm thư. Hòm thư mặc định (128 gói) bị tràn -> Rớt mạng ngay lập tức. Sửa bằng cách tăng hòm thư lên 1024 (`MaxPacketQueueSize = 1024`) để chống nghẽn lúc Load.

### 15. `LanNetworkBridge` (Bộ đàm của người chơi)
Mỗi người có một cái bộ đàm riêng.
- **Throttle (Van điều tiết):** Dù bấm phím 144 lần/giây, bộ đàm chỉ mở cửa gửi lên Server đúng **30 lần/giây (30Hz)** để Server không bị sập vì quá tải tin nhắn. Nút bắn được làm cái "Chốt" để bấm giữa chừng không bị mất đạn.
- **Dự đoán cục bộ (Client Prediction):** Vừa bấm phím là tự đẩy xe trên màn hình của mình lên ngay lập tức, không đợi Server xác nhận. Chống cảm giác Lag, Delay.

### 16. Tại sao tốc độ mạng là `30Hz` (1/30 giây)?
- 60Hz: Gửi quá nhiều, sập Server, rớt WebGL.
- 10Hz: Gửi quá ít, xe trên màn hình bị teleport (giật cục).
- **30Hz (Tick Rate 30):** Tiêu chuẩn vàng của game MOBA/Bắn súng. Cân bằng giữa tiết kiệm mạng và độ mượt mà. Chiều Lên (gửi phím) và Chiều Xuống (Server trả vị trí thật) đều chạy ở 30Hz.

### 17. Mô hình Mắt Client (`LanClientView`) và Quản trò Server (`LanGameCoordinator`)
- **Server-Authoritative:** Mọi chiếc xe tăng thật (có va chạm vật lý) ĐỀU nằm ở máy Server. Máy của bạn (Client) chỉ là cái Tivi. `LanClientView` nhận tọa độ từ Server và vẽ các **Bóng ma (Ghost)** ra màn hình. Hack là điều không thể.
- **LanGameCoordinator:** Ông Quản trò chỉ chạy trên Server. Nhiệm vụ: Nối bộ đàm với xe tăng, ép bàn phím Host chỉ điều khiển xe Host. Cứ mỗi 1/30s, gom toàn bộ bản đồ nén thành 1 gói tin cực nhỏ (`FastBufferWriter`) ném về cho các máy Client.

### 18. Mã 6 Ký Tự vs Link (Internet Server)
- **Mã 6 ký tự (Relay Mode):** Do máy chủ Cloud của Unity tự sinh ngẫu nhiên (ví dụ `A8X9K2`). Nhanh, "mì ăn liền", không cần mở Port wifi.
- **Link mời (Dedicated GCP + Registry):** Máy chủ GCP báo cáo lên 1 Web API. Web API sinh ra một ID Phòng (`?room=xyz`) ẩn IP/Port thật đi thành một đường Link trang web. Bạn bè click vào Link là vào thẳng game rất chuyên nghiệp.
