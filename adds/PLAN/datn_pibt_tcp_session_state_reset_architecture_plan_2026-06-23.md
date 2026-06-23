# PLAN: PIBT-TCP — Cơ chế XOÁ STATE / RESET SESSION không cần restart (client + server) cho production

Ngày lập: 2026-06-23
Phạm vi: Mode `PIBT_TCP`. Server C++ `~/projectY/Server-PIBT-TeamNoMan-sSky`
(`./build/pibt_tcp_server --host 0.0.0.0 --port 7777`), client Unity (`PIBTTcpClient`,
`MapScenarioBootstrapPIBT_TCP`).

> **Vấn đề user nêu (bản chất kiến trúc, không chỉ 1 bug):**
> Khởi động sạch cả client + server → chọn map **Maze** (PIBT-TCP) → enemy chạy mượt.
> Thoát ra menu → chọn map **Gallows** → enemy đi vài bước rồi **đứng yên**.
> Production cũng vậy: **không thể cứ restart client + server mỗi lần đổi map**.
> ⇒ Cần **một hành động xoá state rõ ràng, đáng tin cậy** để mỗi ván/map đều bắt đầu sạch.

Plan này **kế thừa và nối tiếp**
`datn_pibt_tcp_nonsquare_map_dimension_mismatch_root_cause_plan_2026-06-23.md` (đã xác định +
áp F-S1). Ở đây tập trung vào **cơ chế reset** và **độ bền cho production**, không lặp lại phần
số học root-cause.

---

## 0. TL;DR

1. **Triệu chứng Maze→Gallows = đúng `static delta` bug.** Đã có fix **F-S1** (bỏ `static` ở
   `UnityStartKitAdapter.cpp:123`). Sau khi **build lại + chạy lại** server, đổi map sẽ hết đứng yên
   vì đường reset hiện tại (mỗi `hello` → reset toàn bộ planner) **vốn đã sạch** — `static delta` là
   lỗ DUY NHẤT thoát khỏi nó (đã kiểm chứng toàn bộ state, Mục 2).
2. **NHƯNG** cơ chế reset hiện tại **ngầm định và mong manh**: phụ thuộc đúng trình tự "client đóng
   socket → server bắt EOF → reset → client mở socket mới + hello". Không có **lệnh reset tường minh**.
3. **VÀ** server là **single-session**: vòng `accept()` tuần tự + state planner là **biến global**
   (không phải per-session). Một process chỉ phục vụ **một client tại một thời điểm**; nhiều client
   đồng thời sẽ **đè state lên nhau**. Đây là giới hạn cứng cho production.
4. **Hướng giải quyết (phân lớp, Mục 4):** L1 F-S1 (xong) → L2 reset **tường minh + idempotent**
   (lệnh `reset`, "action xoá state" mà user muốn) → L3 **guard chống leak tái diễn** (dual-width smoke
   test) → L4 client **teardown/khởi tạo session chuẩn mọi lần đổi map** → L5 **topology production**
   (cần user chọn).
5. **Trả lời "5 map có swap sạch khi thoát-về-menu rồi chọn map khác không?": CÓ — sau khi build lại +
   relaunch server (F-S1).** Đã kiểm chứng phía client (Mục 2.7): cả 5 map dùng **chung 1 scene** load
   **Single mode** → huỷ sạch instance cũ → `hello` mới → server reset toàn bộ. Mode 2 (local PIBT) cũng
   sạch. **Điểm mong manh duy nhất:** teardown lúc chơi-thường dựa vào **EOF ngầm**, không tường minh →
   nâng lên mức "đảm bảo" bằng **C-R2** (L4).

---

## 1. Ánh xạ case Maze→Gallows về nguyên nhân

| Bước | Việc xảy ra | State server |
| --- | --- | --- |
| Boot | server + Unity khởi động sạch | `delta` chưa init |
| Maze (128×128) | `hello` → `ApplyMap` (cols=128) → `Initialize` → plan_step… | lần gọi `NextLoc` ĐẦU TIÊN bake `delta=±128`; mọi thứ khác đúng → **chạy mượt** |
| Về menu | scene unload → client `Disconnect()` → server EOF → `ResetSessionState` | planner globals + env reset; **nhưng `delta` vẫn = ±128** |
| Gallows (180×251) | `hello` → `ApplyMap` (cols=251) → `Initialize` → plan_step… | env/planner đúng cols=251, **`delta` vẫn kẹt ±128** → `FW` dọc nhảy sai hàng → `SanitizeAction` (đúng cols=251) trả `fw_row_wrap` → ép `W` |
| Hệ quả | enemy đi vài bước bằng **dead-reckon fallback** (Unity) rồi tắc → **đứng yên** | — |

"Đi vài bước rồi đứng" = phần `ApplyAction` phía Unity còn dead-reckon `FW` theo orientation cục bộ khi
`serverNextLoc` không hợp lệ; nhưng vì server lập kế hoạch trên hình học hỏng nên nhanh chóng kẹt.
**Khớp 100% với `static delta`.** F-S1 bịt đúng chỗ này.

---

## 2. AUDIT TOÀN BỘ STATE SỐNG THEO VÒNG ĐỜI PROCESS (đã đọc source)

> Mục tiêu: chứng minh "đổi map đứng yên" chỉ do `static delta`, và xác định những gì cần đảm bảo
> luôn-reset cho production.

| # | State (process-lifetime) | Vị trí | Có được reset mỗi `hello`? | Phụ thuộc kích thước map? |
| --- | --- | --- | --- | --- |
| 1 | `static const int delta[4]` | `UnityStartKitAdapter.cpp:123` `NextLoc` | ❌ **KHÔNG** (đây là bug) → **F-S1 bỏ `static`** | ✅ (±cols) |
| 2 | `decision, prev_decision, p, p_copy, prev_states, next_states, ids, occupied, decided, checked, require_guide_path, dummy_goals, trajLNS, mt1` | `planner.cpp:14-28` (global namespace `DefaultPlanner`) | ✅ `ResetPlannerGlobals()` gọi trong **cả** `reset()` lẫn `initialize()`; resize theo `env` mới | ✅ nhưng được dựng lại đúng |
| 3 | `global_heuristictable, global_neighbors` | `heuristics.cpp:7-8` | ✅ clear ở `ResetPlannerGlobals` (planner.cpp:46-47) + `init_heuristics(env)` resize theo `env->map.size()`, `init_neighbor` dùng `env->cols/rows` | ✅ dựng lại đúng |
| 4 | `g_operations, g_prev_operations, g_last_agent_traces, g_last_stats, g_initialized` | `epibt.cpp:26-30` (global namespace `EpibtPlanner`) | ✅ `ResetState()` gọi trong **cả** `reset()` lẫn `initialize()` | ⚠️ kích thước theo `num_of_agents`, dựng lại đúng |
| 5 | `static const std::array<Action,4> base_actions` | `epibt.cpp:263` | ❌ không reset — **nhưng là hằng bất biến, không phụ thuộc map** → vô hại | ❌ |
| 6 | `FileLogger` statics (file handle) | `FileLogger.*` | n/a — log, không phải state ván chơi | ❌ |

**Kết luận audit:**
- Sau **F-S1**, **không còn** state phụ thuộc-kích-thước nào thoát khỏi reset. Đường reset hiện tại
  (`ResetSessionState` + `reset()` + `initialize()` mỗi `hello`) **đã sạch**.
- `EpibtPlanner::ApplyAction` (epibt.cpp:64+) dùng `switch(orientation)` đọc `env` mỗi lần gọi → **không**
  có lỗi static tương tự (chỉ DefaultPlanner đi qua `UnityAdapter::NextLoc`).
- Vấn đề còn lại **không phải "thiếu reset"** mà là **"reset ngầm định, mong manh, single-session"** (Mục 3).

### 2.7 Audit phía CLIENT (Unity) — đổi map có sạch khi thoát-về-menu không? *(đã kiểm chứng)*

Câu hỏi: "thoát ván → về menu → chọn map khác" có reset sạch để 5 map swap trơn tru không. Truy vết
luồng thật:

| Mắt xích | Bằng chứng (file:line) | Sạch? |
| --- | --- | --- |
| Cả 5 map dùng **chung 1 scene** PIBT-TCP | `MenuViewBootstrap.cs:72-76` — 5 `MapDef` đều `scenePIBT="MapF_TankTest_PIBT"`; map chọn lưu `PlayerPrefs["SelectedMapFile"]` (`MenuViewBootstrap.cs:630/1177`) | — |
| Đổi map = `SceneManager.LoadScene` **Single** | thoát/chọn map đều load Single → **huỷ toàn bộ** GameObject scene cũ | ✅ |
| Client TCP **không có field `static`** | `MapScenarioBootstrapPIBT_TCP` + `PIBTTcpClient` toàn bộ state là **instance** → sinh mới mỗi scene; `ClearScenario()` còn reset thêm các counter (gồm F-U2/F-U3) | ✅ |
| Thoát map → ngắt kết nối | `MapScenarioBootstrapPIBT_TCP.cs:1037 OnDestroy` → `_client.Disconnect()` → server **EOF** → `ResetSessionState` | ✅ (ngầm) |
| Vào map mới → `hello` mới | bootstrap mới → `ConnectAndHello` → socket mới + `sessionId` mới → server `ResetSessionState` + `ApplyMap`(dims mới) + `initialize` | ✅ |
| MapLoader đọc đúng map mới | `MapLoader.cs:95-96` đọc `SelectedMapFile` mỗi lần build | ✅ |

**Mode 2 (local PIBT) — cũng sạch (đã kiểm):** `PIBTPlanner` là `static class` (state sống qua scene),
**nhưng** `Init` (`PIBTPlanner.cs:51`) chốt theo **danh tính `MapLoader` instance**; mỗi scene mới có
`MapLoader` mới ⇒ guard không early-out ⇒ re-init đầy đủ `_cols/_rows/_nbrs/_h/_trajs/...`. Không dính
bug static song song.

**Kết luận client:** kiến trúc client **vốn đã reset đúng** mỗi lần đổi map (nhờ scene load Single +
`hello` mới). `static delta` của server là thứ DUY NHẤT phá vỡ swap; F-S1 bịt xong là 5 map swap sạch.
**Khoảng trống còn lại = P4:** chơi-thường reset bằng **EOF ngầm** (chỉ `BacktestRunner` gửi `shutdown`
tường minh) → để đạt yêu cầu "thoát menu là reset MỌI thứ" ở mức **đảm bảo**, áp **C-R2** (L4).

---

## 3. CÁC VẤN ĐỀ KIẾN TRÚC CÒN LẠI (vì sao production sẽ đau)

### P1 — Reset là NGẦM ĐỊNH, phụ thuộc trình tự socket
- Server chỉ reset khi: nhận `hello` (PibtTcpServer.cpp:154 `ResetSessionState` + new `PlannerSession` +
  `Initialize`), nhận `shutdown` (dòng 201-208), hoặc EOF/lỗi socket (dòng 107-119).
- **Không có lệnh `reset` tường minh** để tái dùng một kết nối đang mở cho ván mới.
- Hiện client mỗi lần vào map gọi `Connect()` (tự `Disconnect()` trước) + `_sessionId` mới + `hello`
  ⇒ vô tình "đủ" để reset. Nhưng đây là **may mắn về trình tự**, không phải hợp đồng rõ ràng. Bất kỳ
  thay đổi nào (giữ kết nối lâu dài, reconnect, đổi luồng) đều có thể bỏ sót bước reset.

### P2 — State planner là GLOBAL ⇒ SINGLE-SESSION (giới hạn cứng production)
- `PibtTcpServer::Run` (dòng 248-253): vòng lặp `accept()` rồi gọi `HandleClient(socket)` **đồng bộ,
  blocking** tới khi client đó ngắt. ⇒ **một client tại một thời điểm**; client thứ 2 phải chờ.
- `PlannerSession` *trông* như per-session, nhưng state thật của planner (Mục 2 #2-#4) là **biến
  global của namespace**, **dùng chung** cho mọi session. Hai ván chạy chồng nhau (nếu sau này đa luồng)
  sẽ **đè state**.
- Production (nhiều trận multiplayer/nhiều client cùng lúc trên 1 server) → **không an toàn** với mô
  hình hiện tại.

### P3 — Mẫu code DỄ RÒ RỈ (leak-prone), đã thất bại 1 lần
- Mỗi global/static mới phải **nhớ thủ công** thêm vào `ResetPlannerGlobals`/`ResetState`. `static delta`
  là bằng chứng mẫu này đã rò một lần. Không có cơ chế cấu trúc nào ngăn lần sau.

### P4 — Client teardown KHÔNG đồng nhất
- `ShutdownGracefully()` (gửi `shutdown` tường minh) **chỉ** được `BacktestRunner` gọi.
- Chơi thường: thoát map → `OnDestroy` → `Disconnect()` (đóng socket, **không** gửi `shutdown`). Server
  dựa vào EOF để reset — *hiện hoạt động*, nhưng kém tường minh và dễ vỡ khi mạng chập chờn / proxy giữ
  kết nối (đặc biệt bản WebGL relay).

---

## 4. HƯỚNG GIẢI QUYẾT (phân lớp — làm từ trên xuống)

> Triết lý: **một nguồn sự thật duy nhất cho "trạng thái sạch"**, **kích hoạt tường minh**, **tự kiểm
> chứng** để bug hình học không bao giờ tái diễn, và **quyết định rõ topology** cho đa client.

### L1 — Bịt leak gốc *(ĐÃ XONG — F-S1)*
- `const int delta[4]` thay cho `static const`. Đây là điều kiện đủ để **đổi map trong 1 process chạy
  đúng** qua đường reset sẵn có. Vẫn cần **build lại + chạy lại** server một lần để nạp binary mới.

### L2 — Reset TƯỜNG MINH + IDEMPOTENT (đây là "action xoá state" user muốn)
**Server (S-R1):** gom toàn bộ reset vào một hàm duy nhất, công khai ý đồ:
```cpp
// PibtTcpServer.cpp — đổi tên/ý nghĩa ResetSessionState cho rõ "xoá sạch mọi state ván chơi"
void ResetAllSessionState(std::unique_ptr<PlannerSession>& session)
{
    session.reset();
    DefaultPlanner::reset();   // ResetPlannerGlobals: clear + reseed mt1/srand
    EpibtPlanner::reset();     // ResetState
    FileLogger::Info("[PibtTcpServer] session state fully reset");
}
```
**Server (S-R2):** thêm message type `reset` trong `HandleClient` — cho phép **tái dùng 1 kết nối lâu
dài** để bắt đầu ván mới mà không cần đóng/mở socket:
```cpp
if (type == "reset") {                       // ván mới trên cùng connection
    ResetAllSessionState(session);
    WriteJsonLine(socket, BuildResetAck(request));   // {"type":"reset_ack","status":"ok"}
    continue;
}
```
**Server (S-R3):** đảm bảo `hello` **luôn** `ResetAllSessionState` **trước** `Initialize` (đã đúng) và
ghi log map dims (đã có ở `Initialize`; F-S3 thêm dims vào log lỗi). `hello` lặp lại trên cùng connection
phải an toàn (idempotent) — hiện đã vậy.

**Client (C-R1):** trước khi vào map mới, nếu kết nối còn sống thì gửi `reset` (nhanh, không cần
reconnect); nếu không thì `Connect()`+`hello` như cũ. Một sessionId mới cho mỗi ván.

> Lưu ý: L2 **không sửa hành vi đang chạy** (mỗi map vẫn reset sạch nhờ hello mới); nó **biến cơ chế
> ngầm thành tường minh + bền** và cho phép giữ kết nối lâu dài (tốt cho production/latency).

### L3 — GUARD chống leak hình học tái diễn *(giá trị phòng ngừa cao nhất)*
**Test quyết định (G-1):** thêm một smoke test cạnh `src/epibt_smoke_main.cpp` (đã có harness
`BuildEnv`) chạy **hai map khác width liên tiếp trên cùng process** rồi assert không có hành vi
`fw_row_wrap` do sai stride. Test này **sẽ bắt** đúng `static delta`:
```cpp
// dual_width_smoke_main.cpp — chạy trong CI/compile.sh, exit!=0 nếu leak
// 1) ép NextLoc/SanitizeAction qua 2 cols khác nhau và kiểm stride dọc đổi theo cols:
assert(UnityAdapter::NextLoc(State(loc,0,/*south*/1), Action::FW, 128) == loc + 128);
assert(UnityAdapter::NextLoc(State(loc,0,/*south*/1), Action::FW, 251) == loc + 251); // FAIL nếu còn static
// 2) (mức cao) Initialize map A (cols=128) → plan; Initialize map B (cols=251) → plan;
//    assert mọi action FW của B có nextLoc kề-không-wrap trên cols=251.
```
**Quy ước code (G-2):** comment cảnh báo tại `NextLoc`/adapter: *"KHÔNG dùng `static`/global mutable cho
giá trị suy ra từ kích thước map; phải tính lại mỗi lần hoặc lấy từ `env`."* Thêm vào checklist review.

### L4 — Client: teardown + khởi tạo session CHUẨN HOÁ cho MỌI lần đổi map (không chỉ backtest)
- **C-R2 *(đáp ứng trực tiếp yêu cầu "thoát menu là reset mọi thứ")*:** hiện chơi-thường **đã** reset
  được nhờ EOF (Mục 2.7) — nhưng đó là "đúng nhờ trình tự đóng socket", không tường minh. C-R2 chuyển
  thành **đảm bảo**: gọi `ShutdownGracefully()` (hoặc `reset` của L2) từ **một điểm teardown chung** khi
  rời map (vd: `OnDisable` của bootstrap, hoặc bọc lại các chỗ `SceneManager.LoadScene` về menu/đổi map
  trong `MapWinController`/`MapGameOverController`/`PauseMenuController`), **không chỉ** trong
  `BacktestRunner`. Sửa nhỏ (~10 dòng, chủ yếu 1 file `MapScenarioBootstrapPIBT_TCP.cs`), rủi ro thấp,
  loại bỏ phụ thuộc thời điểm TCP FIN.
- **C-R3 (đã có một phần — F-U1/F-U2/F-U3):** assert dims `hello_ack`; đếm `invalidNextLoc`; phát hiện
  "cả đội đứng" → log `GEOMETRY STALL`. Giữ nguyên, đây là lưới an toàn runtime.
- **C-R4 (tuỳ):** khi `GEOMETRY STALL` được phát hiện giữa ván → tự động gửi `reset`+`hello` lại một lần
  (self-heal) thay vì đứng cho tới timeout 120s.

### L5 — TOPOLOGY PRODUCTION cho ĐA CLIENT *(cần user quyết định — Mục 7)*
State global ⇒ 1 process phục vụ 1 ván tại một thời điểm. Các hướng:

| Hướng | Mô tả | Ưu | Nhược | Hợp với |
| --- | --- | --- | --- | --- |
| **A. 1 process / 1 ván** *(khuyến nghị cho thesis)* | Orchestrator (script/container) spawn 1 `pibt_tcp_server` cho mỗi trận, kill khi xong | Đơn giản; cô lập tuyệt đối; không refactor | Cần lớp quản lý vòng đời process; overhead khởi tạo | DATN/demo, multiplayer quy mô nhỏ |
| **B. Tuần tự hoá + reset giữa ván** | Giữ single-process, hàng đợi request, `ResetAllSessionState` giữa các ván | Không refactor planner | Thông lượng thấp; client phải chờ | tải rất nhẹ |
| **C. Đóng gói state vào `PlannerSession`** | Chuyển global của DefaultPlanner/EpibtPlanner thành thành viên instance | "Đúng" về kiến trúc; đa session thật | Refactor lớn, rủi ro; planner LoRR vốn viết theo global | sản phẩm dài hạn |
| **D. Pool worker process** | 1 frontend + N worker, mỗi worker 1 ván | Mở rộng ngang tốt | Hạ tầng phức tạp nhất | quy mô lớn |

**Khuyến nghị:** **A** cho phạm vi DATN (kèm L2 để mỗi process vẫn reset tường minh giữa các map của
cùng một phiên backtest). Ghi rõ ràng giới hạn "1 session/process" vào README server. Cân nhắc **C** chỉ
khi thực sự cần đa trận đồng thời trên 1 process.

---

## 5. CÔNG VIỆC TRIỂN KHAI (đề xuất, theo lớp)

**Server (WSL `~/projectY/Server-PIBT-TeamNoMan-sSky`):**
- [x] **F-S1** `src/UnityStartKitAdapter.cpp:123` bỏ `static`.
- [x] **F-S2** `src/PibtTcpServer.cpp` `BuildHelloAck` echo `width/height/cellCount`.
- [x] **F-S3** `src/PlannerSession.cpp` log map dims khi exception.
- [x] **S-R1** đổi `ResetSessionState` → `ResetAllSessionState` (rõ ý đồ) + log 1 dòng.
- [x] **S-R2** thêm message `reset` + `BuildResetAck` trong `HandleClient`. Cả `reset` và `shutdown`
      handler đều wrap `WriteJsonLine` trong try-catch để không crash khi client fire-and-forget.
- [x] **G-1** `src/dual_width_smoke_main.cpp` + target `dual_width_smoke_test` trong `CMakeLists.txt`;
      `compile.sh` chạy `./build/dual_width_smoke_test` sau make, fail build nếu leak.
- [x] **G-2** comment cảnh báo tại `NextLoc` giải thích lý do không dùng `static`.
- Build: `./compile.sh` (hoặc cmake trong `build/`), chạy `./build/pibt_tcp_server --host 0.0.0.0 --port 7777`.

**Client (repo Unity, chỉ 3 file TCP — không đụng A*/local PIBT):**
- [x] **F-U1** `PIBTTcpClient.Hello` assert dims `hello_ack`.
- [x] **F-U2/F-U3** `MapScenarioBootstrapPIBT_TCP` đếm `invalidNextLoc` + phát hiện `GEOMETRY STALL`.
- [x] **C-R1** thêm `PIBTTcpClient.Reset()` gửi `{"type":"reset",...}` + đọc `reset_ack`; đối xứng
      `Shutdown()`; dùng khi tái dùng kết nối lâu dài mà không cần reconnect.
- [x] **C-R2** thêm `PIBTTcpClient.SendShutdown()` (fire-and-forget, không block main thread);
      `MapScenarioBootstrapPIBT_TCP.OnDestroy` gọi `_client?.SendShutdown()` + `_client?.Disconnect()`
      — mọi lần rời map đều gửi `shutdown` tường minh, không chỉ backtest.
- [ ] **C-R4 (tuỳ)** self-heal khi `GEOMETRY STALL`.

**KHÔNG đụng:** `GridEnemyAgentPIBT.cs`, `MapScenarioBootstrapPIBT.cs`, `GridEnemyAgent.cs`,
`GridAStarPathfinder.cs`, `MapLoader.cs`, và toàn bộ logic A*/local PIBT.

---

## 6. TEST & ACCEPTANCE

### 6.1 Test quyết định (chứng minh reset sạch giữa map, KHÔNG restart)
1. Khởi động server sạch (đã build F-S1). Unity: chơi **Maze** (PIBT-TCP) → enemy chạy mượt.
2. **Không restart gì cả.** Về menu → chơi **Gallows** → enemy phải **tiếp tục di chuyển** (không đứng
   yên), server log **không** `fw_row_wrap` do sai stride.
3. Đảo thứ tự (Gallows trước, Maze sau) → cả hai vẫn đúng.
4. Lặp menu↔map ≥ 5 lần đan xen **cả 5 map** (5 width khác nhau) → map nào cũng có enemy di chuyển, không
   `fw_row_wrap`, `invalidNextLoc ≈ 0`, không log `GEOMETRY STALL`.
5. (Mode 2 đối chứng) Chơi local PIBT đổi 5 map tương tự → cũng phải sạch (xác nhận `PIBTPlanner` re-init
   theo từng `MapLoader` mới).

### 6.2 Test guard (G-1, mức binary, để hồi quy tự động)
- `dual_width_smoke` exit 0 khi có F-S1; **phải exit ≠ 0** nếu cố tình thêm lại `static` (kiểm chứng test
  thật sự bắt được lỗi).

### 6.3 Test reset tường minh (sau L2)
- Giữ 1 kết nối, gửi `reset` rồi `hello` map mới (không đóng socket) → server trả `reset_ack` + chạy map
  mới đúng. Mô phỏng production "đổi ván trên kết nối lâu dài".

### 6.4 Backtest end-to-end
- PIBT_TCP đủ **5/5 map** trên **cùng 1 process** → `BacktestResults/` có cột PIBT_TCP hợp lệ cả 5 map.

### 6.5 Acceptance
- Đổi map bất kỳ thứ tự, không restart → không map nào đứng yên do hình học; `invalidNextLoc ≈ 0`;
  không log `GEOMETRY STALL`.
- (Sau L2) có "action xoá state" tường minh (`reset`/teardown) và đã kiểm chứng.
- (Sau L5) topology production được chốt + ghi tài liệu.

---

## 7. QUYẾT ĐỊNH CẦN TỪ USER

1. **Topology production (L5):** chọn A (1 process/ván + orchestrator) — khuyến nghị — hay cần C (đóng
   gói state per-session để đa trận đồng thời trên 1 process)? Quyết định này định hình mức refactor.
2. **Có làm L2 `reset` message ngay không**, hay tạm chấp nhận "reconnect+hello mỗi map" (đã đủ đúng sau
   F-S1) và để L2 cho vòng sau?

---

## 8. RỦI RO & ROLLBACK
- **F-S1**: cực nhỏ, đã làm. Rollback = thêm lại `static` (không khuyến nghị; G-1 sẽ chặn).
- **S-R1/S-R2 (reset message)**: thêm nhánh xử lý mới, không đổi đường `hello` hiện tại → rủi ro thấp;
  rollback = bỏ nhánh `reset`.
- **C-R1/C-R2**: khu trú trong `PIBTTcpClient`/`MapScenarioBootstrapPIBT_TCP`; revert dễ.
- **L5-C (đóng gói state)**: rủi ro cao nhất (đụng planner LoRR) → chỉ làm nếu user chọn; tách nhánh +
  smoke test đầy đủ trước.

---

## 9. QUAN HỆ VỚI CÁC PLAN TRƯỚC
- **Nối tiếp** `datn_pibt_tcp_nonsquare_map_dimension_mismatch_root_cause_plan_2026-06-23.md`: plan đó tìm
  ra + sửa `static delta` (F-S1) và hardening Unity (F-U1..F-U3). Plan này trả lời câu hỏi tiếp theo của
  user: *"làm sao xoá state để chạy lại mà không restart, cho cả production?"* → reset tường minh +
  guard + topology.
- **Độc lập** với V3/V4 (execution model: nextLoc, xoay vật lý, stuck-recovery, staging goal) — những thứ
  đó áp dụng **sau** khi enemy đã di chuyển ổn định trên cả 5 map.
