# Hỏi Đáp Chuyên Sâu: GridAStarPathfinder và Thuật Toán Tìm Đường A\*

Tài liệu này giải thích chi tiết `GridAStarPathfinder.cs` — bộ não tìm đường **baseline** của đồ án. Đây là thuật toán A\* thuần chạy trên lưới ô của `MapLoader`, đóng vai trò mốc so sánh với hai tầng cao hơn là PIBT C# và PIBT C++/TCP. Trình bày theo mạch: đầu vào/đầu ra, từng khối logic, và các câu "Tại sao lại làm thế".

---

## 1. GridAStarPathfinder: Đầu vào, Đầu ra và Quá trình xử lý (Workflow)

**Bản chất:** Đây là một `static class` — **không có trạng thái riêng, không cần khởi tạo (`new`)**. Nó giống một cái "máy tính bỏ túi": đưa đề bài vào, nó nhả kết quả ra, dùng xong quên luôn. Mọi agent trong game xài chung đúng một bộ máy này.

**Đầu vào (Input):**
- `mapLoader`: để hỏi "ô này đi được không" (`IsWalkable`) và đổi ô ↔ tọa độ world.
- `start`, `goal`: ô xuất phát và ô đích (kiểu `Vector2Int`, tọa độ nguyên).
- `path`: một `List<Vector2Int>` **rỗng do bên gọi đưa vào** để hàm ghi kết quả vào đó (xem mục 8 giải thích vì sao không `return` list mới).
- `navMask` (tùy chọn): lớp mặt nạ điều hướng bổ sung — cho phép mỗi agent có vùng cấm/vùng đắt riêng, và gán "chi phí mềm" cho từng ô.
- `blockedCells` (tùy chọn): tập ô bị chặn tạm thời (ví dụ chỗ đang có agent khác đứng).
- `clearanceRadius`, `enableSmoothing` (tùy chọn): phục vụ làm mượt đường, mặc định tắt.

**Đầu ra (Output):**
- `bool`: tìm được đường hay không.
- Danh sách `path` được đổ đầy chuỗi ô từ `start` → `goal` (nếu thành công).

**Luồng xử lý (Workflow) của A\*:**
1. **Chuẩn hóa điểm đầu/cuối** (`TryResolveEndpoint`): nếu `start` hoặc `goal` lỡ rơi vào tường, tự "nắn" về ô đi được gần nhất. Không nắn được → thất bại luôn.
2. **Reset bộ nhớ tạm**: dọn 4 cấu trúc `heap`, `closed`, `cameFrom`, `gScore`.
3. **Vòng lặp A\***: lấy ô có tổng chi phí ước lượng `f` nhỏ nhất ra khỏi heap, xét 4 ô hàng xóm, cập nhật chi phí, đẩy vào heap.
4. **Chạm đích**: khi lấy ra đúng ô `goal`, truy vết ngược (`BuildPath`) để dựng lại con đường, rồi (tùy chọn) làm mượt.
5. **Hết heap mà chưa tới đích**: trả `false` — không có đường.

---

## 2. Trái tim A\*: `f = g + h` nghĩa là gì?

A\* xếp hạng các ô cần khám phá bằng công thức **f = g + h**:
- **g (`gScore`)**: chi phí **thật** đã đi từ điểm xuất phát tới ô này (số bước đã bước).
- **h (`Heuristic`)**: chi phí **ước lượng** còn lại từ ô này tới đích (đoán bằng khoảng cách Manhattan).
- **f**: tổng ước lượng của cả hành trình nếu đi qua ô này.

A\* luôn ưu tiên mở ô có `f` nhỏ nhất — tức ô "có vẻ nằm trên con đường ngắn nhất". Đây là điều khiến A\* nhanh hơn Dijkstra: nó không mò lung tung mà **nhắm hướng về đích**.

### Hàm `Heuristic` (dòng 230): tại sao dùng Manhattan mà không dùng đường chim bay?
```
Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)
```
Đây là khoảng cách Manhattan (đi theo ô ngang + dọc, như taxi chạy trong phố ô bàn cờ). Lý do:
- Bản đồ chỉ cho đi **4 hướng** (lên/xuống/trái/phải), không đi chéo. Manhattan phản ánh **đúng** số bước tối thiểu trong lưới 4 hướng.
- Nếu dùng khoảng cách Euclid (đường chim bay), heuristic sẽ **đánh giá thấp hơn thực tế** một cách không cần thiết → A\* mở thừa nhiều ô, chậm đi mà không lợi gì.
- Manhattan là "admissible" (không bao giờ ước lượng vượt quá chi phí thật) → **đảm bảo A\* luôn tìm ra đường ngắn nhất**.

> Ghi chú thiết kế (theo CLAUDE.md): A\* của đồ án **cố tình giữ 4 hàng xóm**, không chuyển sang 8 hàng xóm, để nhất quán với mô hình bài toán MAPF và tương thích với tầng PIBT phía sau.

---

## 3. Tại sao dùng Binary Min-Heap thay vì List thường?

Ở mỗi bước, A\* phải lấy ra ô có `f` nhỏ nhất trong hàng đợi. Có 2 cách:
- **List thường:** mỗi lần muốn lấy min phải quét cả danh sách → chậm O(n) mỗi bước. Trên map 251×180 với hàng vạn ô, đây là thảm họa hiệu năng.
- **Binary min-heap (`s_heap`):** cây nhị phân luôn giữ phần tử nhỏ nhất ở gốc. Lấy min và chèn mới chỉ tốn O(log n).

**`HeapPush` (dòng 131):** thêm phần tử vào cuối rồi "trồi lên" (bubble-up) — hoán đổi với cha chừng nào còn nhỏ hơn cha.
**`HeapPop` (dòng 144):** lấy gốc (min), đưa phần tử cuối lên gốc rồi "chìm xuống" (sift-down) — hoán đổi với con nhỏ hơn cho tới đúng chỗ.

Phép `>> 1`, `<< 1 | 1` chỉ là mẹo tính nhanh chỉ số cha/con bằng dịch bit (cha = `(i-1)/2`, con trái = `2i+1`).

### "Lazy deletion" (xóa lười) — dòng 95-97 là gì?
Heap này **không hỗ trợ cập nhật độ ưu tiên** của một ô đã nằm trong heap. Thay vào đó, khi tìm ra đường tốt hơn tới một ô, ta cứ **đẩy thêm một bản mới** của ô đó vào heap (giá trị `f` mới, nhỏ hơn). Kết quả: heap có thể chứa **bản trùng cũ** của cùng một ô.

Xử lý: mỗi khi `HeapPop` lấy ra một ô, nếu ô đó **đã nằm trong `s_closed`** (đã xử lý xong rồi) thì **bỏ qua** (`continue`). Bản cũ giá trị lớn hơn sẽ luôn bị lấy ra sau bản mới, nên khi tới lượt nó thì ô đã closed → vứt đi. Cách này đơn giản và nhanh hơn nhiều so với đi dò tìm để sửa phần tử trong heap.

---

## 4. Tại sao dùng cache `static` dùng chung? Có nguy hiểm không?

Bốn cấu trúc dữ liệu lõi được khai báo `static readonly` và **dùng chung cho mọi lần gọi**:
```
s_heap, s_closed, s_cameFrom, s_gScore
```

- **Mục đích:** Tránh cấp phát bộ nhớ mới (`new`) mỗi lần tìm đường. Với hàng chục agent replan liên tục mỗi giây, việc `new` các Dictionary/List mới sẽ tạo rác (garbage) khiến bộ dọn rác (GC) chạy giật hình. Dùng chung + `Clear()` đầu mỗi lần chạy là gần như **không sinh rác**.
- **Có bị tranh chấp (race condition) không?** Không. Comment ở dòng 14 nói rõ: `Unity Update() is single-threaded`. Toàn bộ logic game chạy trên **một luồng chính duy nhất**, không có 2 agent nào gọi A\* cùng một khoảnh khắc. Vì thế xài chung một bộ nhớ tạm là an toàn tuyệt đối.

> Đây là điểm hay để trả lời phản biện về tối ưu: "Em dùng cache tĩnh để khử áp lực GC, an toàn vì Unity đơn luồng."

---

## 5. `IsWalkable` nhiều tầng: Ai được đi, ai bị cấm? (dòng 222)

Đây là "người gác cổng" quyết định một ô có được bước vào không. Thứ tự kiểm tra **rất quan trọng**:

1. **`blockedCells` chặn trước tiên:** nếu ô nằm trong danh sách chặn tạm (ví dụ agent khác đang đứng) → **cấm**. Đây là mức ưu tiên cao nhất.
2. **Ô "phá được" thì cho đi:** nếu ô là chướng ngại **có thể phá** (`IsDestructibleBlocked` — thùng gỗ, rào chắn) → **cho đi qua** dù nó đang là vật cản. Lý do: xe tăng có thể bắn vỡ nó, nên A\* nên coi đó là đường tiềm năng thay vì né hẳn.
3. **Còn lại hỏi `navMask` hoặc `mapLoader`:** nếu có mặt nạ điều hướng riêng của agent (`navMask`) thì hỏi nó; không thì hỏi thẳng lưới gốc `mapLoader.IsWalkable`.

> **Tại sao ô phá được lại coi là "đi được"?** Vì nếu coi nó là tường, AI sẽ đi vòng thật xa hoặc bó tay khi lối duy nhất bị một cái thùng chặn. Coi nó "đi được" khiến AI dám tiến tới, rồi cơ chế bắn phá sẽ mở đường.

---

## 6. `GridNavMask` và "chi phí mềm" (soft cost)

> File: `Assets/Scripts/Pathfinding/GridNavMask.cs` (453 dòng). A\* gọi tới nó ở hai chỗ: `IsWalkable` (cấm cứng) và `GetCellCost` (phụ phí mềm — dòng 113-116 của `GridAStarPathfinder`).

### 6.1. GridNavMask là gì? — "kính lọc" đặt chồng lên lưới gốc

`MapLoader` chỉ biết ô nào là tường thật (`@`). Nhưng xe tăng **có bề rộng**, không phải một điểm. Nếu để A\* đi sát mép tường, thân xe sẽ cạ vào tường và kẹt. `GridNavMask` là một lớp "kính lọc" phủ lên lưới gốc, tính sẵn cho mỗi ô **hai thông tin**:

1. **`agentWalkable[x,y]` (lớp CỨNG):** ô này xe tăng có đứng được không — chặt hơn `mapLoader.IsWalkable` vì đã trừ hao bề rộng xe.
2. **`proximityCost[x,y]` (lớp MỀM):** nếu đứng được thì "đắt" bao nhiêu — ô càng sát tường càng đắt, để A\* **thích đi ở giữa lối** hơn là men mép.

Cả hai được tính **một lần** khi build (rồi cache vào mảng 2 chiều), nên lúc A\* tra cứu chỉ là đọc mảng — cực nhanh.

### 6.2. Lớp CỨNG: hai cách "phình" vật cản

Có 2 chế độ tạo `agentWalkable`, dùng cho thí nghiệm đối chứng:

- **Chế độ Chebyshev / inflate (cũ, dòng 108):** một ô bị cấm nếu **trong bán kính `InflateRadius` ô** có bất kỳ tường nào (`HasBlockedNeighborWithinRadius`, dòng 355). Tức là "nới" tường ra thêm vài ô cho an toàn. Đơn giản, chạy được cả trong Edit Mode không cần vật lý.
- **Chế độ Physical (mới v4.2, dòng 151 `RebuildPhysical`):** đặt một **hình tròn bán kính `physicalRadius`** ngay tâm ô rồi bắn `Physics2D.OverlapCircle`. Nếu hình tròn chạm collider vật cản → cấm ô đó (dòng 178). Cách này **khớp đúng bán kính vật lý thật của xe tăng**, chính xác hơn kiểu "phình theo ô".

> **Tại sao có 2 chế độ?** Chế độ cũ giữ lại để so sánh (ablation): chứng minh chế độ physical cho đường sát thực tế hơn. Đây là kiểu quyết định "giữ code cũ để đối chứng khoa học" xuất hiện nhiều lần trong đồ án.

### 6.3. Lớp MỀM: `GetCellCost` trả về gì? (dòng 84)

Đây là phần "soft cost" cốt lõi. A\* cộng nó vào chi phí mỗi bước:
```
int tentativeG = s_gScore[current] + 1 + stepCost;   // 1 = bước cơ bản, stepCost = phụ phí mềm
```
`GetCellCost(cell)` trả về theo 3 mức:

| Tình huống | Giá trị trả về | A\* hiểu là |
|---|---|---|
| Ô không đứng được (`!IsAgentWalkable`) | `int.MaxValue` | **Tường — bỏ qua** (dòng 114 chặn) |
| Ô đứng được, **sát tường** (Chebyshev = 1) | `SoftCostNear` | Đi được nhưng **đắt** |
| Ô đứng được, **gần tường** (Chebyshev = 2) | `SoftCostMid` | Đi được, **hơi đắt** |
| Ô đứng được, ở giữa lối (Chebyshev ≥ 3) | `0` | Rẻ nhất — **ưu tiên** |

Phụ phí này được tính trong `BuildProximityCost` (dòng 377) dựa trên `ChebyshevDistanceToBlocked` — **khoảng cách vua-cờ (đi được cả 8 hướng) từ ô tới ô cấm gần nhất** (dòng 428). Càng gần tường, khoảng cách càng nhỏ, phụ phí càng cao.

> **Chebyshev chứ không Manhattan ở đây — vì sao?** Vì "sát tường" cần tính cả góc chéo: một ô nằm chéo góc so với tường cũng bị coi là kề. Chebyshev (max của |dx|,|dy|) mới bắt được lớp viền vuông quanh tường. (Còn heuristic tìm đường ở mục 2 dùng Manhattan vì chuyển động chỉ 4 hướng — hai chỗ dùng hai loại khoảng cách cho hai mục đích khác nhau, đừng lẫn.)

### 6.4. Tại sao tách hẳn "cấm cứng" và "phạt mềm"?

- **Cấm cứng (`int.MaxValue`):** ô mà xe **không thể** đứng (đâm tường). A\* không bao giờ chọn.
- **Phạt mềm (`SoftCostNear/Mid`):** ô xe **đứng được nhưng không nên** (cạ mép, dễ va agent khác). A\* **vẫn đi khi buộc phải** (ví dụ hành lang hẹp chỉ có đường sát tường), nhưng **né nếu có đường thoáng hơn**.

Nếu chỉ có cấm cứng, trên các map hẹp (Mansion, Gallows) nhiều đoạn sẽ thành "không có đường" một cách vô lý. Soft cost giải quyết đúng điểm này: **giữ được đường qua chỗ hẹp, nhưng ưu tiên đi giữa chỗ rộng** → xe chạy mượt, ít kẹt, ít đâm nhau.

### 6.5. Cập nhật lúc chạy: destructible & chướng ngại động

`GridNavMask` không phải build một lần rồi bất động — nó đồng bộ với thay đổi runtime:

- **`PatchDestructibleCells` (dòng 284):** sau khi build, mở lại các ô có thùng gỗ/rào phá được (và vùng lân cận bị "phình" chặn nhầm), để A\* dám tìm đường xuyên qua chúng — khớp với triết lý ở mục 5.
- **`SetCellAgentWalkable` (dòng 320):** `DynamicObstacleSpawner` gọi hàm này để bật/tắt walkability một ô khi **thùng sắt động xuất hiện/biến mất** → lần replan sau, A\* thấy ngay và né. Đây là mắt xích khiến **chế độ động** trong backtest hoạt động.

### 6.6. `HasClearance` — người gác cho khâu làm mượt (dòng 196)

Khi bật `enableSmoothing` (mục 10), trước khi cắt góc nối thẳng 2 ô, A\* hỏi `navMask.HasClearance(startWorld, endWorld, radius)`: liệu một **hình tròn bán kính `radius` trượt dọc đoạn thẳng** có cạ vào vật cản nào không. Đây là phép kiểm hình học thuần (đoạn thẳng vs ô-đã-nới-rộng, dòng 245 `SegmentIntersectsAabb`), **chạy được trong Edit Mode mà không cần vật lý**. Nhờ nó, đường tắt sau khi làm mượt vẫn đủ rộng cho thân xe tăng đi qua.

> **Tóm lại vai trò của `GridNavMask` trong hệ thống:** nó là lớp trung gian biến "lưới điểm lý tưởng" của `MapLoader` thành "lưới thực tế cho một khối xe tăng có bề rộng" — vừa cấm cứng chỗ đâm, vừa phạt mềm chỗ sát mép. Đây chính là cơ chế giúp agent "nhường đường mềm" nhau, tạo tiền đề để so sánh với PIBT (vốn phối hợp đa agent bài bản hơn ở tầng trên).

---

## 7. Chuẩn hóa điểm đầu/cuối: `TryResolveEndpoint` (dòng 166)

**Vấn đề:** Đôi khi `goal` là vị trí Eagle Base hoặc người chơi, mà ô đó lại **không đi được** (đứng sát tường, hoặc chính là ô vật cản). A\* thuần sẽ trả "không có đường" một cách vô lý.

**Giải pháp:** Trước khi chạy, nắn cả `start` và `goal` về ô đi được gần nhất:
- Nếu ô đã đi được → giữ nguyên.
- Nếu có `blockedCells` → dùng `TryFindWalkableNear` nội bộ (quét vòng tròn lan dần, có xét cả blocked/destructible).
- Nếu có `navMask` → nhờ mặt nạ tìm ô hợp lệ gần nhất theo luật riêng của agent.
- Không thì nhờ `mapLoader.TryFindWalkableNear`.

Nắn không được (cả map không có ô nào hợp lệ) → trả `false`, khỏi chạy A\* cho tốn công.

---

## 8. Tại sao truyền `List path` vào thay vì `return` một list mới?

Nhìn các hàm đều nhận `List<Vector2Int> path` rồi ghi vào đó, thay vì tạo và trả về list mới. Lý do là **tối ưu rác bộ nhớ (GC)**, giống mục 4:
- Mỗi agent giữ sẵn một `path` của riêng nó. Mỗi lần replan, A\* gọi `path.Clear()` (dòng 74) rồi đổ kết quả mới vào **đúng list cũ** đó.
- Không có list nào được `new` → không sinh rác → không giật hình khi hàng chục agent replan liên tục.

**`BuildPath` (dòng 235):** khi chạm đích, ta chỉ có bảng "ô này đến từ ô nào" (`cameFrom`). Hàm đi ngược từ `goal` về `start` theo bảng đó, thêm dần vào list, rồi **`Reverse()`** để có thứ tự xuôi `start → goal`.

---

## 9. Sáu phiên bản `TryFindPath` chồng nhau (overload) — dòng 20-72

Có tận 6 hàm cùng tên, khác số tham số. Đây là **method overloading**: các phiên bản ngắn chỉ là "lối vào tiện lợi", tất cả **đổ dồn về một phiên bản đầy đủ nhất** (dòng 64) với các tham số mặc định.
- Bên gọi đơn giản chỉ cần `TryFindPath(mapLoader, start, goal, path)`.
- Bên gọi cao cấp (PIBT, có mặt nạ, có ô chặn, có làm mượt) dùng phiên bản dài.

> **Tại sao không gộp làm một với tham số mặc định?** C# có hỗ trợ tham số mặc định, nhưng viết overload tường minh giúp mỗi tầng gọi đọc rõ ý định, và tránh nhầm lẫn thứ tự tham số kiểu `bool`/`float` đứng cạnh nhau.

---

## 10. Làm mượt đường: `SmoothPath` & `HasLineOfSight` (mặc định TẮT)

### `SmoothPath` (dòng 248)
Đường A\* thô đi theo từng ô nên trông "răng cưa" (đi bậc thang). `SmoothPath` cắt bớt các khúc thừa bằng thuật toán "string pulling": từ một ô neo, cố nối thẳng tới ô **xa nhất mà vẫn nhìn thấy nhau** (không có tường chắn giữa), bỏ hết các ô trung gian.

### `HasLineOfSight` (dòng 286)
Kiểm tra 2 ô có "nhìn thẳng" tới nhau không, bằng thuật toán vẽ đường kiểu Bresenham (supercover): đi từng bước dọc đường thẳng nối 2 ô, nếu **gặp bất kỳ ô nào không đi được** thì kết luận bị chắn.

### Tại sao mặc định TẮT làm mượt? (comment dòng 246-247)
Đây là **quyết định thiết kế quan trọng**, dễ bị hỏi:
> "Path raw 4-neighbor cell-to-cell is preferred for deterministic follow + PIBT compatibility."

- **Tính tất định (deterministic):** đường thô ô-sang-ô luôn cho kết quả y hệt, dễ tái lập trong backtest — trụ cột của việc so sánh khoa học.
- **Tương thích PIBT:** PIBT phối hợp đa agent theo **từng ô, từng bước thời gian**. Nếu làm mượt đường thành đoạn xiên chéo, agent sẽ "nhảy" qua ranh giới ô một cách không đồng bộ, phá vỡ mô hình đặt chỗ theo ô của PIBT.
- Cờ `enableSmoothing` vẫn để đó, bật được khi cần **thí nghiệm đối chứng (ablation study)** — so đường có mượt vs không mượt.

---

## 11. Câu hỏi phản biện có thể gặp

**"A\* của em khác gì Dijkstra?"**
→ A\* thêm heuristic `h` để nhắm hướng về đích, nên mở ít ô hơn Dijkstra rất nhiều mà vẫn đảm bảo tối ưu (vì heuristic Manhattan là admissible).

**"Vì sao chọn A\* làm baseline mà không dùng luôn PIBT cho tất cả?"**
→ A\* là chuẩn mực ai cũng biết, dễ hiểu, tất định. Nó làm **mốc so sánh**: cho thấy khi nhiều agent cùng chạy A\* độc lập thì đâm nhau / kẹt ra sao, từ đó làm nổi bật giá trị của PIBT (phối hợp đa agent). Không có baseline thì không có gì để so.

**"Nhiều agent cùng dùng một bộ cache tĩnh, sao không loạn?"**
→ Unity chạy đơn luồng, các lần gọi A\* nối tiếp nhau chứ không song song. `Clear()` đầu mỗi lần đảm bảo sạch. Đổi lại khử được áp lực GC.

**"A\* có xử lý va chạm giữa các agent không?"**
→ Bản thân A\* **không**. Nó chỉ tìm đường cho một agent, coi agent khác cùng lắm là `blockedCells`/`soft cost` tại thời điểm replan. Việc phối hợp thật sự (chống hoán đổi chỗ, chống deadlock) là nhiệm vụ của tầng PIBT — đó chính là lý do đồ án cần các tầng cao hơn.

**"Ô phá được (thùng) sao A\* lại cho đi xuyên?"**
→ Vì xe tăng bắn vỡ được nó. Coi nó "đi được" giúp AI chọn đường ngắn có thùng thay vì đi vòng vô lý; khi tới nơi, cơ chế bắn sẽ dọn đường.

**"Đường đi bị răng cưa, sao không làm mượt luôn cho đẹp?"**
→ Cố tình giữ thô để tất định và tương thích với mô hình ô-theo-bước của PIBT. Làm mượt chỉ bật khi cần thí nghiệm đối chứng.

**"Soft cost (chi phí mềm) để làm gì, khác gì cấm cứng?"**
→ Cấm cứng loại hẳn ô xe không đứng được (đâm tường). Soft cost chỉ **phạt** ô xe đứng được nhưng sát mép tường: A\* vẫn đi khi hành lang hẹp buộc phải đi, nhưng ưu tiên đi giữa lối khi có chỗ thoáng. Nhờ đó xe chạy mượt, ít cạ tường, ít kẹt trên các map hẹp như Mansion/Gallows. Cơ chế này nằm ở `GridNavMask.GetCellCost`, phạt theo khoảng cách Chebyshev tới tường gần nhất (sát = `SoftCostNear`, gần = `SoftCostMid`, xa = 0).

**"Xe tăng có bề rộng, A\* chạy trên ô điểm thì sao không đâm tường?"**
→ Đó là việc của `GridNavMask`: nó "phình" vật cản ra (theo `InflateRadius` hoặc bán kính vật lý qua `Physics2D.OverlapCircle`) rồi đánh dấu các ô sát tường là không-đứng-được. A\* chạy trên mặt nạ đã trừ hao này nên đường ra luôn chừa đủ khoảng trống cho thân xe.
