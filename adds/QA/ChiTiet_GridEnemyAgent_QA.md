# Hỏi Đáp Chuyên Sâu: GridEnemyAgent — Bộ Điều Khiển Hành Vi Xe Tăng Địch

Tài liệu này giải thích chi tiết `GridEnemyAgent.cs` (944 dòng) — thành phần **lái từng chiếc xe tăng địch**. Nếu `GridAStarPathfinder` là "bộ não tìm đường" và `MapLoader` là "bản đồ", thì `GridEnemyAgent` là "người tài xế": nó cầm con đường A\* trả về rồi biến thành lệnh xoay/tiến/bắn thật cho xe tăng, đồng thời tự xoay xở khi bị kẹt. Trình bày theo mạch: đầu vào/đầu ra → vòng lặp quyết định → từng cơ chế → phản biện.

---

## 1. GridEnemyAgent: Đầu vào, Đầu ra và Quá trình xử lý (Workflow)

**Bản chất:** một `MonoBehaviour` gắn trên mỗi xe tăng địch. Mỗi frame (`Update`) nó ra đúng **một** quyết định cho chiếc xe đó. Nó **không tự di chuyển** mà ra lệnh qua `TankController` — đúng cái tay lái mà người chơi cũng dùng, nên xe địch và xe người tuân theo cùng một hệ vật lý.

**Đầu vào (Input):**
- `mapLoader`, `navMask`: để hỏi đường đi được và tra tọa độ.
- `eagleTarget`, `playerTarget`, `playerTargets[]`: các mục tiêu — Eagle Base (đích chính) và (các) người chơi.
- `tankController`: cái "vô-lăng" để ra lệnh xoay thân, xoay nòng, bắn.
- Hàng loạt tham số tinh chỉnh (Inspector): `replanInterval`, các ngưỡng căn hướng, các mốc phát hiện kẹt...

**Đầu ra (Output):**
- Lệnh điều khiển gửi vào `TankController` mỗi frame: `HandleMoveBody`, `HandleTurretMovement`, `HandleShoot`.
- Số liệu backtest (`btReplanCount`, `btShotCount`, `btRecoveryCount`...) để đo lường so sánh thuật toán.

**Luồng xử lý (Workflow) mỗi frame — chính là hàm `Update` (dòng 99):**
1. **Có mục tiêu trong tầm bắn?** → Dừng lại, xoay nòng, bắn. (Không cần đi đâu nữa.)
2. **Đang lùi thoát kẹt?** → Tiếp tục lùi cho hết thời gian.
3. **Có vật cản phá được chặn đường?** → Chuyển sang chế độ "bắn thông đường".
4. **Tới giờ tính lại đường (hoặc chưa có đường)?** → Chạy A\* (`ReplanPath`).
5. **Bám đường** (`FollowPath`): biến ô kế tiếp thành lệnh xoay/tiến.
6. **Theo dõi tiến độ** (`UpdateProgressTracking`): phát hiện kẹt để kích hoạt cứu hộ.

> Đây là một **cây quyết định có thứ tự ưu tiên**: bắn > thoát kẹt > phá vật cản > đi đường. Việc nào cao hơn được xử lý trước và `return` luôn, không rơi xuống việc thấp hơn trong cùng frame.

---

## 2. Cơ chế Bắn: Khi nào nổ súng, khi nào nhịn?

### `GetShootingTarget` (dòng 156) — chọn mục tiêu
Ưu tiên **người chơi gần nhất trong tầm** trước (`GetNearestPlayerInRange`), nếu không có thì tính tới **Eagle Base**. Lưu ý hai tầm khác nhau: `playerShootingRange = 7`, `eagleShootingRange = 5`.

### `CanShootTarget` (dòng 208) — có bắn trúng không? Đây là hàm tinh tế nhất
Không phải cứ trong tầm là bắn. Hàm này bắn một tia (`Physics2D.RaycastAll`) từ nòng tới mục tiêu và xét **vật cản chắn giữa**:
- Nếu tia đụng **tường** trước khi tới mục tiêu → **không bắn** (bắn cũng trúng tường).
- Nếu tia đụng **xe tăng địch cùng phe** (friendly) → **bỏ qua nó, xét tiếp** — vì đạn được thiết kế xuyên qua đồng đội (dòng 231).
- Nếu tia chạm đúng mục tiêu → **bắn**.

> **Tại sao dùng `RaycastAll` rồi sắp xếp theo khoảng cách thay vì `Raycast` thường?** Vì `Raycast` chỉ trả về vật **đầu tiên** trúng — mà vật đó có thể là một xe đồng đội vô hại đứng chắn. `RaycastAll` cho ta **toàn bộ** vật trên đường đạn, sắp theo gần→xa, để "nhìn xuyên" qua đồng đội mà vẫn dừng lại ở tường/kẻ địch. Nếu không, xe địch sẽ **đứng ngây ra không bắn** chỉ vì có một đồng đội đứng lấp ló phía trước.

### Trường hợp đồng đội che mục tiêu: `TryQueueFriendlyShotBlocker` (dòng 182)
Ngược lại với trên: khi mục tiêu **đang bị đồng đội che** khiến ta không có đường đạn sạch, ta không bắn (sợ... thực ra đạn xuyên đồng đội nên vấn đề là đường đi). Cơ chế này đánh dấu ô mà đồng đội đang đứng thành "ô chặn tạm" (`pendingBlockedCells`) rồi ép tính lại đường — để xe **tự dịch sang chỗ khác** tìm góc bắn/đi tốt hơn thay vì chen chúc.

---

## 3. Cơ chế "Bắn thông đường" (Shoot-to-Clear vật cản phá được)

Trên bản đồ có những vật cản **phá được** (thùng gỗ, rào chắn — `IsDestructibleBlocked`). A\* coi chúng là "đi được" (xem tài liệu A\*), nhưng thực tế xe phải **bắn vỡ** mới qua được.

**Luồng xử lý (dòng 130-145 + `HandleShootToClear` dòng 367):**
1. Khi A\* không tìm được đường sạch, `ReplanPath` gọi `TryFindDestructibleOnPath` (dòng 314) — chạy **BFS loang** tìm xem trên đường tới Eagle có cái thùng nào chắn không.
2. Nếu có, ghi lại ô đó vào `_destructibleTarget`.
3. Frame sau, `HandleShootToClear`: nếu **đã tới đủ gần** thùng thì dừng lại, xoay nòng, bắn vỡ nó; nếu **còn xa** thì đi tới chỗ thùng trước (`ReplanToDestructible`).
4. Khi thùng vỡ (`!IsDestructibleBlocked`), xóa mục tiêu thùng, ép tính lại đường bình thường → giờ đường đã thông.

> **Tại sao phải xử lý riêng?** Nếu không, xe sẽ hoặc là đứng đâm đầu vào thùng mãi, hoặc coi thùng như tường và đi vòng thật xa. Cơ chế này cho AI hành vi "thông minh": *thấy thùng chắn thì bắn vỡ mà đi thẳng*.

---

## 4. Cơ chế Tính lại đường (Replan) và Chiến lược Dự phòng

### `ReplanPath` (dòng 285)
Cứ mỗi `replanInterval` (0.75s) hoặc khi mất đường, xe tính lại lộ trình:
1. Lấy ô hiện tại (`start`) và ô Eagle (`goal`).
2. Dựng danh sách **ô chặn động** = ô đồng đội đang đứng (`BuildDynamicBlockedCells`) + ô chặn tạm đang chờ (`pendingBlockedCells`).
3. Chạy A\* qua `TryFindPathWithFallback`.

> **Tại sao coi đồng đội là "ô chặn"?** Để hai xe **không cùng nhắm một ô** rồi húc nhau. Đây là một dạng "tránh va chạm nghèo" của A\* — cũng chính là điểm yếu mà PIBT (tầng trên) giải quyết bài bản hơn.

### `TryFindPathWithFallback` (dòng 391) — hạ dần độ an toàn khi bí
Đây là chiến lược "nới lỏng dần". A\* chạy trên `navMask` đã "phình" vật cản để chừa chỗ cho thân xe. Nhưng trên map cực hẹp, phình quá tay có thể khiến **không còn đường nào**. Khi đó:
- Thử lại với bán kính phình **nhỏ hơn dần** (radius giảm về 0).
- Radius 0 = không phình = chấp nhận đường sát tường còn hơn không có đường.

> Triết lý: "Thà đi sát tường một chút còn hơn đứng chết tại chỗ." Đảm bảo xe **luôn có đường** miễn là về mặt lưới có lối thông.

---

## 5. Trái tim của "tài xế": `FollowPath` và logic Xoay-rồi-Tiến (dòng 417)

Đây là chỗ biến ô lưới rời rạc thành **chuyển động mượt** của xe tăng có hướng thân. Xe không thể "dịch ngang"; nó phải **xoay thân về hướng ô đích rồi mới tiến**. Cách quyết định dựa trên **tích vô hướng (dot product)** giữa hướng mũi xe và hướng tới ô đích:

| Độ căn hướng (dotProduct) | Ý nghĩa | Hành động |
|---|---|---|
| ≥ `0.97` (`forwardAlignmentThreshold`) | Gần như thẳng hướng | **Tiến thẳng** hết ga |
| ≥ `0.85` | Hơi lệch | Vừa tiến vừa **bẻ lái nhẹ** |
| ≥ `0.5` | Lệch kha khá | **Vừa xoay vừa tiến ngắt quãng** (duty cycle) |
| < `0.5` | Lệch nhiều / gần vuông góc | **Xoay tại chỗ** (hoặc arc nhỏ để thoát góc) |

- **`dotProduct`** cho biết "thẳng hướng tới đâu" (1 = trùng hướng, 0 = vuông góc, -1 = ngược).
- **`cross`** (tích có hướng) cho biết **quay trái hay quay phải** để về đúng hướng.
- **Duty cycle (`StepPartialDriveCycle`, dòng 490):** khi lệch vừa phải, xe tiến "nhấp nhả" (ví dụ 65% thời gian tiến, 35% chỉ xoay) — giúp vừa quay đầu vừa nhích, tránh vẽ vòng cung quá rộng.

> **Tại sao không xoay xong hẳn rồi mới đi cho đơn giản?** Vì như thế xe sẽ khựng-giật ở mỗi khúc cua, trông rất cứng và chậm. Ngưỡng dot-product nhiều mức tạo chuyển động **liền mạch như tài xế thật**: đoạn thẳng thì phóng, vào cua thì vừa lượn vừa đi. Đây chính là cải tiến "khử xoay giật" nêu trong luận văn.

### `GetWaypointReachDistance` (dòng 502) — coi như "đã tới ô" khi nào?
Ngưỡng "chạm ô" thay đổi thông minh: nếu **phía trước là khúc cua** thì dùng ngưỡng **chặt** (`0.12`) để bám cua sát; nếu là **đường thẳng** thì dùng ngưỡng **rộng** (`0.3`) để đi lướt cho nhanh, không cần chạm chính giữa từng ô.

### Hai chốt chặn an toàn đầu hàm
- **Ô kế tiếp có đồng đội đứng?** (`IsCellOccupiedByFriendly`) → xóa đường, tính lại (tránh húc nhau).
- **Ô kế tiếp là thùng phá được?** → dừng, bắn vỡ trước khi đi tiếp.

---

## 6. Hệ thống Phát hiện Kẹt và Cứu hộ Leo thang (phần công phu nhất)

Xe tăng trong môi trường đông đúc rất dễ kẹt (đâm góc, hai xe chèn nhau, đi lòng vòng). File này có **ba tầng cảm biến kẹt** khác nhau, và một **thang cứu hộ 4 bậc**.

### 6.1. Ba kiểu cảm biến "bị kẹt"

1. **Kẹt theo thời gian (`stuckTimeout`, dòng 563):** đứng gần như một chỗ (di chuyển < `progressEpsilon`) quá 1.5s → kẹt.
2. **Kẹt không gian / đi lòng vòng (`IsSpatiallyStuck`, dòng 658):** đây là cái tinh vi. Nó lấy lịch sử vị trí trong 3s gần nhất và hỏi: *"Xe có đi được quãng đường dài (`totalTravelDistance` lớn) NHƯNG lại không rời khỏi chỗ cũ bao nhiêu (`displacement` nhỏ) và không tiến gần đích hơn?"* → tức là **đang chạy vòng tròn tại chỗ**. Kiểu kẹt này timeout thường không bắt được vì xe vẫn "đang chuyển động".
3. **Kẹt do cạ tường (`IsScuffing` / `TryTriggerScuffRecovery`, dòng 569):** xe **đang chạm** vật cản (`rb2d.IsTouchingLayers`) mà **vận tốc gần bằng 0** quá `scuffTimeout` (0.4s) → đang húc tường đứng im.

### 6.2. Thang cứu hộ 4 bậc: `TriggerRecovery` (dòng 737)
Mỗi lần kẹt, xe **leo lên một nấc mạnh hơn** (`enum RecoveryLevel`), không lặp lại nấc cũ:

| Bậc | Tên | Làm gì |
|---|---|---|
| 1 | `ForcedReplan` | Ép tính lại đường ngay lập tức (có thể đường cũ đã lỗi thời). |
| 2 | `Reverse` | **Lùi xe** trong `reverseRecoveryDuration` (0.8s) kèm bẻ lái, để dứt khỏi chỗ chèn. |
| 3 | `ExpandedMask` | Tạo `navMask` mới với bán kính phình **lớn hơn +1** → buộc A\* tìm đường tránh xa tường hơn. |
| 4 | (lặp lại `ExpandedMask`) | Cứ ép replan tiếp. |

Khi xe **đi lại bình thường** một lúc (`RegisterProgress`, dòng 796), thang cứu hộ **tự tụt về None** để lần kẹt sau lại bắt đầu từ nhẹ.

### 6.3. Cứu hộ không gian: `TriggerSpatialRecovery` (dòng 689)
Riêng với kiểu "đi lòng vòng", cách xử lý thông minh hơn: nó lấy **các ô vừa mới đi qua** (`recentVisitedCells`) đánh dấu thành **ô cấm tạm**, rồi tính lại đường. → Ép A\* **không quay lại vết cũ**, buộc phải tìm hướng mới. Giống như bảo tài xế: *"Mấy chỗ vừa đi lòng vòng đó, cấm, tìm đường khác đi."*

> **Tại sao phải nhiều tầng phức tạp thế?** Vì đây là **điểm chết** của mọi hệ multi-agent: xe kẹt thì không tới được Eagle, làm hỏng cả phép đo so sánh thuật toán. Một cảm biến/một cách cứu là không đủ cho mọi kiểu kẹt (đứng im ≠ lòng vòng ≠ cạ tường). Hệ thống nhiều tầng + leo thang đảm bảo xe **luôn tự thoát được** mà không cần can thiệp tay.

---

## 7. Số liệu Backtest (dòng 84-90)

Các biến `bt...` (`[System.NonSerialized]`, tự reset về 0 khi spawn) là **dụng cụ đo lường** phục vụ so sánh 3 thuật toán:
- `btReplanCount`: số lần tính lại đường (thuật toán càng loạn càng replan nhiều).
- `btRecoveryCount`: số lần phải cứu hộ kẹt.
- `btShotCount`: số phát bắn.
- `btCellsVisited`: số ô đã đi qua (≈ độ dài quãng đường thực).
- `btInitialPathLength`: độ dài đường A\* ban đầu.

> Đây chính là dữ liệu chảy vào hệ thống backtest 900-run để vẽ biểu đồ trong luận văn. `GridEnemyAgent` vừa **hành động** vừa **tự ghi nhật ký** về hành động của mình.

---

## 8. `OnDrawGizmos` (dòng 931) — vẽ đường debug

Khi bật `drawPath`, xe vẽ một đường màu đỏ nối các ô trong lộ trình hiện tại (chỉ hiện trong Editor). Đây là công cụ **quan sát trực quan** để thấy A\* đang tính đường ra sao — rất hữu ích khi demo và gỡ lỗi.

---

## 9. Câu hỏi phản biện có thể gặp

**"Xe tăng có hướng thân, làm sao đi mượt theo đường A\* rời rạc?"**
→ Qua logic dot-product nhiều ngưỡng trong `FollowPath`: thẳng hướng thì phóng, hơi lệch thì vừa đi vừa lượn, lệch nhiều thì xoay tại chỗ. Cộng thêm ngưỡng "chạm ô" co giãn theo cua/thẳng. Nhờ đó chuyển động liền mạch, không khựng-giật ở mỗi khúc.

**"Nếu hai xe địch cùng nhắm một chỗ thì sao?"**
→ Mỗi xe coi đồng đội là ô chặn khi replan (`BuildDynamicBlockedCells`) và kiểm tra ô kế tiếp có đồng đội không trước khi đi (`IsCellOccupiedByFriendly`). Đây là tránh-va-chạm mức cơ bản của A\*; PIBT ở tầng trên phối hợp bài bản hơn — đó là lý do đồ án cần các tầng cao hơn.

**"Xe bị kẹt thì xử lý thế nào?"**
→ Ba cảm biến kẹt (đứng im theo thời gian / đi lòng vòng theo không gian / cạ tường theo vật lý) và thang cứu hộ 4 bậc leo thang: ép replan → lùi xe → phình mask rộng hơn → lặp. Riêng lòng vòng thì cấm các ô vừa đi để ép đổi hướng.

**"Vì sao xe không bắn dù địch ở ngay trước mặt?"**
→ Vì `CanShootTarget` kiểm tra đường đạn: có tường chắn thì nhịn. Nhưng đồng đội chắn thì vẫn bắn (đạn xuyên đồng đội) nhờ `RaycastAll` sắp theo khoảng cách để "nhìn xuyên" qua bạn.

**"Thùng gỗ chặn đường thì xe làm gì?"**
→ Cơ chế shoot-to-clear: BFS tìm thùng trên đường, đi tới gần, bắn vỡ, rồi replan đi tiếp. Không đứng đâm đầu, không đi vòng vô lý.

**"GridEnemyAgent có tự di chuyển xe không?"**
→ Không. Nó chỉ ra lệnh qua `TankController` — đúng bộ điều khiển mà người chơi dùng. Nhờ vậy xe địch và xe người chung một hệ vật lý, đảm bảo so sánh công bằng (không xe nào có lợi thế điều khiển riêng).

**"Vì sao dùng `Update` mỗi frame chứ không phải một coroutine định kỳ?"**
→ Vì điều khiển xe cần phản hồi **mượt theo từng frame** (xoay/tiến liên tục). Còn phần nặng (A\*) đã được tiết chế bằng `replanInterval` để không chạy mỗi frame — chỉ tính lại đường mỗi 0.75s, còn lại chỉ bám đường sẵn có.
