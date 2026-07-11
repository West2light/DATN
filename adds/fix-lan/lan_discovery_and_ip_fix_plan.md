# Kế hoạch sửa LAN: không tự quét host + không hiển thị IP để Join

**Ngày:** 2026-07-07
**Nhánh:** `feature/fix_backtest_PIBT_TCP`
**Phạm vi:** `Assets/Scripts/Multiplayer/LanLobbyController.cs`, `Assets/Scripts/Multiplayer/LanDiscovery.cs`
**Trạng thái:** ĐÃ IMPLEMENT (2026-07-07) — Fix 1/2/3/5 xong, compile 0 lỗi. Fix 4 (firewall) là tài liệu, chưa cần code. Còn lại: test 2 máy thật.

### Đã làm & xác minh
- **Fix 1** `GetLocalIP`/`GetLanIPv4Candidates`: liệt kê NIC, loại WSL/ảo/APIPA, ưu tiên `192.168.*`. Chạy thử trong Editor → **PRIMARY IP = `192.168.5.101`** (đúng Wi-Fi), WSL `172.31.240.1` bị phạt score −160.
- **Safety net**: Hosting screen luôn tự điền ô IP nếu trống (bất kể nguyên nhân trước đó).
- **Fix 2** `LanDiscovery.SendBroadcast`: broadcast per-NIC vật lý, directed broadcast. Chạy thử → target `192.168.5.101 -> 192.168.5.255` (loại hẳn WSL). Payload nhét IP host: `TANK_MAPF_HOST:<port>:<hostIp>`.
- **Fix 3** `ListenLoop`: `ReuseAddress` + parse IP host từ payload; UI thêm `OnDiscoverTimeout` (7s) đổi sang gợi ý nhập tay nhưng vẫn quét tiếp.
- **Fix 5**: log candidates ở host, log target broadcast, log host-found ở client.
- Compile: `refresh_unity` + `read_console` = **0 error** (chỉ còn warning cũ không liên quan).

### Chưa làm
- Test 2 máy thật (mục 6) — bắt buộc để nghiệm thu auto-scan + connect.
- Fix 4 firewall: chỉ áp dụng nếu test 2 máy vẫn không thông.

---

## 9. Phát hiện khi test bản build thật (2026-07-07)

Đọc `Player.log` tại `%USERPROFILE%/AppData/LocalLow/DefaultCompany/TankMAPF/Player.log` (build ở `F:\Build-TankMAPF\`):

### 9.1. Bản build đang chạy là STALE (build từ code cũ)
- Log dòng hosting hiện `"[LAN] Hosting on 0.0.0.0:7777  (LAN IP: 192.168.5.101)"` — **format cũ**, không phải format mới (`Primary LAN IP: … All candidates: […]`).
- ⇒ Bản build chưa chứa Fix 1/2/3. IP `192.168.5.101` đúng chỉ vì route máy này tình cờ ra Wi-Fi. **Phải Build & Run lại** để có các fix LAN + fix font bên dưới.

### 9.2. BUG: HUD mất chữ HP / BASE / ENEMY trong build (đã sửa)
- **Bằng chứng:** Player.log lặp lại `"Trying to add Label (UnityEngine.UI.Text) for graphic rebuild while we are already inside a graphic rebuild loop. This is not supported."` — các label HUD tên GameObject `"Label"` (HP/BASE, fontSize 14, dựng ở `LanClientView.FindOrBuildBar`/`AddLabel` và `MapScenarioBootstrapPIBT`).
- **Nguyên nhân gốc:** `UiFontProvider.OnAtlasRebuilt` gọi `ForceRefreshAllTexts()` → `SetAllDirty()` **ngay trong vòng Canvas graphic-rebuild** (font động Roboto bake glyph giữa lúc Text đang dựng mesh). Unity từ chối → Text không được re-mesh → **chữ trắng**. Editor không dính vì `GetDefaultFont()` trả `LegacyRuntime.ttf` (font tĩnh), không phát `textureRebuilt`.
- **Fix v1 (deferred refresh):** `UiFontProvider.OnAtlasRebuilt` chỉ set cờ `_refreshQueued`; `FontPreloader.Update()` drain cờ → `ForceRefreshAllTexts()` ngoài vòng rebuild. → **Xoá được lỗi console** nhưng **text vẫn trắng** ở build sau (ô IP + HUD). Chứng tỏ re-mesh (SetAllDirty) không đủ; glyph/atlas của font động Roboto không render cho Text cập-nhật-muộn trên standalone.
- **Fix v2 (font — gốc rễ thật):** Editor chạy tốt vì `GetDefaultFont()` trả `LegacyRuntime.ttf` (font builtin ổn định); build lại dùng Roboto (động) → Text set `.text` lúc runtime bị trắng. **Đổi `GetDefaultFont()` dùng `LegacyRuntime.ttf` cho Editor + mọi build standalone** (`#if !UNITY_WEBGL || UNITY_EDITOR`); chỉ WebGL giữ Roboto + FontPreloader. → build render giống hệt Editor.
  - Bằng chứng build v1 (Player.log 20:47): log format LAN mới ✓ (code đã vào build), IP tính đúng `192.168.5.101` ✓, hết lỗi "graphic rebuild loop" ✓, nhưng ô IP + HP/BASE/ENEMY vẫn trắng → nên chuyển sang Fix v2.
  - Lưu ý phụ: LegacyRuntime (Liberation Sans) có thể thiếu vài ký tự trang trí (▶ ✔ ✖) trên nút menu — giống hệt những gì Editor đang hiển thị, chấp nhận được; text quan trọng (chữ+số) được phủ đầy đủ.
- **KHÔNG liên quan** đến sửa đổi LAN; đây là bug build-only có sẵn.

### 9.3. Timeout khi client kết nối `192.168.5.101:7777`
- Host lắng nghe `0.0.0.0:7777` (log xác nhận). Client báo `Connection timed out … check IP address and firewall`.
- ⇒ **Windows Firewall trên máy host chặn inbound TCP 7777** (đúng Fix 4). Mở cổng trên MÁY HOST (chạy PowerShell **Administrator**):
  ```powershell
  netsh advfirewall firewall add rule name="TankMAPF" dir=in action=allow program="F:\Build-TankMAPF\TankMAPF.exe" enable=yes
  netsh advfirewall firewall add rule name="TankMAPF TCP 7777" dir=in action=allow protocol=TCP localport=7777
  netsh advfirewall firewall add rule name="TankMAPF UDP 47776" dir=in action=allow protocol=UDP localport=47776
  ```
  Hoặc khi chạy host lần đầu, popup Windows Firewall hiện → tick **Private networks** → Allow.

### 9.4. Việc cần làm theo thứ tự
1. **Build & Run lại** (lấy fix LAN + fix font).
2. Mở firewall trên máy host (9.3).
3. Test 2 máy: host hiện IP → client auto-scan hoặc nhập `192.168.5.101` → vào phòng.

---

## 10. Host cũng thấy tank preview (đã implement)

**Yêu cầu:** khi host chọn map/mode và vào màn "Waiting for players", host không thấy UI preview/chọn tank — chỉ người được mời (client, `Screen.Lobby`) mới thấy. Muốn host có trải nghiệm giống hệt, **tái dùng UI lobby của Internet**.

**Cách làm (LanLobbyController.cs):** host vẫn có `LanNetworkBridge.Local` (là owner) nên chọn tank hoạt động sẵn. Sửa `Screen.Hosting` để hiện chính `_lobbyBox` (preview tank + tên + bảng màu + danh sách người chơi) thay cho `_ipBox`/`_playersTxt` cũ; **ô "Room code" đổi thành "Your IP: …"** để host chia sẻ, nút Copy relabel "COPY IP" và copy IP.
- `RefreshLobby`: nếu `_screen == Hosting` → hiện `Your IP`; block ready/owner-START chỉ chạy khi `Screen.Lobby` (host giữ nút START GAME tức thời, không cần ready-gate).
- Thêm field `_shareIp`, `_copyLabel`; `DoHost` set `_shareIp`.
- Không đụng luồng client `Screen.Lobby`.

**Chưa test:** MCP Unity đang ngắt nên chưa compile-check; cần build lại (hoặc mở lại Unity để chạy `refresh_unity` + `read_console`).

---

## 11. Menu JOIN auto-scan + START ≥2 người (đã implement)

**Yêu cầu:** (1) nút **JOIN ROOM** ở màn `Screen.InternetEntry` phải tự quét LAN ngay, không phải đi vòng qua HOST GAME → chọn map mới kích hoạt được searching; (2) **START cần ≥2 người**.

**Nguyên nhân (1):** `MenuViewBootstrap.OpenJoinPrompt` → `LanLobbyController.ShowJoinPrompt` → `OpenJoinScreen` → chỉ `SwitchTo(Screen.Joining)`, **không gọi `StartAutoDiscover`**. Auto-scan chỉ có ở `ShowJoinRow` (nút JOIN trong lobby, tới được sau HOST GAME + map). ⇒ thêm `StartAutoDiscover()` vào `OpenJoinScreen` (guard `#if !UNITY_WEBGL`).

**Fix (2):** `DoStartGame` chặn nếu `ConnectedClients.Count < minStart` (Editor=1, build=2) + báo trạng thái. `RefreshLobby` (nhánh `Screen.Hosting`) làm mờ nút START và đổi nhãn `START (need ≥2 players · N)` khi chưa đủ.

**Thông báo vàng "No host found on the LAN yet":** là hint sau timeout 7s của auto-scan; vẫn tiếp tục quét ngầm, tự điền IP khi có host. Nếu test 2 máy mà vẫn "No host found" khi host ĐANG chạy → kiểm tra firewall UDP 47776 + đúng subnet (mục 9.3).

---

## 1. Triệu chứng (theo ảnh người dùng)

Màn hình đang hiển thị là **Hosting screen** của `LanLobbyController` (`SwitchTo(Screen.Hosting)`):

- Tiêu đề `Waiting for players to connect…` (gold).
- Ô `Your IP — share it with other players:` **trống trơn** — không có địa chỉ nào để chia sẻ.
- `Players (1/8) · Host (you)`.
- 3 nút: `→ JOIN (enter host IP)`, `▶ START GAME`, `CANCEL`.

Hai than phiền của người dùng:

1. **Client không còn tự quét (auto-scan) địa chỉ host trên LAN nữa.**
2. **Không có IP/mã code nào để nhập vào Join room** (ô "Your IP" ở host trống, nên bên Join cũng không biết nhập gì).

---

## 2. Đính chính quan trọng: đây KHÔNG phải FishNet

Người dùng gọi là "bản LAN chỗ fish-net", nhưng qua rà soát:

- `grep -ri fishnet Packages/manifest.json Assets/` → **không có** FishNet trong project.
- Toàn bộ multiplayer dùng **Unity Netcode for GameObjects (NGO)** + `UnityTransport` (`using Unity.Netcode;`, `using Unity.Netcode.Transports.UTP;`).
- "Phần LAN" chính là `LanLobbyController` + `LanDiscovery` (UDP broadcast tự viết) chứ không phải hệ discovery của FishNet.

→ Khi sửa, tìm đúng 2 file NGO ở trên, đừng đi tìm component FishNet (không tồn tại).

---

## 3. Bằng chứng từ máy dev (nguyên nhân gốc)

`ipconfig` trên máy hiện tại cho thấy **nhiều network interface song song**:

| Adapter | IPv4 | Loại |
|---|---|---|
| Wireless LAN adapter **Wi-Fi** | `192.168.5.101` | **LAN thật** (peer join được) |
| vEthernet (**WSL** Hyper-V) | `172.31.240.1` | Ảo (WSL) — peer KHÔNG join được |
| Wi-Fi 2 / Wi-Fi 3 / Bluetooth | (không IPv4 hoặc down) | Ảo/phụ |

Đây là môi trường "multi-NIC" điển hình — và cả 2 cơ chế LAN hiện tại đều **giả định chỉ có 1 interface**, nên hỏng.

---

## 4. Phân tích nguyên nhân

### A. `GetLocalIP()` — chọn sai/không ra IP LAN (nguồn của ô "Your IP" trống hoặc sai)

`LanLobbyController.cs:1237-1255`:

```csharp
using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
s.Connect("8.8.8.8", 65530);
return ((IPEndPoint)s.LocalEndPoint)?.Address.ToString() ?? "127.0.0.1";
```

Vấn đề:
- Chỉ trả **đúng 1 IP** = source IP của default route tới 8.8.8.8.
- Nếu default route đi qua **VPN / WSL vEthernet / adapter ảo**, IP trả về là `172.31.x` (hoặc IP VPN) — peer trên Wi-Fi **không kết nối được**.
- Nếu máy **offline** hoặc không có route ra 8.8.8.8, `Connect` ném exception → nhánh `catch` gọi `Dns.GetHostAddresses(...)`; nhánh này **không được bọc try/catch**, nếu ném tiếp thì `GetLocalIP()` throw → `DoHost()` (dòng 939) throw **sau khi** `StartHost()` đã thành công nhưng **trước** `SwitchTo(Screen.Hosting)`.
- Chỉ hiển thị 1 IP → người dùng không có cách chọn IP đúng khi máy nhiều NIC.

> Ghi chú điều tra: với routing hiện tại (`8.8.8.8` đi qua Wi-Fi) hàm này *nên* trả `192.168.5.101`. Việc ô IP trống trong ảnh cần **xác nhận bằng log runtime** (xem Fix 5) — có thể do build cũ (stale) hoặc do lúc chụp default route đang đi qua adapter khác. Fix 1 xử lý cả hai khả năng bằng cách liệt kê + hiển thị mọi IP LAN hợp lệ.

### B. `LanDiscovery` broadcast sai interface (nguồn của "không tự quét được")

`LanDiscovery.cs:34-43` (host phát):

```csharp
using var udp = new UdpClient { EnableBroadcast = true };
udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, BroadcastPort)); // 255.255.255.255:47776
```

Vấn đề:
- `IPAddress.Broadcast` = **limited broadcast** `255.255.255.255`. Trên Windows nhiều NIC, gói này chỉ ra **một** interface do OS chọn (thường theo metric) — dễ ra nhầm vEthernet WSL thay vì Wi-Fi.
- Socket không bind vào interface Wi-Fi cụ thể → không kiểm soát được hướng phát.
- Client (`ListenLoop`, dòng 56-78) bind `new UdpClient(BroadcastPort)` không đặt `ReuseAddress` → nếu có tiến trình khác/địa chỉ đã bind (2 instance trên cùng máy khi test) sẽ ném và **im lặng dừng** (chỉ `Debug.LogWarning`).

→ Host phát ra nhầm interface / client không nhận được → **auto-scan không thấy host**.

### C. Windows Firewall chặn UDP 47776 / TCP 7777

- Discovery dùng UDP **47776** (`LanDiscovery.BroadcastPort`), game dùng TCP/UDP **7777** (`GamePort`).
- Nếu Firewall chưa cho Unity Editor / build nhận inbound trên các cổng này → client không nhận broadcast **và** không connect được, kể cả khi nhập đúng IP.

### D. Wiring auto-scan bên client (tồn tại nhưng mong manh)

- Client CÓ gọi auto-scan: `ShowJoinRow()` (`LanLobbyController.cs:857-863`) → `StartAutoDiscover()` (dòng 885-897) → `LanDiscovery.StartListening()`.
- Nhưng phụ thuộc hoàn toàn vào (B): nếu broadcast không tới nơi thì listener chờ vô hạn, UI vẫn đứng ở "Searching for a host on the LAN…" mà không báo lỗi hay hết giờ.
- Không có nút "quét lại" hay timeout để chuyển sang nhập tay rõ ràng.

---

## 5. Kế hoạch sửa (ưu tiên từ trên xuống)

### Fix 1 — `GetLocalIP()` → liệt kê & chọn IP LAN đúng, hiển thị nhiều IP
**File:** `LanLobbyController.cs` (thay hàm `GetLocalIP`, cập nhật `DoHost` dòng 939-941)

- Viết `GetLanIPv4Candidates()` dùng `NetworkInterface.GetAllNetworkInterfaces()`:
  - Chỉ lấy NIC `OperationalStatus.Up`, `AddressFamily.InterNetwork`.
  - **Loại**: loopback, `169.254.*` (APIPA), interface có tên/description chứa `WSL`, `Hyper-V`, `Virtual`, `VPN`, `Loopback`, `vEthernet`.
  - **Ưu tiên**: private LAN thật — `192.168.*` > `10.*` > `172.16–31.*` (dải này hay là Docker/WSL, để cuối).
- `GetLocalIP()` trả candidate ưu tiên cao nhất; nếu rỗng mới fallback `127.0.0.1`.
- Ô "Your IP" hiển thị IP chính `:7777`; nếu có nhiều candidate, hiện thêm dòng phụ "Địa chỉ khác: …" để người dùng chọn khi máy nhiều NIC.
- Bọc toàn bộ trong try/catch để không bao giờ throw ra `DoHost`.

### Fix 2 — `LanDiscovery` phát broadcast trên MỌI interface hợp lệ (directed broadcast)
**File:** `LanDiscovery.cs` (`SendBroadcast`)

- Với mỗi NIC hợp lệ (cùng bộ lọc như Fix 1), tính **directed broadcast** của subnet đó (IP OR (~mask)) và `Send` tới `<broadcast>:47776`, đồng thời vẫn gửi `255.255.255.255` để tương thích.
- Bind socket phát theo `Send(..., new IPEndPoint(nicUnicastIp, 0))` hoặc set `SocketOptionName.MulticastInterface`/bind local để ép ra đúng NIC.
- Nhét IP host thật vào payload (`TANK_MAPF_HOST:<port>:<hostLanIp>`) để client lấy đúng IP thay vì chỉ dựa vào `ep.Address` của gói.

### Fix 3 — Listener client bền hơn + timeout + quét lại
**File:** `LanDiscovery.cs` (`ListenLoop`) + `LanLobbyController.cs` (`StartAutoDiscover`)

- Bật `ReuseAddress` trước khi bind `UdpClient(BroadcastPort)` (cho phép nhiều instance/test trên 1 máy).
- Parse IP host từ payload (Fix 2) thay vì chỉ `ep.Address`.
- Thêm timeout ~6-8s: nếu không thấy host → đổi status "Không tìm thấy host — nhập IP thủ công" + hiện sẵn ô nhập; thêm nút "Quét lại".

### Fix 4 — Firewall (tài liệu + tùy chọn tự thêm rule)
**File:** tài liệu trong folder này + (tùy chọn) helper editor

- Ghi rõ lệnh mở cổng (chạy admin):
  ```powershell
  netsh advfirewall firewall add rule name="TANK LAN UDP 47776" dir=in action=allow protocol=UDP localport=47776
  netsh advfirewall firewall add rule name="TANK LAN TCP 7777"  dir=in action=allow protocol=TCP localport=7777
  ```
- Hoặc khi Unity lần đầu mở socket, Windows bật popup "Allow access" → phải chọn **Private networks**.

### Fix 5 — Instrumentation để xác nhận nguyên nhân ô IP trống
**File:** `LanLobbyController.cs` (`DoHost`), `LanDiscovery.cs`

- Log rõ: danh sách mọi IPv4 candidate + IP đã chọn + `_ipVal.text` sau khi gán.
- Log mỗi lần host phát broadcast (ra interface nào) và mỗi lần client nhận.
- Mục tiêu: chạy 1 lần trên 2 máy để chốt xem ô trống là do (A) build cũ, (B) route qua adapter ảo, hay (C) exception — trước khi kết luận cuối.

---

## 6. Cách kiểm thử (bắt buộc 2 máy cùng LAN)

1. **Máy A (host):** Menu → chọn map/thuật toán → `HOST GAME`. Kỳ vọng ô "Your IP" hiện `192.168.5.101:7777` (đúng IP Wi-Fi, KHÔNG phải `172.31.x`).
2. **Máy B (client):** Menu → `JOIN`. Kỳ vọng trong ~vài giây tự điền IP host vào ô nhập ("Host found: …"). Nhấn JOIN → vào Lobby.
3. **Fallback:** tắt Wi-Fi máy B, bật lại; hoặc nhập tay IP máy A → vẫn phải connect được (kiểm tra Firewall).
4. Xác nhận `read_console` (Unity MCP) 0 lỗi sau khi sửa.

> Test trên cùng 1 máy 2 instance chỉ kiểm được compile + UI, KHÔNG kiểm được broadcast đa-NIC. Phải test 2 máy thật.

---

## 7. Phạm vi KHÔNG đụng

- **Không** phá luồng Internet/registry (WebGL): `ShowJoinPrompt`, `ShowAndJoin`, `CreateInternetRoomAndJoin`, `InternetSessionClient`, `RelayManager`. Chỉ sửa nhánh LAN desktop/editor.
- **Không** đụng code hiển thị máu + số đếm Enemy (nằm ở `MapScenarioBootstrap`/backtest) — đã tách biệt, không liên quan file LAN.
- **Không** đổi `GamePort=7777`, `BroadcastPort=47776`, giao thức payload theo hướng phá tương thích (chỉ mở rộng payload thêm field, giữ prefix `Signature`).

---

## 8. Thứ tự thực thi đề xuất

1. Fix 5 (instrument) → chạy 2 máy → chốt nguyên nhân ô IP trống.
2. Fix 1 (GetLocalIP robust) → test host hiện đúng IP.
3. Fix 2 + 3 (broadcast/listen đa-NIC) → test auto-scan.
4. Fix 4 (firewall) nếu vẫn không thông.
5. `refresh_unity` + `read_console` = 0 lỗi, rồi commit (không kèm Co-Authored-By).
