# PLAN: PIBT TCP chỉ chạy được 1/5 map — ROOT CAUSE XÁC ĐỊNH = `static delta` cache width map đầu tiên (server C++ giữ state qua các trận)

Ngày lập: 2026-06-23 · Cập nhật: 2026-06-23 (đã đọc source server C++ trong WSL, xác nhận root-cause).
Phạm vi: Mode `PIBT_TCP`. Server C++ `~/projectY/Server-PIBT-TeamNoMan-sSky` (chạy local
`./build/pibt_tcp_server --host 0.0.0.0 --port 7777`), client Unity trỏ `127.0.0.1:7777`.
Áp dụng Single Play + Backtest.

> **Insight của user (ĐÃ ĐƯỢC XÁC NHẬN):** "Server còn lưu cache từ trận trước, chơi xong round chưa
> clear sạch state nên chỉ 1 trong 5 map chạy được." → ĐÚNG. Có một biến **`static` trong server**
> bám theo **vòng đời process**, không một bước reset session nào xoá được. Nó cache **width của map
> được plan ĐẦU TIÊN** kể từ lúc khởi động server và dùng lại cho **mọi map sau** ⇒ chỉ map đầu tiên
> chạy đúng.

---

## 0. TL;DR — kết luận (đã verify bằng source + số học log)

**Bug:** `UnityStartKitAdapter.cpp:123` trong hàm `NextLoc(...)`:
```cpp
int NextLoc(const State& s, Action a, int cols)
{
    static const int delta[4] = {1, cols, -1, -cols};   // ← BUG: 'static'
    ...
    return s.location + delta[s.orientation];
}
```
`delta` là **`static` local** ⇒ C++ chỉ khởi tạo **một lần duy nhất, ở lần gọi đầu tiên**, "đóng băng"
giá trị `cols` của map đầu tiên cho **toàn bộ vòng đời process**. Các lần gọi sau, dù truyền `cols`
mới (đúng), `delta[1]`/`delta[3]` vẫn = `±cols_map_đầu_tiên`.

**Hệ quả:**
- `delta[1]` (south) và `delta[3]` (north) = bước dọc = đáng lẽ phải `±env.cols` của map hiện tại,
  nhưng lại kẹt ở `±width_map_đầu`.
- Ở map thứ 2+ (khác width), `FW` dọc nhảy sai số ô ⇒ bộ `SanitizeAction` (dùng `env.cols` ĐÚNG của
  map hiện tại) thấy bước "nhảy hàng" ⇒ trả `reason=fw_row_wrap`, ép action thành `W` ⇒ **toàn bộ
  agent đứng yên**. Đây chính là log user gửi.

**Vì sao "chỉ 1 map chạy":** map **được plan đầu tiên** sau khi khởi động server có `width ==` giá trị
baked ⇒ chạy đúng. Mọi map sau có `width` khác ⇒ vỡ. (Khái niệm "map vuông/chữ nhật" ở bản plan trước
chỉ là **trùng hợp**: map chạy được tình cờ là map đầu — `random-32-32-10`. Biến quyết định thật là
**"width == width của map plan đầu tiên"**, không phải vuông hay chữ nhật.)

**Fix (1 dòng):** bỏ `static`:
```cpp
const int delta[4] = {1, cols, -1, -cols};   // tính lại theo cols mỗi lần gọi
```

**Phía Unity:** không sai gì (đã verify nhất quán); chỉ nên thêm **hardening** để bug kiểu này lộ ra
ngay thay vì "đứng im không lý do" (Mục 4).

---

## 1. Bằng chứng số học (khớp 100%)

Log user gửi là phiên đang chạy map **`lt_gallowstemplar_n`** (180×251 ⇒ `env.cols = 251`,
45.180 ô), nhưng `delta` đã bị baked = **162** (= width của `ht_chantry`, map plan **trước đó** trong
cùng process). Tính lại `nextLoc = loc + delta[ori]` rồi kiểm `IsAdjacentNoWrap(loc, next, cols=251)`:

| agent | loc | ori | nextLoc dự đoán (loc±162) | nextLoc trong log | khớp | Manhattan trên cols=251 | kết quả |
| ---: | ---: | ---: | ---: | ---: | :--: | ---: | --- |
| 0 | 19296 | 1 | 19458 | 19458 | ✅ | 90 | fw_row_wrap |
| 1 | 31394 | 3 | 31232 | 31232 | ✅ | 90 | fw_row_wrap |
| 2 | 36111 | 3 | 35949 | 35949 | ✅ | 162 | fw_row_wrap |
| 3 | 9937 | 1 | 10099 | 10099 | ✅ | 90 | fw_row_wrap |
| 4 | 19846 | 1 | 20008 | 20008 | ✅ | 162 | fw_row_wrap |
| 5 | 26093 | 1 | 26255 | 26255 | ✅ | 90 | fw_row_wrap |

- Mọi `nextLoc` dự đoán bằng **baked delta 162** trùng khít log ⇒ xác nhận `delta` đang dùng width của
  map TRƯỚC (chantry 162), không phải map hiện tại (gallows 251).
- `loc=36111` hợp lệ trên gallows (45.180 ô) nhưng vô lý trên chantry (22.842 ô) ⇒ map hiện tại đúng
  là gallows; `ApplyAgents` không throw vì loc nằm trong biên gallows ⇒ planner chạy, nhưng `FW` bị
  sanitize sạch ⇒ đứng yên.
- Kích thước 5 map (header `.map`): `random-32-32-10` 32×32 · `maze-128-128-10` 128×128 ·
  `ht_mansion_n` 270×133 · `ht_chantry` 141×162 · `lt_gallowstemplar_n` 180×251. **5 width khác nhau**
  ⇒ trong backtest (chạy tuần tự cùng 1 process) chỉ map đầu (random-32, width 32) có cột PIBT_TCP
  ra số; 4 map sau hỏng. Khớp đúng "1/5 map".

---

## 2. NGHIÊN CỨU SERVER C++ (`~/projectY/Server-PIBT-TeamNoMan-sSky`)

### 2.1 Bản đồ file & luồng xử lý
- `src/tcp_server_main.cpp` → `PibtTcpServer::Run()` (`src/PibtTcpServer.cpp`): accept socket, mỗi client
  vào `HandleClient`.
- `src/PibtTcpServer.cpp` · `HandleClient` (vòng đọc JSON-lines):
  - `hello` → `ResetSessionState(session)` → `make_unique<PlannerSession>(sid)` → `session->Initialize(request)`
    → trả `hello_ack` (`BuildHelloAck`, **không** echo width/height — xem F-U1).
  - `plan_step` → `session->Plan(request, 90ms)` → trả `plan_result`.
  - `shutdown` hoặc EOF/lỗi socket → `ResetSessionState(session)`.
  - `ResetSessionState` (dòng 87–92) = `session.reset(); DefaultPlanner::reset(); EpibtPlanner::reset();`
- `src/PlannerSession.cpp`:
  - `Initialize` → `UnityAdapter::ApplyMap(env_, hello["map"])` (set `env_.cols/rows/map`) + `DefaultPlanner::reset()/initialize()`.
  - `Plan` → `UnityAdapter::ApplyAgents(env_, ...)` → `DefaultPlanner::plan(...)` → vòng build action,
    gọi `SanitizeAction(env_, s, raw, &reason)` và `NextLoc(s, a, env_.cols)` (dòng 269 & 295).
- `src/UnityStartKitAdapter.cpp`: `ApplyMap` (build `env.map`), `ApplyAgents` (nạp `curr_states[i].location`
  = `loc` Unity gửi), `NextLoc` (**chứa bug**), `SanitizeAction` (`fw_out_of_bounds` / `fw_row_wrap` /
  `fw_blocked`), `IsAdjacentNoWrap`.

### 2.2 Phần đã ĐÚNG (không phải nguồn lỗi — đừng sửa nhầm)
- **Session lifecycle ĐÚNG:** mỗi `hello` reset sạch `PlannerSession` + `DefaultPlanner` + `EpibtPlanner`;
  `ApplyMap` rebuild `env.cols/rows/map` theo từng map; `shutdown`/EOF cũng reset. ⇒ state cấp-session
  được dọn đúng. (Nghĩa là client "clear" rồi, nhưng vẫn dính bug vì `static` nằm ngoài tầm reset.)
- **`ApplyMap` ĐÚNG:** `env.cols=width`, `env.rows=height`, kiểm `symbols.size()==width*height`,
  map row-major `symbols[i]`. Khớp contract Unity (`loc = y*width + x`).
- **`ApplyAgents` ĐÚNG:** lấy `loc/orientation/goalLoc` trực tiếp, validate `IsLocInMap`+`IsLocWalkable`,
  throw nếu sai (đó là vì sao map sai width đôi khi throw thay vì chạy).
- **`SanitizeAction` / `IsAdjacentNoWrap` ĐÚNG:** đều dùng `env.cols` của map hiện tại. Chính vì
  ĐÚNG nên nó phát hiện được bước dọc sai của `NextLoc` (báo `fw_row_wrap`).

### 2.3 Nguồn lỗi DUY NHẤT: `static` local trong `NextLoc`
- `UnityStartKitAdapter.cpp:120–137`. `static const int delta[4] = {1, cols, -1, -cols};`.
- `static` local = lifetime process, init một lần ở lần gọi đầu. `ResetSessionState` / `DefaultPlanner::reset()`
  **không** chạm tới được. Đây đúng là "cache từ trận trước" mà user mô tả.
- **Đã quét toàn bộ `src/` và `default_planner/`:** không còn `static` nào khác cache `cols/rows/width/height`.
  Đây là rò rỉ cross-session **duy nhất**. (Các `reset()` của planner đều xoá state node/memory theo session.)

### 2.4 FIX SERVER (đề xuất, tách commit riêng phía WSL)
- **F-S1 (bắt buộc, 1 dòng):** bỏ `static` ở `NextLoc`:
  ```cpp
  int NextLoc(const State& s, Action a, int cols)
  {
      const int delta[4] = {1, cols, -1, -cols};   // KHÔNG 'static' — tính theo cols mỗi lần gọi
      switch (a) {
          case Action::FW:
              if (s.orientation >= 0 && s.orientation < 4)
                  return s.location + delta[s.orientation];
              return s.location;
          default:
              return s.location;
      }
  }
  ```
- **F-S2 (nên có, phòng vệ):** trong `hello_ack` echo lại `width/height/cellCount` đã nhận để client
  assert (hỗ trợ F-U1). Sửa `BuildHelloAck` (PibtTcpServer.cpp:45) thêm `r["width"]=...`, `r["height"]=...`.
- **F-S3 (nên có, "ồn ào hoá"):** khi `ApplyAgents` throw (loc/goal invalid) → đang ném exception làm
  `Plan` rơi vào catch trả toàn `W` (PlannerSession.cpp:331–353). Thêm log rõ "agent loc out of map
  WxH" để phân biệt với fw_row_wrap.
- **Build lại:** `cd ~/projectY/Server-PIBT-TeamNoMan-sSky && ./compile.sh` (hoặc cmake trong `build/`),
  rồi chạy lại `./build/pibt_tcp_server --host 0.0.0.0 --port 7777`.

### 2.5 Dự đoán có thể kiểm chứng (để xác nhận fix)
- **Trước fix:** sau khi khởi động server, **map plan đầu tiên luôn chạy**, mọi map sau (khác width)
  `fw_row_wrap`. Đổi thứ tự map ⇒ đổi "map chạy được". Khởi động lại server ⇒ map đầu mới lại chạy.
- **Sau fix:** chạy 5 map liên tiếp trên **cùng 1 process** (không restart) đều đúng, không `fw_row_wrap`
  do sai stride.

---

## 3. Loại trừ phía Unity (đã verify — KHÔNG sửa toạ độ)
`MapLoader` đọc `height` rồi `width` đúng thứ tự, `grid[y][x]`, `IsWalkable` chặn `x<width,y<height`.
`MapScenarioBootstrapPIBT_TCP`: `BuildHelloRequest` gửi `width=BuildWidth, height=BuildHeight`;
`BuildMapSymbols` row-major stride=width; `AgentFlat/FlatToCell` dùng `cols=width`, clamp trong biên
(max chantry = 22.841, không bao giờ tạo loc vượt biên). `OrientationToDelta/ReadAgentOrientation`
nhất quán `0=east,1=south,2=west,3=north`. ⇒ Unity không phải nguồn lỗi; lời dặn V3/V4 "đừng sửa
toạ độ Unity" vẫn đúng.

---

## 4. Hardening phía UNITY (defense-in-depth, làm trong repo, 3 file TCP)
> Không bắt buộc để hết bug (fix server là đủ), nhưng cần để **không bao giờ "đứng im không lý do"** nữa
> và để bug hình học tương lai lộ ra tức thì. Không đụng A*/local PIBT.

- **F-U1 — Validate `hello_ack`** (`PIBTTcpClient.Hello`): nếu server (sau F-S2) echo `width/height`,
  assert khớp giá trị đã gửi; lệch → `LogError "server map dims mismatch"` + fail rõ ràng.
- **F-U2 — Guard `nextLoc` ồn ào** (`MapScenarioBootstrapPIBT_TCP.ApplyAction`): hiện đã có
  `IsInsideBuild`+`IsStepValid` rồi **âm thầm** fallback dead-reckon (≈505–521). Thêm counter
  `btInvalidNextLocCount` + `LastStallReason="server_geometry_invalid"` + cảnh báo (sau `enableTcpTrace`).
- **F-U3 — Phát hiện "cả đội đứng"**: nếu N tick liên tiếp mọi agent velocity≈0 và tỉ lệ invalid-nextLoc
  cao → log 1 dòng tổng kết "PIBT_TCP geometry stall on <map> WxH" để backtest ghi nhận thay vì timeout
  120s mù.
- **F-U4 (tuỳ chọn)** — gửi kèm `mapId`/checksum trong `hello` để server (và log) phát hiện đổi map.

---

## 5. Quy trình test & acceptance

### 5.1 Test quyết định (chứng minh root-cause trước/sau fix)
1. **Khởi động server sạch.** Single Play `lt_gallowstemplar_n` **đầu tiên** → phải chạy đúng (map đầu).
2. **Không restart server.** Single Play `ht_chantry` → trước fix sẽ `fw_row_wrap` (khác width 251→162).
3. **Sau khi áp F-S1, build lại, restart.** Lặp lại (1)+(2): cả hai map đều chạy, không `fw_row_wrap`.

### 5.2 Backtest end-to-end (đích cuối)
- Chạy PIBT_TCP đủ **5/5 map** trên **cùng 1 process server** (không restart giữa map): export
  `BacktestResults/` với cột PIBT_TCP khác 0 và hợp lý cho cả 5 map; so sánh được với A*/PIBT.

### 5.3 Acceptance
- Server log: **không** còn `fw_row_wrap` do sai stride trên bất kỳ map nào (kể cả map thứ 2+).
- Mỗi map chữ nhật: ≥ 4/6 agent có `btCellsVisited ≥ 2` trong 20s (trừ agent đang bắn hợp lệ).
- F-U2 đếm invalid-nextLoc ≈ 0.
- Regression: map đầu tiên (vẫn) chạy như cũ; A* & local PIBT không đổi (không sửa file của chúng).

---

## 6. Files dự kiến đụng
- **Server (WSL, fix chính):** `src/UnityStartKitAdapter.cpp` (F-S1 — bỏ `static`); (tuỳ)
  `src/PibtTcpServer.cpp` (F-S2 echo dims); (tuỳ) `src/PlannerSession.cpp` (F-S3 log throw rõ). Build `compile.sh`.
- **Unity (repo, hardening):** `Assets/Scripts/PIBTTcpClient.cs` (F-U1),
  `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs` (F-U2/F-U3/F-U4),
  `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs` (`LastStallReason`, counter).
- **KHÔNG đụng:** `GridEnemyAgentPIBT.cs`, `MapScenarioBootstrapPIBT.cs`, `GridEnemyAgent.cs`,
  `GridAStarPathfinder.cs`, `MapLoader.cs`.

## 7. Thứ tự thực hiện đề xuất
1. **F-S1** (bỏ `static`) — fix gốc, 1 dòng. Build lại server.
2. Test 5.1 (gallows-rồi-chantry không restart) phải hết `fw_row_wrap`.
3. **F-U2/F-U3** (hardening Unity) để bắt mọi hồi quy hình học về sau.
4. (tuỳ) **F-S2 + F-U1** (echo + assert dims) cho chắc.
5. Backtest 5/5 map cùng process, export, kiểm cột PIBT_TCP. Lưu artifact vào `BacktestResults/`.

## 8. Rủi ro & rollback
- F-S1 cực nhỏ & an toàn (chỉ chuyển `static` → local). Rollback = thêm lại `static` (không khuyến nghị).
- Hardening Unity khu trú 3 file TCP, revert dễ.
- Nếu sau F-S1 vẫn còn agent đâm tường (không phải đứng yên đồng loạt): mới là chuyện heuristic
  `DefaultPlanner` (greedy) hoặc execution-model — xử lý theo plan V3/V4, **độc lập** với bug này.

## 9. Quan hệ với plan cũ
- Bản plan này **thay thế giả thuyết "lỗi lệch chiều vuông/chữ nhật"** ở phiên bản trước: đó chỉ là
  triệu chứng. Root-cause thật = `static delta` cache width map đầu (server giữ state qua các trận),
  đúng như user nhận định.
- V3/V4 (execution model: `nextLoc`, xoay vật lý, stuck-recovery, staging goal) vẫn giữ nguyên giá trị
  và **độc lập** với fix này — áp dụng sau khi enemy đã di chuyển được trên cả 5 map.
