Đoạn code bạn cung cấp chứa toàn bộ các biến cấu hình (cả public lẫn private) của một AI Xe tăng (Enemy Agent). Các biến này đóng vai trò quyết định độ thông minh, khả năng lái xe, kỹ năng bắn súng và đặc biệt là khả năng **tự gỡ rối (Anti-stuck/Recovery)** khi xe bị kẹt tường.

Dưới đây là giải thích chi tiết từng biến, được phân loại theo nhóm chức năng để bạn dễ theo dõi:

---

### 1. Cấu trúc dữ liệu tự định nghĩa
*   `SpatialSample`: Một cấu trúc lưu lại **dấu chân (dữ liệu không gian)** của xe tăng trong quá khứ. Lưu lại 3 thứ: Tọa độ đứng (`Position`), Thời gian lúc đó (`Time`), và Khoảng cách so với đích đến (`GoalDistance`). Dùng để AI tự nhận biết mình có đang bị kẹt hay không (chạy loanh quanh một chỗ).
*   `RecoveryLevel`: Các mức độ báo động khi xe bị kẹt (Bình thường -> Ép tìm lại đường -> Lùi xe -> Mở rộng cấm địa).

---

### 2. Các tham chiếu cốt lõi (Dependencies)
*   `mapLoader`: Để AI có thể hỏi đường, xem ô nào đi được.
*   `navMask`: Bộ lọc đường (chi phí mềm), giúp AI biết né sát tường hoặc né nhau.
*   `tankController`: Bộ phận truyền lệnh (Nhấn ga, bẻ lái, bóp cò) xuống xe tăng thật.
*   `eagleTarget`: Mục tiêu Căn cứ (Đại bàng) để AI ưu tiên tấn công.
*   `playerTarget` / `playerTargets`: Danh sách người chơi để AI lùng sục và tiêu diệt (hỗ trợ cả chơi mạng LAN nhiều người).

---

### 3. Cấu hình Dò đường và Di chuyển (Pathfinding & Navigation)
*   `tankClearanceRadius (0.4f)`: Độ to của xe tăng. Dùng để kiểm tra xem khe hở có đủ rộng để xe chui lọt hay không.
*   `enableSmoothing`: Cho phép làm mượt đường đi (bỏ qua bớt các khúc cua ziczac thừa thãi).
*   `replanInterval (0.75f)`: Bao lâu thì AI suy nghĩ lại đường đi một lần (0.75 giây).
*   Các biến `waypointReachDistance...`: Định nghĩa xem xe tăng **cách điểm mốc bao nhiêu thì coi như đã đến nơi** để chuyển sang điểm tiếp theo. (Tách ra 3 mức: Bình thường, Đang đi thẳng, Đang cua gắt - giúp AI ôm cua mượt hơn).
*   `drawPath`: Bật/Tắt vẽ vạch đường đi màu đỏ (Gizmos) trong màn hình Scene để debug.

---

### 4. Cấu hình Chiến đấu (Combat)
*   `eagleShootingRange (5f)`: Tầm nhìn bắn Căn cứ (5 mét).
*   `playerShootingRange (7f)`: Tầm nhìn lùng sục Người chơi (7 mét, xa hơn bắn căn cứ).
*   `lineOfSightMask`: Khai báo những vật thể nào sẽ chắn tầm nhìn (Ví dụ: Bức tường). Nếu có tường chắn giữa AI và Người chơi thì AI sẽ không bắn mù.

---

### 5. Cấu hình Bẻ lái - Cơ học (Steering)
Do xe tăng không thể "lướt" đi ngang (trượt ngang), nó phải xoay đầu rồi mới đạp ga.
*   `forwardAlignmentThreshold (0.97f)`: Xe phải xoay đầu thẳng tắp gần như tuyệt đối (97%) với mục tiêu thì mới được phép đạp 100% ga.
*   `turningDriveAlignmentThreshold (0.85f)`: Nếu mới xoay được 85%, xe sẽ chỉ dám nhích nhẹ ga để vừa cua vừa tiến.
*   `mostlyAlignedTurnScale (0.3f)`: Khi đã gần thẳng đầu, giảm tốc độ bẻ nòng/xoay xe lại (còn 30%) để tránh bị "lắc lư qua lại" do lố đà (overshoot).
*   `partialDrive...`: Cấu hình thuật toán nhấp nhả ga (băm xung - Duty Cycle) giúp xe lết từ từ qua khúc cua hẹp.

---

### 6. Cấu hình Gỡ rối (Anti-Stuck & Recovery) - Phần tinh hoa nhất
Khi AI lái ngu và húc đầu vào tường, nó cần các biến này để tự thoát ra:
*   `stuckTimeout (1.5f)` & `progressEpsilon (0.05f)`: Nếu trong 1.5 giây mà xe di chuyển được ít hơn 0.05 mét (bị kẹt cứng) -> Kích hoạt báo động kẹt.
*   `obstacleContactMask` & `scuffTimeout (0.4f)`: Nếu húc đầu cọ xát vào tường (`scuff`) quá 0.4 giây mà tốc độ lờ đờ (`scuffVelocityThreshold = 0.1`) -> Báo động kẹt.
*   `reverseRecoveryDuration (0.8f)`: Khi bị kẹt, AI sẽ cài số lùi và lùi lại trong đúng 0.8 giây để thoát khỏi góc kẹt, sau đó mới tìm đường đi tiếp.
*   **Các biến `spatialStuck...`**: (Kẹt không gian) AI có thể không bị tông tường, nhưng nó cứ đi lòng vòng mãi trong 1 khu vực nhỏ (Stuck trong mê cung). Nếu trong 3 giây (`spatialStuckWindow`) mà nó đi loằng ngoằng xa tận 1.5m (`spatialStuckMinTravelDistance`) nhưng vị trí thực tế chỉ xê dịch có 0.75m (`spatialStuckMaxDisplacement`) -> Nó nhận ra mình đang kẹt lòng vòng -> Tự động đánh dấu khu đó là "Vùng cấm" và ép tìm đường vòng xa hơn.

---

### 7. Các biến trạng thái nội bộ (Private State Variables)
Đây là "bộ nhớ ngắn hạn" của AI, chỉ dùng để theo dõi lúc đang chạy (không chỉnh trên Inspector):
*   `currentPath`: Lưu danh sách các ô đường đi hiện tại.
*   `pathIndex`: Đang đi đến ô thứ mấy trong danh sách trên.
*   `spatialSamples` & `recentVisitedCells`: Lưu lại nhật ký các vị trí vừa đi qua để đối chiếu xem có bị kẹt vòng lặp không.
*   `recoveryLevel`: Mức độ hoảng loạn hiện tại (Đang bình thường, đang lùi xe, hay đang tuyệt vọng).
*   `pendingBlockedCells`: Các ô mà xe vừa phát hiện ra là "lỗi/kẹt", ép A* coi nó là Tường để tìm đường khác.
*   `_destructibleTarget`: Mục tiêu tạm thời (VD: Đi đường thấy cục gạch chắn lối, phải lưu lại cái cục gạch đó để bắn vỡ nó rồi mới đi tiếp).
*   Và một đống biến đếm thời gian (`lastProgressTime`, `scuffStartTime`, `reverseRecoveryEndTime`...) để đo lường các bộ đếm Anti-Stuck ở phần 6.

---

### 8. Chỉ số đo lường hiệu năng AI (Backtest Metrics)
Đây là các biến phục vụ cho công tác kiểm thử (Test). Nó đo lường xem con AI này thông minh hay ngu.
*   `btReplanCount`: Đếm số lần nó phải tính lại đường. (Càng nhiều nghĩa là nó chạy càng ngu hoặc bị cản trở nhiều).
*   `btRecoveryCount`: Đếm số lần nó tông tường và phải cài số lùi để thoát.
*   `btShotCount`: Tổng số viên đạn đã bắn.
*   `btCellsVisited`: Số ô đất đã dẫm lên.
*   `btInitialPathLength`: Quãng đường lý thuyết ban đầu lúc mới đẻ ra.
*   `btSpawnTime`: Sinh ra lúc mấy giờ.