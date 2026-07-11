# Hỏi Đáp Chuyên Sâu: PIBTPlanner — Tầng Dẫn Hướng Luồng (Flow-Guidance)

Tài liệu này giải thích chi tiết `PIBTPlanner.cs` (377 dòng) — bộ não điều phối của **mode "PIBT C#"**, tầng thuật toán thứ hai của đồ án (giữa A\* baseline và PIBT C++/TCP). Trình bày theo mạch: bản chất trung thực → đầu vào/đầu ra → từng cơ chế → phản biện.

> ⚠️ **ĐỌC MỤC 0 TRƯỚC.** Đây là điểm phản biện quan trọng nhất của cả đồ án. Trình bày sai một câu ở đây là mất điểm nặng.

---

## 0. Sự thật quan trọng nhất: File này port cái gì, KHÔNG port cái gì?

Bản C++ đoạt giải (Team No Man's Sky, LoRR 2024) có **hai tầng**:
- **Tầng 1 — Dẫn hướng luồng (flow-guidance):** dựng một "bản đồ giao thông" toàn cục, rồi chạy A\* có xét mật độ giao thông để các agent **tự giãn ra nhiều tuyến khác nhau** thay vì chen chúc một hành lang.
- **Tầng 2 — `causalPIBT`:** giải quyết va chạm **từng bước thời gian rời rạc**, bằng cơ chế kế thừa ưu tiên (priority inheritance) + đệ quy quay lui (backtracking) — chính là cái tên "PIBT" gốc.

**`PIBTPlanner.cs` chỉ port TẦNG 1 (flow-guidance).** Nó **không** port Tầng 2 `causalPIBT`.

**Vậy va chạm được xử lý ở đâu?** Trong game Unity, va chạm được thay bằng cơ chế **"chờ và tính lại đường ở mức agent"** nằm trong `GridEnemyAgentPIBT.cs` (`IsCellOccupiedByFriendly` + hệ thống cứu hộ kẹt), **chứ không** trong file này.

**Vì sao thay thế chứ không port nguyên?** Vì `causalPIBT` giả định thế giới chạy theo **bước thời gian rời rạc** (mọi agent nhích một ô cùng lúc). Unity chạy **vật lý thời gian thực liên tục** — xe tăng có gia tốc, có hướng thân, không "nhảy ô". Nên mô hình va chạm theo timestep của causalPIBT không áp thẳng được; ta thay bằng giải pháp mức-agent phù hợp với real-time.

> **Câu chốt để nói khi phản biện (thuộc lòng):**
> *"Em port **lõi dẫn hướng luồng** của bản đoạt giải — thành phần tạo nên khác biệt chính so với A\*. Riêng tầng giải va chạm theo bước-thời-gian rời rạc, em **thích nghi lại** thành cơ chế chờ-và-replan ở mức agent, vì Unity chạy vật lý thời gian thực chứ không phải MAPF theo timestep."*
> **Tuyệt đối không nói "em port 100% PIBT".** Nói "port lõi guidance + thích nghi tầng va chạm" — đó là sự thật và vẫn là đóng góp có giá trị.

Chi tiết đối chiếu C++ ↔ C# đầy đủ nằm ở `adds/QA/03_Giai_thich_code_va_migration_PIBT.md`.

---

## 1. PIBTPlanner: Đầu vào, Đầu ra và Quá trình xử lý (Workflow)

**Bản chất:** một `static class` — **singleton dùng chung cho MỌI agent PIBT**. Khác hẳn `GridAStarPathfinder` (mỗi agent tự tìm đường độc lập, không biết nhau), `PIBTPlanner` giữ **một bản đồ giao thông chung** (`_flow`) mà tất cả agent cùng đọc và cùng ghi. Đây chính là chỗ "phối hợp" xảy ra.

**Đầu vào (Input):**
- `MapLoader ml`: để biết ô nào đi được, kích thước lưới.
- Với mỗi agent: `id`, ô `start`, ô `goal`, và `budgetMs` (ngân sách thời gian tính toán).

**Đầu ra (Output):**
- Với mỗi agent: một **trajectory** (danh sách ô từ start → goal) lưu trong `_trajs[id]`, lấy ra qua `GetTraj(id)`.
- Cập nhật ngầm lên bản đồ luồng `_flow` chung.

**Luồng xử lý (Workflow) mỗi lần điều phối:**
1. Agent tự đăng ký một `id` (`Register`) và báo vị trí hiện tại (`SetCurrentPos`).
2. Gọi `FrankWolfe(id, start, goal, budget)`:
   - **Xóa** dấu vết luồng cũ của agent này khỏi `_flow`.
   - Chạy **A\* có nhận biết luồng** (`AStarFlow`) → ra tuyến mới.
   - **Cộng** dấu vết tuyến mới vào `_flow`.
   - Còn ngân sách thời gian → lặp lại cho các agent khác (round-robin) để cả đàn cùng hội tụ.
3. Agent đọc trajectory của mình rồi lái xe theo (ở `GridEnemyAgentPIBT`).

---

## 2. "Bản đồ giao thông" `_flow` — ý tưởng cốt lõi

Đây là thứ phân biệt PIBT với A\* thường. `_flow` là một mảng đếm:
```
_flow[cell*4 + d] = số tuyến đường đang đi qua cạnh từ "cell" theo hướng d
```
với `d`: 0 = đông (+x), 1 = nam (+y), 2 = tây (−x), 3 = bắc (−y).

Hình dung như **đếm xe trên từng làn đường**: mỗi khi một agent chọn đi qua cạnh nào, cạnh đó +1 (`AddFlow`); khi agent đổi tuyến, cạnh cũ −1 (`RemoveFlow`). Nhờ vậy tại bất kỳ lúc nào, hệ thống biết **chỗ nào đang đông, chỗ nào vắng**.

- `AddFlow` (dòng 189) / `RemoveFlow` (dòng 179): cộng/trừ đếm dọc theo một tuyến.
- `GetDir` (dòng 200): từ 2 ô liền kề suy ra hướng `d` (khớp `get_d` bên C++).

> **Tại sao đây là "phối hợp"?** Vì khi agent B tính đường, nó **nhìn thấy** vết của agent A trên `_flow` và bị "phạt" nếu định đi trùng làn ngược chiều A. Kết quả: các agent **tự động giãn ra** nhiều tuyến, giảm kẹt cứng ở hành lang hẹp — điều mà nhiều A\* độc lập không làm được.

---

## 3. A\* nhận biết luồng: `AStarFlow` (dòng 225)

Cùng là A\*, nhưng công thức ưu tiên khác baseline. Nhắc lại: A\* thường xếp hạng ô bằng `f = g + h`. PIBT **thêm 2 số hạng phạt giao thông**:
```
priority = g + h  +  op_flow  +  vertex_flow
```

### `op_flow` — phạt đi NGƯỢC CHIỀU dòng (dòng 260)
```
op_flow của một cạnh = (flow[curr→d] + 1) × flow[next→hướng-ngược-lại]
```
Ý nghĩa: nếu ô kế tiếp đang có nhiều tuyến đi **ngược chiều** ta định đi, chi phí tăng vọt. → **Tránh đối đầu trực diện** (hai đàn xe đâm mặt nhau trong hành lang). Đây là nguồn kẹt tệ nhất trong MAPF, nên bị phạt nặng nhất (phép nhân).

### `vertex_flow` — phạt đi qua ô ĐÔNG (dòng 263-265)
```
vertex_flow tại ô "next" = tổng flow mọi hướng của ô đó / 2
```
Ý nghĩa: ô nào đang có nhiều tuyến chạy qua (nút cổ chai) thì đắt hơn. → Khuyến khích **trải đều tải**, né các nút thắt.

> **Tại sao dùng phép nhân cho op_flow nhưng phép cộng/chia cho vertex_flow?** Vì đối đầu ngược chiều (op) nguy hiểm theo cấp số nhân — càng đông càng dễ khóa cứng deadlock; còn đi qua chỗ đông (vertex) chỉ chậm tuyến tính. Trọng số phản ánh đúng mức độ nghiêm trọng. (Đây là công thức nguyên bản từ `search.cpp` của bản C++, không phải em tự chế.)

Phần còn lại của `AStarFlow` là A\* chuẩn: min-heap (mục 5), lazy-deletion bỏ qua node cũ (dòng 243-245), truy vết ngược `ReconstructPath` (dòng 281).

---

## 4. Frank-Wolfe: Vì sao phải tính đi tính lại nhiều lần? (dòng 136)

Có một vấn đề "con gà–quả trứng": agent A chọn đường **dựa trên** luồng hiện tại, nhưng chính A lại **làm thay đổi** luồng đó, khiến đường agent B vừa chọn có thể không còn tối ưu. Giải pháp là **lặp cho tới khi ổn định** — đây là ý tưởng thuật toán Frank-Wolfe (mượn từ bài toán cân bằng giao thông):

1. Xóa vết agent hiện tại → tính lại đường nó với luồng mới nhất → thêm vết lại (`ReplanOne`, dòng 160).
2. Dùng **thời gian còn thừa** (`budgetMs`) tính lại lần lượt các agent khác theo vòng tròn (round-robin, dòng 147-155).
3. Mỗi vòng, hệ thống tiến gần hơn tới trạng thái cân bằng: **không agent nào còn muốn đổi tuyến** nếu các agent khác giữ nguyên.

> **Tại sao giới hạn bằng `budgetMs` chứ không chạy tới hội tụ tuyệt đối?** Vì đây là game thời gian thực — không thể để một frame treo chờ tối ưu hoàn hảo. Ta lấy "đủ tốt trong ngân sách thời gian" — đúng tinh thần đánh đổi của PIBT: **hi sinh tối ưu toàn cục để lấy tốc độ thời gian thực**. Đây cũng là câu trả lời cho "sao không dùng CBS": CBS tối ưu nhưng phức tạp hàm mũ, không kịp real-time.

---

## 5. Các thành phần hỗ trợ

### Heuristic bằng reverse BFS: `GetOrBuildH` (dòng 292)
Thay vì đoán khoảng cách bằng Manhattan (như A\* baseline), PIBT tính **khoảng cách thật** từ mọi ô về đích bằng một lượt **BFS loang ngược từ goal**. Kết quả cache lại theo từng goal (`_h`), nên nhiều agent cùng đích chỉ tính một lần.

> **Tại sao dùng khoảng cách thật thay vì Manhattan?** Vì Manhattan bỏ qua tường — trên map nhiều vật cản nó đánh giá sai lệch nhiều, khiến A\* mò lâu. BFS ngược cho heuristic **chính xác tuyệt đối** (đúng số bước né tường), A\* chạy nhanh hơn hẳn. Đổi lại tốn một lượt BFS toàn map, nhưng đã cache nên chấp nhận được.

### `BuildNbrs` (dòng 67): bảng hàng xóm dựng sẵn
Tính trước danh sách ô đi được liền kề cho **mọi ô**, lưu dạng flat-index. → Trong vòng lặp A\* nóng, chỉ việc đọc mảng thay vì hỏi `IsWalkable` 4 lần mỗi ô. Tối ưu tốc độ.

### `MinHeap` (dòng 321): hàng đợi ưu tiên nhị phân
Giống heap trong A\* baseline: giữ ô có `priority` nhỏ nhất ở gốc, push/pop O(log n). Lưu song song 2 mảng `_pri` và `_id` cho gọn nhẹ.

### Toạ độ phẳng: `ToFlat` / `FromFlat` (dòng 105-107)
PIBT dùng **chỉ số phẳng** (một số `int` = `y*cols + x`) thay vì `Vector2Int`, để đánh index mảng `_flow`, `_h`, `_nbrs` cực nhanh và khớp với cách bản C++ đánh số ô.

### `IsAgentWalkable` (dòng 109): phình vật cản cho bề rộng xe
Giống ý tưởng `GridNavMask`: một ô chỉ hợp lệ nếu **cả vùng bán kính `_obstacleInflateRadius` quanh nó** đều đi được — chừa khoảng trống cho thân xe tăng.

---

## 6. Vòng đời agent & quản lý trạng thái

- `Register` (dòng 89): cấp một `id` mới cho agent.
- `SetCurrentPos` (dòng 98): agent báo vị trí hiện tại (Frank-Wolfe cần để replan các agent khác).
- `Unregister` (dòng 91): agent chết → **xóa vết luồng** khỏi `_flow` và dọn trạng thái. Quan trọng: nếu quên xóa, "bóng ma" của xe đã chết vẫn ám lên bản đồ giao thông khiến xe sống né vô cớ.
- `Init` (dòng 48): dựng lại toàn bộ khi đổi map/đổi bán kính phình. Có kiểm tra "nếu đã sẵn sàng đúng cấu hình thì bỏ qua" để không dựng lại thừa.

---

## 7. Câu hỏi phản biện có thể gặp

**"PIBT của em có phải là PIBT gốc trong paper không?"**
→ Thành thật: em port **tầng dẫn hướng luồng (flow-guidance)** của bản C++ đoạt giải — đây là thành phần tạo khác biệt chính so với A\*. Tầng giải va chạm theo bước-thời-gian rời rạc (`causalPIBT`) thì em **thích nghi lại** thành cơ chế chờ-và-replan ở mức agent trong `GridEnemyAgentPIBT`, vì Unity chạy vật lý thời gian thực chứ không phải MAPF theo timestep. (Xem mục 0.)

**"PIBT khác A\* baseline ở đúng chỗ nào trong code?"**
→ Đúng 2 số hạng cộng thêm vào priority: `op_flow` (phạt đi ngược chiều dòng, dòng 260) và `vertex_flow` (phạt đi qua ô đông, dòng 263-265), cùng một **bản đồ luồng `_flow` chung** giữa mọi agent. A\* baseline chỉ có `f = g + h`, mỗi agent mù thông tin về nhau.

**"Frank-Wolfe để làm gì?"**
→ Để phá thế con-gà-quả-trứng: đường mỗi agent phụ thuộc luồng, mà luồng lại do các agent tạo ra. Lặp round-robin trong ngân sách thời gian cho tới khi hệ gần cân bằng — không ai muốn đổi tuyến nữa.

**"Sao không dùng CBS cho tối ưu toàn cục?"**
→ CBS tối ưu nhưng độ phức tạp tăng hàm mũ theo số agent, không kịp thời gian thực. PIBT đánh đổi: chấp nhận "đủ tốt mỗi bước" để chạy real-time với hàng chục agent — đúng bài toán của game.

**"Vì sao dùng BFS ngược làm heuristic thay vì Manhattan?"**
→ Manhattan bỏ qua tường nên sai lệch trên map nhiều vật cản. BFS ngược từ goal cho khoảng cách thật (đúng số bước né tường), A\* hội tụ nhanh hơn. Đã cache theo goal nên chi phí một lần.

**"`static` dùng chung, nhiều agent gọi có loạn không?"**
→ Không — Unity đơn luồng, các agent gọi nối tiếp trong cùng frame. Và việc dùng chung `_flow` chính là **cố ý** (đó là cơ chế phối hợp), khác với cache tạm của A\* baseline.

**"Nếu một agent chết giữa chừng thì sao?"**
→ `Unregister` xóa vết luồng của nó khỏi `_flow`. Nếu không, các agent còn sống sẽ né một "bóng ma" không còn tồn tại.
