# Kiến Trúc Hệ Thống Xe Tăng (Tank Architecture)

Dựa trên các đoạn mã trong dự án, dưới đây là tổng hợp và giải thích về kiến trúc điều khiển cũng như chiến đấu của xe tăng.

## 1. `TankController.cs` — Cửa ngõ điều khiển
- **Vai trò:** Đóng vai trò là bộ não trung tâm (Controller) của một chiếc xe tăng. Nó nhận lệnh và phân phối lệnh xuống các bộ phận cấu thành (`TankMover`, `Turret`, `AimTurret`).
- **Đặc điểm cốt lõi:** File này dài khoảng 50 dòng, rất gọn gàng. Nó chỉ phơi bày ra các hàm chính:
  - `HandleMoveBody(Vector2)` / `HandleMoveWorldDirection(Vector2)`: Truyền lệnh di chuyển xuống `TankMover`.
  - `HandleTurretMovement(Vector2)`: Truyền lệnh xoay nòng súng xuống `AimTurret`.
  - `HandleShoot()`: Ra lệnh cho tất cả các nòng súng (`Turret`) khai hỏa.
- **Ý nghĩa (Rất quan trọng):** Đây là một thiết kế rất tốt theo mẫu Component-Based. **Cả người chơi (`PlayerInput`) lẫn Trí tuệ nhân tạo (AI) đều chỉ giao tiếp thông qua `TankController`**. 
  Điều này có nghĩa là AI và Người chơi hoàn toàn bình đẳng về mặt cơ học. Để làm một AI lái xe tăng, bạn chỉ cần viết một script tính toán ra hướng đi và gọi các hàm `Handle...` y hệt như cách người chơi bấm phím. Hiểu được `TankController` là đã hiểu 50% cách dòng dữ liệu chảy trong xe tăng.

## 2. `TankMover.cs` — Di chuyển và Vật lý
- **Vai trò:** Xử lý toàn bộ logic di chuyển, tăng tốc, giảm tốc và xoay thân xe tăng dựa trên hệ thống Vật lý (`Rigidbody2D`).
- **Cơ chế hoạt động:**
  - Nhận vector di chuyển từ `TankController`.
  - Có 2 chế độ di chuyển: Di chuyển tương đối theo hướng đầu xe (`Move`) hoặc Di chuyển tuyệt đối theo hệ tọa độ thế giới (`MoveWorldDirection` - phong cách game Diep.io).
  - Sử dụng hàm `CalculateSpeed` để tính toán gia tốc (acceleration) và giảm tốc (deacceleration) mượt mà, được giới hạn bởi `maxSpeed`.
  - Trong vòng lặp vật lý `FixedUpdate`, nó áp dụng vận tốc (`linearVelocity`) và góc xoay (`rotation`) trực tiếp vào `Rigidbody2D` để xe di chuyển trên bản đồ mà không bị lỗi xuyên tường.

## 3. Hệ thống Chiến đấu: Sát thương 1 chiều
Bao gồm tổ hợp các file liên kết với nhau: `Turret.cs` + `AimTurret.cs` + `Bullet.cs` + `Damagable.cs`. Hệ thống này tuân theo luồng sát thương 1 chiều rất rõ ràng.

- **`AimTurret.cs` (Ngắm bắn):** Nhận tọa độ mục tiêu từ Controller. Dùng toán học (`Mathf.Atan2`) để tính toán góc và xoay nòng súng từ từ (`turretRotationSpeed`) về phía mục tiêu. Nó có trường `firingAlignmentTolerance` (Dung sai ngắm bắn - ví dụ bằng 4 độ) để quyết định xem nòng súng đã ngắm đủ chuẩn để hệ thống tự động cho phép khai hỏa hay chưa.
- **`Turret.cs` (Tháp pháo):** Chịu trách nhiệm quản lý thời gian nạp đạn (`reloadDelay`) và sử dụng kỹ thuật Object Pool (tái sử dụng object) để tạo ra viên đạn (`Bullet.cs`) tại các đầu nòng súng (`turretBarrels`).
- **`Bullet.cs` (Viên đạn):** Khi sinh ra, đạn tự bay thẳng theo hướng nòng súng. Trong hàm `Update`, nó dùng `Physics2D.LinecastAll` quét một đoạn thẳng để dò va chạm (tránh lỗi đạn bay quá nhanh xuyên qua tường). Nếu chạm vật thể hợp lệ, nó sẽ lấy component `Damagable` trên vật thể đó và gọi hàm `Hit(damage)`.
- **`Damagable.cs` (Máu & Nhận sát thương):** Là điểm cuối cùng của chuỗi sự kiện. Chứa `MaxHealth` và `Health`. Khi bị gọi hàm `Hit(damage)`, nó trừ máu. Nếu máu về `<= 0`, nó kích hoạt sự kiện `OnDead` (báo hiệu xe bị phá hủy).

**Luồng tóm tắt:** Nhấn chuột -> `TankController.HandleShoot()` -> `Turret.Shoot()` -> Sinh ra `Bullet` bay đi -> `Bullet` chạm địch -> `Damagable.Hit()` -> Trừ máu.

## 4. `PlayerInput.cs` — Bơm dữ liệu từ người chơi
- **Vai trò:** Lắng nghe bàn phím và chuột của người chơi, sau đó chuyển đổi thành dữ liệu (Vector) để "bơm" (inject) vào `TankController`.
- **Cơ chế:**
  - `GetBodyMovement()`: Đọc phím W, A, S, D. Nếu dùng `useWorldMovement` (Diep.io style), nó gọi thẳng `tankController.HandleMoveWorldDirection()`.
  - `GetTurretMovement()`: Đọc vị trí chuột trên màn hình, dịch sang tọa độ không gian Game (`ScreenToWorldPoint`), sau đó kích hoạt sự kiện xoay nòng súng.
  - `GetShootingInput()`: Bắt sự kiện click chuột trái để kích hoạt lệnh bắn.
- **Đối chiếu với AI:** Để làm AI, bạn không đọc phím/chuột mà đọc vị trí của xe tăng người chơi (Player). Sau đó dùng thuật toán tìm đường (Pathfinding) tạo ra vector hướng đi. Cuối cùng, AI cũng gọi đúng các hàm `HandleMove...` và `HandleShoot()` của `TankController` hệt như cách `PlayerInput` làm.

---
*Mẹo bổ sung:* Các hàm `Invoke()` (UnityEvent/C# Action) được sử dụng rất nhiều xuyên suốt các file này (như `OnShoot`, `OnDead`, `OnHit`) nhằm giảm sự phụ thuộc code. Các hiệu ứng hình ảnh (VFX), âm thanh (SFX) hay thanh máu UI chỉ cần "lắng nghe" các sự kiện này để chạy mà không cần sửa code bên trong logic điều khiển.
