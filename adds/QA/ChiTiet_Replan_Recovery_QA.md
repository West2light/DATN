# Tổng hợp các tình huống kích hoạt Replan và Recovery

Trong hệ thống AI của `GridEnemyAgent`, việc tính lại đường (`ReplanPath`) và kích hoạt cứu hộ (`TriggerRecovery`) là hai hành động tiêu tốn tài nguyên nhất nhưng lại quan trọng nhất để giúp xe di chuyển thông minh. Dưới đây là tổng hợp toàn bộ các trường hợp khiến mã nguồn phải gọi đến 2 hàm này (và làm tăng bộ đếm `btReplanCount`, `btRecoveryCount`).

---

## 1. Khi nào AI phải tính lại đường? (`ReplanPath` / `btReplanCount`)

Mỗi khi hàm `ReplanPath()` được gọi, biến đo lường `btReplanCount` sẽ tăng thêm 1. Xe tăng sẽ vạch lại đường đi trong các tình huống sau:

1. **Theo chu kỳ (Periodic Replan):**
   - **Điều kiện:** `Time.time >= nextReplanTime`
   - **Giải thích:** Cứ mỗi khoảng thời gian `replanInterval` (ví dụ 0.75 giây), AI sẽ "mở mắt" ra nhìn bản đồ một lần để cập nhật vị trí của người chơi và các xe tăng khác.
2. **Khi chưa có đường đi (Empty Path):**
   - **Điều kiện:** `currentPath.Count == 0`
   - **Giải thích:** Xe vừa mới được sinh ra (Spawn) hoặc vừa đi hết quãng đường cũ mà chưa tới đích.
3. **Khi bị vật thể cản đường (ReplanToDestructible):**
   - **Điều kiện:** Xe phát hiện phía trước có chướng ngại vật phá hủy được (thùng gỗ, gạch).
   - **Giải thích:** Xe không gọi `ReplanPath` gốc mà gọi `ReplanToDestructible()` (hàm này cũng tăng `btReplanCount`). Nó vẽ đường ngắn nhất tới cái thùng để nhắm bắn.
4. **Cứu hộ vòng lặp thành công (Spatial Recovery):**
   - **Điều kiện:** Xe vừa phát hiện mình chạy lòng vòng (`TriggerSpatialRecovery`) và đã thêm các ô vừa đi qua vào danh sách cấm (`pendingBlockedCells`).
   - **Giải thích:** Sau khi cắm biển "Cấm đi" vào các ô cũ, nó tự gọi `ReplanPath()` để bắt A* tìm một lối rẽ khác.
5. **Nằm trong quy trình Cứu hộ kẹt (Recovery Bậc 1 & Bậc 3):**
   - **Bậc 1 (`ForcedReplan`):** Xóa ngay đường cũ và ép `ReplanPath` lập tức.
   - **Bậc 3 (`ExpandedMask`):** Mở rộng bán kính "sợ tường" và gọi `ReplanPath` để ép AI đi ra giữa tim đường, tránh xa bờ tường.

---

## 2. Khi nào AI phải phát tín hiệu Cứu hộ? (`TriggerRecovery` / `btRecoveryCount`)

Hàm `TriggerRecovery()` khởi động **Thang cứu hộ 4 bậc** (ForcedReplan ➔ Reverse ➔ ExpandedMask). Mỗi lần gọi, `btRecoveryCount` tăng thêm 1. Nó chỉ xảy ra khi AI thực sự đang gặp rắc rối vật lý:

1. **Kẹt thời gian (Chết máy / Time Stuck):**
   - **Điều kiện:** `Time.time - lastProgressTime > stuckTimeout`
   - **Giải thích:** Chiếc xe đã luồn lách đủ kiểu, đạp ga liên tục nhưng suốt 1.5 giây vừa qua (`stuckTimeout`) vẫn không thể nhích lên được nổi 5cm (`progressEpsilon`).
2. **Kẹt do cạ tường (Scuffing):**
   - **Điều kiện:** Gọi thông qua `TryTriggerScuffRecovery()`.
   - **Giải thích:** Xe đang va chạm vật lý vào vách tường (`IsTouchingLayers`), đồng thời tốc độ di chuyển tụt xuống gần bằng 0 trong suốt 0.4 giây (`scuffTimeout`). Tài xế đang húc đầu vào đá.
3. **Cứu hộ không gian thất bại (Spatial Stuck Fallback):**
   - **Điều kiện:** Nằm ở cuối hàm `TriggerSpatialRecovery()`.
   - **Giải thích:** Xe phát hiện mình đang chạy vòng tròn. Nó đã cố cấm các đường cũ và gọi `ReplanPath` (như ở mục 1.4). NHƯNG ngõ cụt đó không còn đường nào khác để đi, A* trả về kết quả rỗng. Hết cách, hệ thống bắt buộc phải gọi `TriggerRecovery()` để nhường quyền cho các biện pháp mạnh hơn (như cài số Lùi - Reverse).
