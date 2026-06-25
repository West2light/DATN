# PLAN: Unity Engine local gọi HF PIBT server thay cho WSL TCP local

Ngày lập: 2026-06-23  
Plan nền: `adds/connect-server/pibt_hf_session_connection_plan_2026-06-23.md`  
Mục tiêu: trong **Unity Editor/Unity Engine local**, bấm Play mode `PIBT_TCP` và gọi server C++ đã deploy trên Hugging Face, không cần chạy `./build/pibt_tcp_server` trong WSL local.

## 0. Kết luận

Cập nhật 2026-06-24:

- Unity Engine local phải ưu tiên `HttpRelaySession` cho gameplay PIBT thực tế.
- `HttpPlanOneShot` chỉ giữ vai trò fallback/smoke-test, không còn là mode khuyến nghị trong Editor vì nó làm mất planner session state mỗi tick và có thể gây tank di chuyển giật, replan lặp hoặc false-stuck.
- Code runtime hiện đã được chỉnh theo hướng này trong `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`: nếu chạy Unity Engine mà Inspector vẫn để `HttpPlanOneShot`, runtime sẽ tự ép sang `HttpRelaySession` và log cảnh báo.

Hiện tại Unity Editor **chưa gọi được HF đúng cách** vì code đang tách nhánh:

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    StartCoroutine(ConnectAndHelloWeb());
#else
    ConnectAndHello(); // raw TCP 127.0.0.1:7777
#endif
```

HF Space chỉ expose HTTPS FastAPI, không expose raw TCP `7777` ra ngoài. Vì vậy Unity Editor không thể chỉ đổi `serverHost` từ `127.0.0.1` sang `west2light-server-pibt.hf.space`.

Hướng triển khai đúng: refactor Unity client để Editor cũng có thể dùng HTTP relay bằng `UnityWebRequest`.

## 1. Mục tiêu triển khai

### Must-have

- Unity Editor local gọi `https://west2light-server-pibt.hf.space`.
- Không cần mở WSL server local.
- Không còn log `PIBTTcpClient Connecting raw TCP endpoint host='127.0.0.1'` khi đang chọn HF mode.
- Dùng cùng pipeline map/agent/action hiện tại của `MapScenarioBootstrapPIBT_TCP`.
- Parse cả `action` và `nextLoc` từ response để di chuyển theo cell server-authoritative.
- Khi dừng Play Mode, về menu, thắng/thua round: gửi `shutdown` hoặc để TTL server cleanup như fallback.

### Nice-to-have

- Có toggle trong Inspector để chuyển nhanh giữa:
  - local WSL raw TCP;
  - HF HTTP session relay;
  - HF `/plan` one-shot fallback.
- Có UI/log báo rõ đang dùng transport nào.
- Có health check HF trước khi spawn enemy.

## 2. Hai phương án

### Phương án A - Nhanh để unblock: gọi `/plan` one-shot

Unity Editor gửi một HTTP POST tới:

```text
https://west2light-server-pibt.hf.space/plan
```

Mỗi tick gửi body:

```json
{
  "team_size": 4,
  "map": {
    "width": 32,
    "height": 32,
    "symbols": "..."
  },
  "agents": [
    { "id": 0, "loc": 10, "orientation": 0, "goalLoc": 900 }
  ],
  "timestep": 0
}
```

Ưu điểm:

- HF `/plan` đã được verify live ngày 2026-06-23.
- Không cần deploy lại HF để bắt đầu test Editor.
- Phù hợp để chứng minh Unity Editor gọi được HF thay WSL.

Nhược điểm:

- Mỗi tick server làm `hello -> plan_step -> shutdown`, không giữ session state.
- Mất operation queue dài hạn của C++ planner giữa các tick.
- Không phải kiến trúc gameplay cuối.

Kết luận: dùng làm **fallback/demo nhanh**, không dùng làm đích cuối.

### Phương án B - Khuyến nghị: HTTP session relay

Unity Editor dùng cùng contract với WebGL:

```text
POST https://west2light-server-pibt.hf.space/api/sessions/pibt/hello
POST https://west2light-server-pibt.hf.space/api/sessions/pibt/plan-step
POST https://west2light-server-pibt.hf.space/api/sessions/pibt/shutdown
POST https://west2light-server-pibt.hf.space/api/sessions/pibt/reset
```

Ưu điểm:

- Giữ một `sessionId` trong cả round.
- C++ server giữ operation queue/state đúng như thiết kế.
- Reset rõ ràng khi về menu/dừng Play Mode/hết round.
- Dùng chung cho Editor và WebGL, giảm khác biệt môi trường.

Nhược điểm:

- Cần nâng HF `app.py` thành persistent session relay trước, vì hiện HF live mới có `/plan` one-shot và `/tcp/*` không giữ connection giữa HTTP requests.

Kết luận: đây là **đích chính**.

## 3. Thiết kế Unity

### 3.1 Thêm transport mode

Trong `MapScenarioBootstrapPIBT_TCP.cs`, thêm enum:

```csharp
public enum PibtRemoteTransport
{
    RawTcpLocal,
    HfHttpSessionRelay,
    HfHttpPlanOneShot
}
```

Inspector fields đề xuất:

```csharp
[Header("Remote Transport")]
public PibtRemoteTransport transport = PibtRemoteTransport.HfHttpSessionRelay;
public string httpRelayBaseUrl = "https://west2light-server-pibt.hf.space";
public bool useHttpRelayInEditor = true;
```

Quy tắc:

- `RawTcpLocal`: giữ nguyên đường WSL `PIBTTcpClient`.
- `HfHttpSessionRelay`: gọi `/api/sessions/pibt/*`.
- `HfHttpPlanOneShot`: gọi `/plan`.
- WebGL luôn ép HTTP, không raw TCP.
- Editor theo Inspector để test HF hoặc WSL.

### 3.2 Refactor `PIBTWebRelayClient`

File hiện tại: `Assets/Scripts/PIBTWebRelayClient.cs`.

Đổi vai trò thành HTTP relay dùng chung:

- giữ class nếu muốn giảm rename;
- sửa comment không còn "used only by single-player WebGL";
- thêm `baseUrl` explicit;
- nếu `baseUrl` rỗng thì mới fallback same-origin `Application.absoluteURL`;
- trong Editor phải set `baseUrl = https://west2light-server-pibt.hf.space`, vì `Application.absoluteURL` thường rỗng.

API nên có:

```csharp
public string BaseUrl { get; set; }
public IEnumerator Exchange(string operation, string json, int timeoutSeconds, Action<string, string> completed);
public IEnumerator PostAbsolute(string path, string json, int timeoutSeconds, Action<string, string> completed);
```

Trong đó:

- session relay dùng `Exchange("hello", ...)`;
- `/plan` fallback dùng `PostAbsolute("/plan", ...)`.

### 3.3 Refactor bootstrap

Trong `SpawnScenario()`:

```csharp
if (ShouldUseHttpSessionRelay())
    StartCoroutine(ConnectAndHelloHttp());
else if (ShouldUseHttpPlanOneShot())
    StartCoroutine(ConnectAndWarmupPlanEndpoint());
else
    ConnectAndHello();
```

Trong `Update()`:

```csharp
if (ShouldUseHttpSessionRelay())
    StartCoroutine(DoStepHttpSession());
else if (ShouldUseHttpPlanOneShot())
    StartCoroutine(DoStepHttpPlanOneShot());
else
    StartCoroutine(DoStepAsync());
```

Điểm quan trọng: không để toàn bộ HTTP path bị bọc bởi `#if UNITY_WEBGL && !UNITY_EDITOR`.

### 3.4 Session relay path

`ConnectAndHelloHttp()`:

- tạo `_sessionId = $"unity-editor-{Guid.NewGuid():N}"` khi chạy Editor;
- build map symbols như raw TCP path;
- gọi `PIBTTcpClient.BuildHelloRequest(...)`;
- POST `hello`;
- validate `hello_ack`, `width`, `height` nếu server trả;
- set `_serverReady=true`.

`DoStepHttpSession()`:

- build data bằng `BuildStepData(rows, cols)`;
- gọi `PIBTTcpClient.BuildPlanStepRequest(...)`;
- POST `plan-step`;
- parse `actions = PIBTTcpClient.ParseActions(response, data.Length)`;
- parse `nextLocs = PIBTTcpClient.ParseNextLocs(response, data.Length)`;
- gọi `ApplyStepActions(actions, nextLocs, rows, cols)`.

`ShutdownHttpGracefully()`:

- stop `_serverReady`;
- chờ `_webRequestInFlight=false` trong timeout ngắn;
- POST `shutdown`;
- clear `_sessionId`.

### 3.5 `/plan` one-shot fallback

Nếu cần unblock trước khi HF có session relay:

- tạo DTO riêng cho `/plan` vì endpoint này không nhận JSON-line protocol cũ.
- Body dùng snake_case `team_size`, `map`, `agents`, `timestep`.
- Response lấy `plan_result`.
- Parse bằng parser hiện có nếu response giữ object array `actions`.

Không dùng fallback này để đánh giá chất lượng planner dài hạn vì mỗi tick reset session.

## 4. Thiết kế HF cần có cho phương án B

HF `app.py` cần bổ sung persistent relay:

- global map `sessionId -> PibtRelayConnection`;
- mỗi connection giữ TCP socket tới `127.0.0.1:7777`;
- endpoint `/api/sessions/pibt/hello`: mở TCP, forward `hello`, lưu connection;
- endpoint `/api/sessions/pibt/plan-step`: lookup session, forward request;
- endpoint `/api/sessions/pibt/shutdown`: forward, close, remove;
- endpoint `/api/sessions/pibt/reset`: forward reset, giữ hoặc clear theo policy;
- TTL cleanup session idle;
- giới hạn một active gameplay session nếu chưa làm worker pool.

Khi chưa deploy phần này, Unity Editor chỉ test được phương án A `/plan`.

## 5. Các file sẽ sửa khi triển khai

Unity repo:

- `Assets/Scripts/PIBTWebRelayClient.cs`
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
- có thể thêm DTO file riêng như `Assets/Scripts/PIBTHttpPlanDtos.cs`
- có thể sửa scene/prefab `MapF_TankTest_PIBT.unity` để set default transport HF

HF server repo WSL:

- `/home/west2light/projectY/app.py`
- `/home/west2light/projectY/adds/deploy/UNITY_WEBGL_CLIENT_GUIDE.md`
- optional README/health version

Không đụng:

- thuật toán C++ planner core;
- `GridEnemyAgentPIBT_TCP` trừ khi phát hiện cần đổi cách nhận target;
- A*/local PIBT mode.

## 6. Milestone triển khai

### E1 - Cấu hình transport trong Unity

- Thêm enum transport.
- Thêm `httpRelayBaseUrl`.
- Log rõ transport khi `SpawnScenario`.
- Acceptance: Editor chọn `RawTcpLocal` vẫn chạy y như cũ với WSL.

### E2 - Dùng HTTP path trong Editor

- Gỡ ràng buộc WebGL-only cho HTTP relay.
- Editor chọn `HfHttpSessionRelay` sẽ gọi HTTP.
- Nếu session relay chưa có trên HF, tạm implement `HfHttpPlanOneShot`.
- Acceptance: tắt WSL local, Editor vẫn nhận response từ HF.

### E3 - Parse `nextLoc` cho HTTP response

- Session relay và `/plan` đều parse `nextLoc`.
- `ApplyStepActions(actions, nextLocs, rows, cols)` dùng chung cho TCP và HTTP.
- Acceptance: enemy không chỉ dead-reckon theo action.

### E4 - HF session relay

- Deploy `/api/sessions/pibt/*`.
- Verify bằng curl sequence `hello -> plan-step -> shutdown`.
- Acceptance: cùng `sessionId` chạy nhiều `plan-step`.

### E5 - Shutdown/reset lifecycle

- Gọi shutdown trước khi về menu/thắng/thua/dừng Play Mode nếu có thể.
- Fallback TTL trên HF.
- Acceptance: HF không giữ session cũ sau khi Editor dừng Play quá TTL.

### E6 - End-to-end 5 map

- Không chạy WSL local.
- Unity Editor gọi HF.
- Chạy lần lượt 5 map khác width.
- Không restart HF Space.
- Không `GEOMETRY STALL`.
- Không lỗi raw TCP.

## 7. Test checklist

### Test nhanh HF sống

```powershell
Invoke-RestMethod https://west2light-server-pibt.hf.space/health
```

Kỳ vọng:

```text
status=ok
process_alive=true
tcp_reachable=true
```

### Test Editor không dùng WSL

1. Đảm bảo không chạy `pibt_tcp_server` local.
2. Unity Editor mở scene `MapF_TankTest_PIBT`.
3. Set transport `HfHttpSessionRelay` hoặc `HfHttpPlanOneShot`.
4. Play.
5. Console phải có log HTTP base URL HF.
6. Console không được có raw TCP `127.0.0.1:7777`.

### Test session relay sau deploy

1. `hello` map random.
2. `plan-step` 10 tick cùng session.
3. `shutdown`.
4. Chọn map khác.
5. `hello` session mới.
6. Không có state leak giữa map.

## 8. Rủi ro

| Rủi ro | Tác động | Cách xử lý |
| --- | --- | --- |
| Dùng `/plan` quá lâu | Kết quả planner không phản ánh session thật | Chỉ dùng để unblock, chuyển sang session relay sớm |
| Editor fallback same-origin | Gọi nhầm `http://127.0.0.1:8080` | Bắt buộc set explicit `httpRelayBaseUrl` khi chạy Editor |
| HF cold start | Request đầu chậm/timeout | Gọi `/health` trước Play hoặc tăng hello timeout |
| Không shutdown kịp khi stop Play | Session stale trên HF | TTL cleanup + shutdown ở các exit path chủ động |
| Một HF process chỉ phục vụ 1 active session | Nhiều Editor/WebGL cùng lúc bị chặn | Chấp nhận trong DATN; dài hạn worker process/session |

## 9. Definition of Done

- Unity Editor local chạy `PIBT_TCP` và gọi HF thành công khi WSL server local không chạy.
- Có setting rõ để chuyển giữa local WSL và HF.
- HF session relay chạy được `hello -> plan-step* -> shutdown`.
- Editor/WebGL dùng chung HTTP transport logic.
- Enemy nhận `action` và `nextLoc` từ HF.
- Dừng Play/về menu/hết round không để session cũ sống vô hạn.
