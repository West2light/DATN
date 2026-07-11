# PLAN: Kết nối Unity Editor/WebGL với C++ PIBT TCP server trên Hugging Face bằng session relay

Ngày lập: 2026-06-23  
Phạm vi: Unity `PIBT_TCP` client trong `D:\2025.2\DATN\projectY` gồm cả **Unity Editor local bấm Play** và WebGL build, HF Space `https://west2light-server-pibt.hf.space`, C++ server trong WSL `/home/west2light/projectY`.

## 0. Kết luận ngắn

Không nên tiếp tục bám nguyên hướng dẫn cũ kiểu `POST /plan` cho gameplay chính.

`/plan` hiện vẫn hoạt động và đã verify live trên HF, nhưng nó là mô hình **one-shot mỗi timestep**: HTTP request mở TCP nội bộ, gửi `hello -> plan_step -> shutdown`, rồi đóng session ngay. Cách này hợp để smoke test/CORS test, nhưng không phù hợp với mục tiêu mới vì server C++ đã có session state, operation queue và reset lifecycle.

Yêu cầu mới là **Unity Editor local gọi server deploy trên HF**, không cần chạy WSL `pibt_tcp_server` local. Vì HF Space chỉ expose HTTPS/FastAPI, Unity Editor cũng phải đi qua HTTP relay; raw TCP `serverHost:serverPort` chỉ còn là chế độ local/debug.

Hướng đúng hiện tại:

```text
Unity Editor hoặc WebGL
  -> HTTPS POST /api/sessions/pibt/hello      (tạo TCP session nội bộ)
  -> HTTPS POST /api/sessions/pibt/plan-step  (lặp trong round)
  -> HTTPS POST /api/sessions/pibt/shutdown   (dừng Play Mode / về menu / hết round)
  -> optional POST /api/sessions/pibt/reset   (self-heal hoặc tái dùng connection)
HF FastAPI relay
  -> giữ TCP socket theo sessionId
  -> forward JSON-line tới pibt_tcp_server nội bộ 127.0.0.1:7777
C++ pibt_tcp_server
  -> hello/reset/shutdown đều đi qua ResetAllSessionState
```

## 1. Trạng thái hiện tại đã kiểm tra

### 1.1 HF Space đang chạy

Ngày 2026-06-23 đã gọi live:

```text
GET https://west2light-server-pibt.hf.space/health
```

Kết quả chính:

```json
{
  "status": "ok",
  "binary_exists": true,
  "process_alive": true,
  "tcp_reachable": true,
  "tcp_endpoint": "127.0.0.1:7777",
  "version": "2.1.0"
}
```

CORS preflight `OPTIONS /plan` với origin `https://luminx.io.vn` trả `200 OK` và `access-control-allow-origin: https://luminx.io.vn`.

Smoke `POST /plan` với map 3x3 trả response dạng:

```json
{
  "session_id": "...",
  "hello_ack": { "type": "hello_ack", "status": "ok" },
  "plan_result": {
    "type": "plan_result",
    "actions": [
      { "id": 0, "action": "CR", "nextLoc": 0, "operation": "CR,W,W" }
    ]
  },
  "shutdown_ack": { "type": "shutdown_ack", "status": "ok" }
}
```

Điểm cần lưu ý: response hiện không còn là `List<string>` thuần; `actions` là object array có `action`, `nextLoc`, `operation`, `debugReason`.

### 1.2 C++ TCP server trong WSL đã có reset session

Source `/home/west2light/projectY/src/PibtTcpServer.cpp` hiện có:

- `BuildResetAck(...)` trả `{"type":"reset_ack","status":"ok"}`.
- `ResetAllSessionState(...)` gọi `session.reset()`, `DefaultPlanner::reset()`, `EpibtPlanner::reset()`.
- `hello` luôn gọi `ResetAllSessionState` trước khi tạo `PlannerSession` mới.
- `reset` gọi `ResetAllSessionState` và giữ connection sống.
- `shutdown` trả `shutdown_ack`, gọi `ResetAllSessionState`, rồi đóng session.
- EOF/socket error cũng gọi `ResetAllSessionState`.

`UnityStartKitAdapter.cpp` cũng đã bỏ bug `static delta`; `dual_width_smoke_test` tồn tại để bắt hồi quy sai stride khi đổi map khác width.

### 1.3 Unity client hiện có HTTP relay cho WebGL, nhưng Editor vẫn đi raw TCP

Các file hiện tại:

- `Assets/Scripts/PIBTWebRelayClient.cs`: WebGL transport dùng `UnityWebRequest`, endpoint mặc định là same-origin `/api/sessions/pibt/{operation}`.
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`: WebGL build gọi `ConnectAndHelloWeb()` và `DoStepWeb()` thay vì raw TCP.
- `MapScenarioBootstrapPIBT_TCP.cs` đang dùng `#if UNITY_WEBGL && !UNITY_EDITOR`, nên **Unity Editor local luôn rơi về raw TCP** `ConnectAndHello()` tới `serverHost:serverPort`.
- Vì HF không expose raw TCP 7777 ra Internet, Unity Editor không thể chỉ đổi `serverHost` sang `west2light-server-pibt.hf.space`; bắt buộc phải có nhánh HTTP relay trong Editor.
- `ConnectAndHelloWeb()` gửi operation `hello`.
- `DoStepWeb()` gửi operation `plan-step`.
- `OnDestroy()` hiện chỉ gọi `_client?.SendShutdown()` và `_client?.Disconnect()`. Trong WebGL path `_client` không tồn tại, nên chưa gửi `shutdown` qua HTTP relay.
- `DoStepWeb()` đang parse `actions` nhưng truyền `nextLocs = null` vào `ApplyStepActions(...)`. TCP path non-WebGL đã dùng `LastNextLocs`; WebGL nên parse `nextLoc` tương tự để tránh dead-reckon lệch.

### 1.4 Relay Python trong repo Unity là template tốt nhưng chưa đủ cho HF trực tiếp

`services/invite-registry/app.py` đã có relay same-origin:

- `POST /api/sessions/pibt/hello`
- `POST /api/sessions/pibt/plan-step`
- `POST /api/sessions/pibt/shutdown`
- map `sessionId -> PibtRelayConnection`
- idle cleanup 300s

Nhưng file này hiện:

- chưa hỗ trợ `reset`;
- mặc định trỏ `PIBT_TCP_HOST = 110.172.28.110`;
- nằm trong registry/GCP flow, không phải FastAPI app đang deploy HF.

HF `app.py` hiện vẫn là kiểu `/plan` one-shot; các endpoint `/tcp/hello`, `/tcp/plan_step`, `/tcp/shutdown` mở TCP connection riêng từng request nên **không dùng được làm gameplay session liên tục** nếu chưa refactor.

## 2. Kiến trúc mục tiêu

### 2.1 Contract HTTP chính cho Unity Editor/WebGL

Giữ đường dẫn giống Unity đang dùng để giảm sửa client:

| Operation | Endpoint | Body gửi từ Unity | Response |
| --- | --- | --- | --- |
| Health | `GET /api/sessions/pibt/healthz` hoặc `GET /health` | none | trạng thái relay + TCP |
| Hello | `POST /api/sessions/pibt/hello` | JSON-line protocol `{type:"hello", sessionId, teamSize, map}` | raw `hello_ack` từ C++ |
| Plan step | `POST /api/sessions/pibt/plan-step` | `{type:"plan_step", sessionId, requestId, timestep, agents}` | raw `plan_result` |
| Reset | `POST /api/sessions/pibt/reset` | `{type:"reset", sessionId}` | raw `reset_ack` |
| Shutdown | `POST /api/sessions/pibt/shutdown` | `{type:"shutdown", sessionId}` | raw `shutdown_ack`; relay xoá TCP connection |

`POST /plan` vẫn giữ để:

- test nhanh HF/CORS;
- fallback nếu session relay lỗi;
- tool demo Swagger.

Nhưng gameplay chính không dùng `/plan`.

### 2.2 Session model

Mỗi round/map trong Unity tạo một `sessionId` mới:

```text
unity-{editor|web}-{Guid:N}
```

Vòng đời session:

```text
SpawnScenario
  -> hello(sessionId, map, teamSize)
Update loop
  -> plan-step(sessionId, requestId=timestep, agents)
End lifecycle
  -> shutdown(sessionId)
  -> local clear: _serverReady=false, _sessionId=null, _frame=0
```

Các điểm phải gọi shutdown:

- dừng Play Mode / unload scene;
- nút thoát về menu;
- round thắng/thua;
- backtest chuyển map;
- WebGL page unload/browser close nếu có thể gửi `sendBeacon`.

### 2.3 Giới hạn production cần ghi rõ

C++ `pibt_tcp_server` hiện xử lý `accept -> HandleClient(socket)` tuần tự và planner vẫn có global state. Vì vậy HF Space một process nên được coi là:

```text
1 active gameplay session / 1 pibt_tcp_server process
```

Relay không nên mở nhiều TCP session đồng thời rồi để chúng block lẫn nhau. Với phạm vi DATN/demo, chọn chính sách:

- nếu có active session khác chưa hết hạn: trả `409 active session exists`;
- nếu session cũ idle quá TTL: đóng socket cũ, xoá khỏi registry, cho session mới vào;
- long-term nếu cần nhiều trận đồng thời: spawn một worker process/container cho mỗi trận.

## 3. Milestones triển khai

### M1 - Nâng HF FastAPI app thành session relay

File chính: `/home/west2light/projectY/app.py` và bản deploy lên HF.

Việc cần làm:

1. Thêm class `PibtRelayConnection` giống `services/invite-registry/app.py`: giữ socket, buffer, lock, `last_used`.
2. Thêm map global `PIBT_CONNECTIONS: dict[str, PibtRelayConnection]`.
3. Thêm `ACTIVE_SESSION_TTL_SECONDS`, mặc định 60-300s.
4. `hello`: nếu sessionId cũ tồn tại thì đóng; nếu có session khác đang active và chưa idle thì trả `409`; nếu stale thì đóng stale rồi mở TCP mới.
5. `plan-step`: lookup theo `sessionId`; không có thì trả `404 session not found or expired`.
6. `shutdown`: forward message, xoá session dù forward lỗi hay thành công.
7. `reset`: forward `reset`, giữ connection sống; dùng cho self-heal hoặc hello lại trên cùng connection. Nếu về sau muốn đổi `sessionId` sau reset thì thêm field `newSessionId`.
8. `health`: trả thêm `active_sessions`, `stale_sessions`, `tcp_endpoint`, `version`.
9. Bound payload size 2 MiB, response size 4 MiB, timeout: hello 60-70s, plan 15-20s, shutdown/reset 5s.

Acceptance M1:

```bash
curl https://west2light-server-pibt.hf.space/health
curl -X POST /api/sessions/pibt/hello
curl -X POST /api/sessions/pibt/plan-step
curl -X POST /api/sessions/pibt/shutdown
```

Log C++ phải có đúng thứ tự: `hello -> plan_step* -> shutdown -> session state fully reset`.

### M2 - Sửa Unity client dùng session relay đúng lifecycle cho cả Editor và WebGL

Files chính:

- `Assets/Scripts/PIBTWebRelayClient.cs`
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
- các controller exit/win/lose nếu cần điểm shutdown tường minh

Việc cần làm:

1. `PIBTWebRelayClient` đổi ý nghĩa từ "WebGL-only" thành HTTP relay dùng chung cho Editor/WebGL; có thể giữ tên file để giảm đổi rộng, nhưng comment phải sửa.
2. `PIBTWebRelayClient` thêm cấu hình `relayBaseUrl`.
   - Mặc định: same-origin `Application.absoluteURL` như hiện tại.
   - Production HF trực tiếp: `https://west2light-server-pibt.hf.space`.
   - Không hardcode duy nhất trong code nếu còn dùng cả GCP/luminx same-origin.
3. `MapScenarioBootstrapPIBT_TCP` thêm transport mode rõ ràng:
   - `RawTcpLocal`: giữ đường WSL/local hiện tại.
   - `HttpRelaySession`: Unity Editor/WebGL gọi HF qua `/api/sessions/pibt/*`.
   - `HttpPlanOneShot`: fallback gọi `/plan` nếu session relay chưa deploy.
4. `Auto` mode khuyến nghị:
   - WebGL luôn dùng `HttpRelaySession`.
   - Unity Editor mặc định dùng `HttpRelaySession` khi `useHfInEditor=true`, nếu không thì `RawTcpLocal`.
5. Gỡ giới hạn compile `#if UNITY_WEBGL && !UNITY_EDITOR` quanh các hàm HTTP; đổi tên `ConnectAndHelloWeb/DoStepWeb` thành `ConnectAndHelloHttp/DoStepHttp` hoặc wrapper tương đương để Editor cũng gọi được.
6. Thêm helper `Exchange("shutdown", ...)` và `Exchange("reset", ...)`.
7. Thêm `ShutdownHttpGracefully(timeoutSec)` trong `MapScenarioBootstrapPIBT_TCP`.
8. Trong `OnDestroy`, HTTP path phải có fallback gửi shutdown. Vì coroutine có thể không chạy ổn khi object đang destroy, nên ưu tiên gọi shutdown trước khi unload scene từ flow thoát menu/win/lose; `OnDestroy` chỉ là fallback.
9. Với WebGL/browser close, thêm optional `.jslib` dùng `navigator.sendBeacon` gửi shutdown payload nhỏ.
10. `DoStepHttp()` parse cả `PIBTTcpClient.ParseNextLocs(response, data.Length)` và truyền vào `ApplyStepActions(actions, nextLocs, rows, cols)`.
11. Khi `plan-step` trả `404 session expired`: reconnect bằng sessionId mới và `hello` lại. Không gửi tiếp plan-step trên session cũ.
12. Khi `plan-step` timeout/502: retry giới hạn như hiện tại, nhưng mỗi retry phải quyết định rõ: reuse session nếu relay còn sống, hoặc shutdown stale rồi hello mới.

Acceptance M2:

- WebGL không còn log raw TCP `Connect failed`.
- Unity Editor local bấm Play **không cần chạy WSL server**, không còn log `Connecting raw TCP endpoint host='127.0.0.1'`.
- Unity Editor log phải thể hiện đang gọi HF HTTP relay, ví dụ `[PIBT_HTTP] Sending hello... baseUrl=https://west2light-server-pibt.hf.space`.
- DevTools Network thấy `hello`, nhiều `plan-step`, và `shutdown`.
- Sau bấm về menu, HF/C++ log có `shutdown session=...` và `session state fully reset`.
- Sau thắng/thua round, session cũ được shutdown trước khi round mới hello.
- Enemy Editor/WebGL dùng `nextLoc` từ server, không chỉ action dead-reckon.

### M3 - Đồng bộ deploy HF

Repo WSL `/home/west2light/projectY` đang là nguồn deploy HF. Cần:

1. Sửa `app.py` root, không chỉ `adds/deploy/app.py`.
2. Cập nhật `adds/deploy/UNITY_WEBGL_CLIENT_GUIDE.md` để thay hướng dẫn cũ `/plan` bằng session relay.
3. Cập nhật `README`/landing page liệt kê endpoint mới.
4. Đảm bảo Dockerfile build target `pibt_tcp_server` vẫn đủ.
5. Thêm build/test guard `dual_width_smoke_test` vào CI hoặc Docker build nếu thời gian build HF cho phép. Hiện Dockerfile chỉ build `pibt_tcp_server`, nên guard chưa chắc chạy khi deploy HF.
6. Push main hoặc chạy workflow `deploy-hf.yml`; sau deploy poll `/health`.

Acceptance M3:

- `/health` trả version mới, ví dụ `2.2.0-session-relay`.
- `/api/sessions/pibt/healthz` trả `{"ok":true,"relay":"pibt-tcp","active_sessions":...}`.
- CORS preflight OK với `https://luminx.io.vn`.
- `POST /plan` vẫn chạy để rollback/smoke.

### M4 - Test end-to-end

#### Local WSL

1. Start FastAPI local:

```bash
cd /home/west2light/projectY
uvicorn app:app --host 0.0.0.0 --port 7860
```

2. Test sequence cùng `sessionId`:

```text
hello(random-32)
plan-step t=0
plan-step t=1
shutdown
```

3. Test đổi map không restart:

```text
hello(random-32) -> plan-step -> shutdown
hello(lt_gallowstemplar_n) -> plan-step -> shutdown
```

Không được có `fw_row_wrap` do sai stride.

#### Unity Editor gọi HF trực tiếp

Test chính cho yêu cầu hiện tại: **không chạy WSL `pibt_tcp_server` local**.

- Đảm bảo không có process local nghe `127.0.0.1:7777` để tránh test nhầm local.
- Unity Editor mode `PIBT_TCP`.
- Transport mode: `HttpRelaySession`.
- Base URL: `https://west2light-server-pibt.hf.space`.
- Bấm Play và chọn map.
- Verify console không có raw TCP connect tới `127.0.0.1:7777`.
- Verify Unity log có `hello -> plan-step`.
- Đổi 5 map liên tục: random, maze, mansion, chantry, gallowstemplar.
- Verify `shutdown/reset` khi thoát scene hoặc dừng Play Mode.

#### Unity Editor raw TCP local

Đây chỉ là đường debug/rollback:

- WSL `./build/pibt_tcp_server --host 0.0.0.0 --port 7777`;
- Unity Editor mode `PIBT_TCP`;
- đổi 5 map liên tục: random, maze, mansion, chantry, gallowstemplar;
- verify shutdown/reset khi thoát scene.

#### WebGL + HF

1. Build WebGL.
2. Serve qua domain production `https://luminx.io.vn` hoặc local HTTPS dev.
3. Chọn mode `PIBT_TCP`.
4. DevTools Network phải thấy:

```text
POST https://west2light-server-pibt.hf.space/api/sessions/pibt/hello
POST https://west2light-server-pibt.hf.space/api/sessions/pibt/plan-step
POST https://west2light-server-pibt.hf.space/api/sessions/pibt/shutdown
```

5. Bấm về menu, vào map khác, không restart HF Space.
6. Lặp tối thiểu 5 lần, gồm cả map khác width.
7. Kỳ vọng: không `GEOMETRY STALL`, `invalidNextLoc` thấp, action/nextLoc vẫn hợp lệ.

## 4. Rủi ro và cách giảm rủi ro

| Rủi ro | Tác động | Giảm rủi ro |
| --- | --- | --- |
| Browser/Unity không kịp gửi shutdown khi unload | HF giữ TCP session stale, session mới bị 409 hoặc block | TTL cleanup + explicit shutdown trước scene unload + optional sendBeacon |
| Nhiều người mở WebGL cùng lúc | C++ server single-session bị nghẽn | Trả 409 rõ ràng trong phạm vi DATN; dài hạn worker process/session |
| `/tcp/hello` và `/tcp/plan_step` hiện tại bị hiểu nhầm là session API | plan-step không có active session vì mỗi call mở TCP mới | Deprecate hoặc sửa thành persistent session relay; docs ghi rõ |
| Unity Editor dùng fallback same-origin | `Application.absoluteURL` rỗng, relay trỏ nhầm `127.0.0.1:8080` | Khi Editor gọi HF phải set explicit `relayBaseUrl` |
| HTTP relay bị giữ trong `#if UNITY_WEBGL` | Editor vẫn raw TCP local, không gọi được HF | Refactor HTTP path compile được trong Editor |
| WebGL/Editor chỉ dùng `action`, bỏ `nextLoc` | di chuyển lệch so với server-authoritative cell | Parse `nextLoc` trong `DoStepHttp` |
| HF cold start/build chậm | request đầu timeout | `/health` warm-up trước khi vào game, UI báo "PIBT server waking up" |
| CORS đổi domain | WebGL bị block | `CORS_EXTRA_ORIGINS` env và preflight test trong deploy checklist |

## 5. Rollback

Giữ `/plan` one-shot làm fallback:

- Nếu session relay lỗi trong demo, Unity có thể bật setting `useOneShotPlanEndpoint` để gọi `/plan` như hướng dẫn cũ.
- Fallback này reset mỗi timestep, không giữ operation queue; chỉ dùng để chứng minh kết nối HF/CORS hoặc demo tối thiểu.
- Không rollback C++ `ResetAllSessionState` và `static delta` fix.

## 6. Thứ tự ưu tiên đề xuất

1. Làm M1 trên HF app trước vì đây là điểm khác biệt lớn nhất so với hướng dẫn cũ.
2. Làm M2 transport mode cho Unity Editor/WebGL + shutdown + parse `nextLoc`.
3. Deploy M3 và verify `/health`, CORS, sequence curl.
4. Test M4 trong Unity Editor local gọi HF trước, sau đó WebGL thật.
5. Chỉ sau khi session ổn mới tối ưu multi-session/worker process.

## 7. Definition of Done

Plan này hoàn tất khi:

- Unity Editor local gọi HF bằng HTTPS, không cần chạy WSL server local.
- WebGL gameplay không dùng raw TCP.
- Một round Editor/WebGL giữ cùng `sessionId` từ `hello` tới nhiều `plan-step`.
- Dừng Play Mode / về menu / hết round đều gửi `shutdown` hoặc `reset` tường minh.
- HF relay không còn giữ session stale quá TTL.
- Đổi map khác width không cần restart HF Space và không làm enemy đứng do state cũ.
- `/plan` vẫn hoạt động như smoke/fallback nhưng không còn là đường gameplay chính.
