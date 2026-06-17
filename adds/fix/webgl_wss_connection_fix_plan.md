# Fix: WebGL không kết nối được qua `wss://game.luminx.io.vn:443`

Ngày lập: 2026-06-16
Repo: `F:\DATN`  ·  Nhánh: `networking-gcp`
Bối cảnh: đã xong D1–D4 (HTTPS + domain `luminx.io.vn`). Site load OK qua HTTPS, nhưng **multiplayer không kết nối**. Plan này fix trước khi sang D5.

---

## 0. Triệu chứng (console + ảnh)

```
[LAN] Connecting to game.luminx.io.vn:443 via wss
Invalid network endpoint: game.luminx.io.vn:443.
Target server network address (game.luminx.io.vn) is Invalid!
[Netcode] Client is shutting down due to network transport start failure of UnityTransport!
[LAN] Connection timeout to ?:7777
Exception: WebGL as a server is not supported by Unity Transport, outside the Editor.
  at Unity.Netcode.Transports.UTP.UnityTransport.CreateDriver (...)
```
UI: màn JOIN hiện "Hết thời gian — không thể kết nối. Kiểm tra IP và firewall." User bấm **HOST GAME** vẫn lỗi.

→ Có **2 lỗi độc lập**, phải fix cả hai.

---

## 1. Nguyên nhân gốc (đã truy trong code + source UnityTransport 2.3.0)

### 1.1 Lỗi A — `Invalid network endpoint: game.luminx.io.vn:443` (lỗi kết nối chính)

UnityTransport 2.x **chỉ chấp nhận IP literal** làm địa chỉ kết nối, KHÔNG nhận DNS hostname:

`Library/PackageCache/com.unity.transport@.../Runtime/Transports/UTP/UnityTransport.cs`
```csharp
private static NetworkEndpoint ParseNetworkEndpoint(string ip, ushort port, bool silent=false) {
    if (!NetworkEndpoint.TryParse(ip, port, out endpoint, NetworkFamily.Ipv4) &&
        !NetworkEndpoint.TryParse(ip, port, out endpoint, NetworkFamily.Ipv6))
        Debug.LogError($"Invalid network endpoint: {ip}:{port}.");   // ← hostname rớt vào đây
}
public NetworkEndpoint ServerEndPoint => ParseNetworkEndpoint(Address, Port);
```
`SetConnectionData("game.luminx.io.vn", 443)` → `TryParse` fail (không phải IP) → `Invalid network endpoint` → `Target server network address is Invalid!` → transport start failure.

**Phát hiện then chốt** — cách UTP dựng URL WebSocket (WebGL):
`com.unity.transport@.../Runtime/WebSocketNetworkInterface.cs` → `GetServerURL()`:
```csharp
FixedString512Bytes url = secureHostname.IsEmpty ? "ws://" : "wss://";
if (endpoint.Family == NetworkFamily.Custom || secureHostname.IsEmpty)
    url.Append(endpoint.ToFixedString512Bytes());   // IP:port
else {
    url.Append(secureHostname);     // ← HOST của URL = secureHostname (domain)
    url.Append(':'); url.Append(endpoint.Port);     // ← chỉ PORT lấy từ endpoint
}
url.Append(Path);                   // mặc định "/"
```
Nghĩa là khi có `secureHostname` (đặt qua `SetClientSecrets`):
- **Host của `wss://` = `secureHostname`** (domain) — KHÔNG dùng IP của endpoint.
- **Chỉ PORT** lấy từ endpoint.

⇒ **Endpoint chỉ cần một IP hợp lệ (để TryParse pass) + đúng PORT; domain phải đi qua `SetClientSecrets`, KHÔNG đi qua `SetConnectionData`.**

**Bug hiện tại** (`Assets/Scripts/Multiplayer/InternetSessionClient.cs`, `ResolveSession`):
```csharp
string resolvedHost = preferWebEndpoint && !string.IsNullOrWhiteSpace(response.webHost)
    ? response.webHost          // = "game.luminx.io.vn" (DOMAIN)
    : response.host;
...
host = resolvedHost,            // ← domain → SetConnectionData → INVALID ENDPOINT
secureWebSocketHost = secureWebSocket ? resolvedHost : "",  // domain (đúng cho TLS)
```
`host` (dùng cho `SetConnectionData`/ConnectionData.Address) đang là **domain** → vỡ. Trong khi registry **đã trả sẵn IP** ở field `host` (`"host": PUBLIC_IP` trong helper `tank-mapf-create-room`).

### 1.2 Lỗi B — `WebGL as a server is not supported by Unity Transport`

`LanLobbyController.DoHost()` gọi `NetworkManager.Singleton.StartHost()` (line ~877). Trên WebGL, `UnityTransport.CreateDriver` ném exception "WebGL as a server is not supported" vì trình duyệt không mở được listening socket.

`DoHost` đang **với tới được trên WebGL** qua nút **HOST GAME** của màn `Screen.Choose` (LAN cũ): `BtnFull("BtnHost", ..., DoHost)` (line ~347). `ShowInternal` luôn `SwitchTo(Screen.Choose)` trước khi auto-join (line ~189) → nút HOST (StartHost) hiển thị/bấm được trên WebGL.

> Mô hình production: WebGL **không bao giờ là server**. "HOST" trên web = tạo room trên dedicated server (qua registry) rồi **vào làm client** (đã có `CreateInternetRoomAndJoin` ở menu). Màn `Choose`/`DoHost` kiểu LAN chỉ dành cho Editor/Desktop.

> Ghi chú: dòng `[LAN] Connection timeout to ?:7777` chỉ là log gây hiểu nhầm — `OnConnectionTimeout` in hằng `GamePort` (7777) thay vì port thật (443). Cosmetic, không phải lỗi riêng.

---

## 2. Fix Lỗi A — endpoint dùng IP, domain chỉ dùng cho TLS

**File:** `Assets/Scripts/Multiplayer/InternetSessionClient.cs` — hàm `ResolveSession`.

Đổi đúng **một chỗ** (gán `host`): khi secure WebSocket, `ConnectionData.Address` phải là IP (`response.host`), không phải domain.

```csharp
// host (ConnectionData.Address) BẮT BUỘC là IP parse được — UTP NetworkEndpoint.TryParse
// loại bỏ DNS name. Domain đi qua secureWebSocketHost → trở thành host của wss:// URL
// (xem WebSocketNetworkInterface.GetServerURL). Registry đã trả IP ở response.host.
host = secureWebSocket ? response.host : resolvedHost,
...
// giữ nguyên: domain cho TLS/SNI + cert validation
secureWebSocketHost = secureWebSocket
    ? (string.IsNullOrWhiteSpace(response.webHost) ? resolvedHost : response.webHost)
    : string.Empty,
port = resolvedPort,   // 443 (giữ nguyên)
```

Kết quả runtime (cả nhánh HOST auto-join lẫn JOIN-by-code/link đều qua `ResolveSession`):
- `SetConnectionData("35.240.203.91", 443)` → TryParse OK (IPv4 hợp lệ).
- `UseEncryption = true`, `SetClientSecrets("game.luminx.io.vn", null)`.
- jslib dựng `wss://game.luminx.io.vn:443/` → TLS cert (Let's Encrypt cho `game.luminx.io.vn`) khớp → kết nối qua nginx → proxy tới `127.0.0.1:7778`.

> IP của endpoint **không** được dùng làm host của URL (chỉ cần parse được + đúng port), nên dù `response.host` là IP nào hợp lệ cũng được; kết nối thật đi tới domain.

> Không cần đổi `BeginClientConnection` (LanLobbyController ~1117–1130): nó đã `SetConnectionData(endpoint.host, endpoint.port)` + `UseEncryption=endpoint.secureWebSocket` + `SetClientSecrets(secureHost)` với `secureHost = endpoint.secureWebSocketHost` (domain). Sau fix, `endpoint.host` = IP nên chuỗi này đúng.

---

## 3. Fix Lỗi B — chặn HOST/StartHost trên WebGL

**File:** `Assets/Scripts/Multiplayer/LanLobbyController.cs`.

### 3.1 Guard `DoHost()` (bắt buộc — chặn exception)
Đầu hàm `DoHost()`:
```csharp
private void DoHost()
{
#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGL không thể làm server (UnityTransport ném "WebGL as a server is not supported").
    // Trên web, "HOST" = tạo room qua registry + vào làm client (menu CreateInternetRoomAndJoin).
    SetStatus("Trên web không tự host được — hãy tạo phòng từ menu.", new Color(1f,0.6f,0.3f));
    return;
#else
    // ... giữ nguyên code host LAN cho Editor/Desktop ...
#endif
}
```

### 3.2 Không hiển thị màn Choose/HOST trên WebGL (sạch UX)
- Trong `ShowInternal` (line ~189): trên WebGL **bỏ** `SwitchTo(Screen.Choose)`. Vì WebGL chỉ vào lobby qua 2 đường:
  - `ShowAndJoin(...)` (HOST từ menu): đã có `_autoJoinTarget` → đi thẳng `BeginAutoJoin()` → `Screen.Joining` → `Screen.Lobby`.
  - `ShowJoinPrompt(...)` (JOIN từ menu): đã `SwitchTo(Screen.Joining)` (line ~174).
```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    if (string.IsNullOrWhiteSpace(_instance._autoJoinTarget))
        _instance.SwitchTo(Screen.Joining);   // JOIN prompt; KHÔNG vào Choose
#else
    _instance.SwitchTo(Screen.Choose);
#endif
    if (!string.IsNullOrWhiteSpace(_instance._autoJoinTarget))
        _instance.BeginAutoJoin();
```
- (Phòng thủ thêm) ẩn `_btnHost` trên WebGL trong `SwitchToChoose`/build: `#if UNITY_WEBGL` → `SetVis(_btnHost, false);` (dù theo trên Choose không còn xuất hiện trên WebGL).

> `DoHost` guard (3.1) là tối thiểu bắt buộc. 3.2 dọn để người dùng không thấy nút host vô nghĩa trên web.

---

## 4. Không cần đổi server / registry / nginx / Terraform

- Registry đã trả đủ: `host = PUBLIC_IP` (IP cho endpoint), `webHost = game.luminx.io.vn` (domain cho TLS), `webGamePort = 443`, `webTransport = wss`. Fix nằm hoàn toàn **phía client Unity** ở cách *diễn giải* các field này.
- nginx (443 site + `game.` WS proxy), cert, firewall 443: giữ nguyên (D2/D3 đã làm).
- ⇒ Chỉ cần **rebuild WebGL client + redeploy** (`BuildWebClient` → artifact → `deploy-web`). Không restart server/registry.

---

## 5. Verify

Sau khi rebuild + redeploy:

**Console (không còn lỗi):**
```
[LAN] Connecting to ... via wss
```
KHÔNG còn `Invalid network endpoint` / `Target server network address is Invalid` / `WebGL as a server`.

**DevTools → Network → WS:** thấy `wss://game.luminx.io.vn:443/` trạng thái **101 Switching Protocols** (không phải failed/closed).

**E2E (đúng workflow):**
- Tab A: Menu → MULTIPLAYER INTERNET → **HOST GAME** → Alpha-32 → A* → vào Waiting Lobby (owner), KHÔNG còn "WebGL as a server".
- Tab B: MULTIPLAYER INTERNET → **JOIN ROOM** → dán link `https://luminx.io.vn/play?session=...` hoặc gõ code → vào đúng lobby.
- 2 người Ready → owner START → vào game: mỗi người 1 tank.

**Sanity nhanh (không cần client):** WS upgrade qua nginx hoạt động:
```bash
curl -sS -m 10 -o /dev/null -w "%{http_code}\n" \
  -H "Connection: Upgrade" -H "Upgrade: websocket" \
  -H "Sec-WebSocket-Version: 13" -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" \
  https://game.luminx.io.vn/
#   → 101 (nếu server WS đang chạy / có room) hoặc 502 (chưa có room) — KHÔNG phải lỗi TLS/cert.
```

---

## 6. Milestones

| MS | Nội dung | File | Done khi |
|----|----------|------|----------|
| **F1** | Endpoint dùng IP, domain cho TLS | `InternetSessionClient.cs` (`ResolveSession`) | Build editor/log: `endpoint.host` = IP, `secureWebSocketHost` = domain |
| **F2** | Guard `DoHost` + bỏ Choose trên WebGL | `LanLobbyController.cs` | WebGL không còn gọi StartHost; HOST từ menu → lobby |
| **F3** | Rebuild WebGL + redeploy | `BuildWebClient` / `deploy-web` | Artifact mới lên production |
| **F4** | Verify E2E 2 tab (§5) | — | wss 101; HOST + JOIN vào cùng lobby; vào game |

Phụ thuộc: F1 + F2 → F3 → F4. (F1 là fix kết nối cốt lõi; F2 chặn crash khi bấm HOST.)

---

## 7. Rủi ro & lưu ý

- **Giả định `response.host` luôn là IP**: đúng theo helper hiện tại (`"host": PUBLIC_IP`). Nếu sau này đổi `host` thành domain thì phải thêm field IP riêng. (Hiện an toàn.)
- **`UseEncryption` + reverse proxy**: server NGO (sau nginx) chạy plaintext ws; TLS do trình duyệt↔nginx. Client `UseEncryption=true` chỉ chọn scheme `wss` + dùng `secureHostname` cho URL/cert — đúng pattern Unity docs khuyến nghị (`websockets.md`: "run your own reverse proxy that performs TLS termination").
- **Mixed content**: trang HTTPS + `wss://` → hợp lệ (cùng nguyên tắc secure-origin). Nếu còn `ws://` ở đâu sẽ bị chặn — đảm bảo mọi nhánh internet client đều `secureWebSocket=true` khi port 443/`wss`.
- **Không đụng** D2/D3 (cert, nginx, firewall) và build Brotli — lỗi này thuần client logic.
- (Tùy chọn) sửa log `OnConnectionTimeout` in đúng host:port thật thay vì hằng `GamePort` để debug đỡ nhầm.
- Sau khi xong → tiếp tục **D5** (Terraform hóa domain/cert/nginx/firewall cho bền vững khi re-create VM) như trong `adds/domain/https_domain_migration_plan.md`.
```
