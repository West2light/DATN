# Giải thích code chi tiết (có trích dẫn) + Trả lời: PIBT migrate từ C++ về C# thế nào

> Tài liệu này đi theo đúng **sườn đọc code** ở `02_Plan_on_tap_phan_bien.md`, trích dẫn thẳng dòng code. Nửa sau (PHẦN B) trả lời câu hỏi lớn nhất của bạn: *tại sao từ C++ mà vẫn giữ được logic cốt lõi khi đưa PIBT về C#*.
>
> Quy ước trích dẫn: `File.cs:dòng` cho code Unity (bấm vào là nhảy tới). Code C++ gốc nằm ngoài repo, trích theo `file.cpp:dòng` trong thư mục `Team_No_Man_s_Sky/.../default_planner/`.

---

# PHẦN A — Đọc code theo sườn (có trích dẫn)

## A1. `TankController.cs` — cửa ngõ điều khiển (nền của mọi thứ)

Cả **người chơi lẫn AI** đều điều khiển xe qua đúng 3 hàm này, không có đường tắt nào khác:

```csharp
// TankController.cs:23-45
public void HandleShoot()            { foreach (var turret in turrets) turret.Shoot(); }
public void HandleMoveBody(Vector2 movementVector)   { tankMover.Move(movementVector); }
public void HandleTurretMovement(Vector2 pointerPosition) { aimTurret.Aim(pointerPosition); }
```

**Ý nghĩa để bảo vệ:** đây chính là lý do so sánh 3 thuật toán *công bằng*. A*, PIBT C#, PIBT TCP đều **không tự lái xe** — chúng chỉ tính ra ô kế tiếp, rồi gọi `HandleMoveBody` giống hệt người chơi. Khác biệt quan sát được là **do thuật toán**, không phải do tầng vật lý khác nhau (đúng như "nguyên tắc công bằng" ở Chương 1 luận văn). `TankController.cs:11-20` (Awake) chỉ tự resolve `tankMover`/`aimTurret`/`turrets` nếu chưa gán.

## A2. `MapLoader.cs` — biến file `.map` thành lưới + toạ độ

Mọi spawner và pathfinder **bắt buộc** đi qua 4 hàm public này (đừng tự tính toạ độ ở nơi khác):

- `IsWalkable(cell)` — ô đi được không (`.` = được, `@` = tường).
- `CellToWorld(cell)` / `WorldToCell(pos)` — đổi giữa ô lưới ↔ toạ độ world. Gốc **trên-trái, Y tăng xuống dưới** (khớp thứ tự dòng trong file `.map`).
- `TryFindWalkableNear(cell, out result)` — nếu spawn/goal rơi vào tường, tìm ô đi được gần nhất.

**Ý nghĩa:** đây là "ngôn ngữ chung". A* nói chuyện bằng `Vector2Int` (ô lưới); PIBT nói chuyện bằng `flat = y*cols + x` (số nguyên) — nhưng cả hai đều quy về cùng lưới từ `MapLoader`. Xem `GridPIBTPathfinder.cs:39-40` chỗ đổi ô → flat: `PIBTPlanner.ToFlat(start)`.

## A3. `GridAStarPathfinder.cs` — baseline A* (để làm mốc so sánh)

Trái tim A* là vòng lặp mở nút theo priority `f = g + h`:

```csharp
// GridAStarPathfinder.cs:116-122
int tentativeG = s_gScore[current] + 1 + stepCost;      // g = số bước
if (!s_gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
{
    s_cameFrom[neighbor] = current;
    s_gScore[neighbor] = tentativeG;
    HeapPush(neighbor, tentativeG + Heuristic(neighbor, goal)); // f = g + h(Manhattan)
}
```

Đặc điểm cốt lõi để nhớ: **A* mỗi agent tự tính đường, KHÔNG biết agent khác tồn tại.** Không có ô nào bị "phạt" vì đông. Đó là lý do khi nhiều xe cùng chạy, chúng tranh nhau 1 ô hẹp → kẹt/đâm. `Heuristic` là Manhattan, 4 hướng (khớp CLAUDE.md: "A* hiện là 4-neighbor + Manhattan").

**Ghi nhớ con số vàng cho PHẦN B:** priority của A* baseline =
```
f = g + h
```
Hãy giữ công thức này trong đầu. PIBT chỉ thêm **2 số hạng** vào đây.

---

# PHẦN B — Câu trả lời: PIBT migrate từ C++ → C# thế nào mà "vẫn là PIBT"?

Đây là phần quan trọng nhất. Mình chia làm 4 bước: (1) hiểu đúng kiến trúc gốc C++, (2) C# port đúng phần nào, (3) trích dẫn song song chứng minh giống hệt, (4) cái gì phải sửa và cách nói với thầy.

## B1. Kiến trúc gốc của Team No Man's Sky có HAI tầng, không phải một

Đọc `planner.cpp:plan()` (hàm chạy mỗi timestep) sẽ thấy rõ 2 tầng nối tiếp:

**Tầng 1 — Guidance / Traffic flow (lập kế hoạch đường tránh tắc nghẽn):**
```cpp
// planner.cpp:141-151
if (require_guide_path[i]) { remove_traj(trajLNS, i); update_traj(trajLNS, i); }
...
frank_wolfe(trajLNS, updated, end_time);   // tối ưu lại đường cho các agent bằng time budget
```
Tầng này dùng **flow-aware A*** (`search.cpp:astar`) + **Frank-Wolfe** (`flow.cpp:frank_wolfe`): mỗi cạnh lưới có một "mức lưu lượng" (flow); A* bị phạt khi đi ngược dòng hoặc qua ô đông, nên các agent **tự động tản ra nhiều tuyến** thay vì chen một tuyến.

**Tầng 2 — causalPIBT (chọn hành động không va chạm cho từng bước):**
```cpp
// planner.cpp:161-171
for (int i : ids) {                       // duyệt theo thứ tự ưu tiên p[]
    if (next_states[i].location == -1)
        causalPIBT(i, -1, prev_states, next_states, prev_decision, decision, occupied, trajLNS);
}
```
`causalPIBT` (`pibt.cpp:25-112`) mới là **PIBT "kinh điển"**: agent ưu tiên cao chọn ô trước; nếu ô đó đang bị agent thấp hơn giữ, nó **đệ quy ép agent thấp nhường** (priority inheritance), nhường không được thì **backtrack** thử ô khác (`pibt.cpp:96` gọi lại chính nó cho `lower_id`).

> **Chốt quan trọng:** trong bản gốc, "PIBT" là cả một hệ: **Tầng 1 định hướng (đi đâu cho đỡ tắc), Tầng 2 phân xử va chạm tức thời (bước này ai đi ô nào)**. Heuristic của Tầng 2 (`get_gp_h`, `pibt.cpp:12-23`) lấy chính từ đường guidance của Tầng 1 — hai tầng khớp nhau.

## B2. `PIBTPlanner.cs` port **Tầng 1** (guidance flow) — và đây là phần "cốt lõi" bạn hỏi

File `PIBTPlanner.cs` tự khai báo rõ ở đầu (`PIBTPlanner.cs:8-17`): *"Port từ C++ default_planner: flow grid, flow-aware A*, Frank-Wolfe, heuristic reverse BFS."* Nghĩa là nó bê nguyên **Tầng 1**. Lý do Tầng 1 là "logic cốt lõi": nó chính là cái làm cho nhiều agent phối hợp — bỏ nó đi thì PIBT tụt về A* thường.

Còn **Tầng 2 (causalPIBT)** thì C# **không port đệ quy y hệt**, mà thay bằng phối hợp ở tầng agent thời gian thực (`GridEnemyAgentPIBT.cs`): xe đi theo đường guidance, tới ô kế nếu bị đồng đội chiếm thì **chờ** rồi replan (`GridEnemyAgentPIBT.cs:438` `IsCellOccupiedByFriendly`, và cơ chế recovery). Đây là điểm **phải nói trung thực** — xem B4.

## B3. Trích dẫn SONG SONG — chứng minh Tầng 1 giống hệt từng dòng

Đây là "bằng chứng" để trả lời "làm sao giữ được logic cốt lõi": vì thuật toán Tầng 1 là **số học số nguyên trên lưới**, không dính gì đặc thù C++, nên nó dịch 1–1 sang C#. So từng mảnh:

### (a) Mã hoá hướng `get_d` — giống tuyệt đối
```cpp
// utils.cpp:7-11  (C++)
int get_d(int diff) { return (diff==1)?0 : (diff==-1)?2 : (diff==cols)?1 : 3; }
//  0=east(+1), 2=west(-1), 1=south(+cols), 3=north(-cols)
```
```csharp
// PIBTPlanner.cs:200-208  (C#)
private static int GetDir(int from, int to) {
    int diff = to - from;
    if (diff == 1)      return 0;   // east
    if (diff == _cols)  return 1;   // south
    if (diff == -1)     return 2;   // west
    if (diff == -_cols) return 3;   // north
    return -1;
}
```
→ **Cùng quy ước 0/1/2/3.** Đây là nền để `flow[cell*4 + d]` (C#) khớp `flow[loc].d[d]` (C++).

### (b) Flow-aware A* — công thức phạt op_flow / vertex_flow giống hệt
```cpp
// search.cpp:97   op_flow (phạt đi ngược dòng)
temp_op = ( (flow[curr->id].d[d]+1) * flow[next].d[(d+2)%4] );
// search.cpp:101-108   vertex_flow (phạt qua ô đông)
temp_vertex = 1; for (j=0;j<4;j++) temp_vertex += flow[next].d[j];
all_vertex_flow += (temp_vertex-1)/2;
```
```csharp
// PIBTPlanner.cs:260   op_flow
int opEdge = d >= 0 ? (_flow[curr*4 + d] + 1) * _flow[next*4 + (d + 2) % 4] : 0;
// PIBTPlanner.cs:263-265   vertex_flow
int vsum = 1; for (int di = 0; di < 4; di++) vsum += _flow[next*4 + di];
int vEdge = (vsum - 1) / 2;
```
→ **Từng phép nhân, từng `(d+2)%4`, từng `(...-1)/2` khớp chính xác.**

### (c) Hàm priority — giống hệt
```cpp
// search_node.h:106  re_of: op_flow + all_vertex_flow + get_f()   với get_f()=g+h
return lhs.get_op_flow() + lhs.get_all_vertex_flow() + lhs.get_f() < rhs...;
```
```csharp
// PIBTPlanner.cs:222
public int Pri(int hVal) => G + hVal + OpFlow + VertexFlow;
```
→ Đây chính là chỗ mấu chốt so với A* ở PHẦN A3:
```
A* baseline :  f = g + h
PIBT flow-A*:  f = g + h + op_flow + vertex_flow
                          └──────────┬──────────┘
                    2 số hạng "traffic": chia sẻ qua lưới _flow chung
```
**Toàn bộ khác biệt thuật toán nằm gọn ở 2 số hạng này + một lưới flow dùng chung.** Đó là câu trả lời một dòng khi thầy hỏi "PIBT khác A* chỗ nào trong code".

### (d) Frank-Wolfe — cùng cấu trúc round-robin theo time budget
```cpp
// flow.cpp:117-127
while (steady_clock::now() < timelimit) {
    a = replan_order[count % num_agents].id; count++;
    remove_traj(lns,a); update_traj(lns,a);   // xoá flow cũ → A* lại → thêm flow mới
}
```
```csharp
// PIBTPlanner.cs:147-155
while ((Time.realtimeSinceStartup - t0)*1000f < budgetMs && count < maxCount) {
    int other = ids[count % ids.Count]; count++;
    ReplanOne(other, pos, g);                 // ReplanOne = remove flow → AStarFlow → add flow
}
```
`ReplanOne` (`PIBTPlanner.cs:160-175`) = đúng bộ ba `RemoveFlow → AStarFlow → AddFlow`, tương ứng `remove_traj → astar → add_traj` của C++. → **Cùng ý tưởng Frank-Wolfe:** lặp lại "gỡ một agent ra, tính lại đường với flow hiện tại, cắm vào" cho tới hết ngân sách thời gian, để hệ hội tụ về phân bố lưu lượng cân bằng.

### (e) Heuristic — reverse BFS từ goal, cache theo goal
```csharp
// PIBTPlanner.cs:292-317  GetOrBuildH: BFS ngược từ goal ra toàn map, cache _h[goal]
```
Khớp `heuristics.cpp` bên C++ (`init_heuristic` — BFS khoảng cách thật tới goal, cache theo goal). Điểm khác nhỏ so với A* baseline: A* dùng **Manhattan** (ước lượng), PIBT dùng **khoảng cách thật trên lưới** (chính xác hơn, cần vì có vật cản). Nói được ý này là một điểm cộng.

## B4. Cái gì PHẢI sửa khi đưa về C# (và cách trả lời trung thực)

Không thể bê 100% vì môi trường khác nhau. Ba khác biệt, kèm cách giải thích:

| Bản gốc C++ (benchmark) | Bản C# (Unity) | Lý do phải đổi |
|---|---|---|
| `MemoryPool` cấp phát `s_node` thủ công (`Memory.h`) | `Dictionary<int,ANode>` + `MinHeap` tự viết (`PIBTPlanner.cs:321-376`) | C# có GC; không quản bộ nhớ tay. Thuật toán không đổi, chỉ đổi *chỗ chứa node*. |
| Chạy theo **timestep rời rạc** (mỗi bước mọi agent nhảy 1 ô đồng thời), có model hướng MAPF-T | Chạy theo **vật lý thời gian thực** của Unity; xe có thân, phải xoay rồi tiến | Game không "đóng băng thời gian" từng bước; phải chuyển đường lưới thành chuyển động mượt (chính là cải tiến EPIBT, Mục 5.5 luận văn). |
| **Tầng 2 `causalPIBT`** phân xử va chạm tức thời (đệ quy priority-inheritance + backtracking) | Thay bằng: đi theo guidance flow + agent **chờ/replan** khi ô kế bị đồng đội chiếm (`GridEnemyAgentPIBT.cs:438`, recovery) | Trong thời gian thực, hai xe không bao giờ "cùng một tick"; va chạm được xử lý ở tầng agent + vật lý, nên không cần đệ quy causalPIBT nguyên bản. |

> **Cách nói với thầy (trung thực + đúng):**
> *"Bản gốc của Team No Man's Sky có hai tầng: một tầng định hướng chống tắc nghẽn bằng flow-aware A* + Frank-Wolfe, và một tầng phân xử va chạm theo bước gọi là causalPIBT. Em port nguyên vẹn tầng định hướng — đó là phần cốt lõi tạo ra hành vi phối hợp — với công thức phạt lưu lượng op_flow/vertex_flow và vòng lặp Frank-Wolfe giống hệt (em có thể chỉ từng dòng tương ứng). Còn tầng phân xử va chạm theo timestep rời rạc thì em thay bằng cơ chế chờ-và-replan ở tầng agent, vì trong Unity các xe chạy theo vật lý thời gian thực chứ không theo bước đồng bộ. Nhờ hai tầng gốc dùng số học số nguyên trên lưới, không phụ thuộc đặc thù C++, nên logic cốt lõi dịch sang C# gần như 1–1."*

Câu này vừa cho thấy bạn **hiểu sâu bản gốc**, vừa **thành thật về phần đã đơn giản hoá** — thầy đánh giá cao hơn nhiều so với khẳng định "port 100%".

## B5. Vì sao "logic cốt lõi được bảo toàn" — trả lời gọn 3 gạch (để tự nhắc, không đọc cho thầy)

1. **Bản chất thuật toán là bất biến toán học trên lưới số nguyên:** flow grid, phạt op_flow/vertex_flow, mã hoá hướng `get_d`, hàm priority `g+h+op_flow+vertex_flow`, vòng Frank-Wolfe — không có gì phụ thuộc ngôn ngữ. Đổi ngôn ngữ không đổi kết quả các phép tính này.
2. **Chỉ phần "giàn giáo runtime" phải viết lại:** cấp phát bộ nhớ (pool→Dict/heap) và mô hình thời gian (timestep→physics). Đây là kỹ thuật, không phải thuật toán.
3. **Kiểm chứng được:** có thể đặt hai file cạnh nhau và chỉ ra từng dòng khớp (đã làm ở B3) — đó là bằng chứng "giữ được logic cốt lõi", không phải nói suông.

---

## Phụ lục — Bản đồ file để tra nhanh khi thầy hỏi

| Chủ đề thầy có thể hỏi | Mở file/dòng nào |
|---|---|
| "Điều khiển xe chung cho người và AI?" | `TankController.cs:23-45` |
| "A* tính priority thế nào?" | `GridAStarPathfinder.cs:116-122` |
| "PIBT khác A* ở đâu trong code?" | `PIBTPlanner.cs:222` và `:260-265` (2 số hạng flow) |
| "Frank-Wolfe là gì?" | `PIBTPlanner.cs:136-156` ↔ C++ `flow.cpp:94-130` |
| "Chứng minh port đúng từ C++?" | Bảng B3 (a)-(e), so `search.cpp:97` ↔ `PIBTPlanner.cs:260` |
| "Xử lý va chạm giữa các xe?" | `GridEnemyAgentPIBT.cs:438` + recovery; C++ `pibt.cpp:causalPIBT` |
| "Nối sang server C++ (mode TCP)?" | `PIBTTcpClient.cs`, `GridEnemyAgentPIBT_TCP.cs` |
