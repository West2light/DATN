# Hỏi Đáp Chuyên Sâu: MapLoader và Hệ Thống Tọa Độ, Vật Lý

Tài liệu này tổng hợp các câu hỏi và giải thích chi tiết về luồng hoạt động của `MapLoader.cs`, hệ tọa độ Lưới (Grid) so với không gian thực (World), và các quyết định thiết kế vật lý trong game.

---

## 1. MapLoader: Đầu vào, Đầu ra và Quá trình xử lý (Workflow)

**Đầu vào (Input):**
- Một file text định dạng `.map` (MAPF). File có chứa thông tin chiều cao, chiều rộng và một ma trận các ký tự (ví dụ: `.` là đường đi, `@`, `T`, `W` là vật cản).
- Các tham số cấu hình trên Inspector (Hình ảnh cỏ, tường, kích thước cửa sổ cắt map...).

**Đầu ra (Output):**
- **Dữ liệu RAM:** Một mảng 2 chiều `char[][] grid` lưu trữ logic đi lại của toàn bộ map (để AI tra cứu).
- **Scene Unity:** Một bức ảnh nền (Ground) khổng lồ, hàng ngàn `GameObject` vật cản (Tường, Cây) có gắn Collider, và 4 bức tường vô hình bao bọc mép bản đồ. Camera cũng được tự động zoom cho vừa vặn.

**Luồng xử lý (Workflow):**
1. **Khởi tạo:** Xóa sạch map cũ. Dựa vào nền tảng (WebGL hay Windows) để quyết định đường dẫn đọc file.
2. **Đọc File:** Hàm `ReadMapLines` bóc tách từng dòng text và chuyển nó thành mảng ký tự `grid[][]`.
3. **Build Map (Coroutine):** 
   - Hàm `ComputeBuildWindow` tính toán phần bản đồ cần vẽ.
   - Vẽ nền đất chung `CreateGroundBackground`.
   - Vòng lặp `BuildTilesCoroutine` tạo ra các vật cản. Để tránh giật lag (freeze) game, cứ tạo 150 ô gạch thì vòng lặp sẽ nghỉ 1 frame.
   - Hàm `CreateMapBounds` bọc 4 bức tường ngoài. Cục static batching được gọi để tối ưu đồ họa GPU.

---

## 2. Tính năng Build Window (Cửa sổ cắt map)

Trong Inspector có 4 biến: `mapOffsetX`, `mapOffsetY`, `maxBuildWidth`, `maxBuildHeight`.
Được xử lý thông qua hàm `ComputeBuildWindow()`.

- **Mục đích:** Nếu bản đồ quá to (ví dụ 10.000 x 10.000), việc load tất cả sẽ làm sập game. Tính năng này cho phép chỉ trích xuất một vùng nhỏ (Ví dụ: từ tọa độ 20, 30 vẽ ra 50x50 ô) để chơi.
- **Biến `buildStartX` và `buildStartY`:** Là điểm neo gốc (góc dưới bên trái) của cái "khung hình" mà game thực sự sẽ vẽ.
- **Nếu các biến này set = 0:** Tính năng Cắt bản đồ sẽ bị tắt (Auto). Khung vẽ `buildWidth/Height` sẽ lấy toàn bộ diện tích của bản đồ gốc (`availableWidth`).

**Thuật toán chống lỗi Tràn viền (Out of Bounds):**
Đoạn code dùng `Mathf.Clamp` để đảm bảo điểm bắt đầu không âm và không rớt ra khỏi lề phải. Nó dùng `Mathf.Min(maxBuildWidth, availableWidth)` để nếu người dùng tham lam đòi vẽ 50 ô, nhưng thực tế bản đồ chỉ còn 20 ô, nó sẽ tự động chốt hạ vẽ 20 ô để không bị văng lỗi.

---

## 3. Hệ Tọa Độ Ô (Cell/Grid) vs Hệ Tọa Độ Thực (World)

**Hệ Tọa Độ Ô (Grid):**
- Gốc `(0,0)` nằm ở **Góc trên - Trái**. Trục X tăng sang phải, trục Y **tăng xuống dưới** (Giống hệt cách đọc chữ trong file Text).

**Hệ Tọa Độ Thực (Unity World):**
- Gốc `(0,0)` nằm ở **Chính giữa màn hình**. Trục X tăng sang phải, trục Y **tăng lên trên**. Z = 0 (mặt phẳng 2D).

### Tại sao lại cần hàm phiên dịch `CellToWorld` và `WorldToCell`?

1. **`CellToWorld` (Logic -> Vật lý):** Dùng để vẽ mọi thứ (Xe tăng, Tường, Vị trí AI cần đến) từ lý thuyết ra màn hình.
   *Hàm này tự động bù trừ nửa ô (`tileSize / 2`) và nhân với `-localY` (đảo ngược trục Y) để cả bản đồ luôn nằm chuẩn xác ngay tâm màn hình.*
2. **`WorldToCell` (Vật lý -> Logic):** Dùng khi có sự kiện vật lý xảy ra. Ví dụ:
   - Khi người chơi click chuột vào màn hình -> Hỏi xem đang bấm vào ô nào.
   - Khi AI chuẩn bị chạy thuật toán tìm đường -> Hỏi xem AI đang đứng ở ô nào.
   - Khi đạn bắn vỡ bức tường vật lý -> Tính ra tọa độ ô của bức tường để cập nhật mảng `grid` thành đất trống, giúp AI biết đường đã thông.

### Tại sao không bỏ Lưới (Cell) và dùng luôn tọa độ World cho tiện?
- **Hiệu suất (Performance):** Tra cứu phần tử mảng `grid[y][x]` nhanh gấp hàng vạn lần so với việc bắn tia dò tìm vật lý `Physics.Raycast` của không gian World.
- **Tìm đường (Pathfinding):** AI như A* bắt buộc phải chạy trên một ma trận Lưới (Node/Graph) rời rạc, không thể chạy trên không gian điểm vô cực.
- **Chính xác & Dữ liệu:** Dùng Lưới (số nguyên `int`) ngăn chặn mọi lỗi sai số làm tròn của số thập phân `float`. Lưu trữ 10.000 ký tự text cực nhẹ so với việc phải lưu 10.000 cái GameObjects tọa độ Transform trong RAM.

---

## 4. Các thuật toán Sinh Ra (Spawn) Tối Ưu

Trong quá trình tìm chỗ trống để thả xe tăng / vật phẩm, `MapLoader` cung cấp 4 hàm theo thứ tự thông minh tăng dần:

1. **`TryFindWalkableNear`:** "Nếu ô X vướng tường, hãy thả ở chỗ đất trống gần nhất". (Dễ đè lên nhau nếu thả nhiều vật).
2. **`TryFindWalkableWithSpace`:** Đất trống nhưng phải kiểm tra xem có nhiều lối thoát (`minNeighbors`) và thuộc vùng đất rộng rãi (`minRegionSize`) không. Tránh thả xe tăng vào một hốc cụt hở nóc.
3. **`TryFindAvailableSpawnNear`:** Thả nhiều xe tăng cùng lúc. Nó dùng danh sách `reservedCells` (Sổ xí chỗ). Xe nào đáp xuống sẽ ghim tọa độ, xe rớt sau phải tìm ô đất trống khác **cách xa** xe trước. Chống hiện tượng 48 AI sinh ra đè lên đầu nhau.
4. **`TryFindAvailableSpawnInRange`:** Giống hàm 3, nhưng ép khu vực thả rơi vào một **vành đai** (Donut - Min Distance đến Max Distance) quanh một điểm gốc (Ví dụ: Căn cứ Eagle Base). Đảm bảo địch không sinh ra sát vách căn cứ, cũng không sinh ra quá xa gây nhàm chán.
   *(Thuật toán này cực kỳ tối ưu vì nó tính ra hình vuông Bounding Box bằng `Mathf.Max/Min`, rồi chỉ chạy vòng lặp tìm kiếm trong hình vuông bé xíu đó thay vì quét cả bản đồ).*

---

## 5. Kiến trúc Vật Lý: Tại sao Tường có tận 2 Collider?

Trong game, mỗi bức tường gạch có:
- Một `BoxCollider2D` đặc cứng đơ (`isTrigger = false`), nằm ở Layer `Walls`.
- Một `BoxCollider2D` xuyên thấu (`isTrigger = true`), nằm ở Layer `Hittable`.

**Nguyên nhân bắt buộc phải tách đôi:**
1. **Chia để trị Layer:** Có những vật cản (như Dòng Sông) chặn không cho Xe Tăng đi qua (cần layer `Walls`), nhưng lại cho phép Đạn bay xuyên ngang mặt nước (Không có layer `Hittable`). Tách 2 Collider giúp ta dễ dàng gán vật lý tùy thích cho từng loại địa hình.
2. **Tính chất phản lực:** Xe tăng cần va vào tường cứng để không đi xuyên tường (Tạo ma sát, chặn đứng tốc độ). Đạn thì bay cực nhanh bằng Raycast hoặc Xuyên thấu, nó đụng tường chỉ để báo "Bùm" rồi gọi hàm trừ máu `Damagable.Hit`, chứ đạn không thể "tông" bức tường lùi lại phía sau được.
3. **Hiệu suất CPU:** Xe tăng đi đường chỉ va chạm với `Walls`. Tia đạn quét qua chỉ dò tìm `Hittable`. Hai tiến trình này bỏ qua nhau (bằng bàng Physics Collision Matrix), giúp CPU máy tính được giảm tải một nửa gánh nặng tính toán va chạm.
