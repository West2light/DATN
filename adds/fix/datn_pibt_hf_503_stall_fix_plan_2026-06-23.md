# PLAN (FIX): Unity Client gọi HF PIBT C++ bị 503 + enemy đứng yên (stall)

Ngày lập: 2026-06-23
Nhánh: `feature/fix_backtest_PIBT_TCP`
Tham chiếu nền (đọc trước):
- `adds/connect-server/pibt_hf_session_connection_plan_2026-06-23.md` (kiến trúc session relay — đích chính)
- `adds/connect-server/unity_engine_hf_pibt_connection_plan_2026-06-23.md` (transport mode Unity)

Triệu chứng người dùng báo:
```
[PIBT_TCP] EnemyPIBT_TCP_6 stuck: no-progress 3.0s (target=(156,70), cell=(157,70)) → forced replan
...
INFO: 10.16.36.201:46901 - "POST /plan HTTP/1.1" 200 OK
INFO: 10.16.17.162:14294 - "POST /plan HTTP/1.1" 200 OK
INFO: 10.16.11.28:27247 - "POST /plan HTTP/1.1" 200 OK
INFO: 10.16.36.201:46901 - "POST /plan HTTP/1.1" 503 Service Unavailable
```
WSL local TCP chạy mượt; HF thì enemy đứng + thỉnh thoảng 503.

---

## A. CHẨN ĐOÁN GỐC RỄ (grounded vào code hiện tại)

### A.1 Vì sao enemy đứng yên (stall) — nguyên nhân CHÍNH
Scene đang dùng `transport = PibtRemoteTransport.HttpPlanOneShot`
(`MapScenarioBootstrapPIBT_TCP.cs:47`, baseUrl `:49 = https://west2light-server-pibt.hf.space`).

Mỗi tick (`tcpTickInterval = 0.25f`, `:44`) Unity gọi `DoStepHttpPlanOneShot()` (`:341`) → `POST /plan`.
Endpoint `/plan` trên HF là mô hình **one-shot**: mỗi request server làm `hello → plan_step → shutdown`,
tức **reset toàn bộ session-state của planner C++ ở MỖI timestep** (xác nhận trong
`pibt_hf_session_connection_plan` §0, §1.1, §2.1).

PIBT/EPIBT giữ thứ tự ưu tiên + operation queue giữa các bước. Reset mỗi tick → planner mất tính liên tục
→ agent dao động/không tiến → `TrackStuckAndRecover()` (`GridEnemyAgentPIBT_TCP.cs:110`) đếm
`no-progress > stuckTimeout(2.5s)` → `NeedsForcedReplan` → sau `fallbackTimeout(4s)` chạy
`GreedyStepTowardEagle()` (NON-PIBT nudge). Đây chính là log "stuck → forced replan".

> Đây là **lỗi kiến trúc, không phải lỗi mạng**: kể cả khi mọi `/plan` đều `200 OK`, enemy vẫn stall
> vì planner bị reset liên tục. WSL local mượt vì... thực ra WSL dùng **raw TCP giữ 1 session xuyên suốt**
> (`ConnectAndHello()` + `DoStepAsync()`), không phải `/plan`.

### A.2 Vì sao có độ trễ làm stall nặng hơn
HTTP path được serialize bằng `_webRequestInFlight` (`Update()` `:225`): Unity **không** gửi tick mới
khi tick cũ chưa về. Nên **tick-rate thực tế bị chặn bởi RTT tới HF**, không phải `tcpTickInterval`.
RTT HF (VN→HF) thường 300–800ms → agent chỉ nhận lệnh mỗi ~0.5–1s. Với `stuckTimeout=2.5s`, chỉ vài tick
chậm/È là kích hoạt "no-progress".

### A.3 Vì sao có 503 Service Unavailable
- 3 IP khác nhau `10.16.x.x` là **các node proxy/router nội bộ của HF** (mỗi request có thể đi qua node
  khác → source IP khác). KHÔNG phải Unity gửi song song (Unity đã serialize ở A.2). Đây là bình thường.
- 503 = HF Space tạm thời không phục vụ được 1 request: cold-start/wake-from-sleep, uvicorn worker đơn đang
  bận trong 1 lời gọi `/plan` chậm (mỗi `/plan` block làm hello→plan_step→shutdown trên TCP server tuần tự),
  hoặc HF throttle tài nguyên.
- **Cách Unity xử lý 503 đang sai**: `PIBTWebRelayClient.Send()` (`:79-105`) gộp MỌI lỗi (kể cả 503 tạm thời)
  thành `request.error` chung, **không đọc `request.responseCode`**. `DoStepHttpPlanOneShot()` coi đó là lỗi
  kết nối cứng → vào nhánh reconnect (`:359-376`): `_serverReady=false` + `ConnectAndHelloHttp()` lại
  → càng churn, càng stall.

### A.4 Kết luận
| Vấn đề | Gốc rễ | Hướng sửa |
|---|---|---|
| Enemy đứng yên | `/plan` reset planner mỗi tick (mất session-state) | **Chuyển sang HTTP session relay** giữ 1 session/round |
| Tick chậm → false-stuck | RTT cao + serialize | Tăng `tcpTickInterval` remote + nới `stuckTimeout` khi remote |
| 503 làm rớt session | Client coi 503 = lỗi cứng → reconnect | 503/502/504/429 = **transient**: backoff + retry **cùng session**, không tear-down |
| 503 ở request đầu | HF cold-start | `/health` warm-up trước `hello` |

**Đích chính = HTTP session relay** (`/api/sessions/pibt/*`). Unity client **đã** hỗ trợ sẵn
(`transport = HttpRelaySession` → `ConnectAndHelloHttp()` nhánh session `:258-288` + `DoStepHttpSession()` `:291`).
**Mảnh còn thiếu = deploy session relay trên HF `app.py`** (hiện HF mới chỉ có `/plan`).

---

## B. PHẠM VI & FILE

### Repo Unity (D:\2025.2\DATN\projectY) — track client
- `Assets/Scripts/PIBTWebRelayClient.cs`
- `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`
- Scene `Assets/Scenes/MapF_TankTest_PIBT.unity` (đổi default transport) — qua Inspector
- (tham chiếu) `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs` — chỉ chỉnh tham số timeout khi remote

### Repo server HF (WSL `/home/west2light/projectY`) — track server
- `app.py` (nâng thành session relay; **template có sẵn** ở `services/invite-registry/app.py` trong repo Unity)
- Dockerfile / deploy workflow HF nếu cần

> **KHÔNG đụng**: thuật toán C++ planner core, mode A*/local-PIBT, `GridEnemyAgentPIBT.cs`,
> `MapScenarioBootstrapPIBT.cs`.

---

## C. KIỂM TRA NHANH HIỆN TRẠNG HF (làm trước, 5 phút)

```powershell
# 1. HF còn sống?
Invoke-RestMethod https://west2light-server-pibt.hf.space/health
# kỳ vọng: status=ok, process_alive=true, tcp_reachable=true

# 2. Session relay đã tồn tại chưa? (nếu 404 → CHƯA deploy → phải làm M1)
try { Invoke-RestMethod https://west2light-server-pibt.hf.space/api/sessions/pibt/healthz }
catch { $_.Exception.Response.StatusCode }
```
- `/api/sessions/pibt/healthz` trả `{"ok":true,...}` → relay đã có → bỏ qua M1, làm M2 trở đi.
- Trả 404 → relay CHƯA có → phải làm M1 (server) trước.

---

## M1 — (SERVER/HF) Nâng `app.py` thành persistent session relay

> Làm trong repo WSL deploy HF. Mẫu tham chiếu **đã có sẵn** trong repo Unity:
> `services/invite-registry/app.py` (`PibtRelayConnection` `:52`, `prune_pibt_connections` `:94`,
> `handle_pibt_relay` `:257`, healthz `:186`). Copy mô hình đó sang HF `app.py`.

### Việc cần làm
1. Thêm `class PibtRelayConnection`: giữ 1 TCP socket tới `127.0.0.1:7777`, buffer đọc theo dòng (`\n`),
   `lock`, `last_used`.
2. `PIBT_CONNECTIONS: dict[sessionId -> PibtRelayConnection]` + `prune` theo `ACTIVE_SESSION_TTL` (60–300s).
3. Endpoint mới (giữ ĐÚNG path Unity đang gọi, xem `PIBTWebRelayClient.RelayPath = "/api/sessions/pibt/"`):
   - `POST /api/sessions/pibt/hello`: nếu sessionId cũ tồn → đóng; nếu có session khác active & chưa idle
     → `409 active session exists`; nếu stale → đóng stale rồi mở TCP mới; forward `hello`, trả `hello_ack`.
   - `POST /api/sessions/pibt/plan-step`: lookup sessionId; không có → `404 session expired`; forward, trả `plan_result`.
   - `POST /api/sessions/pibt/shutdown`: forward + xoá session (kể cả forward lỗi).
   - `POST /api/sessions/pibt/reset`: forward `reset`, giữ connection.
   - `GET /api/sessions/pibt/healthz`: trả `{ok, active_sessions, tcp_reachable}`.
4. Timeout forward: hello 60–70s, plan-step 15–20s, shutdown/reset 5s. Bound payload ~2MiB.
5. **Giảm 503**: vì C++ server tuần tự 1-session, relay phải **serialize forward theo socket lock**, và khi
   đang bận thì trả `409`/`429` (chứ không để HF trả 503 mơ hồ). TTL prune để không kẹt session chết.
6. Giữ `/plan` one-shot cũ làm smoke/fallback (không xoá).

### Acceptance M1 (curl)
```bash
curl https://west2light-server-pibt.hf.space/api/sessions/pibt/healthz      # {"ok":true,...}
# sequence cùng 1 sessionId:
curl -X POST .../api/sessions/pibt/hello       -d '{...hello...}'
curl -X POST .../api/sessions/pibt/plan-step   -d '{...t=0...}'
curl -X POST .../api/sessions/pibt/plan-step   -d '{...t=1...}'
curl -X POST .../api/sessions/pibt/shutdown    -d '{...}'
```
Log C++ phải đúng thứ tự `hello → plan_step* → shutdown → session reset` (KHÔNG hello/shutdown mỗi tick).

---

## M2 — (CLIENT) Phân biệt lỗi transient (503/502/504/429) vs lỗi cứng

> Mục tiêu: 503 không làm rớt/reset session. Làm được ngay cả khi vẫn ở `/plan` (giảm nhẹ), và đặc biệt quan
> trọng cho session relay (đừng hello lại chỉ vì 1 tick 503).

### M2.1 `PIBTWebRelayClient.cs` — lộ HTTP status code
Hiện `Send()` (`:79-105`) chỉ trả `(response, error)`, mất `responseCode`. Thêm callback giàu thông tin:

Thêm overload `Send`/`Exchange` trả thêm `long statusCode`. Cách ít phá vỡ nhất: thêm field public cập nhật
sau mỗi call:
```csharp
public long LastStatusCode { get; private set; }
```
Trong `Send(...)`, sau `yield return request.SendWebRequest();` set:
```csharp
// (Send là static → đổi thành instance method, hoặc truyền callback set field)
```
> **Lưu ý kỹ thuật**: `Send` đang là `static`. Đổi thành **instance method** (bỏ `static`) để set
> `LastStatusCode = request.responseCode;`. `PostRelative/PostAbsolute/Exchange` đã là instance nên chỉ cần
> bỏ `static` ở `Send` và gọi `Send(...)` (không qua class). Set `LastStatusCode` ở cả nhánh success lẫn fail:
```csharp
private IEnumerator Send(string url, string json, int timeoutSeconds, Action<string,string> completed)
{
    ...
    yield return request.SendWebRequest();
    LastStatusCode = request.responseCode;   // 200/404/409/503/0(timeout)...
    string response = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
    if (request.result != UnityWebRequest.Result.Success)
    {
        string error = string.IsNullOrWhiteSpace(response) ? request.error : response;
        completed?.Invoke(null, error);
        yield break;
    }
    completed?.Invoke(response, null);
}
```

### M2.2 `MapScenarioBootstrapPIBT_TCP.cs` — helper phân loại + retry tại chỗ
Thêm helper:
```csharp
private static bool IsTransientStatus(long code)
    => code == 0 || code == 408 || code == 429 || code == 502 || code == 503 || code == 504;
```
Trong `DoStepHttpSession()` (`:309`) và `DoStepHttpPlanOneShot()` (`:359`), TRƯỚC khi vào nhánh reconnect,
xử lý transient bằng **backoff + retry cùng session**, KHÔNG `_serverReady=false`, KHÔNG hello lại:
```csharp
if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(response))
{
    if (IsTransientStatus(_webClient.LastStatusCode) && _transientRetries < maxTransientRetries)
    {
        _transientRetries++;
        Debug.LogWarning($"[PIBT_HTTP] transient {_webClient.LastStatusCode} (retry {_transientRetries}/{maxTransientRetries}), giữ session.");
        yield return new WaitForSeconds(transientBackoffSec); // ví dụ 0.5s
        yield break;   // KHÔNG đụng _serverReady; tick sau gửi lại trên cùng session
    }
    // ... (giữ nguyên nhánh reconnect cũ cho lỗi cứng / hết transient) ...
}
else { _transientRetries = 0; }
```
Khai báo thêm field:
```csharp
[Header("HTTP resilience")]
[Min(0)] public int   maxTransientRetries = 6;
[Min(0f)] public float transientBackoffSec = 0.5f;
private int _transientRetries;
```
> Quan trọng: khi transient, **không** chuyển agent sang stall ngay — vì chỉ `yield break`, target hiện tại
> giữ nguyên, agent tiếp tục đi theo lệnh cũ tới khi tick kế thành công.

---

## M3 — (CLIENT) Warm-up `/health` trước hello (chống cold-start 503)

Trong `ConnectAndHelloHttp()` (`:239`), TRƯỚC khi gửi `hello`, gọi health 1 lần (best-effort) để đánh thức Space:
```csharp
_webClient.RelayBaseUrl = httpRelayBaseUrl;
// warm-up: gọi /health, bỏ qua kết quả, chỉ để wake cold-start
yield return _webClient.PostAbsolute("/health", "", 70, (_, __) => { });
ShowToast("PIBT server đang khởi động...", 3f);
```
> Dùng timeout dài (70s) cho lần đầu vì cold-start HF có thể chậm. Không fail-hard nếu health lỗi — chỉ log.

---

## M4 — (CLIENT) Chuyển transport sang session relay + chỉnh nhịp cho remote

### M4.1 Đổi default transport (sau khi M1 deploy xong)
- Cách bền vững: trong Inspector của `MapScenarioBootstrapPIBT_TCP` ở scene `MapF_TankTest_PIBT.unity`,
  đặt `transport = HttpRelaySession`, `httpRelayBaseUrl = https://west2light-server-pibt.hf.space`.
- Hoặc đổi default field (`:47`) `HttpPlanOneShot → HttpRelaySession`.
- Giữ `HttpPlanOneShot` làm fallback (chọn lại khi relay lỗi).

### M4.2 Nhịp tick & ngưỡng stuck khi chạy remote
RTT HF cao → cần nới để tránh false-stuck (A.2). Khi `UsesHttpTransport()`:
- Tăng `tcpTickInterval` remote: ví dụ set 0.25 → **0.5** ở Inspector scene PIBT_TCP
  (hoặc ép trong code khi `UsesHttpTransport()` true).
- Nới `stuckTimeout`/`fallbackTimeout` của `GridEnemyAgentPIBT_TCP` khi remote (qua bootstrap, lúc
  `ConfigureEnemy`): ví dụ `stuckTimeout 2.5 → 5.0`, `fallbackTimeout 4.0 → 8.0` để 1–2 tick chậm không bị
  coi là stuck. (Chỉ khi remote; local TCP giữ nguyên.)

> Lý do nới chứ không siết: session relay giữ planner-state nên enemy sẽ đi mượt; vấn đề còn lại chỉ là
> độ trễ mạng → cho agent thêm thời gian thay vì kích hoạt greedy-nudge NON-PIBT (làm bẩn số liệu backtest).

### M4.3 Shutdown sạch khi thoát (HTTP)
Đảm bảo khi dừng Play/về menu/đổi map (backtest) gửi `shutdown` qua relay để HF không giữ session stale
(tránh `409` cho round sau). Backtest đã có `ShutdownGracefully()` cho TCP path; thêm nhánh HTTP:
gọi `_webClient.Exchange("shutdown", BuildShutdownRequest(_sessionId), 5, ...)` trước khi unload.

---

## D. THỨ TỰ THỰC HIỆN
1. **C** (kiểm tra HF) → xác định relay đã có chưa.
2. Nếu chưa: **M1** (server relay) — đây là điểm chặn lớn nhất, sửa stall tận gốc.
3. **M2** (client 503-aware) — làm song song được, có ích cả khi chưa xong M1.
4. **M3** (warm-up) + **M4** (đổi transport + nới nhịp).
5. Test theo mục E.

> Nếu CHƯA kịp deploy M1: làm M2+M3+M4.2 trước → 503 bớt làm rớt, false-stuck giảm; **nhưng enemy vẫn sẽ
> kém mượt** vì `/plan` reset planner mỗi tick (A.1). Chỉ M1 mới sửa triệt để stall.

---

## E. TEST CHECKLIST (Editor gọi HF, KHÔNG chạy WSL local)
1. Tắt mọi `pibt_tcp_server` local (đảm bảo không có gì nghe `127.0.0.1:7777`).
2. Scene `MapF_TankTest_PIBT`, `transport = HttpRelaySession`, baseUrl HF.
3. Play 1 map (random-32):
   - Console có `[PIBT_HTTP] Connected and initialized through relay`.
   - KHÔNG có `Connecting raw TCP endpoint host='127.0.0.1'`.
   - HF log: 1 `hello` → nhiều `plan-step` → `shutdown` (KHÔNG hello/shutdown mỗi tick).
4. Enemy di chuyển liên tục tới Eagle, **không** spam `stuck: no-progress`.
5. Cho 1–2 tick 503 (nếu xảy ra): log `transient 503 (retry n/..), giữ session`, enemy KHÔNG khựng/reset.
6. Chạy lần lượt 5 map khác width (random, maze-128, mansion, chantry, gallows) không restart HF, không
   `GEOMETRY STALL`, không lỗi raw TCP.
7. Backtest mode đổi map: mỗi map 1 sessionId mới, round trước đã `shutdown`.

---

## F. RỦI RO
| Rủi ro | Tác động | Giảm thiểu |
|---|---|---|
| Chưa deploy M1 mà chỉ sửa client | Enemy vẫn stall (planner reset/tick) | Ưu tiên M1; client hardening chỉ giảm nhẹ |
| HF single-process, nhiều client | Session sau bị 409/blocked | TTL + shutdown tường minh; DATN chấp nhận 1 session/lúc |
| Nới `stuckTimeout` quá tay | Backtest che giấu agent thật sự kẹt | Chỉ nới khi remote-HTTP; local giữ nguyên; ghi log rõ |
| Warm-up `/health` chậm lần đầu | Vào game trễ vài giây | Toast "đang khởi động"; timeout 70s chỉ lần đầu |
| `Send` đổi static→instance | Lỗi biên dịch chỗ gọi khác | Chỉ `Send` dùng nội bộ; rà `PostRelative/PostAbsolute/Exchange` (đều instance) |
| Đổi default transport ảnh hưởng WebGL | WebGL vốn ép HTTP (`:471`) | Không sao — WebGL luôn HTTP; chỉ default Editor đổi |

---

## G. DEFINITION OF DONE
- HF có `/api/sessions/pibt/{hello,plan-step,shutdown,reset,healthz}` (relay giữ session).
- Unity Editor (không WSL local) chạy PIBT_TCP gọi HF, enemy đi mượt như local TCP.
- 1 round = 1 sessionId xuyên suốt; 503/502/504 được retry-cùng-session, không tear-down.
- Đổi 5 map khác width không restart HF, không stall, không greedy-nudge NON-PIBT bất thường.
- Thoát/về menu/đổi map đều `shutdown`; HF không giữ session stale quá TTL.
- `/plan` one-shot vẫn còn làm fallback/smoke, không phải đường gameplay chính.
```
